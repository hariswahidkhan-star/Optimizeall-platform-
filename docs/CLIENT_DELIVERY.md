# Client delivery guide

How the agency runs client work in Optimize All: clients, projects, tasks, deliverable approvals, time,
reports and the client portal. API details are in [`docs/api/clients-projects.md`](api/clients-projects.md).

## Who does what

| Person | Portal | Typical permissions | Can |
|---|---|---|---|
| Account manager | Agency (`/agency`) | clients.manage, projects.manage, reports.manage, time.view_all | Everything below, plus client health, SLA settings, timesheet approval, publishing reports |
| Strategist / specialist / designer | Agency | clients.view, projects.view, deliverables.submit, time.track | Work tasks, upload versions, review deliverables they are assigned to, track time |
| Client **Owner** | Client (`/client`) | client.portal | Approve, brief, message, invite and remove colleagues |
| Client **Approver** | Client | client.portal | Approve or request changes, submit briefs, comment and message |
| Client **Billing** / **Viewer** | Client | client.portal | Read only for delivery (including messages, see below) |

**Who may post messages.** Only Owners and Approvers write in conversations (start one or reply). Viewers and Billing
users read every conversation shared with the client but have no composer, and the API refuses their posts with
`403 client.insufficient_role`. There are no billing-specific conversations, so Billing users are read-only for
messages too (invoices and payments have their own pages). Each conversation says whether the signed-in user can reply
(`canReply`).

A client user can belong to several organizations; the **Organization** switcher at the top of the client portal
changes between them (the choice is remembered on that device and can be linked with `?org=<id>`).

## Setting up a client

1. **Clients → New client.** Enter name, website, country, currency and time zone. The client starts as
   *Onboarding* with the standard onboarding checklist (access to GA4/Search Console/ad accounts, brand assets,
   kickoff call, reporting setup, …). Items are owned by either the client or the agency. A client Owner ticks off the
   client's steps in the portal. When the client confirms a step another way (say, on the kickoff call), staff with
   clients.manage can set it to *Done* **on the client's behalf**. They confirm first, and they can undo it the same
   way. The step records who marked it done and when, the audit log records it as "completed by staff on behalf of
   client", and the client's home page lists it under *Done on your behalf by your agency team* with the staff
   member's name and the date (for 30 days).
2. **Team tab.** Assign the account manager and the service team (strategist, SEO, ads, social, content, design).
   The client sees this team, with email links, on their home page.
3. **Members tab.** Invite client users by email with a role. They receive an email to set their password. Every
   organization always keeps at least one Owner.
4. **Brand kit tab.** Colours, fonts, voice, personas, competitors, do's and don'ts, and brand files (logos,
   guidelines). The client can see the brand kit and upload assets too. Staff with clients.manage can remove an asset
   (after a confirmation; audited). Its file is deleted unless it is also used somewhere else, such as a message.
5. **Settings.** *Approval SLA* (business days the client has to give feedback, default 3) and *Auto-approve*
   (off by default; if set, deliverables still waiting after that many business days are approved automatically and
   marked "auto-approved").
6. Change the status to **Active** when onboarding is done. Pausing or churning a client requires a reason, which is
   kept in the audit log.

## Client health

Every client has a Green / Amber / Red health badge with a 0–100 score. The badge always lists its reasons, for
example "6 overdue tasks", "2 deliverables waiting on the client, oldest 8 days", "No activity for 16 days",
"Average CSAT 3.1/5", "Latest NPS response is 4/10". Other areas (such as billing) can add their own reasons. The
account-manager dashboard shows every client worst-first.

## Projects and tasks

- **Projects → New project.** Pick the client and, optionally, a template (SEO monthly retainer, social media
  monthly content, Google Ads launch, website build, email program setup). A template creates milestones, tasks
  with due dates relative to the start date, checklists and recurring tasks.
- **Board and list views.** Drag cards between columns (To do, In progress, In review, Blocked, Done). With a
  keyboard: focus a card title and use **←/→** to move it between columns and **↑/↓** to reorder. On a phone the
  board shows one column at a time with a column picker and a "Move to" menu on each card.
- **Task drawer.** Assignees, due date, estimate, labels, checklist, dependencies ("blocked by"), attachments,
  watchers and comments. Attachments can be removed (deliverables.submit, after a confirmation; audited). The file is
  deleted unless it is also used somewhere else. Type `@` in a comment to mention a teammate; they are notified. A task that is blocked by
  an unfinished task cannot be moved to Done.
- **Client-visible tasks.** Only tasks marked *Visible to client* appear in the client portal's project view.
- **Recurring tasks.** Weekly or monthly rules create the next task automatically (at most one per occurrence).
- **My tasks** lists everything assigned to you: open, overdue, due today, this week and done.

## Deliverables and approvals

A deliverable is something the client signs off: a design, a post set, an article, an ad set, a landing page, a
video, a report. It moves through:

**Draft → Internal review → Client review → Approved → Published**, with *Changes requested* sending it back.

1. Create the deliverable and add **version 1** (a file, a link, or text).
2. **Submit for internal review.** The assigned reviewer (or a project manager) approves it — which sends it to the
   client — or requests changes with a comment.
3. The client's approvers are notified and see it under **Approvals**. They compare versions side by side (older
   version on the left, latest on the right, comments pinned to each version), then **Approve version N** or
   **Request changes** (a comment is required).
