# API: discount codes (affiliate sales)

Base path `/api/v1`. Conventions as in [campaigns-submissions-review.md](campaigns-submissions-review.md): camelCase
JSON, string enums (an undefined value is a 400), UTC ISO-8601 timestamps, RFC 7807 problems with `code`, `traceId`
and `errors`. Concepts, payout rules, validation order and locking: [DISCOUNT_CODES.md](../DISCOUNT_CODES.md).

Permissions: reads `codes.view`; program, payout, override, code and import writes `codes.manage`; assignment
`codes.assign`; decisions `sales.review`; refunds `sales.reverse`; participants `participant.portal` (own data only —
another person's code or sale is a **404**). Every staff write and the participant's sale writes are denied while
impersonating (`403 auth.impersonation_forbidden_action`) and audited. Writes that carry `concurrencyStamp` answer
`409 concurrency.conflict` when it is stale.

Enums: `CodeProgramStatus` Draft|Active|Paused|Archived; `CodePayoutType` FlatPerSale|PercentOfNet;
`DiscountCodeStatus` Available|Assigned|Paused|Expired|Retired (Expired is derived); `DiscountCodeSource`
Manual|Import|Generated; `CodeAssignmentTarget` Person|Group; `CodeSaleStatus`
Pending|NeedsInfo|Approved|Rejected|Withdrawn|Cancelled|Refunded; `CodeSaleSource` Participant|Admin|Import;
`CodeSaleVerification` Unverified|Matched|Mismatch|ReportedByBrand; `CodeSaleDecision` Approve|Reject|RequestInfo;
`CodeReportGrouping` Program|Code|Person|Group. Ledger: `EarningType` adds `SaleCommission`, `SaleTierBonus`;
`RateSourceLevel` adds `CodePersonOverride`, `CodeGroupOverride`, `CodeProgramTier`, `CodeProgramRules`; ledger rows
carry `codeProgramId` and `codeSaleId` (also in the ledger CSV).

---

## Programs — `/admin/code-programs`

### `GET /admin/code-programs` — `codes.view`
Query: `search` (name or brand, LIKE-escaped), `status` (default: all but Archived), paging.
`Paged<CodeProgramListItem>`: `{ id, name, brandName, status, currency, startsAt, endsAt, payoutSummary, codes,
assignedCodes, pendingSales, approvedSales, commissionApproved, updatedAt }`.

### `GET /admin/code-programs/{id}` — `codes.view`
`CodeProgram`: `{ id, name, brandName, client, campaign, description, terms, storeUrl, discountLabel, currency, startsAt,
endsAt, status, payoutType, flatAmount, percent, tiers: [{ thresholdSales, flatAmount, percent, bonusAmount }],
dailyCapPerPerson, programCapPerPerson, budgetAmount, maxOrderAgeDays, requireProof, payoutVersion, payoutSummary,
overrides: CodePayoutOverride[], stats: { codes, availableCodes, assignedCodes, pendingSales, approvedSales,
commissionApproved, commissionPaid, budgetRemaining }, createdAt, updatedAt, concurrencyStamp }`.

### `POST /admin/code-programs` — `codes.manage` → **201** `CodeProgram`
```json
{ "name": "Summer affiliate", "brandName": "Glow Cosmetics", "clientAccountId": null, "campaignId": null,
  "description": "…", "terms": "No coupon sites.", "storeUrl": "https://glow.example.com", "discountLabel": "15% off",
  "currency": "USD", "startsAt": "2026-09-01T00:00:00Z", "endsAt": null, "maxOrderAgeDays": 60, "requireProof": false,
  "payout": { "payoutType": "PercentOfNet", "percent": 10,
              "tiers": [ { "thresholdSales": 5, "percent": 12, "bonusAmount": 25 } ],
              "dailyCapPerPerson": 150, "programCapPerPerson": null, "budgetAmount": 5000 },
  "activate": true }
```
400 `code_program.currency_unsupported|invalid_window|invalid_store_url|invalid_payout|campaign_not_found|client_not_found`;
409 `fx.rate_missing` (no rate from the program currency to the settlement currency).

### `PUT /admin/code-programs/{id}` — `codes.manage`
The same fields without `payout`/`activate`, plus `concurrencyStamp`. 409 `code_program.currency_locked` (has sales),
`code_program.archived`.

### `PUT /admin/code-programs/{id}/payout` — `codes.manage`
Payout fields as above + `reason` (5–500) + `confirm: true` (else 400 `confirmation.required`) + `concurrencyStamp`.
Replaces the tiers, increments `payoutVersion`, audited `code_program.payout_changed`.

### `POST /admin/code-programs/{id}/status` — `codes.manage`
`{ "status": "Active|Paused|Archived", "reason": "…", "concurrencyStamp": "…" }`. 400 `code_program.invalid_status`
(back to Draft); 409 `code_program.status_unchanged`, `code_program.has_pending_sales` (archiving).

### `POST /admin/code-programs/{id}/overrides` — `codes.manage` → **201** `CodeProgram`
`{ "target": "Person|Group", "userId"|"groupId": "…", "payoutType": "…", "flatAmount"|"percent": …, "reason": "…" }`.
400 `code_override.invalid_target|invalid`, `codes.not_participant`; 409 `code_override.duplicate`,
`codes.group_automatic`, `rate_group.archived`.

### `POST /admin/code-programs/{id}/overrides/{overrideId}/end` — `codes.manage`
`{ "reason": "…" }`. 409 `code_override.ended`.

## Codes

### `GET /admin/code-programs/{id}/codes` — `codes.view`
Query: `search` (code), `status` (including the derived `Expired`), `userId`, `groupId` (live holder), paging.
`DiscountCode`: `{ id, programId, code, status, source, validFrom, validTo, note, assignment: CodeAssignment|null,
sales, createdAt, concurrencyStamp }`.

### `POST /admin/code-programs/{id}/codes` — `codes.manage` → **201** `DiscountCode`
`{ "code": "GLOW-SARA15", "validFrom": null, "validTo": null, "note": null }`. 400 `code.invalid|invalid_window`;
409 `code.duplicate` (case-insensitive, per program).

### `POST /admin/code-programs/{id}/codes/import?dryRun=true|false` — `codes.manage`
multipart `file` (CSV ≤ 10 MB, ≤ 50,000 rows; columns `code` + optional `validFrom`, `validTo`, `note`, `email`).
`CodeImportResult`: `{ dryRun, rows, valid, created, duplicates, rejected: [{ row, value, code, message }],
warnings: […], sample: ["…"] }` (`row` = spreadsheet line). 400 `csv.empty|csv.invalid|csv.too_large|csv.too_many_rows|csv.missing_column`.

### `POST /admin/code-programs/{id}/codes/generate?dryRun=true|false` — `codes.manage`
`{ "pattern": "GLOW-????-##", "count": 50, "validFrom": null, "validTo": null, "note": null }` → `CodeImportResult`.
400 `code.invalid_pattern|pattern_too_small`.

### `GET /admin/discount-codes/{codeId}` — `codes.view`
`{ code: DiscountCode, program: { id, name, brandName, currency }, history: CodeAssignment[] }` (newest first).
`CodeAssignment`: `{ id, codeId, code, target, person, group, validFrom, validTo, endedAt, endReason, reason, createdAt,
createdBy, isLive }`.

### `PUT /admin/discount-codes/{codeId}` — `codes.manage`
`{ "status": "Available|Paused|Retired"|null, "validFrom", "validTo", "note", "reason", "concurrencyStamp" }`.
Retiring ends the live assignment. 409 `code.retired`; 400 `code.invalid_status`.

### `POST /admin/discount-codes/{codeId}/assign` — `codes.assign`
`{ "target": "Person|Group", "userId"|"groupId": "…", "validFrom": null, "validTo": null, "reassign": false, "reason": "…" }`
→ `DiscountCodeDetail`. 409 `code.already_assigned` (without `reassign`), `code.paused|expired|retired`,
`code_assignment.overlap`, `codes.group_automatic`, `codes.user_deactivated`; 400 `codes.not_participant`,
`code_assignment.invalid_window|invalid_target`.

### `POST /admin/discount-codes/{codeId}/unassign` — `codes.assign`
`{ "reason": "…" }`. 409 `code.not_assigned`.

### `POST /admin/code-programs/{id}/codes/auto-assign` — `codes.assign`
`{ "groupId": "…", "validFrom": null, "validTo": null, "reason": "…", "dryRun": true }` →
`{ dryRun, members, assigned, alreadyHadCode, skipped, availableCodes, issues: [{ value: email, code, message }],
assignments: CodeAssignment[] }` (`codes.pool_exhausted`, `codes.user_deactivated`, warnings for suspended/test accounts).

### `GET /admin/code-programs/{id}/assignments` — `codes.view`
Paged assignment history of the program.

## Sales (staff)

### `GET /admin/code-sales` — `codes.view` or `sales.review` or `sales.reverse`
Query: `programId`, `status`, `verification`, `source`, `userId`, `codeId`, `groupId`, `from`, `to` (order date),
`search` (order reference, code, participant name/email; LIKE-escaped), paging. Open statuses sort oldest first.
`CodeSaleListItem`: `{ id, program, code: { id, name }, person, sharedCode, orderReference, orderDate, netAmount,
discountAmount, currency, programNetAmount, status, source, verification, submittedAt, estimatedCommission,
commissionAmount, isTestAccount, userStatus, canDecide, concurrencyStamp }`.

### `GET /admin/code-sales/{id}` — same
`CodeSale`: list fields + `personEmail, group, exchangeRate, programDiscountAmount, productNote, proofUrl, createdBy,
payoutSourceLabel, appliedCaps[], payoutVersion, verificationNote, reportedNetAmount, reportedOrderDate, decidedAt,
decidedBy, decisionReason, refundedAt, refundReason, cannotDecideReason, events: [{ fromStatus, toStatus, action, actor,
reason, at }], earnings: [{ id, type, status, amount, currency, rateSourceLabel, createdAt }]`.

### `POST /admin/code-programs/{id}/sales` — `codes.manage` → **201** `CodeSale`
`{ "code": "GLOW-SARA15", "userId": null, "orderReference": "…", "orderDate": "…", "netAmount": 40, "discountAmount": 0,
"currency": "USD", "productNote": null, "reason": "Brand emailed the order" }`. Attributed to the code's personal
holder on the order date unless `userId` (required for shared codes: 400 `code_sale.user_required`). Someone else must
approve it.

### `POST /admin/code-sales/{id}/decision` — `sales.review`
`{ "decision": "Approve|Reject|RequestInfo", "reason": "…" (required for Reject/RequestInfo), "concurrencyStamp": "…" }`
→ `CodeSale`. 403 `code_sale.self_review|four_eyes`; 409 `code_sale.already_decided`, `participant.not_active`,
`fx.rate_missing`; 400 `code_sale.reason_required`.

### `POST /admin/code-sales/bulk-approve` — `sales.review`
`{ "saleIds": ["…"] (≤ 200), "reason": null, "onlyMatched": true }` →
`{ requested, approved, skipped, items: [{ saleId, succeeded, code, message }] }`.

### `POST /admin/code-sales/{id}/refund` — `sales.reverse`
`{ "reason": "…", "confirm": true }` → `CodeSale` (Approved → Refunded with reversal entries; Pending/NeedsInfo →
Cancelled). 403 `code_sale.self_review`; 409 `code_sale.not_refundable`, `ledger.in_payout_batch` (finalized batch).

### `POST /admin/code-programs/{id}/sales/import?dryRun=true|false` — `codes.manage`
multipart `file`: the brand's sales report (`order id`, `code`, `amount`, `date`, optional `status`, `currency`,
`discount`; header aliases accepted). `SalesImportResult`: `{ dryRun, rows, matched, mismatched, created, cancelled,
refunded, unchanged, rejected, issues: [{ row, orderReference, code, outcome (mismatch|create|refund|cancel|flagged|rejected|ignored), message }] }`.

