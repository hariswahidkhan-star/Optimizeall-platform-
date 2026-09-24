# CRUD coverage — core platform and public website

Scope: the modules Website, Content, Campaigns, Submissions, Review, Support, Marketing, Notifications, Accounts
(profile) and Admin (settings, content, jobs). Legend: ✓ available · **new** added in this pass · — not applicable (with
the reason in the notes). "UI" is the admin/portal screen; every mutating endpoint is permission-checked, audited and
(where two editors can collide) protected by a concurrency stamp with a friendly 409 message.

## Website CMS (`site.manage`, blog/careers permissions)

| Entity | Create | Read / list / search | Update | Delete / archive | Status / complete | Reorder | Notes |
|---|---|---|---|---|---|---|---|
| Service lines (categories) | ✓ API+UI | ✓ | ✓ | ✓ (409 while in use) | Publish toggle ✓ | ✓ API, **new** UI (drag + keyboard) | |
| Services + packages | ✓ | ✓ search, filter | ✓ | ✓ | Publish/featured ✓, package active ✓ | ✓ API, **new** UI | |
| Industries | ✓ | ✓ | ✓ | ✓ | Publish ✓ | **new** API + UI | |
| Case studies | ✓ | ✓ filter | ✓ | ✓ | Publish/featured ✓ | **new** API + UI | |
| Testimonials | ✓ | ✓ | ✓ | ✓ | Publish/featured ✓ | ✓ API, **new** UI | |
| Team members (also blog authors) | ✓ | ✓ | ✓ | ✓ | Publish ✓ | ✓ API, **new** UI | |
| CMS pages (incl. legal) | ✓ | ✓ | ✓ with live preview | ✓ API + **new** UI (editor and list, confirmation) | Publish ✓, **new** scheduled go-live | — (menu order is in Site settings) | **new** version history, preview old version, restore, change notes; renaming a live page **new** redirects the old address (301) |
| Blog posts | ✓ | ✓ filter, search | ✓ | ✓ | Draft → review → schedule/publish → unpublish/return ✓ | — (date ordered) | Markdown sanitised server-side |
| Blog categories | ✓ | ✓ | ✓ | ✓ | — | **new** API + UI | |
| Job openings | ✓ | ✓ | ✓ | ✓ | Draft/Open/Closed ✓ | — | |
| Job applications | Public form | ✓ filter, pipeline board | Stage move ✓, notes ✓ | **new** erase (application, notes, CV) | Pipeline stages ✓ | — | CV download audited |
| Website inquiries | Public forms | ✓ filter (type, status, **new** assignee), search, CSV | Status, notes ✓, **new** assignee picker, **new** "Mark closed" | **new** erase | New/In progress/Qualified/Converted/Closed/Spam ✓ | — | |
| Consultations | Public booking | ✓ filter | Reschedule ✓ | Cancel ✓ | Completed/No-show ✓ | — | Availability, slot length, notice, blackout days editor ✓; emails **new** editable |
| Newsletter subscribers | Public double opt-in | ✓ filter, search, CSV | — (consent is the subscriber's) | **new** erase | **new** staff unsubscribe | — | |
| Site settings | — (singleton) | ✓ | ✓ | — | Announcement bar on/off ✓ | Menu/footer order ✓ | Navigation, footer, contact, social, SEO, analytics |
| Page texts | — (catalog) | **new** by page + search | **new** | **new** reset to default | — | — | See DYNAMIC_CONTENT.md |
| Redirects | **new** automatic on slug renames of live content + manual (UI) | **new** list, search, source filter | — (delete and re-add) | **new** ✓ | — | — | 301 via the web server gate; chains collapsed, loops refused; denied while impersonating |

## Platform content and settings (`content.manage`, `settings.manage`, `jobs.view`)

| Entity | Create | Read | Update | Delete | Status | Reorder | Notes |
|---|---|---|---|---|---|---|---|
| Home banners | ✓ | ✓ filter | ✓ | ✓ | Active, schedule window ✓ | ✓ | |
| Announcements | ✓ | ✓ filter | ✓ | ✓ | Active, publish/expire ✓ | — (by publish date) | |
| FAQ | ✓ | ✓ filter | ✓ | ✓ | Published ✓ | ✓ | |
| Onboarding steps | ✓ | ✓ | ✓ | ✓ | Active ✓; participants mark steps done ✓ | ✓ | |
| Portal texts | — | **new** | **new** | **new** reset | — | — | |
| Email templates | — (catalog) | **new** list, filter, search | **new** with variables + preview | **new** reset to default | — | — | |
| Platform settings | — | ✓ | ✓ (confirm + reason) | **new** restore default (confirm + reason) | — | — | |
| Jobs | — | ✓ runs | — | — | Run now ✓ | — | |
| Notification outbox | — | ✓ filter | — | — | Retry ✓ | — | |

## Campaigns, submissions, review (`campaigns.*`, `submissions.review`, `appeals.resolve`)

| Entity | Create | Read | Update | Delete / archive | Status | Reorder | Notes |
|---|---|---|---|---|---|---|---|
| Campaigns | ✓ | ✓ filter, search | ✓ (budget change needs confirm + reason) | Archive ✓, **new** restore from archive (→ Draft or Ended) | Publish/pause/resume/end ✓, duplicate ✓ | — | |
| Campaign assets | ✓ | ✓ | ✓ | ✓ | — | ✓ | |
| Reward rules | ✓ (version 1) | ✓ history | New version ✓ | — (versions are immutable) | — | — | Owned by the Rewards module |
| Disclosures | ✓ replace set | ✓ | ✓ | ✓ (by replacing) | — | — | |
| Campaign categories | ✓ | ✓ | ✓ | ✓ (deactivates when in use) | Active ✓ | **new** API + UI | |
| Submissions | Participant ✓ | ✓ | Resubmit when correction is requested ✓ | — | Review decisions ✓ | — | Immutable after decision by design (ledger); no participant withdrawal: it would need a new status across Rewards/Ledger (other workstreams) |
| Review decisions | ✓ | ✓ queue, filters | — (immutable) | Reverse ✓ (audited, ledger) | Claim/release ✓, live checks ✓, assignment ✓ | — | |
| Appeals | Participant ✓ | ✓ | — | — | Resolve ✓ | — | |
| Marketing: invitations, templates, calendar, experiments, achievements | ✓ | ✓ | ✓ | ✓ | Experiments start/pause/resume/complete ✓ | — | |
| Referrals | Automatic | ✓ | — | — | Reject ✓ | — | Reward approval lives in Rewards |

## Support and notifications

| Entity | Create | Read | Update | Close / reopen | Notes |
|---|---|---|---|---|---|
| Support tickets (participant) | ✓ | ✓ | Reply ✓ | Close ✓, **new** reopen within 30 days | |
| Support tickets (staff) | — | ✓ filter (status, priority, category, assignee), search | Status, priority, assignee ✓, **new** category (re-file) | Via status ✓ | Internal notes ✓ never shown to participants |
| Notifications (user) | System | ✓ | Mark read / all read ✓, **new** mark unread | — | Preferences matrix ✓ |
| Notification templates | — | **new** | **new** | **new** reset | See DYNAMIC_CONTENT.md |

## Accounts (profile)

| Entity | Read | Update | Notes |
|---|---|---|---|
| Profile | ✓ | ✓ | |
| Payout details | ✓ masked | ✓ encrypted | |
| Home / onboarding | ✓ | Mark step done ✓ | |

## Before → after

* Endpoints added: 25 (copy ×5, email templates ×5, page revisions ×3, reorder ×4, erase ×3, unsubscribe, unarchive,
  ticket reopen, setting reset, notification unread).
* UI gaps closed: reorder for 8 lists (service lines, services, industries, case studies, testimonials, team, blog
  categories, campaign categories), page history/restore/scheduling, inquiry assignment/filter/erase/mark closed,
  subscriber unsubscribe/erase, application erase, campaign restore, ticket reopen and re-file, settings restore,
  notification mark unread, page-text and email-template editors.
* Reorder UIs share `admin/shared/ReorderList` which now supports pointer drag in addition to the keyboard
  (Space to pick up, arrows to move, Space to drop, Escape to cancel) and move up/down buttons.
