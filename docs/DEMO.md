# Optimize All — Demo data and demo scripts

> **STAGING / DEMO DATA ONLY. NEVER SEED THIS IN PRODUCTION.**
> Every demo account shares one published password, the brands are fictional, and payout destinations are
> fake (example.com PayPal addresses and generated IBANs/wallet numbers). The Demo seed profile must never
> appear in a production `Database:Seed` setting.

## How the data is created

The `Demo` seed profile (`backend/src/OptimizeAll.Api/Modules/Seed`) runs after the Baseline seeders when
`Database:Seed` lists it. Development already uses `["Baseline", "Demo"]`. For staging, set:

```
Database__Seed__0=Baseline
Database__Seed__1=Demo
```

* **Idempotent.** The seed does nothing if the `demo.seeded` system setting or `sara.participant@demo.optimizeall.app`
  already exists. Everything is written in one transaction; if it fails, the screenshots and images it wrote are
  deleted again.
* **Images are uploads.** Campaign heroes, image assets and homepage banners are generated PNGs written through
  `IFileStorage` as public `CampaignAsset`/`ContentImage` files and referenced as `/api/v1/files/{id}`, so they load
  under the web CSP (`img-src 'self'`) without any external image host.
* **Follows the live rules.** Seeded submissions are declared posted at most 7 days before submission, store the
  canonical post key of `PlatformUrlRules.Parse`, are never decided or live-checked by their own participant, and no
  earnings are approved for a suspended user after the suspension (asserted by `DemoSeedTests`). Notification links
  use `AppLinks` (real web routes).
* **Always current.** All dates are relative to "now". The seed replays about three months of activity in
  chronological order, anchored to the biweekly payout periods. "c0" in this document means the cutoff of the last
  completed period, "c1" the cutoff before that, and so on.
* **Realistic and consistent.**
  * Submissions go through the real rules: eligibility at the time they were made, normalized unique post URLs,
    risk flags, the reward rule version captured at submission, caps and budgets.
  * Earnings are written by the production `LedgerWriter`, driven by a simulated clock, so FX conversion, rounding,
    hold periods and idempotency keys match production (`submission:{id}:PostReward`, `firstpost:{campaign}:{user}`,
    `referral:{id}`, `adjustment:{guid}`, `reversal:{entryId}`).
  * Payout batches are built with the Payouts module's planner and store operations. Reconciliation is balanced
    for every batch.
  * Screenshots are real PNGs (320×560), saved through `IFileStorage` with a `StoredFile` row and SHA-256.
  * Payout destinations are encrypted with the Accounts module's data-protection purpose and masked with its helper.
* **Deterministic.** Uses a fixed random seed (`20260923`). If you re-seed a fresh database on another day, the same
  story plays out on dates shifted relative to that day.

To reset a staging database, drop it and restart the API. Migrations, Baseline and Demo run again.

## Demo accounts

All accounts use the password **`Demo#2026!pass`**.

