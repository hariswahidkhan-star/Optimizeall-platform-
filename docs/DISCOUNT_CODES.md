# Discount codes (affiliate sales)

Brands give the agency discount codes. The codes are assigned to individual participants (influencers) or to rate
groups; participants share them and report the sales made with them; a reviewer approves each sale (checked against
the brand's own sales report when one is imported) and the approval writes a commission to the ledger, which is paid by
the ordinary payout batches. Refunds reverse the commission (a clawback when it was already paid).

* Code: `backend/src/OptimizeAll.Domain/Codes` (entities and the pure `CodePayoutCalculator`),
  `backend/src/OptimizeAll.Api/Modules/Codes` (services, endpoints), EF mappings in
  `Infrastructure/Persistence/Configurations/CodeConfigurations.cs`.
* UI: *Manage → Discount codes* (programs, codes, sales, report), *Review → Code sales* (queue and decision),
  *Finance → Code sales* (refunds), *Participant → My codes* (codes, share link, report a sale, own sales).
* API: [`docs/api/discount-codes.md`](api/discount-codes.md). Demo data: [`DEMO.md`](DEMO.md) § Discount codes.
* Tests: `UnitTests/Codes/CodePayoutCalculatorTests.cs`, `IntegrationTests/Codes/*`, `frontend/src/features/codes/codes.test.tsx`,
  E2E suite `frontend/e2e/j-codes` (`E2E_SUITE=j-codes scripts/e2e-journeys.sh`).

## Design decisions

| Question | Decision |
|---|---|
| Campaign type or a separate concept? | A separate **code program** (`code_programs`). Campaigns price *posts* (proof URL, platform, live checks, reward rules); code sales price *orders* (order id, value, discount, brand report). Sharing one entity would have forced both workflows through each other's validation. A program may *link* to a campaign (its participants' tracking links then count clicks for conversion reporting) and to an agency client (the company). |
| Groups | **Rate groups are reused** (`rate_groups`, manual groups only): a shared code is assigned to a rate group, group payout overrides target rate groups, "auto-assign" hands one code to each member, reports group by rate group. No second grouping concept. Automatic (tier/follower) groups have no fixed membership and are refused (`409 codes.group_automatic`). |
| When is a commission priced? | **At approval**, with the payout rules in force then (the program's `payoutVersion` is stored on the sale and in the ledger label). Unlike post rewards, a sale has no "rate at submission" promise to keep: the brand's commission terms apply to what is approved. The participant sees an *estimate* (before caps) while the sale is pending. |
| Attribution | By **order date**: the assignment (personal, or the group's shared code) whose window contains the order date. A reassigned code's earlier orders stay with the previous holder, who can still report them (within the program's "report within N days"). |
| Shared code, two members report the same order | One live claim per order and program (unique index on `(ProgramId, ActiveOrderKey)` + the program lock): the **first report wins**, the second gets `409 code_sale.duplicate_order` ("already reported by another member of your group"). A withdrawn or rejected claim frees the order. |
| Money | Commissions, caps and budget are in the **program currency**. Orders in another currency are converted **at the order date** with the platform exchange rates; a missing rate is refused (`409 fx.rate_missing`) — a sale is never priced at 0. The ledger then converts to the settlement currency as for every earning. Rounding per currency (JPY 0, KWD 3 decimals); capped amounts are rounded **down**. |
| Proof | Optional or required per program. Images only (PNG/JPEG/WebP, 10 MB, metadata stripped) through the existing Files module (`FilePurpose.SaleProof`, private: owner, `sales.review`, `codes.view`). |
| Tier bonus after a refund | Kept (a one-off milestone bonus is not re-evaluated); the refunded sale's own commission is reversed. Tier *rates* use the number of **currently approved** sales, so later sales are priced on the reduced count. |

## Data model

| Table | What it holds |
|---|---|
| `code_programs` | Brand, name, optional client / campaign, description, terms, store URL, "what customers get", currency, window `StartsAt`–`EndsAt`, status (Draft → Active ↔ Paused → Archived), payout rules (type, flat or percent, caps, budget), `MaxOrderAgeDays`, `RequireProof`, `PayoutVersion`. |
| `code_program_tiers` | `ThresholdSales` (unique per program), optional new rate (flat **or** percent) for further sales, optional one-off bonus. |
| `code_payout_overrides` | Per-person or per-rate-group rate for one program; ended, never deleted. |
| `discount_codes` | Code as given and `NormalizedCode` (upper-case, **unique per program**), stored status Available / Assigned / Paused / Retired (**Expired is derived** from the code's or program's end), source Manual / Import / Generated, optional validity, import batch. |
| `discount_code_assignments` | Person **or** group (check constraint), `ValidFrom`, `ValidTo`, `EndedAt` (reassign / unassign / retire keep the row: history). At most one live assignment per code; windows never overlap. |
| `code_sales` | Program, code, attributed person, assignment and group (shared code), order reference (+ normalized, + `ActiveOrderKey`), order date, net value, discount, currency, rate to the program currency and converted amounts, product note, proof, status, source (Participant / Admin / Import), estimate, commission, payout source label, applied caps, payout version, reconciliation (verification, note, reported value/date, import batch), refund. |
| `code_sale_events` | Status history (participant timeline, audit). |
| `code_import_batches` | Committed code imports and brand sales-report imports with their counts. |
| `earning_entries` | New types `SaleCommission` and `SaleTierBonus`; new immutable columns `CodeProgramId`, `CodeSaleId` (copied onto reversal legs). |

Sale statuses: `Pending` → `Approved` / `Rejected` / `NeedsInfo` (→ edited → `Pending`) / `Withdrawn`;
`Pending`/`NeedsInfo` → `Cancelled` (brand reported the order cancelled/refunded before approval);
`Approved` → `Refunded` (commission reversed).

## Payout rules

Evaluated by `CodePayoutCalculator` at approval, under the program lock:

1. The sale is the person's *k*-th approved sale in the program (*k* = currently approved sales + 1).
2. **Per-sale rate**: a **person override**, else a **group override** (manual rate groups the person is in; higher
   group priority wins, then the older override), else the **tier** with the highest threshold below *k* that sets a
   rate, else the **program rate**. Flat amount, or percent × the order value net of the discount, rounded to the
   currency's minor unit.
3. **Tier bonuses**: every tier with a bonus whose threshold ≤ *k* that the person hasn't earned yet (idempotency key
   `codetier:{program}:{person}:{threshold}` → once per person, even under races).
4. **Caps**, line by line (commission first): per-person **daily** cap (commissions of sales *reported* that UTC day —
   the reporting day, like post caps, so back-dated orders can't spread over several days), per-person **program** cap,
   program **budget** (all people). A capped line is rounded down; lines that end at 0 are dropped; the sale is still
   approved (commission 0, `appliedCaps` lists `daily_cap`, `program_cap`, `program_budget`).
5. Each line is recorded through `ILedgerWriter` (idempotency key `codesale:{sale}:commission`), `RequiresApproval =
   false` (the approval is the review), with `RateSource` = `CodePersonOverride` / `CodeGroupOverride` /
   `CodeProgramTier` / `CodeProgramRules`, `RateSourceLabel` "Program vN · <rate>", `RateGroupId` for group overrides,
   `CampaignId` = the linked campaign.

Worked example (program: 10 % of net, tier "after 5 sales: 12 %, bonus 25", daily cap 150, budget 5,000):
Ann's 5th approved sale of 96.40 → 9.64 + bonus 25 = 34.64; her 6th (73.25) → 12 % = 8.79. A group override of 12 %
for her rate group would price both at 12 % (the bonus still applies).

## Codes

* **Add** one code (`409 code.duplicate`, case-insensitive), **import** a CSV from the brand (columns `code`, optional
  `validFrom`, `validTo`, `note`, `email` = assign to that participant; header aliases such as "Coupon Code",
  "Expires"). The import is **checked first** (dry run) and reports every rejected line with the line number a
  spreadsheet shows (blank lines count); duplicates in the file or already in the program are refused, never
  overwritten; up to 50,000 rows / 10 MB, inserted in batches of 500 in one transaction under the program lock.
* **Generate** from a pattern (`#` digit, `?` letter, `*` either; 0/O/1/I/L never used), with a capacity check.
* **Pause / resume / retire** (retiring ends the live assignment), validity edits; expired codes can't be assigned.
* **Assign** to a person (personal) or a manual rate group (shared); **reassign** ends the current assignment at the
  new start (history kept; `409 code.already_assigned` without `"reassign": true`); **unassign**; **auto-assign** one
  unique available code to every member of a group who has no personal code in the program (preview first; members
  beyond the pool are reported `codes.pool_exhausted`). Assignees are notified (`codes.assigned`).

## Reporting a sale (participant)

`POST /me/code-sales` (multipart): code, order reference, order date, value (net of discount), discount, currency,
product/notes, optional proof. Validation, in order:

| Rule | Result |
|---|---|
| Code never held by the person (or their group), or program draft/archived | `404` (no enumeration of other people's codes) |
| Program paused | `409 code_program.paused` |
| Account suspended/deactivated | `409 participant.not_active` |
| Order date in the future (> 10 min) / older than `MaxOrderAgeDays` / outside the program window | `400 code_sale.order_in_future` / `400 code_sale.order_too_old` / `409 code_sale.outside_program` |
| Order placed before the code was assigned to them / while it was someone else's | `409 code_sale.before_assignment` / `409 code_sale.not_assigned_on_date` |
| Code retired / paused / used after its expiry / before its start | `409 code_sale.code_retired` / `code_paused` / `code_expired` / `code_not_yet_valid` |
| Proof required and missing | `400 code_sale.proof_required` |
| Currency unsupported / no exchange rate on the order date | `400 code_sale.currency_unsupported` / `409 fx.rate_missing` |
| Order already reported in the program (live claim) | `409 code_sale.duplicate_order` (idempotent, concurrency-safe) |

While `Pending` or `NeedsInfo` the participant can **edit** (`PUT`, stamp-checked; a `NeedsInfo` sale goes back to
`Pending`; a matched sale whose facts change becomes unverified again) or **withdraw** it.

## Brand sales report (reconciliation)

`POST /admin/code-programs/{id}/sales/import?dryRun=true|false` with the brand's CSV (`order id`, `code`, `amount`,
`date`, optional `status` completed/refunded/cancelled…, `currency`, `discount`). Per row:

| Situation | Outcome |
|---|---|
| Unknown code, bad amount/date/status, order twice in the file | rejected (line-numbered) |
| A live sale with that order: same code, amount within 1 % (min 0.01), date within 36 h | **Matched** (bulk-approvable) |
| A live sale with that order but something differs | **Mismatch**, with the differences in the note |
| Reported refunded/cancelled: approved sale / pending sale | **Refunded** (commission reversed) / **Cancelled** |
| Nobody claimed it, the code's holder on that date is a person | a **Pending** sale for them (source Import, "Reported by brand") |
| Nobody claimed it, shared code or nobody held it | flagged (a shared code can't be attributed to one member) |

Rows are applied in chunks of 200, each under the program lock, re-planned against the current data (a sale reported
meanwhile is matched, not duplicated). Re-importing the same file changes nothing.

## Review, four-eyes and refunds

* Queue: `GET /admin/code-sales?status=Pending` (oldest first) — *Review → Code sales*.
* Decisions: Approve, Reject (reason required, shown to the participant; frees the order), Request info (reason
  required). Stamp-checked; a sale is decided once (`409 code_sale.already_decided`, also under concurrent approvals —
  program lock + conditional state check + ledger idempotency).
* **Four-eyes / self-review**: nobody decides their **own** sale (`403 code_sale.self_review`) or a sale they
  **entered or imported** (`403 code_sale.four_eyes`). All decisions are audited.
* **Bulk approve** (up to 200): approves each sale individually (same rules), by default only brand-matched ones.
* Approval requires an **active** participant (`409 participant.not_active`, shared row lock like post approvals).
  **Test accounts** are approved like anyone and excluded from payouts by the payout batches, as elsewhere.
* **Refund** (`sales.reverse`, confirm + reason): an approved sale's commission is reversed through
  `ILedgerWriter.ReverseAsync` — unpaid → cancelled with a zero-sum leg; **paid → a negative Approved entry netted
  against the next payout (clawback)**. An earning in a *draft* payout batch is released by holding the participant's
  item (same coordinator as submission reversals); a *finalized* batch refuses (`409 ledger.in_payout_batch`) until
  finance fails the item.

## Reporting

`GET /admin/code-reports?programId=&groupBy=Person|Code|Group|Program&from=&to=` (and `/export.csv`): uses, pending,
approved, rejected, refunded, gross sales, discount given, net sales, commission pending (estimate), approved (unpaid),
paid and reversed — in the program currency. With a linked campaign: unique non-bot clicks on the participants'
tracking links and conversion (reported sales ÷ clicks). `GET /admin/code-sales/export.csv` exports sales. Participants
see only their own numbers (`GET /me/codes` stats; `GET /me/code-sales`).

## Permissions

| Permission | Built-in roles | Allows |
|---|---|---|
| `codes.view` | Campaign manager, Finance, Admin | Programs, codes, assignments, sales, reports, exports, sale proofs |
| `codes.manage` *(sensitive)* | Campaign manager, Admin | Programs and payout rules, overrides, codes (add/import/generate/pause/retire), staff-entered sales, brand report import |
| `codes.assign` *(sensitive)* | Campaign manager, Admin | Assign / reassign / unassign / auto-assign |
| `sales.review` *(sensitive)* | Reviewer, Admin | Review queue and decisions, bulk approval, sale proofs |
| `sales.reverse` *(sensitive)* | Finance, Admin | Refund / cancel sales (reverses commissions) |

All usable in custom roles. Every staff write and the participant's sale create/edit/withdraw are
`[DeniedWhileImpersonating]` (in `ImpersonationCoverageTests`' deny list); reads are allowed while impersonating.

## Concurrency

Every write that touches a program's codes, assignments, sales or money acquires the named lock
`codes:program:{id}` **before** its write transaction and then locks the `code_programs` row inside it
(`IDatabaseDialect`, no raw SQL). Unique indexes are the final guards: `(ProgramId, NormalizedCode)` on codes,
`(ProgramId, ActiveOrderKey)` on sales, the ledger's idempotency keys. Edits carry concurrency stamps (`409
concurrency.conflict`).