4. The client's approval always refers to the exact version they looked at. If a newer version was sent in the
   meantime they are told the version is outdated and the page reloads to the latest one. If two approvers act at
   once, only the first decision counts.
5. Reminders go out once when feedback is due within 24 hours and once when it is overdue (per version).
6. After approval the client can rate the deliverable (1–5, CSAT). Staff can then mark it **Published**.

Comments marked *Internal* are never shown to the client.

## Time tracking

- **Timer.** Start the timer from the dashboard or the Time page, choosing a project (and optionally a task). You
  can have only one timer running; stopping it creates the time entry.
- **Manual entries** for anything you forgot, with billable / non-billable.
- **Timesheets.** Submit your week (Monday–Sunday). Submitted weeks are locked; a project manager approves or
  rejects (with a comment) — never their own.
- **Utilization** (time.view_all): hours, billable %, and utilization against 8 hours per weekday.
- **Budget burn** on every project: hours and cost used against the budget. Cost uses the most specific rate —
  the person's rate, then their role's rate, then the project default.
- **Export CSV** from the Time page.

## Reports

- A draft monthly report is created automatically for each active client at the start of the month; you can also
  create one by hand from a template (Monthly performance report, Campaign wrap-up).
- Each section is filled by a data source where one is connected. **Every number is labelled** *Measured* (from a
  connected source), *Estimated* (modelled) or *Manual* (typed in, with its source), and shows its source and the
  change against the previous period. Sections with no connected source say so to staff and are hidden from the
  client.
- **Publish** makes the report visible in the client portal and notifies the client. Published reports are
  read-only; the client can print them.

## Briefs, messages and meetings

- **Briefs.** Clients submit briefs from templates (social content, paid ads campaign, blog/article, SEO landing
  page, email campaign, design request, video). The account manager reviews and converts a brief into a new project
  or tasks and deliverables in an existing project.
- **Messages.** One or more threads per client, optionally about a project. Each message shows who has read it.
  **Internal threads**: when starting a conversation, staff can tick *Internal — only the agency team sees this
  conversation*. This is set when the thread is created and can't be changed later. An internal thread is marked
  *Internal* in the list, and client users never get it: it isn't listed, opening or replying to it by id is a 404,
  it isn't on their home page, its messages notify only the account team, and files attached only there are not
  served to the client. Global search and exports don't include threads. To share something from an internal thread,
  post it in a client-visible conversation.
- **Meetings.** Agenda, notes, attendees and action items; an action item can be turned into a task in one click.

## The client portal

`/client` shows, for the selected organization:

- **Home** — onboarding progress, deliverables awaiting approval, the latest report, upcoming meetings, recent
  messages, the account team and, once per quarter, a short "How likely are you to recommend us?" (NPS) question.
- **Approvals**, **Projects** (client-visible tasks and milestones), **Reports**, **Briefs**, **Messages**,
  **Brand kit**, **Team** (owners manage colleagues here) and **Feedback**.

Viewers and Billing users see the same pages read-only.

## Editing and templates

- **Templates** (*Delivery → Templates*): project, brief and report templates can be created, edited, deactivated and —
  for your own templates — deleted; built-in templates can be edited or deactivated. The **Onboarding checklist** tab
  sets the steps every new client starts with (existing clients keep theirs); on a client, steps can be edited or
  removed.
- **Milestones and recurring tasks** can be edited and deleted; recurring tasks can be paused and resumed.
- **Task comments** can be edited by their author and deleted by the author or a project manager.
- **Deliverables** can be edited (title, description, owner, reviewer); one the client never saw can be deleted.
  Approved versions never change: add a new version instead.
- **Time**: entries can be edited until the week is submitted. The owner can *Recall* a submitted week; a project
  manager can reopen someone else's approved (with a reason) or returned week.
- **Briefs** get a status (In review, Accepted, Declined); **meetings** can be edited (notes, status, action items) and
  an action item can become a task; staff can rename a **conversation**.

## Files and privacy

Uploads accept PNG, JPEG, WebP, PDF and MP4 up to 50 MB; the file content is checked, not just the extension.
Files are private: they are only served to signed-in staff and to members of the owning client, and clients only
see files attached to things shared with them (not unsent versions, task attachments or files attached only in
internal threads). Removing a brand asset or a task attachment deletes the file and its stored bytes, unless the
file is still used by another brand asset, task, deliverable version, the client logo or a message. Removals are
serialized per client and lock the row, so a second or concurrent removal gets a 404 and never deletes a file that
is still in use. Removals are refused while an admin impersonates the staff member.

## Demo data

The demo seeder (profile Demo) creates four clients — Nimbus Fitness (US, USD), Wanderly Travel (UK, GBP), Aurora
Skincare (UAE, AED) and Karachi Eats (Pakistan, PKR, onboarding) — with projects, tasks, deliverables in every
state, time entries, a published report, threads, meetings and feedback. Sign in with password `Demo#2026!pass`:

| Account | Role |
|---|---|
| am@demo.optimizeall.app | Account manager (Amira Haddad) |
| strategist@, content@, designer@, seo@, ads@, social@demo.optimizeall.app | Delivery staff |
| owner@nimbus.demo.optimizeall.app | Nimbus Fitness Owner |
| approver@nimbus.demo.optimizeall.app | Nimbus Fitness Approver |
| billing@nimbus.demo.optimizeall.app | Nimbus Fitness Billing |
| owner@aurora.demo.optimizeall.app | Aurora Skincare Owner |
