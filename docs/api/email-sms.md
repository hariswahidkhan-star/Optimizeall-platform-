# Email & SMS marketing API

Base path `/api/v1`. JSON uses camelCase and enums as strings. Errors are RFC 7807 problem documents with a stable
`code` (for example `email.send_blocked`, `email.automation_invalid`, `concurrency_conflict`). Every editable record
returns a `concurrencyStamp`; send it back on update/transition or the request answers `409`.

**Workspaces.** Every list, contact, template, segment, campaign, journey and setting belongs to one workspace: the
agency's own marketing (`clientAccountId: null`, key `agency`) or one client account (key = the client id). Staff
endpoints take `clientId` (query) or `clientAccountId` (body/query) to choose it and check client access through the
client scope. Records of another workspace answer `404`.

## Staff API: `/agency/email` (`email.manage`)

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/workspaces` | Workspaces the caller can manage (agency + accessible clients). |
| GET | `/overview?clientId` | Counts, last-30-day KPIs, recent campaigns. |
| GET | `/kpis?clientId&from&to` | Email/SMS KPIs of a workspace for a period. `delivered` is measured (provider webhooks) or estimated (sent − bounces) per campaign, as in the campaign report, then summed. |
| GET | `/clients/{id}/kpis?from&to` | Same, for client reports (**use this from the reporting module**; there is no `IClientReportSection`). |
| GET, POST | `/lists` | `?clientId`; create with name, description, `doubleOptIn`, `showInPreferenceCenter`, `consentText`, `consentTextVersion`. |
| GET, PUT, DELETE | `/lists/{id}` | DELETE archives the list. |
| GET | `/lists/{id}/health?days=90` | Tiers (active/warm/cold/new), bounced/complained, daily growth. |
| GET | `/lists/{id}/export.csv` | Contacts with consent evidence (audited). |
| POST | `/lists/{id}/imports/preview` | `{ csv }` → headers, sample rows, suggested mapping. |
| POST | `/lists/{id}/imports` | `{ fileName, csv, mapping, tags, confirmConsent: true, consentSource, grantSmsConsent }`. Up to 10 MB; small files finish inline, large ones run in `SubscriberImportJob`. Never re-subscribes unsubscribed/bounced/suppressed addresses, nor a contact who unsubscribed from that list. |
| GET | `/lists/{id}/imports`, `/imports/{id}` | Import status and per-row errors (`row` is the line of the file, counting the header, blank lines and multi-line values). |
| GET, POST | `/subscribers` | Paged; filters `clientAccountId, listId, status, tag, search`. POST adds a contact (`attestEmailConsent` + `consentSource`, else a double opt-in email is sent). |
| GET, PUT, DELETE | `/subscribers/{id}` | Detail with consent history and activity. DELETE = GDPR erasure (audited). |
| POST | `/subscribers/{id}/tags` | `{ add, remove }`. |
| POST | `/subscribers/{id}/consent` | `{ channel, status, source }` – record a withdrawal or evidenced grant. |
| POST | `/subscribers/{id}/lists` | Add to / remove from lists. |
| GET, POST | `/suppressions` | Paged list; add `{ channel, value, reason: Manual, note }`. |
| DELETE | `/suppressions/{id}?reason=` | Reason required (audited). |
| POST | `/suppressions/bounces` | Manual bounce/complaint import (`content`, `defaultType`). |
| GET, POST | `/segments` | Rule tree `{ match: all\|any, conditions[], groups[] }` (3 levels). |
| GET, PUT, DELETE | `/segments/{id}` | |
| POST | `/segments/preview` | `{ clientAccountId, listId?, definition }` → count, reachable count and a sample. |
| GET, POST | `/templates` | Workspace + agency library. Body: name, category, subject, previewText, `design` (block JSON). |
| GET, PUT, DELETE | `/templates/{id}` | DELETE archives. |
| POST | `/templates/{id}/duplicate?clientId` | Copy (e.g. a starter template into a client workspace). |
| POST | `/templates/render` | Server render with sample data → `{ subject, html, text, sizeBytes, errors, warnings }`. |
| POST | `/templates/{id}/test` | `{ to, clientAccountId?, senderProfileId? }`: test send to a **verified staff address only**, subject prefixed `[Test]`. |
| POST | `/events` | Record a custom event for a contact (staff tool). |
| GET, PUT | `/settings` | Workspace: organization name, postal address, client approval, throttle, time zone; SMS quiet hours and costs need `sms.manage`. Returns provider readiness and webhook URLs. |
| PUT | `/settings/provider` | **`integrations.manage`**; `{ clientAccountId, emailProvider, confirm: true }`. |
| GET, POST | `/senders` | Sender identities. |
| PUT, DELETE | `/senders/{id}` | A new address must be verified again; while a scheduled, sending or paused campaign or an active journey uses the sender, its address cannot change and it cannot be deleted (`409 email.sender_in_use`). |
| POST | `/senders/{id}/send-verification` | Emails a 6-digit code (rate limited). |
| POST | `/senders/{id}/verify` | `{ code }`. Unverified senders cannot send. |

### Campaigns: `/agency/email/campaigns` (email) and `/agency/email/sms/campaigns` (SMS/WhatsApp, `sms.manage`)

| Method | Path | Notes |
| --- | --- | --- |
| GET, POST | `/` | Paged (`clientAccountId, status, search`). Create/update: audience (`listId` or `segmentId`), sender, subject/preview/design or `smsBody` / WhatsApp template, schedule (`Immediate`, `FixedTime`, `RecipientTimeZone` + `scheduledLocalTime`), send window, throttle, A/B (`abTestPercent`, `abWinnerMetric`, `abWaitHours`, `variants`). |
| GET, PUT, DELETE | `/{id}` | Only drafts can be edited or deleted. |
| POST | `/{id}/duplicate` | |
| GET | `/{id}/checklist` | Pre-send checklist: audience with consent, verified sender, postal address, unsubscribe link, content checks, links, provider readiness, SMS segments/cost, approval. `Fail` items block sending. |
| GET | `/{id}/preview` | Rendered with sample data. |
| POST | `/{id}/test` | Test send (verified staff only). |
| GET | `/{id}/report` | Delivery, human vs machine opens, clicks per link, devices, mail clients, A/B, timeline, unsubscribes, complaints, conversions and revenue, SMS cost. |
| POST | `/{id}/send` | **`email.send`**. `{ confirm: true, confirmName: <campaign name>, concurrencyStamp, reason? }`. Queues (or schedules) the send; `CampaignSendJob` does the sending. |
| POST | `/{id}/unschedule`, `/pause`, `/resume`, `/cancel` | **`email.send`**. `{ concurrencyStamp, reason? }`. |
| POST | `/sms/campaigns/segments` | `{ text, recipients, costPerSegment }` → encoding (GSM-7/UCS-2), segments, cost estimate. |

### Journeys: `/agency/email/automations`

| Method | Path | Notes |
| --- | --- | --- |
| GET, POST | `/` | `?clientId`. Body: name, trigger (`ListSubscribed`, `TagAdded`, `FormSubmitted`, `NewsletterConfirmed`, `DateAnniversary`, `CustomEvent`) + `triggerConfig`, `reentry`, `reentryCooldownDays`, `goal`, `senderProfileId`, `entryStepKey`, `steps[]`. |
| GET, PUT | `/{id}` | Steps: `SendEmail`, `SendSms`, `Wait`, `Condition` (`next`/`altNext`), `AddTag`, `RemoveTag`, `NotifyStaff`, `Exit`. The graph is validated (unique keys, known targets, no cycles, ≤ 50 steps). |
| POST | `/{id}/activate` | Re-validates and requires a verified sender. |
| POST | `/{id}/pause`, `/{id}/archive` | |
| GET | `/{id}/enrollments` | Latest 100. |
| POST | `/{id}/enroll` | `{ subscriberId }` (active journeys only). |

## Client portal: `/client/email` (`client.portal`)

Only organizations the caller belongs to; other tenants answer `404`; drafts are never shown.

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/clients` | The caller's organizations. |
| GET | `/campaigns?clientId&page&pageSize` | Scheduled/sent campaigns with headline numbers. |
| GET | `/approvals?clientId` | Campaigns waiting for client approval (`canApprove` for Approver/Owner members). |
| GET | `/campaigns/{id}/report`, `/campaigns/{id}/preview` | |
| POST | `/campaigns/{id}/approval` | `{ approve, note }` (Approver or Owner). |
| GET | `/kpis?clientId&from&to` | Viewer or higher. |

