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
  and 14 days after. The schedule can be changed in settings. At most one reminder is sent per run, so a client never
  receives several at once.

Every total is calculated on the server, rounded to the currency's minor units (for example 0 decimals for JPY and 3
for KWD). Amounts are never converted between currencies: overviews and reports show one figure per currency.

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
- Revenue reports show gross invoiced amounts and don't net out credit notes.
- Service slugs on lines are free text. They are not yet validated against the website's service catalog.
