# Sales & Billing guide

This guide covers the CRM, sales pipeline, proposals, contracts (retainers) and client billing in Optimize All.
The API reference is [`api/crm-billing.md`](api/crm-billing.md).

## Who can do what

| Area | Permission | Default roles |
|---|---|---|
| View CRM (contacts, companies, deals, tasks) | `crm.view` | Admin, Sales Rep, Account Manager, Strategist |
| Edit CRM, move deals, import contacts, scoring rules | `crm.manage` | Admin, Sales Rep, Account Manager |
| Build, send and withdraw proposals | `proposals.manage` | Admin, Sales Rep, Account Manager |
| Contracts and retainers | `contracts.manage` | Admin, Account Manager |
| See invoices, payments and reports | `billing.view` | Admin, Account Manager, Sales Rep |
| Create or issue invoices, record payments, credit notes, void or write off | `billing.manage` | Admin |
| Billing settings and tax rates | `billing.settings` | Admin |
| Client portal billing | `client.portal` plus the **Billing** or **Owner** duty in the organization | client users |

The defaults come from the platform's role matrix; an administrator can change them. Staff who are not agency-wide
(for example an Account Manager) only see the clients they are assigned to.

## CRM

**Contacts and companies.** Contacts are deduplicated by email and companies by domain. Each record has an owner,
tags, a lifecycle stage (Subscriber → Lead → MQL → SQL → Opportunity → Customer → Evangelist) and a consent status.
Companies can hold custom fields. The contact page shows a timeline of notes, calls, meetings, emails, tasks and
system events (stage changes, proposals, inbound forms), with the first and last UTM touch.

**Import and export.** On *CRM → Contacts → Import*, upload a CSV (at most 2 MB) with a header row. The columns are
`first_name, last_name, email, phone, job_title, company, company_domain, lifecycle_stage, consent, source, tags`.
Run a *dry run* first: it shows the result for each row without saving anything. Existing emails are skipped unless
you tick *Update existing*. *Export* downloads the current filtered list as a CSV.

**Inbound leads.** Website inquiries and agency form submissions create or update the contact and company. They also
open a deal in the first stage with the UTM source and assign an owner round-robin: Sales Reps first, then Account
Managers. The inquiry (message, services, budget, UTM) is logged on the deal's timeline and the owner is notified.
The same submission is never processed twice.

**Lead scoring.** Under *CRM → Settings → Scoring*, rules add points for **fit** (industry, company size, budget
range, country, source, lifecycle stage) and **engagement** (website inquiries, form submissions, email opens and
clicks, proposal views, booked meetings, newsletter sign-ups). Each rule can
be capped at a maximum number of occurrences. The score shows on contacts and deals, with a breakdown. After changing
rules, press *Recompute* to update every contact.

**Pipeline.** *CRM → Deals* shows a kanban board with count and value per stage, per currency. Drag a card to move
it. With the keyboard, focus a card's move handle, press **Enter** or **Space** to pick it up, use the **arrow keys**
to choose a stage, **Enter** to drop and **Escape** to cancel; a screen reader announces each step. Moving a deal to
**Lost** asks for a reason. Stages, their win probability and their order are edited under *CRM → Settings →
Pipeline*. The pipeline always has exactly one Won stage and one Lost stage, and a stage that still has deals can't
be removed. Weighted value is the deal value multiplied by the stage's win probability.

**Tasks.** *CRM → Tasks* lists your open tasks with overdue ones first. A task can have a reminder time; reminders
and overdue notices are sent by a background job every few minutes.

**Saved views.** The deal and contact lists can save their filters as a private or shared view.

## Proposals

1. **Build.** Go to *Proposals → New*, or start from a deal. Fill in the executive summary, goals, scope,
   deliverables, timeline and terms, then add price lines. Each line has a quantity, unit price, discount (percent or
   amount), tax rate and recurrence (one-time, monthly, quarterly, annually). The totals panel is calculated by the
   server as you type. It shows the tax breakdown, the monthly recurring value, the first invoice and the first-year
   value.
2. **Send.** Choose *Send*. The proposal needs a recipient email, terms and a validity date in the future. The
   customer receives a private link (`/p/…`). You can also copy the link. Opening the link marks the proposal
   **Viewed** and notifies you.
3. **Revise.** Editing a sent proposal creates a new **version**. The customer's link shows "being revised" until
   you send the new version, and earlier versions stay readable.
