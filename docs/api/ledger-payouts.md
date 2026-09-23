# Ledger & Payouts API

Base path `/api/v1`. JSON is camelCase, enums are strings, timestamps are UTC ISO-8601, `DateOnly` values are
`"yyyy-MM-dd"`, money is a JSON number with its currency alongside. Errors are RFC 7807 problems with `code` and
`traceId`; validation errors (400) carry `errors`. See [`../PAYOUTS.md`](../PAYOUTS.md) for the business rules.

Common error codes on every endpoint: `401` (not signed in), `403 auth.forbidden` / plain 403 (missing permission),
`404 <entity>.not_found`, `409 concurrency.conflict` (stale `concurrencyStamp`), `400 request.confirm_required`
(sensitive action without `"confirm": true`).

Paged lists accept `?page=1&pageSize=25&search=` (pageSize ≤ 200) and return:

```json
{ "items": [ ... ], "total": 42, "page": 1, "pageSize": 25, "totalPages": 2 }
```

## Shared shapes

```jsonc
// CampaignRef / UserRef / PayoutUser
{ "id": "guid", "title": "Spring launch" }
{ "id": "guid", "displayName": "Sara Khan", "email": "sara@example.com" }
{ "id": "guid", "displayName": "Sara Khan", "email": "sara@example.com", "country": "PK" }

// Earning (participant view)
{
  "id": "guid", "createdAt": "2026-09-23T13:00:00Z",
  "type": "PostReward|FirstPostBonus|TimeLimitedBonus|QualityBonus|ReferralReward|Adjustment|Reversal",
  "status": "PendingApproval|Approved|Scheduled|Paid|Reversed|Declined",
  "description": "Post reward", "campaign": { "id": "guid", "title": "..." } /* or null */,
  "submissionId": "guid|null",
  "originalAmount": 10.00, "originalCurrency": "EUR", "exchangeRate": 1.1,
  "settlementAmount": 11.00, "settlementCurrency": "USD",
  "ruleSetVersion": 3, "availableAt": "…Z|null", "paidAt": "…Z|null", "reason": "string|null"
}

// LedgerRow (finance view) = Earning fields plus:
{
  "user": { "id", "displayName", "email" }, "exchangeRateId": "guid|null", "approvedAt": "…|null",
  "approvedByUserId": "guid|null", "createdByUserId": "guid|null", "payoutItemId": "guid|null",
  "reversedAt": "…|null", "reversesEntryId": "guid|null", "reversedByEntryId": "guid|null",
  "concurrencyStamp": "guid"
}
```

(LedgerRow field order: `id, createdAt, user, type, status, description, campaign, submissionId, originalAmount,
originalCurrency, exchangeRate, exchangeRateId, settlementAmount, settlementCurrency, ruleSetVersion, availableAt,
approvedAt, approvedByUserId, createdByUserId, payoutItemId, paidAt, reversedAt, reversesEntryId, reversedByEntryId,
reason, concurrencyStamp`.)

```jsonc
// EarningsSummary
{
  "currency": "USD",
  "pending": 15.00, "approved": 15.00, "onHold": 15.00, "scheduled": 0, "paid": 20.00, "reversed": 4.00,
  "availableForNextPayout": 15.00, "lifetimeEarned": 35.00,
  "nextPayout": {
    "periodKey": "2026-09-27", "cutoffAt": "2026-09-27T23:59:59Z", "paymentDate": "2026-10-02",
    "minimumPayoutAmount": 10.00, "meetsMinimum": true, "estimatedAmount": 15.00
  },
  "activeHold": false, "holdMessage": null,
  "pendingByCurrency": [ { "currency": "EUR", "amount": 5.00, "converted": false } ],
  "byCurrency": [ { "currency": "USD", "pendingApproval": 6, "approved": 15, "scheduled": 0, "paid": 20, "reversed": 4 } ]
}
```

Bucket definitions: see PAYOUTS.md §1 "Balance buckets". `holdMessage` is neutral and never reveals the hold reason.

---

## Participant

### `GET /me/earnings/summary` — `participant.portal`
Returns **EarningsSummary** for the caller.

