# Email & SMS marketing: operations and compliance

This guide covers running the email/SMS module (`backend/src/OptimizeAll.Api/Modules/EmailMarketing`,
`frontend/src/features/agency/email`, `frontend/src/features/client/email`). The endpoints are listed in
[api/email-sms.md](api/email-sms.md).

## How sending works

1. Staff build a campaign (audience, sender, content, schedule). The pre-send checklist blocks the send while any
   item fails. Blocking items include:
   - no contacts with consent
   - an unverified sender
   - a missing postal address or unsubscribe link
   - broken links or content errors
   - a provider that is not ready
   - pending client approval
2. A user with `email.send` confirms the send by typing the campaign name. This checks the concurrency stamp and
   records an audit entry. Nothing is sent from the browser. An admin "viewing as" a user cannot do this (see
   [Impersonation](#impersonation-viewing-as)).
3. `CampaignSendJob` runs every 30 seconds and does the following:
   - **Recipients:** creates one recipient row per contact. `(campaign, subscriber)` is unique, so a contact never
     gets two copies.
   - **Claiming:** claims rows with a conditional update (`Pending → Sending` plus a claim id), so two instances
     never send the same row.
   - **Throttling:** respects the per-minute throttle and the send window. For the recipient-time-zone schedule it
     uses each contact's local time.
   - **Stopping:** re-checks pause/cancel every 10 sends.
   - **Consent:** re-checks consent and suppression immediately before each message.
   - **Status:** a message counts as `Sent` only when the provider acknowledged it (message id). Otherwise it is
     retried with backoff or marked `Failed`.
   - **Crash recovery:** rows stuck in `Sending` after a crash are marked failed, never re-sent.
4. **A/B tests:** the test slice is split deterministically between variants. After `abWaitHours` the variant with
   the better open or click rate is released to the remaining recipients.
5. **Journeys:** `AutomationJob` runs every minute and advances enrollments. Each step's side effects run at most
   once, because run rows are unique per (enrollment, step).

## Impersonation ("viewing as")

Messages to a client's audience are refused while an admin is viewing as another user, like payouts: the endpoints
answer 403 `auth.impersonation_forbidden_action` ("This action is not available while you are viewing as another
user. Exit the impersonation session first.", shown in the send dialog), nothing changes and the refused request is
recorded as `impersonation.request` on the impersonated user.

| Refused while impersonating | Why |
| --- | --- |
| `POST /agency/email/campaigns/{id}/send` and `/agency/email/sms/campaigns/{id}/send` (send now **and** schedule) | reaches the whole audience |
| `POST …/campaigns/{id}/resume` (email and SMS) | restarts a paused send to the audience |
| `POST /agency/email/automations/{id}/activate` | starts messaging every contact the trigger matches |
| `POST /agency/email/automations/{id}/enroll` | sends the journey's messages to a real contact |
| `POST /client/email/campaigns/{id}/approval` | releases a send, and is the client's consent, which staff cannot give for them |

Still allowed, by decision: **test sends** (`POST …/campaigns/{id}/test`, `/templates/{id}/test`), because they go only
to verified staff addresses; **pause, unschedule and cancel** of campaigns and **pause/archive** of journeys, because
they stop messages; and all reads and drafting (create, edit, duplicate, delete drafts, checklists, previews).
`UnitTests/Admin/ImpersonationCoverageTests` keeps these on its maintained deny list (and the stopping actions and test
sends on its must-stay-reachable list), and `IntegrationTests/EmailMarketing/ImpersonatedSendTests` covers each case.

## Consent and suppression rules

- **Consent required:** a contact receives marketing email only with email consent `Granted` (or `Pending` in the
  double opt-in flow, which receives only the confirmation email). SMS and WhatsApp need their own channel consent.
- **Imports:** require a consent attestation and a source. Both are stored on every contact's consent history.
- **Suppression wins:** the workspace suppression list always wins over list membership. It is fed by:
  - unsubscribes (link, one-click, preference center)
  - hard bounces
  - spam complaints
  - SMS `STOP` keywords
  - manual entries

  Removing a suppression requires a reason and is audited.
- **Re-subscribing:** imports and forms never re-subscribe an unsubscribed, bounced, complained or suppressed
  address, and an import never puts a contact back on a list they unsubscribed from (only the contact can: preference
  center or a confirmed sign-up).
- **Senders in use:** while a scheduled, sending or paused campaign or an active journey sends from a sender, its
  address cannot change (a new address is unverified, so every message would fail) and it cannot be deleted.
- **Test sends:** go only to verified staff accounts.
- **Erasure:** GDPR erasure (`DELETE /subscribers/{id}`) removes the contact, its consent history and its activity.
  Any suppression entries for the address are kept, so a suppressed address is never mailed again.

## Deliverability checklist (per sending domain)

- **SPF:** add the ESP's include to the domain's SPF record, e.g. `v=spf1 include:sendgrid.net ~all` or
  `include:mailgun.org`. Keep the record under 10 DNS lookups.
- **DKIM:** publish the CNAME/TXT keys that SendGrid/Mailgun generate for the domain (use 2048-bit keys).
- **DMARC:**
  - Start with `v=DMARC1; p=none; rua=mailto:dmarc@yourdomain` and review the reports.
  - Then move to `p=quarantine` and later to `p=reject`.
  - The From domain must align with SPF or DKIM.
- **Bulk-sender rules (Gmail/Yahoo, 2024):** SPF + DKIM + DMARC, one-click unsubscribe, and a spam rate below 0.3%.
  One-click unsubscribe is RFC 8058 (`List-Unsubscribe` + `List-Unsubscribe-Post`), which this module adds to every
  marketing email.
- **Tracking domain:** serve `/e/` from the app's domain (below) or a branded subdomain on the same organizational
  domain.
- **Warming a new domain or IP:**
  - Start with your most engaged contacts (segment: opened or clicked in the last 30 days).
  - Send about 500–1,000 on day 1, then roughly double every day or two while bounces stay under 2% and complaints
    under 0.1%.
  - Use the campaign throttle (`throttlePerMinute`) to spread volume.
- **List hygiene:**
  - Use double opt-in for new sign-ups.
  - Contacts that hard bounce or complain are suppressed automatically.
  - Every quarter, run a re-engagement journey for the Cold tier (no opens or clicks in 90 days), then stop mailing
    contacts who do not respond.
  - Engagement tiers ignore machine opens (Apple Mail Privacy Protection, security scanners).

## Legal notes (not legal advice)

- **CAN-SPAM (US):**
  - Every commercial email carries the sender's postal address (workspace setting, enforced by the checklist) and a
    working unsubscribe link.
  - Opt-outs are honoured immediately (the law allows up to 10 business days).
  - Subjects must not mislead.
- **GDPR / UK GDPR:**
  - Consent is recorded with the exact wording, its version, the source, the time and a hashed IP.
  - People can withdraw at any time from the preference center.
  - Erasure and export are available to staff.
  - Keep a lawful basis for every list, and avoid pre-ticked boxes (the hosted form's box is unticked).
- **PECR (UK) / ePrivacy (EU):**
  - Electronic marketing to individuals needs consent. The exception is the "soft opt-in" for existing customers
    about similar products, with an opt-out in every message.
  - SMS falls under the same rules.
- **SMS (TCPA and carrier rules):**
  - Send only with SMS consent, and include opt-out wording ("Reply STOP to opt out"). The checklist warns when it is
    missing.
  - Quiet hours are enforced in the recipient's time zone (workspace setting; the default is 21:00–08:00).
  - STOP/START replies are processed from the Twilio inbound webhook.
- **WhatsApp:** only pre-approved templates can start a conversation, and only with WhatsApp consent.

## Configuration

| Key | Purpose |
| --- | --- |
| `EmailMarketing:PublicBaseUrl` | Public origin used in tracking links (`/e/...`) and webhook URLs. Defaults to the app base URL. |
| `Email:AppBaseUrl` | Frontend origin for the unsubscribe/preferences/confirm/sign-up pages (optional: Site URL → this → the last origin seen on a request; see DEPLOYMENT.md "Public URL"). |
| `EmailMarketing:TokenSecret` | HMAC key for tracking/unsubscribe tokens (≥ 32 random bytes). If unset it is derived from `Security:HashSalt`. **Rotating it invalidates links in emails already sent.** |
| `Tracking:PostbackSecret` | HMAC key for the signed conversions/events APIs (`X-OA-Signature: sha256=<hex>`). Without it those endpoints answer 503. |
| `Email:Mode`, `Email:Smtp:*` | Platform SMTP relay used by the default `smtp` provider (`File` mode writes `.eml` files for development). |

Provider credentials live in the integrations vault (per workspace, with the agency's as the fallback) and are
managed with `integrations.manage`:

- **`sendgrid`**
  - Secret: `apiKey`.
  - Settings: `fromEmail`, `fromName`, `webhookPublicKey` (Signed Event Webhook; without it bounce and spam-report
    events are refused with 503).
- **`mailgun`**
  - Secrets: `apiKey`, `webhookSigningKey` (HTTP webhook signing key; without it bounce and complaint events are
    refused with 503).
  - Settings: `domain`, `fromEmail`, `region` (`eu` sends through `https://api.eu.mailgun.net`).
- **`twilio`**
  - Secret: `authToken` (also verifies webhook signatures).
  - Settings: `accountSid`, and either `fromNumber` or `messagingServiceSid`.
- **`whatsapp`** (Cloud API)
  - Secret: `accessToken`.
  - Settings: `phoneNumberId`, optional `apiBaseUrl`.

The settings page shows whether each provider is ready and lists the webhook URLs to paste into SendGrid, Mailgun and
Twilio. Amazon SES and Postmark appear as provider options but are not implemented yet: choosing them makes the
checklist block sending.

## Reverse proxy (nginx)

Tracking and one-click unsubscribe links are served by the API under `/e/`. When the SPA and the API share a domain,
proxy that prefix to the API with the `/api/` location. The following block must not be cached:

```nginx
location /e/ {
    proxy_pass http://api_upstream;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    add_header Cache-Control "no-store" always;
}
```

The public pages (`/email/unsubscribe/:token`, `/email/preferences/:token`, `/email/confirm/:token`,
`/email/subscribe/:key`) are SPA routes, served like any other frontend path.

## Metrics caveats

- **Opens:** reported as unique human opens. Machine opens (Apple Mail Privacy Protection, image proxies, security
  scanners) are counted separately and are excluded from rates and engagement tiers.
- **Clicks:** clicks within 10 seconds of delivery, or from known scanners, are flagged as machine clicks.
- **Delivered:** measured only when the provider's delivery webhook is configured. Otherwise it is estimated as
  sent minus bounces, and labelled "Estimated". Workspace KPIs apply this per campaign and add the results up.
- **Revenue:** attributed to the last email click within 7 days before the conversion.