## Public (anonymous, rate limited)

Links in emails (host these under the API; nginx must proxy `/e/`):

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/e/o/{token}.gif` | Open pixel (1×1 GIF, bot/privacy-proxy filtered). |
| GET | `/e/c/{token}` | Click redirect (only to the link recorded for that message; scanner clicks flagged). |
| GET | `/e/u/{token}` | Redirects to the unsubscribe confirmation page (scanners do not unsubscribe). |
| POST | `/e/u/{token}` | RFC 8058 one-click unsubscribe (`List-Unsubscribe-Post`). |

Public pages' API `/api/v1/public/email`:

| Method | Path | Notes |
| --- | --- | --- |
| POST | `/unsubscribe/{token}` | Unsubscribe page button. |
| GET, PUT | `/preferences/{token}` | Topics, frequency, `unsubscribeAll` (reached from a campaign email, it counts as that campaign's unsubscribe). |
| GET, POST | `/forms/{key}` | Hosted sign-up form (explicit consent box, honeypot `website`); answers `202` whether or not the address was known. |
| POST | `/confirm/{token}` | Double opt-in confirmation. |
| POST | `/conversions` | Server-to-server, header `X-OA-Signature: sha256=<hex HMAC-SHA256(Tracking:PostbackSecret, raw body)>`. `{ clientAccountId?, email, externalReference, value, currency, occurredAt? }`; idempotent per reference; last click within 7 days is attributed. |
| POST | `/events` | Same signature. `{ clientAccountId?, email, name, properties?, eventId?, occurredAt? }`; starts `CustomEvent` journeys; properties available as `{{event.key}}`. |
| POST | `/webhooks/sendgrid/{workspace}` | Signed Event Webhook (ECDSA public key: setting `webhookPublicKey` of the workspace's SendGrid connection, entered on the Integrations page). |
| POST | `/webhooks/mailgun/{workspace}` | HMAC signature (secret `webhookSigningKey` of the workspace's Mailgun connection, entered on the Integrations page), 15-minute window. |

SMS webhooks (`/api/v1/public/sms/webhooks/twilio/{workspace}/inbound` and `/status`) verify `X-Twilio-Signature`.
`{workspace}` is `agency` or the client id; the URLs are listed on the settings page.