4. **Accept or decline.** On the link, or in the client portal, the customer types their full name and job title and
   ticks *I agree to the terms*. They can only accept the version you last sent, before it expires. The signature,
   time, IP hash and browser are recorded.

Acceptance happens exactly once, and in the same step:
- The deal is marked **Won**.
- If the company isn't a client yet, a client account is created and the signer is invited as its **Owner**.
- One **contract** is created for each billing frequency on the proposal (monthly, quarterly, annual).
- The **first invoice** is created, when *Invoice on acceptance* is on. It covers the one-time lines plus the first
  period of the recurring lines. It stays a draft unless *Auto-issue invoices* is on.

Proposals expire automatically after their validity date. You can **withdraw** a sent proposal, which disables its
link.

## Contracts and retainers

A contract has a client, currency, start date, optional end date, billing frequency, auto-renewal (term in months),
notice period, payment terms and lines. **Activate** a draft to start billing. The recurring job runs every hour and
creates one invoice per period: exactly one per period, even if the job is run twice.
- **Pause** stops invoicing. Periods skipped while paused are not billed later.
- **Resume** continues from the next period.
- **Cancel** needs a reason and confirmation.
- A contract past its end date is **Ended**, unless it auto-renews, in which case it is extended by the renewal term.

*Generate invoices now*, on a contract, runs the same logic for that contract only.

## Invoices

**Lifecycle:** Draft → Issued → Partially paid → Paid, and Overdue once past the due date. **Void** and
**Written off** close an invoice without payment.

- **Drafts** can be edited or deleted freely and have no number yet.
- **Issuing** assigns the next number in a gapless sequence (for example `OA-2026-0007`). It freezes the lines,
  including a snapshot of the tax rate, and sets the due date from the payment terms. Tick *Send* to email the client
  at the same time.
- **Sending** emails the client's billing contacts a private link (`/i/…`) to view and download the invoice.
- **Recording a payment** needs the amount, method, reference and date. You can't record more than the balance, and
  the same reference twice on one invoice is refused. If your connection drops and you retry, the payment is not
  duplicated. When the balance reaches zero the invoice becomes **Paid** and the account manager is notified.
- **Credit notes** reduce what the client owes. Issue one against an invoice, where it is applied immediately, or
  for the client in general and apply it to invoices later. Once an invoice has payments, use a credit note instead
  of voiding it.
- **Void** and **write-off** need a reason and confirmation, and must be done by a different person from the one who
  issued the invoice (four-eyes).
- **Reminders:** when enabled, the client gets one reminder 3 days before the due date, on the due date, 7 days after
  and 14 days after. The schedule can be changed in settings, and per client (see *Payments hub*). At most one reminder
  is sent per run, so a client never receives several at once.
- **Correcting a payment:** the reference, date, method and notes of a payment can be edited (with a reason). The
  amount can't: reverse the payment and record it again. See *Payments hub*.

Every total is calculated on the server, rounded to the currency's minor units (for example 0 decimals for JPY and 3
for KWD). Amounts are never converted between currencies: overviews and reports show one figure per currency.

## Payments hub

