# Earnings, payout periods and payout batches

This document describes how money moves through Optimize All: from an approved post to a payout recorded as paid.
It is the operating manual for finance and the design reference for developers. Endpoint details are in
[`api/ledger-payouts.md`](api/ledger-payouts.md).

> **Automatic transfers do not exist yet.** The only payment provider is `manual`: the platform prepares and tracks
> payouts, finance pays them outside the platform (bank transfer, PayPal, mobile wallet) and records the payment
> reference. Sending money automatically requires a payment-provider account and credentials (e.g. Wise or PayPal
> Payouts) that the business does not have yet. See [Adding a real payment provider](#adding-a-real-payment-provider).

## 1. The ledger

Every amount owed to a participant is an immutable `EarningEntry` row (`earning_entries`), created only through
`ILedgerWriter` (`Api/Common/Ledger/LedgerWriter.cs`):

* Amount, currency, exchange rate, settlement amount, rule version and idempotency key never change after insert
  (enforced in `AppDbContext`). Corrections are new **Adjustment** or **Reversal** entries.
* Each entry stores the original amount/currency **and** the settlement amount/currency with the exchange rate that
  was in force when the entry was created. A later exchange rate never changes an existing entry.
* Idempotency keys (unique index) make creation safe to retry: `submission:{id}:PostReward`, `adjustment:{requestId}`,
  `reversal:{entryId}`, …
* **Manual adjustments.** A credit (positive adjustment) creates money, so it is recorded `PendingApproval` and only
  becomes payable once a *different* user approves it in the pending-earnings queue. A debit (negative adjustment) only
  reduces what is owed and is `Approved` and available immediately. Nobody may adjust their own account
  (403 `ledger.self_adjustment`), and nobody may approve or decline an earning they created or that is credited to
  them (403 `ledger.self_approval`).
* **Exchange rates.** To convert from→to, the latest direct row (from→to) and the latest inverse row (to→from)
  effective at that instant are both looked up and the one with the later `EffectiveAt` is used (inverse = 1/rate,
  rounded to 8 decimals; an exact tie uses the direct row). So a newer rate entered in either direction always wins.

### Earning lifecycle

```
                       (bonus / live check)                    ┌──────────► Declined   (never payable)
  RecordAsync ──► PendingApproval ───────── approve ──────┐    │ decline
       │                  │                               ▼    │
       │ (no approval)    └───────────────────────────────┼────┘
       └──────────────────────────────────────────────► Approved ──(AvailableAt ≤ cutoff, batch prepared)──► Scheduled
                                                          ▲   │                                               │   │
                         item Held at finalize,           │   │ reverse (unpaid)                              │   │ payment recorded
                         item Failed, batch cancelled,────┘   ▼                                               │   ▼
                         batch regenerated              Reversed (+ zero-sum negative leg, status Reversed)   │  Paid
                                                                                                              │   │
     Scheduled earnings cannot be reversed directly (409 ledger.in_payout_batch; see "Reversing …" below) ┘   │ reverse (paid)
                                                                                                                  ▼
                                                       new negative Reversal entry, status Approved, AvailableAt = now
                                                       (a clawback netted against the participant's next payout)
```

* **AvailableAt** = approval time + `EarningHoldDays` (reversal buffer, default 3 days). Debits (negative adjustments)
  and clawbacks are available immediately so they net against the very next payout.
* Status transitions made by the payout module are conditional bulk updates (`ExecuteUpdate … WHERE Status = expected`)
  that also rotate the row's `ConcurrencyStamp`, so a concurrent tracked update (e.g. a reversal) fails with 409
  instead of silently overwriting.

### Reversing an earning that is already in a payout batch

`ILedgerWriter.ReverseAsync` refuses `Scheduled` earnings. The flows that must reverse them handle the batch first
(through `IPayoutReversalCoordinator`, implemented by the Payouts module, in the caller's transaction):

* **Submission reversal** (`ReviewService`, also a failed live check): if an earning of the submission is Scheduled in a
  **Draft** batch, the participant's item is held automatically (reason "Submission reversal pending"). Holding for a
  reversal releases *all* the item's earnings back to `Approved` (exactly what finalize does to held items), then the
  submission's earnings are reversed; the participant's other earnings roll into the next batch. If the item is in a
  **Finalized** batch the reversal is refused (409 `ledger.in_payout_batch`) with a message telling the reviewer to have
  finance mark the payout item failed first, then reverse again.
* **Referral rejection** (`SubmissionReversed` handler and marketing rejection): Paid → clawback via `ReverseAsync`;
  Scheduled in a Draft batch → item held ("Referral reward reversal pending") and reward reversed; Scheduled in a
  Finalized batch → audit note `referral.reward_pending_reversal` (PendingReversal) and a `pending_reversal`
  reconciliation warning on the item until finance reverses it. The referral is always set to `Rejected` with a reason.
  Details in [GROWTH.md](GROWTH.md#undoing-a-reward).
* A draft item held this way has no linked earnings; it cannot be unheld (409 `payout.item_released`) — regenerate the
  batch instead (the hold reason is carried over to the regenerated item).

### Balance buckets (participant summary)

All in the settlement currency of the active schedule (entries settled in an older currency only appear in `byCurrency`):

| Bucket | Definition |
|---|---|
| `pending` | Estimated rewards of the participant's submissions in `Pending`/`UnderReview` (converted with the current rate; currencies without a rate are listed in `pendingByCurrency` with `converted:false` and not added) **+** `PendingApproval` entries. |
| `approved` | `Approved` entries (never in a batch), credits and clawbacks netted. |
| `onHold` | Part of `approved` whose `AvailableAt` is still in the future. |
| `scheduled` | `Scheduled` entries (in a draft or finalized batch, not yet paid). |
| `paid` | `Paid` entries (net of clawbacks recovered through a payout). |
| `reversed` | Each reversed earning counted once: absolute value of Reversal legs + `Reversed` entries without a leg. |
| `availableForNextPayout` | `Approved` entries with `AvailableAt` ≤ next cutoff (net). |
| `lifetimeEarned` | Net of `Approved` + `Scheduled` + `Paid`. |
| `nextPayout.estimatedAmount` | `availableForNextPayout` if it meets the minimum and there is no active hold, else 0. |

## 2. Payout schedule and periods

The schedule is versioned (`payout_schedules`): a change inserts a new row with `EffectiveFrom ≥ now`; the latest row
whose `EffectiveFrom` has passed is active. The Baseline seed inserts the default (biweekly, cutoff Sunday 23:59:59
UTC anchored on 2026-01-04, payment 5 days after cutoff, minimum 10 USD, hold 3 days, auto-prepare on).

Period arithmetic is pure code in `Domain/Payouts/PayoutPeriodCalculator.cs`:

* **Weekly / Biweekly** — cutoff local dates are `AnchorCutoffDate + k·7` / `+ k·14` days for any integer k (also
  before the anchor).
* **Monthly** — the cutoff falls on the anchor's day of month, clamped to the month length (anchor 31 → 30 Apr,
  28 Feb, 29 Feb in leap years; March is 31 again).
* **Cutoff instant** — the cutoff local date at `CutoffLocalTime` in the schedule's IANA `TimeZone`, converted to UTC.
  * Spring-forward gap (the local time does not exist): the first valid instant after the gap, i.e. the moment the
    clocks jump (New York 02:30 on 2026-03-08 → 03:00 EDT = 07:00Z).
  * Fall-back ambiguity (the local time happens twice): the **later** occurrence (New York 01:30 on 2026-11-01 →
    06:30Z, not 05:30Z), so no earning is ever cut off early.
* **Period** = (previous cutoff, this cutoff]. An instant equal to the cutoff belongs to that period; one tick later
  belongs to the next. An earning belongs to the first period whose cutoff ≥ its `AvailableAt`.
* **PaymentDate** = cutoff local date + `PaymentDelayDays`. **PeriodKey** = cutoff local date as `yyyy-MM-dd`.
* The "last completed period" is the latest period whose cutoff is strictly before now.

Changing the **settlement currency** is refused (409 `payout.settlement_currency_in_use`) while unpaid earnings
(`PendingApproval`, `Approved`, `Scheduled`) are settled in the old currency — pay, reverse or decline them first.

## 3. Payout batches

### Batch lifecycle

```
            prepare (API or job)          finalize (4-eyes)              every item Paid/Failed/Held/Cancelled
  (none) ─────────────────────► Draft ───────────────────► Finalized ───────────────────────────────────► Completed
                                 │  ▲                          │
                 hold/unhold item│  │regenerate                │ cancel (no Paid item, nothing exported or in flight)
                                 ▼  │                          ▼
                                Draft                      Cancelled ◄──── cancel (Draft)
```

Item statuses: `Pending` (draft) → `AwaitingPayment` (finalized) → `Paid` | `Failed`; `Held` (excluded by finance or
missing payout details; its earnings are released at finalize); `Cancelled`.

**Generating or finalizing a batch never marks anything paid.** Only a recorded payment (or a provider confirmation
going through the same code path) sets an item and its earnings to `Paid`.

### Preparation

`PayoutBatchService.PrepareAsync(period, actor)` — used by `POST /finance/payout-batches/prepare` and by
`PayoutPreparationJob` (every 15 minutes; only when the active schedule has `AutoPrepareBatches`; prepares the last
completed period if **no batch in any status** exists for it; `PreparedByUserId = null`).

1. Serialization: MySQL named lock `GET_LOCK('oa:payout-prepare:<database>', 30)` on an explicitly opened connection
   (scoped to the database name so staging/test databases on one server do not block each other), released with
   `RELEASE_LOCK` on the same connection. **Creating a payout hold takes the same lock** (on its own transaction's
   connection, before it reads anything): a hold placed while a batch is being prepared waits until the draft is
   committed and then holds the new item; a preparation that starts after the hold sees it and excludes the participant.
2. Idempotency: `IdempotencyKey = period:{periodKey}:{currency}` (unique index = final guard). If a batch exists, it is
   returned with `created:false` (HTTP 200) — retries and concurrent calls never create a second batch.
3. Selection: `Status = Approved AND PayoutItemId IS NULL AND AvailableAt ≤ cutoff AND SettlementCurrency = batch currency`,
   including negative clawbacks and debits.
4. Grouping (`Domain/Payouts/PayoutPlanner.cs`), per participant, net = Σ settlement amounts, in this order:
   active payout hold → excluded (`PayoutHold`); account suspended/deactivated → excluded (`AccountInactive`);
   net ≤ 0 → carried over (`NonPositiveBalance`); net < minimum → carried over (`BelowMinimum`); no payout profile →
   item created as `Held` with reason "No payout details on file"; otherwise a `Pending` item.
   Exclusions are stored on the batch (`ExclusionsJson`) and shown on the review screen.
5. Each item's earnings are attached with ONE conditional update:
   `UPDATE earning_entries SET Status='Scheduled', PayoutItemId=@item WHERE Id IN (…) AND Status='Approved' AND PayoutItemId IS NULL`.
   If the affected row count differs (an earning was reversed or claimed meanwhile) the whole transaction rolls back
   with 409 `payout.earnings_changed`.
6. Totals: `TotalAmount`/`ItemCount` cover payable items (not `Held`/`Cancelled`). Audit `payout.batch_prepared`
   (system or user) and an in-app `payout.batch_prepared` notification to every active Finance user.
7. Reference `PB-{periodKey}` (plus `-{currency}` when not USD; `-R2`, `-R3`… if a cancelled batch already used it).

If nothing is payable the batch is not created (409 `payout.no_eligible_earnings`; the job just logs it).

### Review (Draft)

* Warnings: items held for missing payout details, participants with open appeals, open dispute/payout support
  tickets, high-risk submissions (risk score ≥ setting `fraud.highRiskThreshold`, default 50) submitted in the period
  or included in the batch, and the exclusions list with amounts.
* Hold/unhold an item, regenerate (releases everything and re-runs selection for the same period on the same row; the
  regenerating user becomes the preparer), cancel. Every draft change rotates the batch `ConcurrencyStamp`, so a
  finalize based on a stale review is rejected.
* Regenerate preserves manual item holds: each participant whose item was `Held` (for any reason other than "No payout
  details on file", which the planner re-derives) gets a new item that is `Held` with the same reason.
* Placing a payout hold on a participant automatically holds their `Pending` items in draft batches. Items already
  `AwaitingPayment` are listed in the hold response for finance to decide.

### Finalize

`POST …/{id}/finalize {confirm:true, concurrencyStamp}` with `payouts.finalize`:

* Four-eyes: the finalizer must not be the person who prepared (or last regenerated) the batch (403
  `payout.self_finalize`). System-prepared batches can be finalized by any finance user without a conflict of interest.
* Conflict of interest (403 `payout.conflict_of_interest`): the finalizer must not be the beneficiary of any item in the
  batch, nor the creator or approver of any earning linked to it.
* `UPDATE payout_batches SET Status='Finalized' … WHERE Id=@id AND Status='Draft' AND ConcurrencyStamp=@stamp` —
  of two simultaneous finalizes exactly one wins; the other gets 409.
* Same transaction: every `Pending` item whose participant now has an active payout hold or an account that is not
  Active is moved to `Held` (reason `Payout hold: <hold reason>` / `Account not active (<status>)`, audited
  `payout.item_held`) — a safety net for holds and suspensions after (or racing with) preparation. Then `Held` items
  release their earnings (back to `Approved`, unlinked), `Pending` items become `AwaitingPayment`, totals are
  recomputed, participants are notified (`payout.scheduled`, in-app + email, with amount and expected payment date).
* After commit every `AwaitingPayment` item is dispatched through the payment provider layer.

### Segregation of duties

No single person can both create money and pay it out. The matrix below lists who may **not** perform each step
(everything not listed is allowed to any user holding the permission). "System" means the background job.

| Step (permission) | Refused to | Error |
|---|---|---|
| Positive adjustment (`ledger.adjust`) | Anyone, for their own account. The credit is created `PendingApproval` and is not payable until approved. | 403 `ledger.self_adjustment` |
| Negative adjustment / debit (`ledger.adjust`) | Anyone, for their own account. Otherwise immediate (`Approved`). | 403 `ledger.self_adjustment` |
| Approve / decline a pending earning, incl. credits (`rewards.approve_bonus`) | The earning's creator; the earning's beneficiary. | 403 `ledger.self_approval` |
| Prepare / regenerate a batch (`payouts.prepare`) | — (the regenerating user becomes the preparer). | — |
| Finalize (`payouts.finalize`) | The preparer / last regenerator; the beneficiary of any item in the batch; the creator or approver of any earning in the batch. | 403 `payout.self_finalize`, 403 `payout.conflict_of_interest` |
| Record a payment (`payouts.record_payment`), batch prepared by a **person** | — (preparer ≠ finalizer is already enforced, so the finalizer may record). | — |
| Record a payment, batch prepared by the **system** | The finalizer (there was no human preparer, so the recorder is the second pair of eyes). | 403 `payout.self_record` |
| Record a payment for a participant with an active payout hold | Everyone, unless an `overrideReason` is given (audited `payout.payment_hold_overridden`). | 409 `payout.user_on_hold` |
| Provider confirmation / failure (no human actor) | — (the money already moved; recorded through the same atomic path). | — |

Resulting minimum for a manual credit to leave the bank: creator ≠ approver ≠ beneficiary, then preparer ≠ finalizer
(and the finalizer is none of creator, approver or beneficiary); for system-prepared batches, finalizer ≠ recorder.

## 4. Double-payment protections

| Risk | Protection |
|---|---|
| Two batches for one period (job retry, two finance users, two instances) | Named lock + unique `IdempotencyKey`; existing batch returned. |
| An earning in two items / two batches | Conditional attach `WHERE Status='Approved' AND PayoutItemId IS NULL`; affected-row check; one FK column per earning. |
| Earning reversed while being batched | Conditional attach fails → whole preparation rolls back (409); bulk updates rotate `ConcurrencyStamp` so a stale reversal fails with 409. Scheduled earnings cannot be reversed. |
| Two finalizes | Conditional `Draft→Finalized` with the reviewed stamp. |
| Two finance users (or a retry, or a provider webhook) recording one payment | Batch row lock (`SELECT … FOR UPDATE`) + conditional `AwaitingPayment→Paid`; the loser gets 409 `payout.already_recorded`; earnings `Scheduled→Paid` checked against the item's earning count. |
| Duplicate provider dispatch | One `PaymentAttempt` per item, key `payout-item:{itemId}:dispatch` (unique); retries reuse it and never call the provider again once it has an outcome. |
| Paying a user twice for one period across batches | Reconciliation check `duplicate_period_payment`. |
| The same earning paid by two items, or the same earning set paid in two batches (e.g. two periods) | Every recorded payment writes immutable `payout_item_earnings` rows (item, earning, amount, paidAt); reconciliation checks `duplicate_earning_payment`, `paid_earning_linked_elsewhere`, `duplicate_payment_across_periods`. |
| Reusing a bank reference by mistake | Reconciliation warning `duplicate_payment_reference`. |
| A hold created while a batch is being prepared | Hold creation takes the prepare named lock; finalize re-checks holds and account status and holds affected items. |
| Cancelling a batch whose money may already be moving | Cancel refused once instructions were exported or an attempt is `Submitted`/`Succeeded` (409 `payout.instructions_exported` / `payout.attempt_in_flight`). |

## 5. Recording payments and failures

* `record-payment {paymentReference, paidAt ≤ now}` → item `Paid`, its earnings `Paid` with `PaidAt`, the payment
  attempt `Succeeded` with the reference, audit `payout.payment_recorded`, participant notified (`payout.paid`, in-app +
  email, reference shown by its last 4 characters), event `PayoutItemPaid` published after commit. When no item is
  `Pending`/`AwaitingPayment` any more the batch becomes `Completed`.
* `record-payments` (bulk) processes each line independently with the same rules and returns
  `recorded | already_recorded | invalid` per line.
* Record-payment refusals: 409 `payout.user_on_hold` when the participant has an active payout hold (unless
  `overrideReason` is given — e.g. the transfer left before the hold was placed; audited
  `payout.payment_hold_overridden`), 403 `payout.self_record` for the finalizer of a system-prepared batch.
* `mark-failed {reason}` → item `Failed`, earnings back to `Approved` (unlinked) so they roll into the next batch;
  participant told to check their payout details. Refused while the item's payment attempt is `Submitted` to a provider
  without an outcome (409 `payout.attempt_in_flight`); the provider's own failure callback is still accepted.
* `payment-instructions.csv` → payable items with decrypted destinations; items of participants with an active hold
  or an inactive account are **companion rows** with `Status = EXCLUDED` and the destination replaced by
  `EXCLUDED — do not pay: <reason>`. The first export sets `InstructionsExportedAt` on the batch (shown in the batch DTO
  as `instructionsExportedAt`).
* `cancel` (Draft, or Finalized with zero Paid items) → all open items `Cancelled`, their earnings back to `Approved`,
  open payment attempts `Failed`, the period's idempotency key is released (the job will not re-create it; finance can
  prepare it again deliberately). A Finalized batch can **not** be cancelled once money may be in flight: any attempt
  `Submitted` (409 `payout.attempt_in_flight`), any attempt `Succeeded` or payment instructions already exported (409
  `payout.instructions_exported`). Use per-item `mark-failed` with a reason instead.

## 6. Reconciliation

`GET /finance/payout-batches/{id}/reconciliation` (and `.csv`) returns expected (Σ items not Held/Cancelled),
recorded paid, awaiting, failed, held and cancelled totals, a per-item table and `discrepancies[]`:

| Type | Severity | Meaning |
|---|---|---|
| `item_amount_mismatch` | error | Item amount ≠ Σ settlement amounts of its linked earnings (rounded). |
| `earning_count_mismatch` | error | Item's `EarningCount` ≠ number of linked earnings. |
| `paid_item_unpaid_earning` | error | Paid item with an earning not `Paid`. |
| `unpaid_item_paid_earning` | error | Unpaid item with a `Paid` earning. |
| `earning_status_mismatch` | error | Linked earning not `Scheduled` on an open item. |
| `released_item_has_earnings` | error | `Failed`/`Cancelled` item — or a `Held` item of a Finalized/Completed batch — still has linked earnings. |
| `currency_mismatch` | error | Earning settled in another currency than the item. |
| `duplicate_period_payment` | error | Participant also paid for the same period in another batch. |
| `duplicate_earning_payment` | error | An earning was paid by more than one item (paid-earning rows, plus its current link when that item is Paid). |
| `paid_earning_linked_elsewhere` | error | An earning paid by an item of this batch is now linked to another item (it could be paid again). |
| `duplicate_payment_across_periods` | error | The participant was paid exactly the same earning set by an item of another batch (e.g. another period). |
| `batch_total_mismatch` | error | Batch total ≠ Σ payable items. |
| `duplicate_payment_reference` | warning | Same payment reference on several items. |
| `pending_reversal` | warning | The item contains the reward of a rejected referral that could not be reversed because the batch was finalized (PendingReversal); finance must fail the item and reverse the reward, or reverse it after payment (clawback). |

Expected state per item status: `Pending`/`AwaitingPayment`/`Paid` items (and `Held` items of a Draft batch that still
hold their earnings) must have amount = Σ linked earnings and count = linked earnings; in Finalized/Completed batches
`Held` and `Failed` items (and `Cancelled` items anywhere) are expected to have **zero** linked earnings, since finalize
and mark-failed released them. A Draft `Held` item with no linked earnings was held for a reversal and is fine.

`isBalanced` is true when there is no error-severity discrepancy. Each earning links to at most one item at a time
(single `PayoutItemId` column); the history of which item paid which earning is kept in `payout_item_earnings`.

## 7. Finance runbook (manual payments)

1. **After each cutoff** a draft batch appears (notification "Payout batch PB-… is ready for review"), or prepare it
   under Finance → Payouts → Prepare.
2. **Review** the batch: check warnings (missing payout details, appeals, disputes, high-risk submissions) and the
   exclusions list. Hold items you are not comfortable paying (they return to the participant's balance at finalize),
   or place a payout hold on the participant. Regenerate after changing holds/earnings if needed.
3. **Finalize** — must be done by a second finance user who is not paid in the batch and did not create or approve any
   of its earnings. Items of participants who were put on hold or suspended since preparation are held automatically.
   Participants are told their payout is scheduled.
4. **Download payment instructions** (`payment-instructions.csv?confirm=true`, permission `payouts.record_payment`).
   The file contains decrypted bank/wallet details — store it only in the approved secure location and delete it after
   use. Every download is audited. **Never pay rows marked `EXCLUDED`** (participant on hold or inactive). After the
   first download the batch can no longer be cancelled.
5. **Pay** each participant in the bank/PayPal/wallet portal, using the batch reference and item id in the payment
   description where possible.
6. **Record** each payment with the bank/PayPal transaction reference and the actual payment date (single or bulk).
   Never record a payment that has not left the account. If a transfer bounces, **mark the item failed** with the
   bank's reason; the earnings return to the participant's balance for the next batch. For a batch prepared by the
   system job, payments must be recorded by someone other than the finalizer. Recording a payment for a participant
   on hold requires an override reason.
7. **Reconcile**: open the reconciliation report, confirm `isBalanced`, compare `recordedPaid` with the bank statement,
   investigate any warning (duplicate references, `pending_reversal`). Export the CSV for the accounting archive.
8. Mistakes: a wrongly recorded payment cannot be "un-paid" in the UI (by design). Record a compensating adjustment
   (with the support ticket) and document it; for an unpaid batch prepared in error, cancel it.

## 8. Payment provider integration layer

`Api/Modules/Payouts/Providers/`:

* `IPaymentProvider { Key; Capabilities (SupportsAutomaticTransfer, SupportsWebhooks); DispatchAsync(PaymentDispatchRequest) }`
* `PaymentDispatchRequest { PayoutItemId, UserId, Amount, Currency, IdempotencyKey, DestinationMethod, MaskedDestination }`
* `PaymentDispatchResult { Status (PaymentAttemptStatus), ProviderReference, Message }`
* `ManualPaymentProvider` (`"manual"`) always returns `RequiresManualAction` — "Pay manually and record the payment
  reference". It never reports `Succeeded`.
* `IPaymentProviderRegistry` resolves the active provider from `Payments:Provider` (default `manual`). An unknown key
  fails options validation at startup.
* `PaymentDispatcher` creates one `PaymentAttempt` per item (`payout-item:{itemId}:dispatch`, unique). It inserts the
  attempt in `Created` state first, then calls the provider; a retry reuses the attempt and only calls the provider
  again while the attempt is still `Created` (crash between insert and call). `Succeeded` → the payment is recorded
  through `PayoutPaymentService.RecordPaymentAsync` (the same atomic path as a manual entry); `Failed` →
  `MarkFailedAsync`. `POST …/{id}/dispatch` re-dispatches items awaiting payment.

### Adding a real payment provider

1. Obtain a provider account (e.g. Wise Business API, PayPal Payouts), API credentials and webhook signing secrets;
   store them as secrets (never in appsettings). **None of this exists today.**
2. Implement `IPaymentProvider` (e.g. `WisePaymentProvider`, `Key = "wise"`,
   `Capabilities = new(SupportsAutomaticTransfer: true, SupportsWebhooks: true)`). In `DispatchAsync`, decrypt the
   destination with `IDataProtectionProvider.CreateProtector("OptimizeAll.PayoutProfile.Destination.v1")`, create the
   transfer passing `request.IdempotencyKey` as the provider's idempotency key, and return `Submitted` (accepted,
   awaiting confirmation), `Succeeded` (only if the provider confirms the money was sent synchronously) or `Failed`.
3. Register it: `services.AddSingleton<IPaymentProvider, WisePaymentProvider>()` in `PayoutsModule`, set
   `Payments:Provider=wise`. New batches record `PaymentProvider = "wise"` per item.
4. Webhooks: add an anonymous controller that verifies the provider signature, looks up the `PaymentAttempt` by the
   provider reference or idempotency key, and calls `PayoutPaymentService.RecordPaymentAsync(batchId, itemId,
   providerReference, paidAt, note, actor: null)` for a confirmed transfer or `MarkFailedAsync` for a failure. These
   are the same conditional updates used by finance, so a webhook racing a manual entry (or a redelivered webhook)
   cannot pay twice — the loser gets `payout.already_recorded`, which the webhook handler must treat as success.
5. Keep the idempotency keys unchanged and add a reconciliation job that polls the provider for attempts stuck in
   `Submitted`.
