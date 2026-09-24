# CRUD coverage: Sales & Delivery

This is the inventory of every entity and workflow in the Sales (CRM, proposals, contracts, billing) and Delivery
(clients, projects, deliverables, time, reports, briefs, messages, meetings, client portal) areas. For each one it
shows what can be created, read, updated, archived or deleted, which status changes exist, and whether the API and the
UI support them. Payment recording is covered by the Payments hub and is out of scope here.

Legend: **✓** supported · **✓ new** added in this change · **—** not applicable · **✗ (reason)** deliberately not
offered, with the reason. "UI ✓" means the agency (or client) portal exposes the action. Every write is validated on
the server, audited through `IAuditLogger` and, where a row has a `ConcurrencyStamp`, stamp-checked (a stale stamp
answers **409 `concurrency.conflict`**, which the UI shows as "Someone else changed this… refresh and try again").
Client-owned rows go through `IClientScope` (another tenant's id answers 404).

## Summary

| | Before | After |
|---|---|---|
| Entities / workflows inventoried (rows below) | 46 | 46 |
| Rows with every applicable action available in API **and** UI | 17 | 46 |
| Rows where a gap was closed (marked **✓ new**) | — | 29 |
| Deliberate limits (financial records, sent/approved records, fixed task statuses) | — | 4, listed under "Deliberate limits" |

## Sales

| Entity | Create | Read / list | Update | Archive / delete | Status transitions / complete | Notes |
|---|---|---|---|---|---|---|
| Company | API ✓ UI ✓ | ✓ search, **sort ✓ new** (name, industry, updated) | ✓ | **Archive / restore ✓ new** (API + UI, bulk ✓ new); client companies can't be archived (409 `crm.company_is_client`, UI explains) | — | Archived rows are read-only (409 `crm.archived`) until restored; a new inquiry restores them automatically |
| Contact | ✓ | ✓ filters, **sort ✓ new** (name, email, lifecycle, score, updated) | ✓ | **Archive / restore ✓ new**; **bulk ✓ new**: archive, restore, assign owner, set lifecycle, add/remove tag | Lifecycle stage (form + **bulk ✓ new**) | Import/export CSV unchanged |
| Deal | ✓ | ✓ board + list, **sort ✓ new** (title, value, expected close) | ✓ | **Archive / restore ✓ new** (blocked while a proposal waits for the client, UI explains); **bulk ✓ new**: archive, restore, assign owner | Move stage / won / lost / reopen ✓ | Archived deals leave the board and the forecast |
| Pipeline stages | ✓ | ✓ | ✓ rename, reorder, probability, activate | ✓ remove (not while it has deals) | — | **Stale-edit protection ✓ new** (stage stamps) |
| Lost reasons | **✓ new** (CRM settings → Options) | ✓ | ✓ | ✓ | — | Were free text only; the Lost dialog now offers the list + "Other" |
| Budget ranges / industries | **✓ new** option lists | ✓ | ✓ | ✓ | — | Suggested in contact/deal/company forms (free text still accepted) |
| Lead scoring rules | ✓ | ✓ | ✓ | ✓ delete | active/inactive ✓ | Recompute ✓ |
| Activities & tasks | ✓ | ✓ | ✓ | ✓ delete (system entries protected) | Complete / reopen ✓ | |
| Saved views | ✓ (**share on create ✓ new**) | ✓ | **✓ new** rename, share/unshare, replace filters | ✓ delete (**with confirmation ✓ new**) | — | Owner only; others get 404 |
| Proposal | ✓ | ✓ | ✓ (sent → new version) | **Delete never-sent draft ✓ new** (sent ones: withdraw, UI explains) | Send / resend / withdraw ✓; accept / decline (client) ✓ | **Duplicate ✓ new** (re-issue withdrawn/declined/accepted) |
| Proposal line items | ✓ | ✓ | ✓ | ✓ | — | **Add from catalog ✓ new** |
| Proposal templates | **✓ new** | **✓ new** | **✓ new** | **✓ new** delete / deactivate | — | "Start from a template" in the builder |
| Service catalog (line-item presets) | **✓ new** | **✓ new** | **✓ new** | **✓ new** delete / deactivate | — | Billing settings; seeded with 7 defaults |
| Contract | ✓ | ✓ | ✓ (**payment terms, renewal term, auto-issue ✓ new in UI**; renewal term was silently reset to 12) | **Delete draft ✓ new** (never activated, not from a proposal, no invoices) | Activate / pause / resume / cancel ✓ | Generate due invoices ✓ |
| Invoice | ✓ | ✓ | ✓ draft only | ✓ delete draft (**now confirmed ✓ new**) | Issue / send / void / write-off ✓ | **Duplicate ✓ new**; issued invoices locked — UI explains credit note / void / duplicate |
| Invoice lines | ✓ | ✓ | ✓ draft | ✓ draft | — | **Add from catalog ✓ new** |
| Credit notes | ✓ | ✓ | ✗ (financial record) | ✗ (financial record) | Apply ✓ | Corrections are a new credit note |
| Tax rates | ✓ | ✓ | ✓ | **Delete unused ✓ new**; used rates → deactivate (409 `billing.tax_rate_in_use`) | active ✓ | Issued lines keep their snapshot |
| Billing settings | — | ✓ | ✓ (**contract/proposal prefixes, number digits, reminder schedule, payment-term options ✓ new in UI**) | — | — | Reason + confirm, audited |
| Payment terms | **✓ new** configurable options | ✓ | ✓ | ✓ | — | Invoice and contract editors pick from them |
| Payments | — | ✓ | — | — | — | Owned by the Payments hub |

## Delivery

| Entity | Create | Read / list | Update | Archive / delete | Status transitions / complete | Notes |
|---|---|---|---|---|---|---|
| Client | ✓ | ✓ | ✓ | Status Paused / Churned acts as archive (reason required) | Onboarding → Active → Paused → Churned ✓ | Hard delete deliberately absent (billing and audit history) |
| Client members | ✓ invite | ✓ | ✓ role | ✓ remove (last Owner protected) | — | Portal owners manage colleagues too |
| Account team | ✓ | ✓ | — | ✓ remove | — | |
| Onboarding checklist items | ✓ (**owner choice ✓ new**) | ✓ | Status ✓; **title, description, category, owner ✓ new** | **Delete ✓ new** (confirmed) | Pending / Done / N/A ✓; client owners tick client items | |
| Onboarding checklist template | **✓ new** (Templates → Onboarding checklist) | **✓ new** | **✓ new** (versioned) | ✓ | — | Was hard-coded; new clients start from it |
| Brand kit & assets | ✓ | ✓ | ✓ | ✓ asset delete | — | |
| Project | ✓ | ✓ | ✓ | Status Completed / Cancelled acts as archive | Planning / Active / On hold / Completed / Cancelled ✓ | |
| Milestones | ✓ | ✓ | **Edit title / due / client visibility ✓ new in UI** | **Delete ✓ new in UI** (audited ✓ new) | Done / reopen ✓ | |
| Tasks | ✓ | ✓ board, list, my tasks | ✓ drawer | ✓ delete (not with time logged) | Kanban statuses / Done ✓ | |
| Task comments | ✓ | ✓ | **Edit own ✓ new** ("edited") | **Delete own / manager ✓ new** | — | Mentions aren't re-notified on edit |
| Checklist, attachments, watchers | ✓ | ✓ | ✓ | ✓ | Tick ✓ | |
| Recurring tasks | ✓ | ✓ | **Edit / pause / resume ✓ new in UI** | **Delete ✓ new in UI** | — | |
| Project templates | ✓ | ✓ | ✓ (**service lines and recurring tasks editable ✓ new**) | **Delete custom ✓ new**; built-in → deactivate | active ✓ | |
| Deliverables | ✓ | ✓ | **Edit title / description / owner / reviewer ✓ new in UI** (audited ✓ new) | **Delete never-sent ✓ new** | Submit / internal approve / request changes / client approve / publish ✓ | Approved versions immutable — UI explains adding a new version |
| Deliverable versions | ✓ | ✓ compare | ✗ immutable | ✗ immutable | — | By design: a change is a new version |
| Deliverable comments | ✓ | ✓ | ✗ (client-visible record) | ✗ | — | Internal-only flag available |
| Time entries | ✓ timer + manual | ✓ | **Edit ✓ new in UI** (API existed) | ✓ | — | Locked while the week is submitted/approved |
| Timesheets | ✓ submit | ✓ | — | — | Approve / reject ✓; **recall (owner) ✓ new; reopen approved/rejected (another manager, reason) ✓ new** | Four-eyes kept |
| Hourly rates | ✓ | ✓ | ✓ | ✓ | — | |
| Reports | ✓ | ✓ | ✓ draft | ✓ delete draft | Publish ✓ | Published reports read-only |
| Report templates | **✓ new** | ✓ (**inactive listing ✓ new**) | **✓ new** | **Delete custom ✓ new**; built-in → deactivate; monthly template stays active | active ✓ new | |
| Briefs | ✓ (client + staff) | ✓ | — | — | **Status (In review / Accepted / Declined) ✓ new in UI** (audited ✓ new); convert ✓ | |
| Brief templates | **✓ new** | ✓ | **✓ new** | **Delete custom unused ✓ new**; built-in → deactivate | active ✓ | |
| Message threads | ✓ | ✓ | **Rename ✓ new** (staff) | ✗ (conversation record) | — | Read receipts ✓ |
| Messages | ✓ | ✓ | ✗ | ✗ | — | Sent messages aren't edited so both sides keep the same record |
| Meetings | ✓ | ✓ | **Edit details, notes, attendees kept, status Held / Cancelled ✓ new in UI** (audited ✓ new) | Cancelled status | **Action items: add / edit / remove ✓ new; create task ✓ new in UI** | |
| Client portal pages | — | ✓ | Onboarding ticks, approvals, briefs, messages, members, feedback ✓ | — | — | Unchanged; read-only for Viewer / Billing |

## Deliberate limits

- **Issued invoices, payments and credit notes are immutable.** Correct with a credit note, void an unpaid invoice
  (four-eyes), or duplicate into a new draft. The invoice page explains this.
- **Sent proposals, approved deliverable versions and sent messages are records.** Proposals are withdrawn or
  duplicated, deliverables get a new version, messages are followed up.
- **Task statuses and priorities stay fixed enums** (To do, In progress, In review, Blocked, Done; Low–Urgent): the
  kanban, "blocked by" rules, client-visible project view and dashboards depend on their meaning. Labels are free text.
- **Clients and projects are not hard-deleted**: statuses (Churned, Cancelled/Completed) archive them while keeping
  billing, time and audit history.

## New API endpoints

| Method & path | Permission |
|---|---|
| `POST /agency/crm/{contacts\|companies\|deals}/{id}/archive` · `/restore` (stamp) | crm.manage |
| `POST /agency/crm/{contacts\|companies\|deals}/bulk` | crm.manage |
| `GET/PUT /agency/crm/options` (versioned) | crm.view / crm.manage |
| `PUT /agency/crm/views/{id}` | owner |
| `?archived=true`, new `sort` keys on contacts, companies, deals lists | crm.view |
| `DELETE /agency/proposals/{id}?concurrencyStamp=` · `POST /agency/proposals/{id}/duplicate` | proposals.manage |
| `GET/POST/PUT/DELETE /agency/proposal-templates` | proposals.manage |
| `GET /agency/billing/catalog` · `POST/PUT/DELETE` | billing.view / billing.settings |
| `DELETE /agency/billing/tax-rates/{id}` | billing.settings |
| `POST /agency/billing/invoices/{id}/duplicate` | billing.manage |
| `DELETE /agency/contracts/{id}?concurrencyStamp=` | contracts.manage |
| `paymentTermsOptions` in billing settings | billing.settings |
| `PUT/DELETE /agency/tasks/{id}/comments/{commentId}` | author / projects.manage |
| `DELETE /agency/deliverables/{id}?concurrencyStamp=` | projects.manage |
| `POST /agency/time/timesheets/{id}/reopen` | owner (submitted) / projects.manage (decided, not own) |
| `POST/PUT/DELETE /agency/templates/briefs` · `/reports` · `DELETE /agency/templates/projects/{id}` | projects.manage / reports.manage |
| `GET/PUT /agency/settings/onboarding-template` (versioned) | clients.view / clients.manage |
| `PUT /agency/clients/{id}/onboarding/{itemId}/details` · `DELETE …/onboarding/{itemId}` | clients.manage |
| `PUT /agency/clients/{clientId}/threads/{threadId}` | clients.view (staff) |