| Email | Role | What to show |
|---|---|---|
| `admin@demo.optimizeall.app` | Admin | Users (suspended participant, tiers, roles), settings, audit log, jobs, content (banners, announcements, FAQs) |
| `reviewer1@demo.optimizeall.app` | Reviewer | Review queue, claims, decisions, live checks, reversals, appeals |
| `reviewer2@demo.optimizeall.app` | Reviewer | Second reviewer: holds a live claim, resolves appeals on reviewer1's decisions |
| `manager@demo.optimizeall.app` | Campaign manager | Campaigns in every status, reward rule versions, quality bonus approvals, experiments, invitations, tracking, templates, calendar, analytics |
| `finance1@demo.optimizeall.app` | Finance | Prepared every batch; creates adjustments; records payments; payout holds; FX rates |
| `finance2@demo.optimizeall.app` | Finance | Second pair of eyes: finalizes the draft batch, approves pending adjustments |
| `sara.participant@demo.optimizeall.app` | Participant (PK, Gold) | The full participant journey: every balance bucket, paid history, referral, achievements, notifications, tickets |
| `new.participant@demo.optimizeall.app` | Participant (PK) | Email verified, one Instagram profile created 10 days ago → ineligible (`social.account_too_new`) |
| `unverified@demo.optimizeall.app` | Participant (US) | Email not verified (onboarding state and banner) |
| `hold.participant@demo.optimizeall.app` | Participant (AE) | Active payout hold (KYC review): neutral hold message, excluded from batches |
| `suspended.participant@demo.optimizeall.app` | Participant (GB) | Suspended; sign-in returns `account.suspended` |
| `bilal.ahmed@demo.optimizeall.app` | Participant (PK) | Velocity: 11 posts in under 24 h, daily caps applied, velocity flag |
| `zainab.malik@`, `usman.tariq@`, `karim.mostafa@` | Participants | Sara's referrals: qualified and rewarded; flagged for a shared device (reward pending); still pending |
| `hamza.qureshi@demo.optimizeall.app` | Participant (PK) | Clawback: a paid post was reversed and netted against the next payout; open dispute ticket |
| Other `first.last@demo.optimizeall.app` | Participants | About 25 more participants across PK, AE, SA, GB, US, IN and EG |

### Agency demo accounts

Same password. The agency's demo clients are **Nimbus Fitness** (US, USD), **Wanderly Travel** (UK, GBP),
**Aurora Skincare** (UAE, AED) and **Karachi Eats** (Pakistan, PKR), each with projects, deliverables, CRM history,
invoices, email lists and campaigns, social calendars, ad accounts and SEO sites.

| Email | Role | Try |
|---|---|---|
| `admin@demo.optimizeall.app` | Admin | Every agency area, settings, integrations |
| `am@demo.optimizeall.app` | Account manager | Agency home, clients, projects, approvals, proposals, contracts, reports |
| `sales@demo.optimizeall.app` | Sales rep | CRM pipeline, lead scoring, proposals |
| `strategist@demo.optimizeall.app` | Strategist | Reports, CRM (read), projects |
| `content@demo.optimizeall.app` / `designer@demo.optimizeall.app` | Content creator / Designer | Tasks, deliverables, time tracking, landing pages |
| `seo@demo.optimizeall.app` | SEO specialist | Site audits, rankings, backlinks, local SEO, briefs |
| `ads@demo.optimizeall.app` | Ads specialist | Ad accounts, pacing, alerts, media plans, creatives |
| `social@demo.optimizeall.app` | Social media manager | Calendar, composer, approvals, inbox, listening |
| `finance1@demo.optimizeall.app` | Finance | Invoices, payments, credit notes, aging (plus participant payouts) |
| `owner@nimbus.demo.optimizeall.app` | Client (Nimbus Fitness, Owner) | Client portal: everything for Nimbus |
| `approver@nimbus.demo.optimizeall.app` | Client (Nimbus Fitness, Approver) | Approve deliverables, posts and email campaigns |
| `billing@nimbus.demo.optimizeall.app` | Client (Nimbus Fitness, Billing) | Invoices and proposals |
| `owner@aurora.demo.optimizeall.app` | Client (Aurora Skincare, Owner) | Shows that clients only ever see their own organization |

The public agency website is at `/` (no sign-in).

### How testers sign in as any user

Three ways, from the quickest to the most production-like:

1. **One-click test login (non-production only).** With `DevTools:TestLoginEnabled=true` (the default in
   `appsettings.Development.json` and `appsettings.Staging.json`, i.e. `ASPNETCORE_ENVIRONMENT=Development` or
   `Staging`), the sign-in page shows a **Test accounts** panel listing every active test account and every seeded
   demo account (`*@demo.optimizeall.app`, `*@<client>.demo.optimizeall.app`). Click one to sign in as it — no
   password. The API behind it (`GET /api/v1/dev/test-accounts`, `POST /api/v1/dev/test-login`) answers `404` unless
   the flag is on **and** the environment is not Production, and it refuses any other (real) account. Each use is
   audited as `auth.test_login`. To turn it off on staging set `DevTools__TestLoginEnabled=false`.
