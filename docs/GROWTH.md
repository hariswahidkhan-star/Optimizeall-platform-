# Growth features: rules, integrations and metric definitions

This document explains how Optimize All's growth features behave: referrals, invitation links, tracking links and
advertiser postbacks, A/B experiments, achievements, retention automations, and how performance numbers are
defined. Endpoint details are in [`docs/api/marketing-analytics.md`](api/marketing-analytics.md).

Principle: **never present an estimate as a measurement.** Every analytics number is labelled `counted`,
`measured` or `estimated` (see [Metric definitions](#metric-definitions)).

---

## 1. Referrals

### Program settings
Setting `referral.program` (`ReferralProgramSettings`, editable by admins):

| Field | Default | Meaning |
|---|---|---|
| `enabled` | `true` | When false, new registrations create no referral and qualifying referrals get no reward. |
| `referrerRewardAmount` / `currency` | `5` / `USD` | Reward credited to the referrer (converted to the settlement currency by the ledger). |
| `qualifyingAction` | `FirstApprovedSubmission` | `EmailVerified`, `FirstApprovedSubmission` or `FirstPaidPayout`. Captured on the referral at registration. |
| `qualifyWithinDays` | `60` | The referred participant must qualify within this many days of registering. |
| `requireManualApproval` | `true` | Reward is created as `PendingApproval` (approved by someone with `rewards.approve_bonus`). |
| `maxRewardedReferralsPerUser` | `50` | Referrals beyond the cap still qualify but earn no reward. |

### Lifecycle
1. **Registration.** The web app sends `referralCode` (from `/register?ref=CODE`), `inviteCode` and a random
   per-browser `deviceId` to `POST /api/v1/auth/register`. After commit, auth publishes `UserRegistered` with the
   hashed IP and device id. If the code matches another participant's referral code (case-insensitive), a
   `Referral` is created with status `Registered`, `QualifyBy = now + qualifyWithinDays`, and fraud signals.
   Self-referral is impossible (different user ids; DB check constraint). A participant can be referred once
   (unique `ReferredUserId`), so duplicate events are harmless.
2. **Qualification.** On `EmailVerified`, `SubmissionApproved` (first approved submission) or `PayoutItemPaid`
   (first paid payout) — whichever matches the referral's action — a conditional update moves the referral from
   `Registered` to `Qualified` (only if still `Registered` and not past `QualifyBy`). In the same transaction the
   referrer's reward is recorded through the ledger with idempotency key `referral:{referralId}`
   (`EarningType.ReferralReward`). Reward decisions are serialized per referrer (row lock), so the cap cannot be
   exceeded and duplicate or concurrent events never create a second reward. The referrer is notified
   (`referral.qualified`, in-app + email) and achievements are re-evaluated.
3. **Manual approval.** The reward is `PendingApproval` when `requireManualApproval` is on **or** the referral has any
   fraud signal; otherwise it is `Approved` (then subject to the normal payout hold).
4. **Reversal.** If the qualifying action was the first approved submission and the referred participant's only
   approved submission is reversed (`SubmissionReversed`), the unpaid reward is declined (if pending) or reversed
   (if approved) with reason "Qualifying submission reversed", and the referral becomes `Rejected`. Paid or
   scheduled rewards are not touched automatically.
5. **Expiry.** `ReferralExpiryJob` (hourly) marks `Registered` referrals past `QualifyBy` as `Expired`. A late
   qualifying event never qualifies an expired window.
6. **Manual rejection.** Marketing can reject any referral with a reason
   (`POST /api/v1/marketing/referrals/{id}/reject`): a pending reward is declined, an approved unpaid reward is
   reversed, paid/scheduled rewards stay as they are (finance can adjust). Audited.

### Fraud checks (`ReferralFraudRules`, pure and unit-tested)
Signals never block a referral; they force manual approval and are shown to marketing (filter `flagged=true`).

| Code | Rule |
|---|---|
| `shared_ip` | Another referral by the same referrer registered from the same hashed IP within the last 30 days (or the referrer's own registration used it). |
| `shared_device` | Another referral by the same referrer (any time) or the referrer's own registration used the same hashed device id. |
| `email_alias` | The referred email equals the referrer's after lower-casing, removing a `+tag`, and (for gmail.com/googlemail.com) removing dots. |
| `velocity` | More than 10 referrals by the referrer within 24 hours (including the new one). |
| `disposable_email` | The referred email's domain (or a subdomain of it) is on the built-in disposable list (mailinator.com, guerrillamail.com, 10minutemail.com, temp-mail.org, yopmail.com, trashmail.com, …). |

IPs and device ids are stored only as keyed HMAC-SHA256 hashes (`Security:HashSalt`).

---

## 2. Invitation links & landing pages

* Marketing creates links (`/join/{code}`, 8 random URL-safe characters) for the platform or a specific campaign,
  with UTM labels, optional expiry and optional maximum number of registrations.
* The landing page (`GET /api/v1/public/invitations/{code}`) counts a visit atomically and is `404` once the link is
  inactive, expired or used up, or when its campaign is not Scheduled/Active (invite-only campaigns are allowed).
* A registration with `inviteCode` increments `UseCount` with a single conditional statement
  (`UseCount < MaxUses`), so concurrent sign-ups can never exceed the cap.
* Public campaign pages (`/api/v1/public/campaigns/{slug}`) exist for Active/Scheduled **public** campaigns and show
  landing copy (falling back to title/summary), hero image, platforms, a reward teaser (base rate of the rule version
  in force), dates, image assets and the default disclosure.

---

## 3. Tracking links, UTM parameters and conversion postbacks

### Participant links
A campaign with a *tracking destination URL* lets each participant get one personal short link
`{Tracking:PublicBaseUrl}/t/{code}`. The redirect appends:

| Parameter | Value |
|---|---|
| `utm_source` | `optimizeall` |
| `utm_medium` | `social` |
| `utm_campaign` | the campaign's `UtmCampaign`, or its slug |
| `utm_content` | the participant's referral code (identifies the participant without exposing personal data) |

Existing destination parameters and the fragment are kept; existing `utm_*` parameters with these names are
replaced. The target always comes from stored campaign configuration — never from the request — so `/t/` cannot be
abused as an open redirect.

### Clicks (measured)
Each redirect records a click with `VisitorHash = HMAC(ip | user-agent | UTC date)` (no raw IP or user agent is
stored) and the referrer host.
* **Unique click**: the first click on that link by the same visitor hash (i.e. per visitor per day per link).
* **Suspected bot**: user agent empty or matching `bot|crawler|spider|preview|facebookexternalhit|slackbot|whatsapp|curl|wget|python-requests|headless`.
  Bot clicks are stored but **excluded** from every click metric (reported separately as `botClicksExcluded`).

### Advertiser postback integration guide
Advertisers report conversions server-to-server. Pass `utm_content` (or the tracking code) through your checkout
and send one postback per conversion.

1. Obtain the shared secret from Optimize All (server setting `Tracking:PostbackSecret`; until it is configured the
   endpoint answers `503 tracking.postback_not_configured`).
2. `POST https://<api-host>/api/v1/public/conversions` with `Content-Type: application/json` and body:
   ```json
   {"code":"Qm3xZ81kTa","externalReference":"order-1001","value":49.99,"currency":"USD","occurredAt":"2026-09-23T10:15:00Z"}
   ```
   `code` = the tracking link code (the path segment after `/t/`), `externalReference` = your unique order/lead id
   (≤ 150 chars), `value`/`currency` optional (ISO 4217, currency required with value), `occurredAt` in UTC.
3. Sign the **exact raw body bytes** with HMAC-SHA256 and send `X-OA-Signature: sha256=<lower-case hex>`.

Shell (OpenSSL):
```bash
BODY='{"code":"Qm3xZ81kTa","externalReference":"order-1001","value":49.99,"currency":"USD","occurredAt":"2026-09-23T10:15:00Z"}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$OA_POSTBACK_SECRET" -hex | sed 's/^.* //')
curl -X POST https://api.optimizeall.app/api/v1/public/conversions \
  -H 'Content-Type: application/json' -H "X-OA-Signature: sha256=$SIG" --data-binary "$BODY"
```
Node.js:
```js
const body = JSON.stringify({ code, externalReference: orderId, value: 49.99, currency: "USD", occurredAt: new Date().toISOString() });
const sig = require("crypto").createHmac("sha256", process.env.OA_POSTBACK_SECRET).update(body).digest("hex");
await fetch(url, { method: "POST", headers: { "Content-Type": "application/json", "X-OA-Signature": `sha256=${sig}` }, body });
```
Python:
```python
body = json.dumps(payload, separators=(",", ":")).encode()
sig = hmac.new(secret.encode(), body, hashlib.sha256).hexdigest()
requests.post(url, data=body, headers={"Content-Type": "application/json", "X-OA-Signature": f"sha256={sig}"})
```

Responses: `200 {"id":"…","duplicate":false}`; retries of the same `(code, externalReference)` are safe and return
`duplicate: true`; `401 tracking.invalid_signature`; `400 tracking.postback_invalid`; `404 tracking.link_not_found`.
Only signature-verified conversions (`VerifiedAt` set) count as *verified conversions*.

---

## 4. A/B experiments

* **What can be tested:** campaign title, creative asset, posting instructions (participant portal) and landing page
  headline/body (public campaign page). 2–4 variants `A`–`D`; `A` is the control; weights 1–100.
* **Lifecycle:** Draft (editable) → Running ⇄ Paused → Completed (optionally with a winning variant). Only one Running
  experiment per campaign and element. All transitions are audited.
* **Assignment (`VariantAssigner`):** deterministic and sticky. `bucket = first 8 bytes (big-endian) of
  SHA-256("{experimentId}:{subjectKey}") mod Σweights`; variants ordered by key take consecutive weight ranges.
  The assignment is stored once per `(experiment, subject)` (unique index; a concurrent insert reads back the winner).
  Subjects are `user:{userId}` (participants, via `GET /api/v1/campaigns/{id}/experiment-variants`) or
  `visitor:{hash}` (anonymous visitors that send an `X-Visitor-Id` header; the id is hashed with the server salt).
  Visitors without the header see the base content and are not counted.
* **Attribution:** the frontend sends the `variantId` it displayed with the submission (`Submission.ExperimentVariantId`).
* **Results (`ExperimentMath`):** per variant `assigned` (stored assignments = exposures), `submissions` (distinct
  participants who submitted with the variant), `approved` (distinct participants with an approved submission),
  `submissionRate = submissions / assigned`, `approvalRate = approved / submissions`. Each variant is compared with
  control A on submission rate: absolute lift, relative lift, and a two-sided pooled two-proportion z-test
  (`z = (p₂ − p₁) / √(p̄(1 − p̄)(1/n₁ + 1/n₂))`, `p = 2(1 − Φ(|z|))`).
  A difference is reported as **significant only when p < 0.05 and every compared variant has at least 100
  assignments**; otherwise the note reads "Not enough data for a reliable conclusion". Checking results repeatedly
  and stopping at the first significant p-value inflates false positives — decide the sample size up front.

---

## 5. Achievements

Baseline milestones (seeded idempotently by key, profile `Baseline`; icons are lucide names):

| Key | Criterion | Threshold |
|---|---|---|
| `first-approved-post` | ApprovedSubmissions | 1 |
| `five-approved` | ApprovedSubmissions | 5 |
| `twenty-five-approved` | ApprovedSubmissions | 25 |
| `hundred-approved` | ApprovedSubmissions | 100 |
| `multi-platform` | PlatformsUsed (distinct platforms with an approved submission) | 3 |
| `first-100` | TotalEarnedSettlement (net Approved + Scheduled + Paid earnings in settlement currency, clawbacks included) | 100 |
| `referral-star` | QualifiedReferrals | 5 |
| `campaign-explorer` | CampaignsCompleted (distinct campaigns with an approved submission) | 5 |

`AchievementEvaluator` runs on `SubmissionApproved`, `PayoutItemPaid` and referral qualification. An award is an
`INSERT IGNORE` on the `(UserId, AchievementId)` key, and the in-app notification is staged in the same transaction
only when the row was inserted — each achievement is awarded and announced once. Marketing can add, edit and
deactivate achievements; awarded ones cannot be deleted.

---

## 6. Retention automations

`RetentionJob` runs hourly (no-op when setting `retention.enabled` is false). Each message writes a
`RetentionMessageLog (UserId, Kind, DedupKey)` row (unique) **in the same transaction** as the notification and its
email outbox rows, so a crash or retry can never send twice. At most 500 messages per kind per run. Only active
participants are considered.

| Kind | Key | Who | Channels |
|---|---|---|---|
| `onboarding.verify_email` | `d1` | Email not verified 24 h after registration (registered within the last 30 days). The message asks them to sign in and use "Resend verification" — it never contains a token or link with a token. | In-app + email |
| `onboarding.add_social` | `d3` | Verified, no social profile 3 days after registration (registered within 30 days). | In-app + email |
| `onboarding.first_submission` | `d7` | Verified, has a profile that meets the global eligibility criteria, no submission 7 days after registration (registered within 60 days). | In-app + email |
| `campaign.alert` | campaign id | For Active **public** campaigns published in the last 72 h: verified participants who are eligible (`EligibilityEvaluator` with the campaign's criteria) and whose interests match the campaign topics/interest targeting or who have an eligible profile on one of the campaign's platforms. At most one campaign alert per participant per rolling 24 h. | In-app; email only with marketing consent |
| `reactivation` | start date of the current 30-day window | Verified participants inactive for `retention.inactivityDays` (default 30; last activity, or registration if never active) who are eligible for at least one active public campaign. At most once per 30 days. | In-app; email only with marketing consent |

Participants can mute the email channel of these kinds in their notification preferences (the in-app entry is
always created). Marketing sees counts per kind and the
send log under `/api/v1/marketing/retention/*`.

---

## Metric definitions

| Label | Meaning | Examples |
|---|---|---|
| **counted** | Exact count/sum of records in our own database. | registrations, submissions, approvals, ledger spend |
| **measured** | Behaviour observed by our own tracking of external traffic. | redirect clicks (bots excluded), signature-verified conversions |
| **estimated** | Derived from declared or modelled values; not observed. | reach from declared follower counts |

Time basis (UTC): registrations by account creation, posts and the spend/reach attached to them by submission time,
clicks by click time, conversions by `occurredAt`. Default range: last 30 days; maximum 366 days. Filters
`campaignId`/`platform` apply to posts, spend, reach and the funnel's submission/social-profile steps; clicks and
conversions are filtered by campaign only (they are not attributed to a platform — the note says so).

| Section | Metric | Definition |
|---|---|---|
| Funnel (counted) | `registrations` | Participants who registered in the range (the funnel cohort). |
| | `emailVerified` | Cohort members with a verified email. |
| | `participantsWithSocialAccount` | Cohort members with ≥ 1 social profile (on the filtered platform). |
| | `eligibleAccounts` | Active cohort profiles meeting the global criteria (minimum account age and followers) at the end of the range, evaluated in batches. |
| | `participantsWithSubmission` | Cohort members with ≥ 1 submission (to the filtered campaign/platform). |
| | `submissionRate` | `participantsWithSubmission / registrations` (%). |
| Posts (counted) | `postsSubmitted` … `postsReversed` | Submissions made in the range by current status (`postsPending` = Pending + UnderReview). |
| | `approvalRate` | `approved / (approved + rejected + reversed)` (%). |
| Spend (counted, money) | `spend` | Per settlement currency: earnings linked to the range's submissions, excluding Reversed and Declined entries, plus clawback (negative) reversal entries — i.e. net of clawbacks. Pending-approval earnings are included (committed spend). |
| | `costPerApprovedPost` | `spend / postsApproved` per currency. |
| | `spendByCampaign` | Same, per campaign and currency. |
| Reach (estimated) | `estimatedReach` | "Estimated reach (declared follower counts, not measured views)": sum of the follower counts of the profiles used for each approved post. |
| Traffic (measured) | `trackedClicks` / `uniqueClicks` | Tracking-link clicks / first clicks per visitor per day, suspected bots excluded. |
| | `botClicksExcluded` | Suspected bot clicks (recorded, not counted). |
| Conversions (measured) | `verifiedConversions` | Postback conversions with a valid signature (`VerifiedAt` set). |
| | `conversionValue` | Sum of reported values of verified conversions, per currency. |
| Time series | per UTC day | registrations, submissions, approvals (approved submissions by decision time), clicks (bots excluded). |
| Campaigns table | per campaign | submitted, approved, approvalRate, spend, costPerApproved, clicks, uniqueClicks, verifiedConversions, estimatedReach. |