*Finance → Payments* (`/finance/payments`) is one list of every payment: money received from clients (invoice
payments, open balances, client "I've paid" reports) and participant payouts (see [PAYOUTS.md](PAYOUTS.md#payments-hub)).
It is for Finance and Admin. Who sees and does what:

| What | Permission |
|---|---|
| See incoming records and KPIs (client-scoped) | `billing.view` |
| See outgoing records (payouts) | `payouts.view` |
| Record, edit, reverse, refund, mark paid, confirm/reject client reports, send reminders | `billing.manage` |
| Mark payouts paid, failed or returned | `payouts.record_payment` |
| Per-client reminder schedule | `billing.settings` |

The page opens in the finance portal for users with `payouts.view` or `billing.manage`. Account managers and sales reps
keep using *Agency → Billing*.

**What you see.** KPIs per currency (amounts in different currencies are never added up or converted): received this
month (net of refunds and reversals), outstanding receivables by age (current, 1–30, 31–60, 61–90, 90+ days), payouts
due in batches, the next payout cycle's estimate and what was paid out this month. The table can be filtered by
direction, status, type, method, client, participant, invoice, batch, dates and "overdue only", and exported as CSV
(formula-safe; at most 20,000 rows). Click a row for its detail: the invoice's payments, proof files, reminders sent and
the audit history.

Statuses: **Scheduled** (payout in a draft batch), **Pending** (an open invoice balance, an unconfirmed client report
or a payout awaiting payment), **Paid**, **Failed** (a payout that bounced), **Refunded** and **Voided** (reversed as an
error, rejected report, cancelled payout).

**Manual actions** (every one asks for what it needs, is audited, and is safe to retry — each dialog sends one
idempotency key, so a double click or a retry after a timeout never records twice; a stale screen gets "changed by
someone else"):

- **Record payment** — bank transfer, cash, cheque, card (offline terminal), PayPal, Stripe or other, with reference,
  date, amount and notes. Overpayments are refused, as everywhere in billing. Attach a proof file (PDF, PNG, JPEG or
  WebP, up to 10 MB) from the row afterwards.
- **Mark paid in full** — records the remaining balance as one payment.
- **Edit** — reference, date, method, notes, with a reason. Never the amount.
- **Reverse** (recorded in error) or **Refund** (money sent back) — with a reason. A negative reversal row is added,
  the original stays visible as Voided/Refunded, and the invoice balance goes back up (a paid invoice reopens and can be
  paid again, even with the same bank reference). A refund must be recorded by someone other than the person who
  recorded the payment (four-eyes); reversing your own entry error is allowed.
- **Send reminder now** — emails the client's billing contacts (same template and delivery as the scheduled reminders;
  in `Email:Mode=File` nothing leaves the server). At most one per invoice per hour.
- **Confirm / reject client report** — see below.

**Client "I've paid".** In the client portal each invoice shows its payment history and an *I've paid* button (Billing
or Owner members, open invoices only). The client enters the amount, method, transfer reference, date and optionally a
proof file. Nothing changes on the invoice yet: Finance users and the account manager are notified, the report appears
in the hub as Pending, and a finance user confirms it once the money is on the bank statement (the amount can be
corrected) — which records the payment — or rejects it with a reason the client sees.

**Reminder schedule per client.** `PUT /admin/payments/reminder-policies/{clientId}` sets days relative to the due date
(e.g. 3, 7, 14), turns reminders off for that client, or goes back to the agency schedule. The hourly job uses the
client's schedule when there is one and still sends each stage once per invoice. `GET /admin/payments/reminders/preview`
shows what the job would send today without sending anything.

API (`/api/v1/admin/payments`): `GET /` (list), `GET /summary`, `GET /capabilities`, `GET /records/{kind}/{id}`,
`GET /export.csv`, `POST /invoices/{id}/payments`, `POST /invoices/{id}/mark-paid`, `PATCH /invoice-payments/{id}`,
`POST /invoice-payments/{id}/reverse`, `POST /invoice-payments/{id}/proofs`, `GET /proofs/{id}`,
`POST|GET /invoices/{id}/reminders`, `GET /claims`, `POST /claims/{id}/confirm`, `POST /claims/{id}/reject`,
`GET /reminders/preview`, `GET|PUT /reminder-policies/{clientId}`, `POST /payouts/{itemId}/mark-paid`,
`POST /payouts/{itemId}/mark-failed`, `POST /payout-batches/{id}/mark-paid`. Client portal:
`GET /client/billing/invoices/{id}/payments`, `POST /client/billing/invoices/{id}/payment-claims`,
`POST /client/billing/payment-claims/{id}/proofs`, `GET /client/billing/payment-proofs/{id}`.

### Runbook: incoming payments

1. Each morning open *Finance → Payments*, filter *Status: Pending*. Check client reports ("I've paid") against the
   bank statement: **Confirm** the ones you find (correct the amount if the bank shows a different one), **Reject** the
   others with a clear reason.
2. For money that arrived without a report, **Record payment** on the invoice (or **Mark paid in full**) with the bank
   reference and the value date. Attach the bank slip for cash and cheques.
3. Filter *Overdue invoices only*: send a reminder where the scheduled ones were not enough, or agree a different
   cadence for the client (per-client reminder schedule).
4. A payment on the wrong invoice or with the wrong amount: **Reverse** it (recorded in error) with the reason, then
   record it correctly. Money sent back to a client: ask a second finance user to **Refund** it.
5. Month end: export the CSV for the month and reconcile *Received this month* per currency with the bank.

## Reports

Every report can be downloaded as CSV.
- **AR aging:** outstanding balances per client, bucketed into current, 1–30, 31–60, 61–90 and 90+ days past due.
- **Revenue:** invoiced (excluding tax) against collected, by month, client or service.
- **MRR / ARR:** from active contracts, per client and per currency.
- **Collections:** payments by month, currency and method.
- **Client statement:** available on each client. It shows the opening balance, invoices, payments and credits with
  a running balance.

## Settings

*Billing → Settings*, which requires `billing.settings`, holds the following. Every change asks for a reason and is
audited.
- Number prefixes and padding.
- Default payment terms and currency.
- Invoice on acceptance and auto-issue.
- Reminder schedule.
- Company details, bank details, payment instructions and invoice footer.
- Tax rates.

Changing a tax rate never alters documents that were already issued. The baseline tax rates are placeholders marked
**Needs review**: check them with your accountant before going live.

## Client portal

Client users with the **Billing** or **Owner** duty see *Billing* in the portal. It shows open and overdue amounts,
invoices (view and download), proposals waiting for a response (accept or decline), active contracts, payment
instructions and a statement. **Pay online** reports that online payment isn't available yet; clients pay by bank
transfer using the instructions shown. Viewer and Approver members see an explanation instead of billing data.

## Demo data

The `Demo` seed profile (order 300) adds pipeline deals across every stage, contacts and companies, and three
proposals (accepted, sent, draft). It also adds contracts for the four demo clients, about four months of invoices in
USD, GBP, AED and PKR (paid, partially paid, overdue, not yet due) and an applied credit note. It runs once, guarded by the
`demo.crm-billing.seeded` marker. All demo accounts use the password `Demo#2026!pass`:

| Email | Role |
|---|---|
| `sales@demo.optimizeall.app` | Sales Rep (Omar Farooq) |
| `am@demo.optimizeall.app` | Account Manager (Amira Haddad) |
| `owner@nimbus.demo.optimizeall.app` | Nimbus Fitness Owner: sees billing |
| `billing@nimbus.demo.optimizeall.app` | Nimbus Fitness Billing: sees billing |
| `approver@nimbus.demo.optimizeall.app` | Nimbus Fitness Approver: billing hidden |

## Editing, archiving and configurable lists

- **Archive instead of delete.** Contacts, companies and deals can be archived (and restored) from their page or in bulk
  from the lists (select rows, then archive, restore, assign an owner, set the lifecycle stage, or add/remove a tag).
  Archived records are hidden from lists and pickers and are read-only until restored; the *Show: Archived* filter lists
  them. A company that is a client can't be archived, nor a deal with a proposal waiting for the client.
- **Options** under *CRM → Settings*: lost reasons (offered when a deal is lost), budget ranges and industries.
- **Proposal templates** under *CRM → Settings* (sections and price lines); pick one with *Start from a template* in the
  proposal builder. A proposal that was never sent can be deleted; any proposal can be **duplicated** into a new draft.
- **Service catalog** under *Billing → Settings*: what you sell and its list price. Every line editor (proposals,
  contracts, invoices) has *Add from catalog*. Editing the catalog never changes existing documents.
- **Payment terms offered** and the **reminder schedule** are edited under *Billing → Settings*; invoice and contract
  editors offer the configured terms.
- A **draft contract** that never billed can be deleted; an invoice in any status can be **duplicated** into a new
  draft. An unused tax rate can be deleted; a used one is deactivated instead.

See [`CRUD_COVERAGE_SALES_DELIVERY.md`](CRUD_COVERAGE_SALES_DELIVERY.md) for the full coverage table.

## Known limitations

- Online card payment needs a payment gateway; until one is configured, clients pay by bank transfer.
- Finance manages billing (`billing.view`, `billing.manage`, `billing.settings`) and can read client accounts
  (`clients.view`); account managers and sales reps can view invoices. Voids and write-offs need a second person;
  credit notes do not, so a person who issued an invoice can credit its balance themselves (audited).
- Revenue reports show gross invoiced amounts and don't net out credit notes. Collections and revenue reports count a
  reversal or refund as a negative payment on its date, so their totals are net.
- Overpayments are refused (record the balance and refund the rest); there is no client credit balance yet.
- Service slugs on lines are free text. They are not yet validated against the website's service catalog.