### `GET /me/earnings` — `participant.portal`
Query: `type`, `status`, `campaignId`, `from` (inclusive, CreatedAt), `to` (exclusive), paging. Newest first.
Returns `Paged<Earning>` (caller's entries only).

### `GET /me/payouts` — `participant.portal`
Paged history of the caller's payout items in finalized/completed batches with status `AwaitingPayment`, `Paid` or
`Failed` (draft, held and cancelled items are not shown).

```json
{ "items": [ {
  "itemId": "guid", "batchReference": "PB-2026-09-27", "periodKey": "2026-09-27",
  "cutoffAt": "2026-09-27T23:59:59Z", "paymentDate": "2026-10-02", "amount": 20.00, "currency": "USD",
  "status": "AwaitingPayment|Paid|Failed", "paidAt": "…|null", "paymentReference": "••••3456|null", "earningCount": 1
} ], "total": 1, "page": 1, "pageSize": 25, "totalPages": 1 }
```

### `GET /me/payouts/{itemId}` — `participant.portal`
`{ "payout": MyPayout, "earnings": [Earning] }`. Earnings of a `Failed` item were released back to the balance and
are no longer listed under it. 404 for other users' items.

---

## Finance — ledger

### `GET /finance/ledger` — `ledger.view`
Query: `userId`, `search` (email, name, description, or an exact id of earning/user/submission/payout item),
`campaignId`, `type`, `status`, `from`, `to`, paging. Returns `Paged<LedgerRow>`, newest first.

### `GET /finance/ledger/export.csv` — `ledger.view`
Same filters, max 100,000 rows. Columns: Earning ID, Created at (UTC), User ID, User email, User name, Type, Status,
Description, Campaign, Submission ID, Original amount, Original currency, Exchange rate, Settlement amount, Settlement
currency, Rule version, Available at (UTC), Approved at (UTC), Payout item ID, Paid at (UTC), Reverses earning ID,
Reversed by earning ID, Reason.

### `GET /finance/users/{userId}/balance` — `ledger.view`
**EarningsSummary** for any user. 404 `user.not_found`.

### `POST /finance/adjustments` — `ledger.adjust`
```json
{ "requestId": "guid (required, client-generated)", "userId": "guid", "amount": -8.00, "currency": "USD",
  "reason": "≥ 10 chars", "submissionId": "guid|null", "supportTicketId": "guid|null", "confirm": true }
```
Creates an `Adjustment` entry. **Credits** (amount > 0) are created `PendingApproval` and appear in
`GET /finance/pending-earnings`; they become payable only after a **different** user approves them there (approver ≠
creator ≠ beneficiary), then wait for the hold period. **Debits** (amount < 0) are `Approved` immediately and available
at once. Idempotency key `adjustment:{requestId}`: **201** `{ "created": true, "earning": LedgerRow }` when created,
**200** `{ "created": false, "earning": LedgerRow }` for a replay. Audited `ledger.adjustment_created` (with the status).
Errors: 400 (validation, `ledger.zero_amount`, `ledger.currency_unsupported`, `ledger.submission_mismatch`,
`ledger.ticket_mismatch`), 403 `ledger.self_adjustment` (`userId` is the caller: nobody credits or debits their own
account), 404 user/submission/ticket, 409 `ledger.request_id_reused` (same requestId, different payload), 409
`fx.rate_missing` (no rate to the settlement currency).

### `POST /finance/earnings/{id}/reverse` — `ledger.adjust`
`{ "reason": "≥ 10 chars", "confirm": true }` → `{ "original": LedgerRow, "reversal": LedgerRow }`.
Unpaid → original `Reversed` + zero-sum leg (`Reversed`). Paid → negative `Reversal` entry `Approved` (clawback netted
in the next batch). Errors: 409 `ledger.in_payout_batch` (Scheduled), `ledger.already_reversed`,
`ledger.cannot_reverse_reversal`, `ledger.cannot_reverse_debit`, `ledger.declined`. Audited `ledger.earning_reversed`.

### `GET /finance/pending-earnings` — `rewards.approve_bonus`
Query: `type`, `search`, paging (oldest first). Returns `Paged<PendingEarning>`:
```json
{ "id": "guid", "createdAt": "…", "type": "QualityBonus", "description": "…",
  "user": UserRef, "campaign": CampaignRef|null, "submissionId": "guid|null", "referralId": "guid|null",
  "originalAmount": 8, "originalCurrency": "USD", "settlementAmount": 8, "settlementCurrency": "USD",
  "createdByUserId": "guid|null", "awaitingLiveCheck": false, "liveCheckDueAt": "…|null",
  "submissionRiskScore": 10, "concurrencyStamp": "guid" }
```

### `POST /finance/pending-earnings/{id}/approve` — `rewards.approve_bonus`
`{ "concurrencyStamp": "guid" }` → `LedgerRow` (status `Approved`, `availableAt` set). Participant notified
(`earning.approved`). Errors: 403 `ledger.self_approval` (the caller created the entry **or** is its beneficiary),
409 `ledger.awaiting_live_check`, 409 `ledger.not_pending`, 409 `concurrency.conflict`.

### `POST /finance/pending-earnings/{id}/decline` — `rewards.approve_bonus`
`{ "reason": "≥ 5 chars", "concurrencyStamp": "guid" }` → `LedgerRow` (status `Declined`). Same four-eyes/409 rules
(403 `ledger.self_approval` for the creator or the beneficiary).

### `GET /finance/exchange-rates` — `payouts.view`
Query: `base`, `quote`, paging. `Paged<ExchangeRate>`:
`{ "id", "baseCurrency": "EUR", "quoteCurrency": "USD", "rate": 1.08, "effectiveAt", "source", "createdAt", "createdByUserId" }`
(1 base = rate quote). A conversion from→to takes the latest direct (from→to) and the latest inverse (to→from) row
effective at that time and uses whichever has the later `effectiveAt` (inverse = 1/rate, 8 decimals; on an exact tie
the direct row wins).

### `POST /finance/exchange-rates` — `payouts.settings`
`{ "baseCurrency", "quoteCurrency", "rate": "> 0, ≤ 1,000,000 (8 decimals)", "effectiveAt": "≥ now − 1 day", "source": "2–50", "reason": "≥ 10", "confirm": true }`
→ **201** `ExchangeRate`. Rows are immutable; a rate applies to earnings created after `effectiveAt`; existing
earnings keep their stored rate. Errors: 400 `fx.same_currency`, `fx.invalid_rate`, `fx.effective_too_early`,
`ledger.currency_unsupported`; 409 `fx.duplicate`. Audited `fx.rate_created`.

---

## Finance — payout schedule

### `GET /finance/payout-schedule` — `payouts.view`
```json
{
  "current": PayoutSchedule,
  "currentPeriod": PayoutPeriod, "lastCompletedPeriod": PayoutPeriod,
  "upcoming": [PayoutPeriod × 6],            // current period first
  "scheduledChanges": [PayoutSchedule],     // versions with effectiveFrom in the future
  "history": [PayoutSchedule]               // all versions, newest first
}
// PayoutSchedule
{ "id": "guid|null", "frequency": "Weekly|Biweekly|Monthly", "anchorCutoffDate": "2026-01-04",
  "cutoffLocalTime": "23:59:59", "timeZone": "UTC", "paymentDelayDays": 5, "minimumPayoutAmount": 10,
  "settlementCurrency": "USD", "earningHoldDays": 3, "autoPrepareBatches": true, "effectiveFrom": "…|null",
  "createdAt": "…|null", "createdByUserId": "guid|null", "changeReason": "Initial schedule", "isDefault": false }
// PayoutPeriod — earnings with availableAt in (periodStart, cutoffAt] are paid on/after paymentDate
{ "periodKey": "2026-09-27", "periodStart": "2026-09-13T23:59:59Z", "cutoffAt": "2026-09-27T23:59:59Z",
  "cutoffLocalDate": "2026-09-27", "paymentDate": "2026-10-02" }
```
`isDefault: true` (and `id: null`) only when no schedule row exists (code default).

### `PUT /finance/payout-schedule` — `payouts.settings`
```json
{ "frequency": "Biweekly", "anchorCutoffDate": "2026-01-04", "cutoffLocalTime": "HH:mm[:ss]",
  "timeZone": "IANA id", "paymentDelayDays": 0-30, "minimumPayoutAmount": 0-100000, "settlementCurrency": "USD",
  "earningHoldDays": 0-60, "autoPrepareBatches": true, "effectiveFrom": "≥ now (5 min tolerance)",
  "reason": "10–500 chars", "confirm": true }
```
Inserts a new version; returns the GET shape. Errors: 400 `payout.invalid_time_zone`, `payout.effective_in_past`,
`payout.invalid_anchor`, `ledger.currency_unsupported`; 409 `payout.settlement_currency_in_use` (unpaid
PendingApproval/Approved/Scheduled earnings exist in the current settlement currency). Audited `payout.schedule_changed`.

---

## Finance — payout holds (`payouts.hold`)

### `GET /finance/holds`
Query: `active` (true/false), `userId`, `search`, paging. `Paged<PayoutHold>`:
`{ "id", "user": UserRef, "reason", "createdAt", "createdByUserId", "isActive", "releasedAt", "releasedByUserId", "releaseNote" }`

### `POST /finance/holds`
`{ "userId": "guid", "reason": "5–1000" }` → **201**
`{ "hold": PayoutHold, "heldDraftItemIds": ["guid"], "awaitingPaymentItemIds": ["guid"] }` — Pending items of draft
batches are held automatically; items awaiting payment need a manual decision (they are excluded from payment
instructions and record-payment refuses them without an override). Serialized with batch preparation by the same
named lock (`GET_LOCK('oa:payout-prepare:<db>')`), so a hold placed while a batch is being prepared waits for it and
then holds the new draft item. Participant notified (`payout.hold`, neutral text). Errors: 404 user, 409
`payout.hold_exists`, 409 `payout.prepare_busy` (lock not obtained within 30 s). Audited `payout.hold_created`.

### `POST /finance/holds/{id}/release`
`{ "note": "optional" }` → `PayoutHold`. 409 `payout.hold_not_active`. Audited `payout.hold_released`; participant notified.

---

## Finance — payout batches

```jsonc
// PayoutBatchSummary
{ "id": "guid", "reference": "PB-2026-09-27", "periodKey": "2026-09-27", "periodStart": "…Z", "cutoffAt": "…Z",
  "paymentDate": "2026-10-02", "status": "Draft|Finalized|Completed|Cancelled",
  "itemCount": 2,            // payable items (not Held/Cancelled)
  "totalAmount": 47.00,      // Σ payable items
  "currency": "USD", "paidCount": 0, "paidAmount": 0,
  "preparedBy": UserRef|null /* null = system job */, "finalizedBy": UserRef|null,
  "createdAt": "…", "finalizedAt": "…|null", "completedAt": "…|null", "cancelledAt": "…|null",
  "instructionsExportedAt": "…|null" /* first payment-instructions export; the batch can no longer be cancelled */ }

// PayoutItem
{ "itemId": "guid", "user": PayoutUser, "amount": 25.00, "currency": "USD", "earningCount": 1,
  "status": "Pending|Held|AwaitingPayment|Paid|Failed|Cancelled", "paymentProvider": "manual",
  "destinationHint": "••••6702|null", "paymentReference": "…|null", "paidAt": "…|null",
  "holdReason": "…|null", "failureReason": "…|null", "concurrencyStamp": "guid" }

// PayoutExclusion (reason: PayoutHold|AccountInactive|NonPositiveBalance|BelowMinimum)
{ "user": PayoutUser, "reason": "BelowMinimum", "amount": 5.00, "earningCount": 1 }
```

### `POST /finance/payout-batches/prepare` — `payouts.prepare`
`{ "periodKey": "yyyy-MM-dd (optional; default last completed period)", "note": "optional" }` →
**201** (created) or **200** (already existed): `{ "created": true, "batch": PayoutBatchSummary, "exclusions": [PayoutExclusion] }`.
Errors: 400 `payout.invalid_period`, `payout.period_not_completed`; 409 `payout.no_eligible_earnings`,
`payout.earnings_changed` (retry), `payout.prepare_busy`.

### `GET /finance/payout-batches` — `payouts.view`
Query: `status`, `search` (reference/period), paging. `Paged<PayoutBatchSummary>`, newest cutoff first.

### `GET /finance/payout-batches/{id}` — `payouts.view`
Query (items paging): `itemStatus`, `search` (participant email/name), `page`, `pageSize` (≤ 200; items ordered by amount desc).
```json
{
  "batch": PayoutBatchSummary, "concurrencyStamp": "guid", "notes": "…|null", "cancelReason": "…|null",
  "totalsByStatus": [ { "status": "Pending", "count": 2, "amount": 47.00 } ],
  "warnings": {
    "missingPayoutDetails": [ { "user": PayoutUser, "itemId": "guid|null", "detail": "No payout details on file (15.00 USD held)." } ],
    "openAppeals": [UserWarning], "openDisputes": [UserWarning], "highRiskSubmissions": [UserWarning],
    "highRiskThreshold": 50,
    "exclusions": [PayoutExclusion]
  },
  "items": Paged<PayoutItem>
}
```
Pass `concurrencyStamp` to finalize; any draft change (hold/unhold/regenerate/participant hold) rotates it.

### `GET /finance/payout-batches/{id}/items/{itemId}` — `payouts.view`
```json
{ "batchId", "batchReference", "periodKey", "batchStatus", "item": PayoutItem, "earningsTotal": 25.00,
  "earnings": [ { "id", "type", "status", "description", "campaign": CampaignRef|null, "submissionId",
                  "originalAmount", "originalCurrency", "exchangeRate", "settlementAmount", "settlementCurrency",
                  "createdAt", "availableAt" } ],
  "paymentAttempts": [ { "id", "provider": "manual", "idempotencyKey": "payout-item:{itemId}:dispatch",
                         "status": "Created|Submitted|Succeeded|Failed|RequiresManualAction",
                         "providerReference", "message", "createdAt", "updatedAt" } ] }
```

### Draft operations — `payouts.prepare`
| Endpoint | Body | Response | Errors |
|---|---|---|---|
| `POST …/{id}/items/{itemId}/hold` | `{ "reason": "5–1000" }` | `PayoutItem` (Held) | 409 `payout.not_draft`, `payout.item_state` |
| `POST …/{id}/items/{itemId}/unhold` | `{ "note"?: "…" }` | `PayoutItem` (Pending) | 409 `payout.not_draft`, `payout.item_not_held`, `payout.item_released` (the item's earnings were released for a reversal — regenerate instead), `payout.no_payout_profile`, `payout.user_on_hold`, `payout.user_inactive` |
| `POST …/{id}/regenerate` | `{ "reason": "5–1000" }` | `PayoutBatchSummary` | 409 `payout.not_draft`, `payout.settlement_currency_changed`, `payout.earnings_changed` |
| `POST …/{id}/cancel` | `{ "reason": "5–1000", "confirm": true }` | `PayoutBatchSummary` (Cancelled) | 409 `payout.cannot_cancel` (Completed/Cancelled), `payout.has_paid_items`, `payout.attempt_in_flight`, `payout.instructions_exported` |

Regenerate keeps manual holds: a participant whose item was Held (any reason except "No payout details on file", which
the planner re-derives) gets a new item that is Held with the same reason.

Cancel is allowed for Draft batches, and for Finalized batches only while no money can be in flight: no Paid item, no
payment attempt `Submitted` (409 `payout.attempt_in_flight`), no attempt `Succeeded` and payment instructions never
exported (409 `payout.instructions_exported` — mark each unpaid item failed with a reason instead). Audited
`payout.item_held`, `payout.item_unheld`, `payout.batch_regenerated`, `payout.batch_cancelled`.

### `POST /finance/payout-batches/{id}/finalize` — `payouts.finalize`
`{ "confirm": true, "reason": "optional", "concurrencyStamp": "guid" }` →
```json
{ "batch": PayoutBatchSummary,
  "dispatch": [ { "itemId": "guid", "status": "RequiresManualAction", "providerReference": null,
                  "message": "Pay manually and record the payment reference", "reused": false } ] }
```
In the same transaction, before items move to `AwaitingPayment`, every `Pending` item whose participant has an active
payout hold or an account that is not Active is set to `Held` (reason `Payout hold: …` / `Account not active (…)`,
audited `payout.item_held`) and its earnings are released like any held item.
Errors: 403 `payout.self_finalize` (finalizer prepared or last regenerated the batch), 403 `payout.conflict_of_interest`
(finalizer is the beneficiary of an item, or created or approved an earning in the batch); 409 `concurrency.conflict`
(stale stamp), `payout.not_draft` (already finalized — the loser of two concurrent finalizes). Audited
`payout.batch_finalized`. Never marks anything paid.

### `POST /finance/payout-batches/{id}/dispatch` — `payouts.record_payment`
Re-dispatches `AwaitingPayment` items; existing attempts are reused (`reused: true`). Returns `[DispatchResult]`.
409 `payout.batch_not_finalized`.

### `POST /finance/payout-batches/{id}/items/{itemId}/record-payment` — `payouts.record_payment`
`{ "paymentReference": "3–120", "paidAt": "≤ now (5 min tolerance)", "note": "optional", "overrideReason": "optional, ≤ 1000" }` →
`{ "item": PayoutItem, "batchStatus": "Finalized|Completed" }`. The paid earnings are also recorded in
`payout_item_earnings` (used by reconciliation to detect repeated payments).
Errors: 400 `payout.paid_at_in_future`, `payout.invalid_reference`; 403 `payout.self_record` (the batch was prepared
by the system job and the caller finalized it); 409 `payout.user_on_hold` (the participant has an active payout hold
and no `overrideReason` was given — with one, the payment is recorded and audited `payout.payment_hold_overridden`),
`payout.already_recorded` (already paid — concurrent or retried request), `payout.item_not_awaiting_payment`,
`payout.batch_not_finalized`, `payout.earnings_changed`. Audited `payout.payment_recorded`; participant notified
(`payout.paid`); event `PayoutItemPaid` published. Provider confirmations (no human actor) skip the self-record and hold
checks: the money has already moved.

### `POST /finance/payout-batches/{id}/items/{itemId}/mark-failed` — `payouts.record_payment`
`{ "reason": "5–1000" }` → `{ "item": PayoutItem, "batchStatus" }`. Earnings return to `Approved`.
Errors: 409 `payout.already_recorded`, `payout.item_state`, `payout.batch_not_finalized`, `payout.attempt_in_flight`
(the item's payment attempt is `Submitted` to a provider without an outcome; the provider's own failure callback is
still accepted). Audited `payout.payment_failed`.

### `POST /finance/payout-batches/{id}/record-payments` — `payouts.record_payment`
Body: `[ { "itemId": "guid", "paymentReference": "…", "paidAt": "…" } ]` (1–1000 lines) →
`[ { "itemId": "guid", "status": "recorded|already_recorded|invalid", "message": "…" } ]`. Each line is atomic and
independent and follows the single record-payment rules (a held participant or a self-record is reported `invalid`;
overrides are only possible one item at a time).

### `GET /finance/payout-batches/{id}/export.csv` — `payouts.view`
Columns: Batch reference, Period, Item ID, Participant name, Email, Country, Amount, Currency, Earning count, Status,
Method, Account holder, Masked destination, Payment reference, Paid at (UTC).

### `GET /finance/payout-batches/{id}/payment-instructions.csv?confirm=true` — `payouts.record_payment`
Items `AwaitingPayment` only; same columns plus **Destination (confidential)** — decrypted with the Data Protection
purpose `OptimizeAll.PayoutProfile.Destination.v1`. Finalized/Completed batches only (409
`payout.batch_not_finalized`); 400 `request.confirm_required` without `confirm=true`.

**Excluded items.** Items whose participant has an active payout hold or an account that is not Active are not payable.
They are listed after the payable rows as **companion rows** whose `Status` cell is `EXCLUDED` and whose destination
cell is `EXCLUDED — do not pay: <reason>` (`Payout hold: …` or `Account not active (…)`); their destination is never
decrypted. Filter on `Status = EXCLUDED` to count them.

The first download sets the batch's `instructionsExportedAt` (after which the batch cannot be cancelled). Every download
is audited (`payout.payment_instructions_exported` with the payable and excluded item ids and counts — never
destinations).

### `GET /finance/payout-batches/{id}/reconciliation` — `payouts.view`
```json
{ "batchId", "reference", "periodKey", "status", "currency",
  "expected": 65.50, "recordedPaid": 25.50, "awaiting": 40.00, "failed": 0, "held": 0, "cancelled": 0,
  "paidCount": 1, "awaitingCount": 1, "isBalanced": true,
  "discrepancies": [ { "type": "item_amount_mismatch", "severity": "error|warning", "itemId": "guid|null",
                       "userId": "guid|null", "earningId": "guid|null", "message": "…" } ],
  "items": [ { "itemId", "user": PayoutUser, "status", "amount", "earningsTotal", "earningCount",
               "linkedEarningCount", "paymentReference", "paidAt", "ok": true } ] }
```
Discrepancy types (including `duplicate_earning_payment`, `paid_earning_linked_elsewhere`,
`duplicate_payment_across_periods` and the `pending_reversal` warning): see PAYOUTS.md §6.

### `GET /finance/payout-batches/{id}/reconciliation.csv` — `payouts.view`
Per-item rows (Result `OK`/`DISCREPANCY` with details) plus batch-level discrepancy rows.

---

## Background job

`PayoutPreparationJob` (`payouts.prepare`, every 15 minutes, runs only when `Jobs:Enabled`): prepares the last
completed period when the active schedule has `autoPrepareBatches` and no batch (any status) exists for that period and
currency.

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `Payments:Provider` | `manual` | Active payment provider key; unknown keys fail startup validation. |