### `GET /admin/code-sales/export.csv` — `codes.view`
Same filters as the list; one row per sale.

## Reports

### `GET /admin/code-reports` — `codes.view`
Query: `groupBy` (default Person), `programId` (required unless `groupBy=Program`), `from`, `to` (order date).
`{ groupBy, program, currency, from, to, totals: Row, rows: Row[], note }`; `Row`: `{ id, label, detail, uses, pending,
approved, rejected, refunded, grossSales, discountGiven, netSales, commissionPending, commissionApproved,
commissionPaid, commissionReversed, clicks, conversionRate }`. 400 `code_report.program_required|invalid_range`.

### `GET /admin/code-reports/export.csv` — `codes.view`

## Participant

### `GET /me/codes` — `participant.portal`
The caller's personal codes and their groups' shared codes (live, or ended recently enough for older orders), in active
or paused programs: `[{ codeId, code, programId, programName, brandName, discountLabel, description, terms, storeUrl,
shareUrl, shared, assignedFrom, assignedUntil, isActive, inactiveReason, currency, yourRate, tierPerks[], requireProof,
maxOrderAgeDays, programStartsAt, programEndsAt, stats: { sales, pending, approved, grossSales, commissionPending,
commissionApproved, commissionPaid, clicks } }]`. Never other people's codes, group names or other people's rates.

### `GET /me/code-sales`, `GET /me/code-sales/{id}` — own sales only
`MyCodeSale`: `{ id, programId, programName, brandName, codeId, code, orderReference, orderDate, netAmount,
discountAmount, currency, productNote, proofUrl, status, source, submittedAt, decisionReason, estimatedCommission,
commissionAmount, programCurrency, canEdit, canWithdraw, events (staff names hidden), concurrencyStamp }`.

### `POST /me/code-sales` → **201** — multipart
`codeId`, `orderReference`, `orderDate`, `netAmount`, `discountAmount`, `currency`, `productNote`, `proof` (image).
Errors: see [DISCOUNT_CODES.md § Reporting a sale](../DISCOUNT_CODES.md#reporting-a-sale-participant).

### `PUT /me/code-sales/{id}` — multipart, Pending/NeedsInfo only
Any of the create fields + `concurrencyStamp`. 409 `code_sale.not_editable`, `concurrency.conflict`.

### `POST /me/code-sales/{id}/withdraw`
`{ "reason": null }`. 409 `code_sale.not_withdrawable`.