2. **Test users of any role.** As an admin, open **Users → Create test user**, pick the role(s) (for Client, optionally
   a client organization and client role) and copy the generated email (`test+<name>-<random>@test.optimizeall.app`,
   domain from `TestAccounts:EmailDomain`) and password — the password is shown only once. Test users are verified,
   labelled **TEST** in the admin lists, never paid by payout batches and left out of analytics/marketing KPIs and
   the tracking leaderboard. Filter the list with **Account type → Test accounts**; delete (deactivate) them with
   `DELETE /api/v1/admin/test-users/{id}`.
3. **Log in as (impersonation), any environment.** An admin opens a user (or the row action in **Users**) → **Log in
   as**, types the user's email and a reason. The app switches to that user's portal for at most 60 minutes, with a
   high-contrast banner on every page: *"You are viewing as Jane Doe (participant) — Exit"*. **Exit** returns to
   **Admin → Users** as the admin. Admins, other impersonators, yourself and suspended accounts can't be impersonated,
   and password/email/payout-detail changes, payment/payout approvals and recording, API keys and impersonating
   again are refused. Everything done is audited as "admin as user" (see SECURITY.md § 2.1).

Demo script: sign in as `admin@demo.optimizeall.app`, open **Users**, find Sara, **Log in as** → reason "Demo" → Sara's
home with the banner → **Exit** → back on the users page.

## What is in the dataset

* **People.** 6 staff and 41 participants in PK, AE, SA, GB, US, IN and EG, with varied languages, time zones, tiers
  (Standard to Platinum) and interests.
  * 94 social profiles on Instagram, TikTok, X, YouTube, LinkedIn and Facebook. Most are Verified; some are pending
    review or unverified, and one was rejected.
  * Payout profiles for most participants: PayPal, bank IBAN or mobile wallet.
  * 9 participants registered in the last 30 days, so the analytics funnel has a cohort.
* **Campaigns** (fictional brands):

  | Campaign | Status | Currency | Highlights |
  |---|---|---|---|
  | Nimbus Fitness App launch | Active | USD | Reward rules **v1 → v2**; overrides by platform, country and tier; launch-week and weekend bonuses; first-post and manual quality bonuses; daily, weekly and campaign caps; tracking link with UTM; a running A/B experiment |
  | Desert Bloom Skincare — Autumn Glow | Active | **AED** | AE/SA only; 72 h live check; Arabic caption and disclosures |
  | Karachi Eats food festival | Active | **PKR** | PK only; 48 h live check; tracking destination with UTM |
  | LedgerLeaf budgeting app — Save smarter | Ended | USD | Most of the paid history; tracking and conversions; completed experiment |
  | Wanderly Travel — Hidden Gems | Paused | USD | Interest targeting; paused by the client |
  | Aurora Pro headphones — creators circle | Active, **invite-only** | USD | Gold/Platinum only, verified profiles, 5,000+ followers; invitation link |
  | Orbit Arena season 3 trailer | Scheduled | USD | Starts in 5 days; launch-day bonus |
  | CodeSprout Kids coding week | Draft | USD | Incomplete draft |

