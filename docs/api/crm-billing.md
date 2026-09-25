# CRM, Proposals, Contracts & Billing API

Base path `/api/v1`. JSON is camelCase, enums are strings, timestamps are UTC ISO-8601, `DateOnly` values are
`"yyyy-MM-dd"`, money is a JSON number (a `decimal` rounded to the currency's minor units on the server) with its
currency alongside. **Every total is computed by the server**; clients call a `preview` endpoint for live totals.
Errors are RFC 7807 problems with `code` and `traceId`; validation errors (400) carry `errors`. See
[`../SALES_AND_BILLING.md`](../SALES_AND_BILLING.md) for the user guide and business rules.

Common error codes on every endpoint: `401` (not signed in), `403` (missing permission), `404 <entity>.not_found`
(also returned for a record outside the caller's client scope), `409 concurrency.conflict` (stale or missing
`concurrencyStamp`), `400 request.confirm_required` (sensitive action without `"confirm": true`).

Paged lists accept `?page=1&pageSize=25&search=` (pageSize ≤ 200) and return
`{ "items": [...], "total": 42, "page": 1, "pageSize": 25, "totalPages": 2 }`.

Tenancy: staff who are not agency-wide (e.g. Account Managers) only see companies/deals/invoices/contracts of the
clients they are assigned to (`IClientScope`). Records outside scope answer 404.

## Enums

| Enum | Values |
|---|---|
| `LifecycleStage` | Subscriber, Lead, MarketingQualifiedLead, SalesQualifiedLead, Opportunity, Customer, Evangelist |
| `ConsentStatus` | Unknown, Subscribed, Unsubscribed, NotGiven |
| `CompanySize` | Unknown, Solo, Micro, Small, Medium, Large, Enterprise |
| `DealSource` | WebsiteInquiry, Form, Referral, Outbound, Event, Other |
| `StageKind` / `DealStatus` | Open, Won, Lost |
| `ActivityType` | Note, Call, Meeting, Email, Task |
| `ScoringCategory` | Fit, Engagement |
| `ProposalStatus` | Draft, Sent, Viewed, Accepted, Declined, Expired, Withdrawn |
| `InvoiceStatus` | Draft, Issued, PartiallyPaid, Paid, Overdue, Void, WrittenOff |
| `PaymentMethod` | BankTransfer, Card, Cash, Cheque, PayPal, Stripe, Other |
| `ContractStatus` | Draft, Active, Paused, Cancelled, Ended |
| `CreditNoteStatus` | Open, Applied |
| `DiscountType` | None, Percent, Amount |
| `Recurrence` | OneTime, Monthly, Quarterly, Annually |
| `BillingFrequency` | Monthly, Quarterly, Annually |

## Shared shapes

```jsonc
// PriceLineRequest (proposals, invoices, contracts)
{
  "description": "SEO retainer", "serviceSlug": "seo|null", "packageSlug": "growth|null",
  "quantity": 1, "unitPrice": 2500.00, "discountType": "None|Percent|Amount", "discountValue": 0,
  "taxRateId": "guid|null", "recurrence": "OneTime|Monthly|Quarterly|Annually"   // invoices: OneTime only
}
// PriceLine (response) = request fields plus the tax snapshot and server-computed amounts
{ "id", "position", ..., "taxName": "VAT|null", "taxPercent": 20, "taxInclusive": false,
  "discountAmount": 0, "subtotal": 2500, "taxAmount": 500, "total": 3000 }
// Totals
{ "currency": "USD", "grossTotal", "discountTotal", "subtotal", "taxTotal", "total",
  "taxes": [{ "name": "VAT", "ratePercent": 20, "inclusive": false, "taxableAmount", "taxAmount" }] }
// RecurringTotals (proposals/contracts)
{ "oneTimeTotal", "monthlyTotal", "quarterlyTotal", "annualTotal",
  "monthlyRecurringValue", "firstInvoiceTotal", "firstYearValue" }
// UserRef
{ "id": "guid", "displayName": "Hassan Raza", "email": "sales@demo.optimizeall.app" }
```

Line validation (400): `billing.invalid_line` (description, quantity > 0, unit price ≥ 0, discount range),
`billing.invalid_tax_rate` (unknown/inactive rate), `billing.too_many_lines` (> 200), `billing.no_lines`,
`billing.currency_unsupported`, `billing.zero_total` (issuing a zero invoice).

---

## CRM — `/agency/crm` (read: `crm.view`; write: `crm.manage`)

| Method & path | Perm | Body / query | Response |
|---|---|---|---|
| `GET /agency/crm/dashboard` | view | – | `CrmDashboardDto` (pipeline value by stage/currency, weighted value, won/lost this month, sources, overdue tasks) |
| `GET /agency/crm/assignees` | view | – | `UserRef[]` staff who can own deals and receive tasks |
| `GET /agency/crm/companies` | view | paging + `ownerUserId, industry, tag` | paged `CompanySummaryDto` |
| `GET /agency/crm/companies/{id}` | view | – | `CompanyDto` (contacts, deals, custom fields) |
| `POST /agency/crm/companies` | manage | `CompanyRequest` | 201 `CompanyDto` |
| `PUT /agency/crm/companies/{id}` | manage | `CompanyRequest` + `concurrencyStamp` | `CompanyDto` |
| `GET /agency/crm/contacts` | view | paging + `lifecycleStage, ownerUserId, companyId, tag, consentStatus, minScore` | paged `ContactSummaryDto` |
| `GET /agency/crm/contacts/export.csv` | view | same filters | CSV (≤ 50 000 rows; over the cap: 422 `export.too_large`) |
| `POST /agency/crm/contacts/import` | manage | multipart `file` (CSV ≤ 2 MB), `?dryRun=&updateExisting=` | `ImportResultDto` (per-row status/errors) |
| `GET /agency/crm/contacts/{id}` | view | – | `ContactDto` (score breakdown, timeline, UTM touches) |
| `POST /agency/crm/contacts` | manage | `ContactRequest` | 201 `ContactDto` |
| `PUT /agency/crm/contacts/{id}` | manage | `ContactRequest` + stamp | `ContactDto` |
| `GET /agency/crm/stages` | view | – | `StageDto[]` |
| `PUT /agency/crm/stages` | manage | `{ stages: [{ id?, name, winProbability, kind, isActive }] }` (order = position) | `StageDto[]` |
| `GET /agency/crm/deals` | view | paging + `stageId, status, ownerUserId, source, companyId, contactId` | paged `DealSummaryDto` |
| `GET /agency/crm/deals/board` | view | same filters | `{ columns: [{ stage, count, totals: [{currency, amount}], deals }] }` |
| `GET /agency/crm/deals/{id}` | view | – | `DealDto` (contacts, proposals, UTM) |
| `POST /agency/crm/deals` | manage | `DealRequest` | 201 `DealDto` |
| `PUT /agency/crm/deals/{id}` | manage | `DealRequest` + stamp | `DealDto` |
| `POST /agency/crm/deals/{id}/move` | manage | `{ stageId, lostReason?, concurrencyStamp }` | `DealDto` |
| `POST /agency/crm/deals/{id}/contacts` | manage | `{ contactId, role? }` | `DealDto` |
| `DELETE /agency/crm/deals/{id}/contacts/{contactId}` | manage | – | `DealDto` |
| `GET /agency/crm/activities` | view | paging + `contactId, companyId, dealId, type, assignee (me|guid), due (open|overdue|today|completed)` | paged `ActivityDto` |
| `GET /agency/crm/tasks/mine` | view | paging + `includeCompleted` | paged `ActivityDto` (overdue first) |
| `POST /agency/crm/activities` | manage | `ActivityRequest` | 201 `ActivityDto` |
| `PUT /agency/crm/activities/{id}` | manage | `ActivityRequest` + stamp | `ActivityDto` |
| `POST /agency/crm/activities/{id}/complete` | manage | `{ concurrencyStamp }` | `ActivityDto` |
| `POST /agency/crm/activities/{id}/reopen` | manage | `{ concurrencyStamp }` | `ActivityDto` |
| `DELETE /agency/crm/activities/{id}` | manage | – | 204 (system activities cannot be deleted) |
| `GET /agency/crm/scoring/rules` | view | – | `ScoringRuleDto[]` |
| `GET /agency/crm/scoring/meta` | view | – | `{ fitFields, engagementTypes }` |
| `POST /agency/crm/scoring/rules` | manage | `ScoringRuleRequest` | 201 `ScoringRuleDto` |
| `PUT /agency/crm/scoring/rules/{id}` | manage | `ScoringRuleRequest` + stamp | `ScoringRuleDto` |
| `DELETE /agency/crm/scoring/rules/{id}` | manage | – | 204 |
| `POST /agency/crm/scoring/recompute` | manage | – | `{ contacts: n }` |
| `GET /agency/crm/views` | view | `?entity=deals|contacts|companies` | `SavedViewDto[]` (own + shared) |
| `POST /agency/crm/views` | view | `{ name, entity, filters: {k: v}, shared }` | 201 `SavedViewDto` |
| `DELETE /agency/crm/views/{id}` | view | – | 204 (own views only) |

Request bodies:

```jsonc
// CompanyRequest
{ "name", "domain?", "industry?", "size": "Small", "countryCode?": "US", "ownerUserId?", "tags?": ["dental"],
  "customFields?": "{\"json\":\"object\"}", "concurrencyStamp?" }
// ContactRequest
{ "firstName", "lastName?", "email?", "phone?", "jobTitle?", "companyId?", "lifecycleStage": "Lead",
  "ownerUserId?", "consentStatus": "Unknown", "tags?": [], "source?", "budgetRange?", "concurrencyStamp?" }
// DealRequest
{ "title", "companyId?", "primaryContactId?", "stageId?", "value": 36000, "currency": "USD",
  "expectedCloseDate?": "2026-10-31", "serviceSlugs?": ["seo"], "ownerUserId?", "source": "Referral",
  "sourceDetail?", "budgetRange?", "firstTouch?": { "source", "medium", "campaign", "at" }, "lastTouch?",
  "clientAccountId?", "concurrencyStamp?" }
// ActivityRequest
{ "type": "Task", "subject", "body?", "contactId?", "companyId?", "dealId?", "occursAt?", "durationMinutes?",
  "dueAt?", "remindAt?", "assigneeUserId?", "concurrencyStamp?" }
// ScoringRuleRequest
{ "name", "category": "Fit|Engagement", "field": "industry|companySize|jobTitle|budgetRange|country|... or an engagement type",
  "matchValue?", "points": 10, "maxOccurrences?": 3, "isActive": true, "concurrencyStamp?" }
```

CSV import header (case-insensitive): `first_name,last_name,email,phone,job_title,company,company_domain,
lifecycle_stage,consent,source,tags` (`tags` separated by `;`). The header needs at least `first_name` or `email`. Rows are deduplicated by normalized email; a
duplicate email is skipped unless `updateExisting=true`. `dryRun=true` validates without saving.

CRM error codes: `crm.invalid_email`, `crm.invalid_domain`, `crm.invalid_company`, `crm.invalid_contact`,
`crm.invalid_user` (owner/assignee is not active staff), `crm.invalid_stage`, `crm.invalid_pipeline` (needs ≥ 1 active open
stage, exactly one Won and one Lost stage that stay active, unique names), `crm.invalid_value`, `crm.invalid_service`,
`crm.invalid_client`, `crm.currency_unsupported`, `crm.lost_reason_required`, `crm.activity_target_required`,
`crm.due_required` (tasks), `crm.occurs_at_required` (calls/meetings), `crm.invalid_scoring_field`,
`crm.invalid_scoring_value`, `crm.invalid_view`, `crm.import_empty`, `crm.import_too_large`,
`crm.import_invalid_csv`, `crm.import_invalid_header` (400); `crm.duplicate_email`, `crm.duplicate_domain`,
`crm.duplicate_link`, `crm.system_activity` (editing/deleting a system entry), `crm.stage_in_use` (removing a stage that still has deals), `crm.no_pipeline`,
`crm.import_conflict` (409).

Inbound leads are not an endpoint: the module handles the `WebsiteInquiryReceived` and `FormSubmitted` (agency
forms only) domain events. It upserts contact + company by email/domain, creates a deal in the first open stage with
UTM first/last touch, assigns an owner round-robin (SalesRep, then AccountManager), logs a system timeline note
and notifies the owner.
Replays of the same event are ignored (`crm_inbound_events` unique source key).

---

## Proposals — `/agency/proposals` (`proposals.manage`)

| Method & path | Body | Response |
|---|---|---|
| `GET /agency/proposals` | paging + `status, dealId, clientAccountId` | paged `ProposalSummaryDto` |
| `GET /agency/proposals/{id}` | – | `ProposalDto` (current version, version list, share URL if sent, view count) |
| `GET /agency/proposals/{id}/versions/{version}` | – | `ProposalDto` for that version |
| `POST /agency/proposals/preview` | `{ currency, lines: PriceLineRequest[] }` | `{ lines, totals, recurring }` (nothing saved) |
| `POST /agency/proposals` | `ProposalRequest` | 201 `ProposalDto` (Draft, number `PR-YYYY-0001`) |
| `PUT /agency/proposals/{id}` | `ProposalRequest` + stamp | `ProposalDto`. Edits the draft version; if the current version was already sent, creates a new version (the sent one stays immutable and the customer sees "being revised" until you send again). |
| `POST /agency/proposals/{id}/send` | `{ concurrencyStamp, message?, email: true }` | `{ proposal, shareUrl, emailed }`; issues/keeps the 256-bit share token |
| `POST /agency/proposals/{id}/withdraw` | `{ concurrencyStamp, reason? }` | `ProposalDto` (link stops accepting) |

```jsonc
// ProposalRequest
{ "title", "dealId?", "clientAccountId?", "companyId?", "contactId?", "currency?": "USD",
  "validUntil?": "2026-10-31", "executiveSummary?", "goals?", "scope?", "deliverables?", "timeline?", "terms?",
  "recipientName?", "recipientEmail?", "invoiceOnAcceptance?": true, "lines": [PriceLineRequest], "concurrencyStamp?" }
```

Errors: `proposal.name_required`, `proposal.invalid_validity` (past date), `proposal.invalid_deal`,
`proposal.invalid_client`, `proposal.invalid_company`, `proposal.invalid_contact`, `proposal.recipient_required`
(send without an email), `proposal.terms_required` (send without terms) (400); `proposal.locked` (edit an accepted/
declined/withdrawn proposal), `proposal.not_sendable` (409).

## Public proposal — `/public/proposals/{token}` (anonymous, rate limited)

| Method & path | Body | Response |
|---|---|---|
| `GET /public/proposals/{token}` | – | `PublicProposalDto`; records a view (first view: Sent → Viewed, notifies the owner) |
| `POST /public/proposals/{token}/accept` | `{ version, fullName, title, email?, agreeToTerms: true }` | `AcceptProposalResponse` |
| `POST /public/proposals/{token}/decline` | `{ version, reason }` | `PublicProposalDto` |

```jsonc
// PublicProposalDto
{ "number", "title", "status", "agencyName", "preparedFor", "recipientName", "version": ProposalVersionDto,
  "canRespond": true, "expired": false, "beingRevised": false, "acceptedAt", "signerName", "signerTitle", "declinedAt" }
// AcceptProposalResponse
{ "proposal": PublicProposalDto, "clientAccountCreated": true, "invitationSent": true,
  "contractsCreated": 1, "invoiceCreated": true }
```

A malformed token and an unknown token both answer 404 `proposal.not_found`. Acceptance is atomic and exactly-once.
It records the typed signature, hashed IP, user agent and the version hash. It marks the deal Won, converts the
company into a client account (inviting the signer as client Owner) if needed, creates one contract per recurring
billing frequency and, when enabled, the first invoice (one-time lines + first period). A retry after success answers
`409 proposal.already_accepted`.

Errors: `proposal.terms_required` (400, `agreeToTerms` false), `validation` 400 (name/title); 409
`proposal.version_mismatch` (a newer version was sent), `proposal.being_revised`, `proposal.expired`,
`proposal.already_accepted`, `proposal.already_declined`, `proposal.not_open` (withdrawn).

## Client portal proposals — `/client/billing/proposals` (`client.portal` + Billing/Owner duty)

| Method & path | Body | Response |
|---|---|---|
| `GET /client/billing/proposals` | – | `[{ id, number, title, status, currency, total, monthlyRecurringValue, validUntil, sentAt, acceptedAt }]` |
| `GET /client/billing/proposals/{id}` | – | `PublicProposalDto` |
| `POST /client/billing/proposals/{id}/accept` | as public accept | `AcceptProposalResponse` |
| `POST /client/billing/proposals/{id}/decline` | as public decline | `PublicProposalDto` |

Only sent, non-withdrawn proposals of the member's organizations are visible. Another organization's proposal answers
404. A Viewer or Approver member gets `403 client.insufficient_role`.

---

## Contracts / retainers — `/agency/contracts` (`contracts.manage`)

| Method & path | Body | Response |
|---|---|---|
| `GET /agency/contracts` | paging + `status, clientAccountId` | paged `ContractSummaryDto` (incl. MRR) |
| `GET /agency/contracts/{id}` | – | `ContractDto` (lines, totals, next invoice date, invoices) |
| `POST /agency/contracts` | `ContractRequest` | 201 `ContractDto` (Draft) |
| `PUT /agency/contracts/{id}` | `ContractRequest` + stamp | `ContractDto` (the client can't change once invoiced) |
| `POST /agency/contracts/{id}/activate` | `{ concurrencyStamp }` | `ContractDto` |
| `POST /agency/contracts/{id}/pause` | `{ concurrencyStamp }` | `ContractDto` (no invoices while paused; skipped periods are not back-billed) |
| `POST /agency/contracts/{id}/resume` | `{ concurrencyStamp }` | `ContractDto` |
| `POST /agency/contracts/{id}/cancel` | `{ reason, confirm: true, concurrencyStamp }` | `ContractDto` |
| `POST /agency/contracts/{id}/generate-invoices` | – | `ContractDto`. Generates any due periods now; idempotent per period. |

```jsonc
// ContractRequest
{ "clientAccountId", "title", "currency?", "startDate", "endDate?", "billingFrequency": "Monthly",
  "autoRenew": true, "renewalTermMonths": 12, "noticePeriodDays": 30, "paymentTermsDays?": 14,
  "autoIssueInvoices?": false, "notes?", "lines": [PriceLineRequest /* recurrence ignored */], "concurrencyStamp?" }
```

Errors: `billing.client_required`, `billing.invalid_start`, `billing.invalid_end` (400);
`billing.contract_state` (invalid transition), `billing.contract_closed`, `billing.contract_client_locked` (409).

---

## Billing — `/agency/billing` (read: `billing.view`; write: `billing.manage`; settings: `billing.settings`)

### Invoices

| Method & path | Perm | Body | Response |
|---|---|---|---|
| `GET /agency/billing/invoices` | view | paging + `status, clientAccountId, openOnly, contractId` | paged `InvoiceSummaryDto` |
| `GET /agency/billing/invoices/{id}` | view | – | `InvoiceDto` (lines, totals, payments, credits, reminders, public URL) |
| `GET /agency/billing/invoices/{id}/document` | view | – | printable HTML download |
| `POST /agency/billing/invoices/preview` | manage | `{ currency, lines }` (OneTime only) | `{ lines, totals, recurring }` |
| `POST /agency/billing/invoices` | manage | `InvoiceDraftRequest` | 201 `InvoiceDto` (Draft, no number yet) |
| `PUT /agency/billing/invoices/{id}` | manage | `InvoiceDraftRequest` + stamp | `InvoiceDto` (drafts only) |
| `DELETE /agency/billing/invoices/{id}` | manage | – | 204 (drafts only) |
| `POST /agency/billing/invoices/{id}/issue` | manage | `{ concurrencyStamp, send }` | `InvoiceDto`. Allocates a gapless number (`OA-2026-0001`), freezes lines, sets issue and due dates, and emails the client when `send` is true. |
| `POST /agency/billing/invoices/{id}/send` | manage | `{ concurrencyStamp }` | `InvoiceDto` (emails the billing contacts with the public link) |
| `POST /agency/billing/invoices/{id}/void` | manage | `{ reason, confirm: true, concurrencyStamp }` | `InvoiceDto` |
| `POST /agency/billing/invoices/{id}/write-off` | manage | `{ reason, confirm: true, concurrencyStamp }` | `InvoiceDto` |
| `POST /agency/billing/invoices/{id}/payments` | manage | `RecordPaymentRequest` | `{ payment, invoice, replayed }` |

```jsonc
// InvoiceDraftRequest
{ "clientAccountId", "currency?", "paymentTermsDays?": 14, "notes?", "reference?", "lines": [PriceLineRequest], "concurrencyStamp?" }
// RecordPaymentRequest
{ "requestId": "guid (idempotency key)", "amount": 500.00, "method": "BankTransfer", "reference": "TRX-123",
  "paidOn?": "2026-09-22", "notes?", "concurrencyStamp": "invoice stamp" }
```

Payment rules: resending the same `requestId` with the same payload returns the original result with
`replayed: true`. The same `requestId` with a different payload answers `409 billing.request_id_reused`. The same
`reference` twice on one invoice answers `409 billing.duplicate_reference`. An amount above the balance answers
`409 billing.overpayment`. The invoice becomes PartiallyPaid or Paid, and the `InvoicePaid` event fires once, on the
transition to Paid.

Invoice errors: `billing.invalid_amount`, `billing.invalid_reference`, `billing.paid_on_invalid`,
`billing.paid_on_in_future`, `billing.client_required` (400); `billing.invoice_not_draft`,
`billing.invoice_not_open`, `billing.invoice_not_sendable`, `billing.invoice_has_payments` (void after payments: use
a credit note), `billing.overpayment`, `billing.duplicate_reference`, `billing.request_id_reused` (409);
`billing.four_eyes` (the issuer cannot void or write off their own invoice), `billing.self_payment` (403, the recorder is a member of the invoice's client organization).

### Payments & credit notes

| Method & path | Perm | Body | Response |
|---|---|---|---|
| `GET /agency/billing/payments` | view | paging + `clientAccountId, from, to` | paged `PaymentDto` |
| `GET /agency/billing/credit-notes` | view | paging + `clientAccountId, status` | paged `CreditNoteDto` |
| `GET /agency/billing/credit-notes/{id}` | view | – | `CreditNoteDto` (applications) |
| `POST /agency/billing/credit-notes` | manage | `{ requestId, invoiceId?, clientAccountId?, currency?, amount, reason }` | 201 `CreditNoteDto`. With `invoiceId`, the credit is applied to that invoice immediately, up to its balance. |
| `POST /agency/billing/credit-notes/{id}/apply` | manage | `{ invoiceId, amount }` | `CreditNoteDto` |

Errors: `billing.credit_exceeds_balance`, `billing.credit_exhausted`, `billing.credit_mismatch` (currency/client) (409).

### Overview, clients, statements, reports

| Method & path | Perm | Query | Response |
|---|---|---|---|
| `GET /agency/billing/overview` | view | – | outstanding/overdue by currency, collected this month, MRR, recent invoices |
| `GET /agency/billing/clients` | view | `search` | `ClientOptionDto[]` (≤ 200, scoped) |
| `GET /agency/billing/clients/{clientAccountId}/statement` | view | `currency, from, to` | `StatementDto` (opening balance, running balance lines) |
| `GET /agency/billing/reports/aging` (`.csv`) | view | `asOf` | per client+currency: current, 1–30, 31–60, 61–90, 90+ |
| `GET /agency/billing/reports/revenue` (`.csv`) | view | `groupBy=month|client|service, from, to` | invoiced (net of tax) vs collected |
| `GET /agency/billing/reports/mrr` (`.csv`) | view | – | MRR/ARR per client and per currency (active contracts) |
| `GET /agency/billing/reports/collections` (`.csv`) | view | `from, to` | payments by month, currency and method |

Amounts are never converted between currencies; every report groups by currency. `billing.invalid_range` (400)
when `from > to` or the range exceeds 10 years.

### Settings, tax rates, jobs

| Method & path | Perm | Body | Response |
|---|---|---|---|
| `GET /agency/billing/settings` | view | – | `BillingSettings` |
| `PUT /agency/billing/settings` | settings | `{ settings: BillingSettings, reason, confirm: true }` | `BillingSettings` (audited) |
| `GET /agency/billing/tax-rates` | view | `includeInactive` | `TaxRateDto[]` |
| `POST /agency/billing/tax-rates` | settings | `{ name, ratePercent, inclusive, countryCode?, isActive, needsReview, notes? }` | 201 `TaxRateDto` |
| `PUT /agency/billing/tax-rates/{id}` | settings | same + `concurrencyStamp` | `TaxRateDto` (existing documents keep their snapshot) |
| `POST /agency/billing/jobs/recurring-invoices/run` | manage | – | `{ ran, status, summary }` |

```jsonc
// BillingSettings
{ "invoicePrefix": "OA", "creditNotePrefix": "CN", "contractPrefix": "CT", "proposalPrefix": "PR", "numberPadding": 4,
  "paymentTermsDays": 14, "defaultCurrency": "USD", "invoiceOnAcceptance": true, "autoIssueInvoices": false,
  "remindersEnabled": true, "reminderOffsetsDays": [-3, 0, 7, 14], "companyName": "Optimize All",
  "companyAddress", "companyTaxId", "companyEmail", "bankDetails", "paymentLinkText", "paymentInstructions",
  "invoiceFooter", "defaultTaxRateId" }
```

Errors: `billing.invalid_settings` (400 with `errors`), `billing.invalid_tax`, `billing.invalid_tax_rate` (400).

---

## Client portal billing — `/client/billing` (`client.portal` + Billing/Owner duty)

| Method & path | Query / body | Response |
|---|---|---|
| `GET /client/billing/summary` | – | `{ organizations: [{ clientAccountId, name, currency, role }], outstanding: [{currency, amount}], overdue: [...], openInvoices, proposalsAwaitingResponse }` |
| `GET /client/billing/invoices` | paging + `status, clientAccountId, openOnly` | paged `InvoiceSummaryDto` (never drafts) |
| `GET /client/billing/invoices/{id}` | – | `PublicInvoiceDto` |
| `GET /client/billing/invoices/{id}/document` | – | printable HTML download |
| `POST /client/billing/invoices/{id}/pay` | – | `{ available: false, redirectUrl: null, message }` (no gateway configured yet) |
| `GET /client/billing/payment-instructions` | – | `{ bankDetails, paymentLinkText, paymentInstructions, companyName }` |
| `GET /client/billing/contracts` | – | `ContractSummaryDto[]` |
| `GET /client/billing/statement` | `clientAccountId, currency, from, to` | `StatementDto` |

Viewer and Approver members get `403 client.insufficient_role` (the portal explains the restriction). Another
organization's records answer 404.

## Payments hub — `/admin/payments` (see `docs/SALES_AND_BILLING.md` and `docs/PAYOUTS.md`)

One place for incoming client payments and outgoing participant payouts. Reads need `billing.view` (incoming) or
`payouts.view` (outgoing); `GET /admin/payments/capabilities` tells the UI which parts the caller has.

| Method & path | Permission | What it does |
|---|---|---|
| `GET /admin/payments`, `GET …/summary`, `GET …/records/{kind}/{id}`, `GET …/export.csv` | billing.view / payouts.view | unified list, totals, one record, CSV (≤ 20,000 rows; over the cap: 422 `export.too_large`) |
| `POST …/invoices/{invoiceId}/payments`, `POST …/invoices/{invoiceId}/mark-paid` | billing.manage | record a payment / settle the balance |
| `PATCH …/invoice-payments/{paymentId}`, `POST …/invoice-payments/{paymentId}/reverse` | billing.manage | correct or reverse a recorded payment |
| `POST …/invoice-payments/{paymentId}/proofs`, `GET …/proofs/{proofId}` | billing.manage / billing.view | attach and read payment proofs |
| `POST …/invoices/{invoiceId}/reminders`, `GET …/invoices/{invoiceId}/reminders` | billing.manage / billing.view | send a reminder now (idempotent per request id) and its history |
| `GET …/reminders/preview`, `GET/PUT …/reminder-policies/{clientAccountId}` | billing.view / billing.settings | what the reminder job would send; per-client schedule |
| `GET …/claims`, `POST …/claims/{claimId}/confirm`, `POST …/claims/{claimId}/reject` | billing.view / billing.manage | client "I've paid" reports |
| `POST …/payouts/{itemId}/mark-paid`, `…/mark-failed`, `POST …/payout-batches/{batchId}/mark-paid` | payouts.record_payment | outgoing payouts |

Client side (`/client/billing`, `client.portal` + Billing/Owner duty): `GET invoices/{id}/payments`,
`POST invoices/{id}/payment-claims` (report a payment), `POST payment-claims/{claimId}/proofs`,
`GET payment-proofs/{proofId}`. Notifications: `billing.payment_claimed` (finance and the account manager),
`billing.payment_claim_reviewed` (the reporting client user), `billing.invoice_reminder`, `billing.invoice_paid` — all
listed in the notification preference matrix of the users who receive them.

## Public invoice — `/public/invoices/{token}` (anonymous, rate limited)

| Method & path | Response |
|---|---|
| `GET /public/invoices/{token}` | `PublicInvoiceDto` (agency details, lines, totals, balance, payment instructions) |
| `GET /public/invoices/{token}/document` | printable HTML download |

Drafts, and malformed or unknown tokens, answer 404 `invoice.not_found`. A voided invoice still renders, marked Void.

---

## Background jobs

| Job | Schedule | What it does |
|---|---|---|
| `RecurringInvoiceJob` | hourly | For each Active contract with a due period, creates (and optionally issues) the period invoice with key `contract:{id}:{periodStart}`; ends contracts past their end date unless auto-renewing. |
| `InvoiceOverdueJob` | hourly | Marks open invoices past due as Overdue, and sends at most one reminder per invoice per run (the latest applicable offset: 3 days before, on the due date, 7 days after, 14 days after), recorded in `invoice_reminders`. |
| `CrmTaskReminderJob` | every 5 min | Sends task reminders (`remindAt`/overdue) to assignees and expires proposals past `validUntil`. |

## Notifications and events

Notification types (in-app; email where the recipient's preferences allow): `crm.lead_assigned`,
`crm.task_reminder`, `crm.task_overdue`, `crm.proposal_viewed`, `crm.proposal_accepted`, `crm.proposal_declined`,
`billing.invoice_issued`, `billing.invoice_reminder`, `billing.invoice_paid`, `billing.proposal_received`,
`billing.payment_claimed`, `billing.payment_claim_reviewed`. Recipients choose email per kind in their notification
preferences (`/me/notification-preferences`; staff and client users open it from the account menu).

Domain events published: `ProposalAccepted`, `InvoicePaid`. Events handled: `WebsiteInquiryReceived`,
`FormSubmitted`, `ContactEngagementRecorded`.

## Audit actions

`crm.company_created|updated`, `crm.contact_created|updated`, `crm.contacts_imported`, `crm.pipeline_updated`,
`crm.deal_created|updated|moved|won`, `crm.deal_contact_added|removed`, `crm.activity_created|updated|deleted`,
`crm.task_completed|reopened`, `crm.scoring_rule_created|updated|deleted`, `crm.inbound_lead`,
`crm.proposal_created|updated|revised|sent|withdrawn|accepted|declined|expired`,
`billing.invoice_created|updated|deleted|issued|sent|voided|written_off|paid|credited|reminder_sent`,
`billing.payment_recorded`, `billing.credit_note_issued|applied`,
`billing.contract_created|updated|invoiced|renewed|ended`, `billing.recurring_invoice_created`,
`billing.settings_updated`, `billing.tax_rate_created|updated`.