* **Exchange rates** marked `source = demo`: AED, PKR (updated 30 days ago), SAR and GBP to USD.
* **Person-level rates** (docs/REWARD_ENGINE.md § Person-level rates), created about 40 days ago by the manager:
  * rate cards *Macro creators 2026* (v1, v2 raised Instagram 10 days ago, **v3 awaiting a second approval** — the
    four-eyes threshold `rates.fourEyesIncreasePercent` is 50 %), *Micro creators 2026*, *Nano creators 2026*,
    *Standard participants*, *Platinum tier bonus rate*, and an archived *Summer 2026 creators (retired)*;
  * rate groups **Macro influencers** (50k+ followers), **Micro influencers** (10k–50k), **Nano influencers** (2k–10k)
    and **Standard** (everyone else) — every participant is in exactly one band — plus the automatic
    **Platinum tier (automatic)** segment; each has its card for every campaign from 18 days ago (after Nimbus
    rules v2, so v1-priced posts keep their campaign rate);
  * personal deals: **Sara** has a custom rate for every campaign that **expires in 5 days**; Priyanka has a flagship
    YouTube deal for Aurora Pro only (capped by Aurora's 3× maximum); Amelia gets the Macro card for the Nimbus launch;
  * Karachi Eats uses **campaign rates only**; Aurora Pro limits personal rates to 3× the campaign rate;
  * the submissions of the last 18 days are priced with these rates (USD cards converted into AED for Desert Bloom),
    so their ledger lines show the rate source.
* **Discount codes** (docs/DISCOUNT_CODES.md), created about a month ago by the manager:
  * program **Glow Cosmetics — summer affiliate** (Active, USD): 10 % of the order value, a tier *from 5 sales: 12 %
    + 25 USD bonus*, a 50 USD bonus at 15 sales, daily cap 150, per-person cap 1,000, budget 5,000; and a draft
    **Aurora Travel — autumn codes (draft)**;
  * 14 codes: 12 imported from the brand's CSV (one import batch) and 2 added by hand — assigned, available, one
    **paused**, one **retired** ("leaked on a coupon site") and one **expired** 5 days ago;
  * personal codes **GLOW-SARA15** (Sara) and **GLOW-LAYLA15** (Layla, with a negotiated **6 USD per order** override)
    and the shared code **GLOWSQUAD** for the rate group **Glow beauty squad** (Zainab, Kavya, Mona, Chloe; group
    override 12 %); Hannah holds GLOW-HANNAH;
  * sales in every state: Sara's six approved sales (the 5th earns the tier bonus, the 6th is priced at 12 %), one
    **refunded** (reported by the brand; commission reversed), one **pending**, one **needs info**, one **rejected**,
    and an **unclaimed use created from the brand's report**; Layla and Zainab approved; Kavya's pending sale
    **matched by the brand's report** (bulk-approvable) and Mona's pending one;
  * approved sales have `SaleCommission` / `SaleTierBonus` ledger entries with the payout source
    (`CodeProgramRules`, `CodeProgramTier`, `CodePersonOverride`, `CodeGroupOverride`).
* **About 170 submissions** in every status (Pending, UnderReview, Approved, NeedsCorrection, Rejected, Reversed):
  * one live reviewer claim and one expired claim;
  * risk flags: duplicate screenshot, outside the campaign window, high velocity, repeated content, unverified
    profile, new participant;
  * live checks that are pending and not yet due, pending and already due, confirmed, and removed (reversed);
  * appeals that are open, upheld and overturned.
* **Earnings in every state**: PendingApproval (quality bonuses, live checks, a flagged referral reward and a pending
  adjustment), Approved (some still inside the 3-day hold), Scheduled, Paid, Reversed, Declined, and clawbacks netted
  against a later payout. Adjustments carry reasons; positive ones were created by finance1 and approved by finance2.
  Referral rewards are included.
* **Payout batches** (biweekly):
  * c3 and c2: Completed, all items paid with payment references (one c2 payment failed and its earnings rolled into
    c1).
  * c1: Finalized, about half the items Paid and the rest AwaitingPayment.
  * All batches were prepared by finance1. Historical batches were finalized by finance2, or by the admin when
    finance2 had approved an earning inside that batch: nobody finalizes a batch containing earnings they created,
    approved or receive.
  * c0: **Draft**, prepared by finance1. Its exclusions show the payout hold, the suspended account and participants
    below the minimum.
* **Growth.**
  * Referrals (qualified, pending and flagged for a shared device) and invitation links.
  * About 60 tracking links with unique, repeat and bot clicks, plus verified and unverified conversions.
  * One running and one completed experiment with assignments, post templates and calendar entries.
* **Content and support.**
  * Homepage banners for each audience (onboarding, eligible, active earners, inactive, AE/Arabic) and three
    announcements.
  * Support tickets that are open, awaiting the participant, awaiting staff and resolved, with internal notes.
  * Notifications with a mix of read and unread. Achievements are awarded from the real metrics.
* **Learning** (Optimize All Academy, [LEARNING.md](LEARNING.md)): every course pack in the catalog is published by the
  Baseline seed; the Demo seed features "Getting started on Optimize All" and the first course of other categories.
  Sara completed "Getting started" (a failed first attempt, then a pass) and holds its **certificate**, and is part-way
  through up to two more courses; the new participant has started "Getting started"; eight more demo participants have
  enrolments, lesson progress and attempts (two hold certificates), so the Learning admin shows enrolments, pass rates,
  average scores and question analytics.
* **Audit log** entries for staff actions, for example `campaign.reward_rules_changed`, `campaign.published`,
  `submission.approved`, `ledger.adjustment_created`, `payout.batch_prepared`, `payout.batch_finalized`,
  `payout.payment_recorded`, `payout.hold_created` and `admin.user_suspended`.

## Demo scripts

API paths are given for reference (`/api/v1/...`); in the web app use the matching portal pages.

### 1. Participant journey (Sara)

1. Sign in as `sara.participant@…`.
   * The home page shows active-earner banners, announcements and unread notifications.
2. Open **Earnings** (`GET /me/earnings/summary`). Every bucket has a value:
   * **Pending**: the Aurora Pro post waiting for review, the Karachi Eats post whose live check is due, and a
     3.00 USD quality bonus waiting for approval.
   * **Approved / on hold / available for next payout**: recent Nimbus approvals, some still inside the 3-day hold,
     and a goodwill credit.
   * **Scheduled**: her item in the draft batch.
   * **Paid**: her LedgerLeaf and Nimbus history.
   * **Reversed**: a Wanderly post whose disclosure was removed after approval.
3. Open **Submissions**. Show each item's timeline (submitted → approved, etc.):
   * the Nimbus TikTok post submitted under **reward rules v1** and approved after v2 went live, still paid at the
     v1 TikTok rate (7.50, not 8.50);
   * the Wanderly post that **needs a correction** while the campaign is paused.
4. **Payouts** (`GET /me/payouts`): paid items with masked destination and payment reference.
5. **Referrals**:
   * Zainab qualified, and finance approved the reward (it is then included in a payout).
   * Usman qualified, but the reward is pending because of a shared-device signal.
   * Karim is still pending.
6. **Achievements** (first approved post, five approved, multi-platform…), **Notifications** (read and unread mix) and
   **Support**:
   * an open payout question;
   * a ticket awaiting her reply;
   * a resolved ticket that produced the goodwill credit.
7. Contrast with `new.participant@…`:
   * **Social accounts** shows the Instagram profile doesn't qualify yet: `social.account_too_new`, qualifies in 80
     days.
   * Every campaign card shows the same reason.
8. Contrast with `unverified@…` (onboarding banner, email not verified).
9. Contrast with `hold.participant@…`: the earnings page shows the neutral payout-hold message.
10. **Learning** (`/app/learning`, also the Learning panel on the home page and "Earned badges" on the profile):
    * "Continue where you left off" for her in-progress courses and recommended courses;
    * her **Optimize All Certified Creator** certificate: open it to download the PDF, see the "Add to LinkedIn profile"
      and "Share on LinkedIn" links, the Open Badge and the public verification page (`/verify/certificates/{id}`);
    * the course's final assessment page lists her failed and passed attempts; open one to see the per-question review.
    * Anonymous visitors see the same catalog on the website at `/learn` (header "Academy", home page "Free courses").
    * Admin: **Admin → Learning** shows the statistics, question analytics and learners; **Certificates** lets you
      revoke Sara's certificate (reason required; the verification page then shows "Revoked").

### 2. Reviewer journey

1. Sign in as `reviewer1@…` and open the **Review queue** (`GET /review/queue`).
   * One item is **UnderReview**, claimed by Priya Nair (reviewer2); claims last 15 minutes.
   * High-risk items show their flags.
2. Claim a pending submission (`POST /review/submissions/{id}/claim`), check the screenshot and post link, then
   **Approve** it, optionally with a quality bonus.
   * The decision shows the reward lines priced from the submission's captured rule version.
3. Request a correction or reject another submission (a reason is required).
4. **Live checks** (`GET /review/live-checks`): confirm a due Desert Bloom or Karachi Eats post (earnings become
   Approved), or mark one removed (this reverses its earnings).
5. **Appeals** (`GET /review/appeals`): resolve Kavya's open appeal.
   * Show the upheld duplicate-screenshot appeal (Harry) and the overturned LedgerLeaf appeal (Ahmed).
6. Show Bilal's submissions:
   * the velocity flag on his 11th post;
   * "caps applied" on his approvals.

### 3. Campaign manager journey

1. Sign in as `manager@…` and open **Campaigns**. There is a campaign in every status: Active, Scheduled, Paused,
   Ended and Draft, plus one invite-only campaign.
2. Open **Nimbus Fitness → Reward rules**.
   * Compare **v1** and **v2** (base 6.00 → 7.00, TikTok and Pakistan overrides, Platinum tier rates, the
     time-limited bonuses, first-post and quality bonuses, caps).
   * Each version shows how many submissions use it. The change is in the audit log
     (`campaign.reward_rules_changed`).
3. Use the **reward preview** to price a TikTok post from a Platinum creator in PK.
4. Approve or decline pending **quality bonuses** (`/finance/pending-earnings`, permission `rewards.approve_bonus`).
5. **Marketing**:
   * the running Nimbus title experiment (with assignments) and the completed LedgerLeaf landing-page experiment
     (winner B);
   * invitation links (Aurora VIP invites);
   * tracking links with clicks and conversions;
   * templates and the content calendar.
6. **Analytics** (`GET /analytics/overview`):
   * counted sections: funnel, posts, spend;
   * measured sections: tracked clicks with bots excluded, verified conversions;
   * estimated section: reach from declared follower counts.
7. Try to publish **CodeSprout** (Draft). Publishing explains what is missing.
8. **Rate cards / Rate groups** (manager portal): open *Macro creators 2026* → the pending v3 raise (approve it as
   the admin — the manager proposed it and can't approve it himself); open *Micro influencers* → members (select
   several, remove with a reason), **Import CSV** (check first), **Export**, **History**.
9. **Admin → Users → Sara → Rates**: her groups, the expiring custom rate, the effective rate per platform and
   **Explain** (the deal outranks her group's card). In the Nimbus campaign editor → *Rewards* → *Personal & group
   rates*: the price simulator ("price for Sara on TikTok").
10. Sign in as Sara → **Campaigns**: "Your personal rate … until …" on the cards and the campaign page.

### 4. Finance journey (four-eyes payout run)

1. Sign in as `finance1@…` and open **Payout batches**. There are four:
   * two Completed (c3 and c2);
   * one Finalized with items Paid and AwaitingPayment (c1);
   * one **Draft** (c0).
2. **Prepare** (already done): the draft for the last completed period was prepared by finance1.
3. **Review** the draft:
   * Items with masked destinations, totals by status, and **exclusions**: payout hold (Aisha), account inactive
     (Jack, suspended), below minimum.
   * Optionally hold and unhold an item.
4. Try to finalize as finance1. It is refused (`payout.self_finalize`).
5. Sign in as `finance2@…` and **finalize** the draft (confirm + reason).
   * Items become AwaitingPayment and the manual provider asks for payment.
6. **Export**: `GET /finance/payout-batches/{id}/export.csv` and `/payment-instructions.csv`.
7. Sign in as `finance1@…` and **record payment** for an item (`POST /finance/payout-batches/{id}/items/{itemId}/record-payment`)
   with a bank reference. Optionally bulk-record the rest.
   * When every item is paid or failed, the batch completes.
8. **Reconcile**: `GET /finance/payout-batches/{id}/reconciliation` shows *balanced* for every batch.
   * In the c2 batch, show the failed payment whose earnings rolled into c1.
   * Show Hamza's clawback netted inside the c2 batch.
9. **Ledger**:
   * the ledger with adjustments, their reasons and approvers;
   * the pending 15.00 USD credit for Noor, which finance2 can approve (it was created by finance1);
   * the FX rates marked `demo`;
   * payout holds, one active and one released.

### 5. Admin journey

1. Sign in as `admin@…` and open **Users**.
   * Filter to the suspended participant (reason and history).
   * Show tiers and roles; you can reactivate the participant.
2. **Audit log**: filter by action (`campaign.reward_rules_changed`, `payout.batch_finalized`, `payout.payment_recorded`,
   `admin.user_suspended`) or by actor. Seeded rows carry correlation id `demo-seed`.
3. **Settings** (eligibility minimum account age 90 days, velocity limit, claim minutes) and **Jobs**.
4. **Content**: banners per audience (onboarding, eligible, active earners, inactive, AE/Arabic), announcements,
   FAQs and onboarding steps.
5. **Support**:
   * Hamza's urgent dispute with an internal note;
   * Aisha's ticket awaiting staff;
   * the resolved tickets.

### 6. Discount codes (brand codes → sales → commission)

1. **Sara** → *My codes*: GLOW-SARA15 with copy and share link, "You earn 10% of net", the tier perk, her stats and
   sales. *Report a sale* (order number, date, value, discount, optional receipt) → the sale page shows the estimate.
   Open the **needs info** sale to see what the reviewer asked for and update it.
2. **manager** → *Discount codes* → Glow Cosmetics: payout rules and overrides (*Overview*), codes with statuses and
   holders (*Codes*: import a CSV — check first —, generate, assign to a person or a rate group, "give a group one code
   each", pause/retire), *Sales* (add a sale, **import the brand's sales report**), *Report* (by person / code / group,
   CSV).
3. **reviewer1** → *Code sales*: approve, reject or request info; select Kavya's matched sale and *Approve matched*.
   A sale you reported or entered can't be decided by you (four-eyes).
4. **finance1** → *Code sales* → an approved sale → *Mark refunded*: the commission is reversed (a clawback if paid);
   *Ledger* shows `SaleCommission` rows with their payout source.

## Automated checks

`backend/tests/OptimizeAll.IntegrationTests/Seed/DemoSeedTests.cs` boots the API with `["Baseline", "Demo"]` on a fresh
database. It checks that:

* the seed is idempotent;
* the demo logins work;
* every batch reconciles;
* the ledger meets its constraints and key conventions;
* every journey is covered;
* Sara's balance buckets have values;
* the new participant is ineligible;
* analytics has counted, measured and estimated sections;
* a reviewer can claim and decide a submission;
* finance2 can finalize the draft batch that finance1 prepared;
* the person-level rates are there (four groups with members and cards, Sara's expiring deal, a pending raise, an
  archived card), recent submissions were priced with them (snapshots consistent with the card lines and exchange
  rates, never on the campaign-rates-only campaign), their ledger lines carry the source, Sara sees only her own rate.
* Sara holds a valid learning certificate (with a failed and a passed attempt), the new participant has a course in
  progress and the Learning admin shows real statistics.
