# Reward engine

`OptimizeAll.Domain.Rewards.RewardEngine` prices one approved post. It is a pure function — no database, no
clock — so the same rule-set version and context always give the same result. Unit tests:
`backend/tests/OptimizeAll.UnitTests/Rewards/RewardEngineTests.cs`.

```
RewardQuote Quote(RewardRuleSet ruleSet, RewardContext context)
IReadOnlyList<string> Validate(RewardRuleSet ruleSet)      // used when rules are saved
```

## Data model

A campaign's rewards are a list of immutable, numbered **rule-set versions** (`reward_rule_sets`, unique
`(CampaignId, Version)`), each with rules (`reward_rules`):

| RewardRuleSet field | Meaning |
|---|---|
| `Currency` | ISO 4217 code every amount in the version uses (must be in `Money.SupportedCurrencies`). |
| `DailyCapPerParticipant`, `WeeklyCapPerParticipant`, `CampaignCapPerParticipant` | Optional per-participant limits in `Currency`. |
| `EffectiveFrom`, `CreatedByUserId`, `ChangeReason` | When the version took effect, who created it and why (audited). |

| RewardRule field | Used by |
|---|---|
| `Type` | `BaseRate`, `RateOverride`, `TimeLimitedBonus`, `FirstPostBonus`, `QualityBonus` |
| `Amount` | Amount (≥ 0) in the rule set currency; for `QualityBonus` the maximum a reviewer may award |
| `Platform`, `CountryCode`, `Tier` | Optional conditions (unset = any) for overrides and time-limited bonuses |
| `ValidFrom`, `ValidTo` | Window, UTC, inclusive start / exclusive end |
| `ApprovalMode` | `Automatic` or `ManualApproval` (earning recorded as PendingApproval for finance) |
| `Priority` | Tie-breaker among equally specific overrides |
| `Label` | Text shown to participants and on ledger lines |

## Context

`RewardContext` is built by `IRewardQuoteService` (`Api/Modules/Rewards/RewardQuoteService.cs`):

| Field | Source |
|---|---|
| `Platform` | Submission platform |
| `CountryCode`, `Tier` | Participant profile at pricing time |
| `PostedAtUtc` | `min(PostedAt, SubmittedAt)` of the submission (see "Post and submission times") — used only for rate-override and time-limited-bonus windows |
| `IsFirstApprovedPostInCampaign` | No other Approved submission of the participant in the campaign **and** no live (not Reversed, not reversed-by-a-reversal-entry) `FirstPostBonus` earning of the participant in the campaign. Declined bonuses count as taken. |
| `EarnedTodayInCampaign`, `EarnedThisWeekInCampaign` | Sum of the participant's campaign earnings (status PendingApproval/Approved/Scheduled/Paid, reversal legs included) whose submission was **submitted** on the same campaign-local day / Monday-based week as this submission's `SubmittedAt` (campaign `TimeZone`; entries without a submission use their `CreatedAt`) |
| `EarnedInCampaignTotal` | Same, all time |
| `CampaignBudgetRemaining` | `Campaign.BudgetAmount` − all campaign earnings with status PendingApproval/Approved/Scheduled/Paid (written as `Status IN (…)` so it range-scans the `earning_entries (CampaignId, Status)` index); null when no budget (or when the budget currency differs from the rule currency, which validation prevents) |
| `QualityBonusRequested` | Reviewer's proposed quality bonus |
| `PersonalRate` | The person-level rate locked on the submission (`submission_rates`), converted to the rule-set currency, or null — see "Person-level rates" |

Caps are evaluated against the **submission** day/week rather than the approval day, so a review backlog approved
on one day does not unfairly exhaust a participant's daily cap — and not against the participant-declared post
day, so back-dating posts cannot spread them over several daily caps.

## Post and submission times

`PostedAt` is chosen by the participant; `SubmittedAt` is set by the server when the submission is created (a
correction/resubmission keeps it). Money only depends on `PostedAt` within fixed, campaign-independent bounds
(`Domain/Submissions/SubmissionTiming.cs`):

| Rule | Value |
|---|---|
| `PostedAt` may be in the future by at most | 10 minutes (clock skew) → else 400 `submission.posted_at_in_future` |
| `PostedAt` may be before `SubmittedAt` by at most | **7 days** (`SubmissionTiming.MaxPostAgeAtSubmission`) → else 400 `submission.posted_at_too_old` |
| Risk flag `PostedLongBeforeSubmission` (weight 15) | `SubmittedAt − PostedAt > 48 h` |
| Rate-override and time-limited-bonus windows | evaluated at `min(PostedAt, SubmittedAt)` — effectively PostedAt, but never later than the submission (a future PostedAt can't reach into a window that hasn't started) and never earlier than SubmittedAt − 7 days |
| Daily / weekly caps | campaign-local day / week of `SubmittedAt` |
| Live-check due time | `max(PostedAt, SubmittedAt) + MinPostLiveHours`; confirmation before it is refused (409 `review.live_check_not_due`) |

A resubmission after a correction request re-runs all of these checks (against the original `SubmittedAt`),
recomputes the risk flags and re-prices the estimate.

## Algorithm

1. **Validate** the rule set (see below); an invalid set throws `reward.invalid_rules` (400).
2. **Post rate** (`PostReward` line): the most specific `RateOverride` whose conditions all match and whose window
   contains the context's `PostedAtUtc`. Specificity = number of set conditions among Platform/Country/Tier. Ties → higher
   `Priority` → higher `Amount` → lower `Id`. No match → the `BaseRate`. When the context carries a person-level rate
   (and the version allows them) that rate replaces the selected campaign rate, limited by `PersonalRateMaxMultiplier`.
3. **Time-limited bonuses**: every `TimeLimitedBonus` whose window contains `PostedAtUtc` and whose optional
   conditions match adds a `TimeLimitedBonus` line (ordered by window start).
4. **First-post bonus**: added when `IsFirstApprovedPostInCampaign`.
5. **Quality bonus**: only when `QualityBonusRequested > 0`; amount = `min(requested, rule.Amount)`.
   Requested without a `QualityBonus` rule → `reward.quality_bonus_not_configured` (400).
6. `RequiresApproval` = `rule.ApprovalMode == ManualApproval`.
7. **Rounding**: each line's amount is rounded with `Money.Round` (currency minor units, midpoint away from zero:
   USD 2, JPY 0, KWD 3 decimals). `UncappedAmount` is this rounded value.
8. **Caps**, applied to lines in order (post reward first, then bonuses). Each line is reduced to the smallest
   remaining headroom of: daily cap − earned today, weekly cap − earned this week, campaign cap − earned in
   campaign, and remaining budget. Headroom is consumed line by line, never below zero. Every limit that reduced a
   line is reported in `AppliedCaps` (`daily_cap`, `weekly_cap`, `campaign_cap`, `campaign_budget`). A capped
   amount is rounded **down** to the minor unit so it can never exceed the limit. Lines that end at 0 are dropped.
9. `Total` = sum of lines; `RuleSetSummary` e.g. `v2 USD: base 5.00; 1 override; 2 bonuses; daily cap 20.00`.

A quote can legitimately be empty (Total 0) — e.g. the budget is exhausted. The submission is still approved;
the applied caps are returned and written into the approval event reason.

## Validation rules (`RewardEngine.Validate`)

* currency supported; caps > 0 when set;
* exactly one `BaseRate`, with no conditions/window and `Automatic` approval;
* `RateOverride` needs at least one condition or window, `Automatic` approval;
* `TimeLimitedBonus` needs both `ValidFrom` and `ValidTo`;
* `FirstPostBonus` / `QualityBonus`: at most one each, no conditions/window;
* amounts ≥ 0, country codes two letters, `ValidTo > ValidFrom`, label ≤ 150 chars.

## Versioning and historical rates

* Saving rules (`POST /api/v1/admin/campaigns/{id}/reward-rules`, `rewards.edit` + `confirm` + `reason`) inserts
  version `max + 1` in a transaction that locks the campaign row; the unique `(CampaignId, Version)` index is the
  final guard (409 `reward.version_conflict`). Versions are never updated or deleted. The editor sends the version it
  started from as `baseVersion`; if another version was saved in the meantime the save is refused with the same 409
  (checked under the campaign lock), so two managers editing at once can't silently replace each other's rates.
* A submission records `RewardRuleSetId/Version` in force when it was **created** (highest version with
  `EffectiveFrom ≤ now`) and keeps it through corrections. Approval prices it with that version — later rate
  changes do not affect it.
* Approved earnings are immutable `EarningEntry` rows (amount, currency, rule set id/version/rule id) written via
  `ILedgerWriter`; a rate change never touches them. Corrections are reversals/adjustments.
* The currency of a campaign's rules cannot change once it has submissions (`reward.currency_locked`), so every
  aggregate and the budget are in one currency.

## Earnings written on approval

`Api/Modules/Review/ReviewService.cs` records one ledger entry per quote line, inside the transaction that holds
`SELECT … FOR UPDATE` on the campaign row (so caps, budget and the first-post check are evaluated serially):

| Line | Idempotency key |
|---|---|
| PostReward, QualityBonus | `submission:{submissionId}:{Type}` |
| TimeLimitedBonus | `submission:{submissionId}:TimeLimitedBonus:{ruleId}` (several can apply to one post) |
| FirstPostBonus | `firstpost:{campaignId}:{userId}`, or `firstpost:{campaignId}:{userId}:{n}` where `n` = number of the participant's reversed first-post bonuses in the campaign — at most one **live** first-post bonus per participant per campaign, even under races |
| Appeal overturn | the above with `:appeal:{appealId}` appended (first-post key follows the rule above) |

**First-post bonus after a reversal.** A reversed first-post bonus (status Reversed, or a paid one clawed back by a
Reversal entry) no longer counts as "taken": the participant's next approval in the campaign (including an appeal
overturn of the reversed submission) earns it again. Its key carries `n`, so each re-earning has a new, unique key;
`n` and the "taken" check are evaluated under the campaign row lock and the unique idempotency index is the final
guard, so concurrent approvals still produce a single live bonus. A declined bonus stays taken.

`RequiresApproval` on the ledger = `line.RequiresApproval || campaign.MinPostLiveHours > 0`. When a live check is
required, confirming the post is still live approves every pending line except `ManualApproval` bonuses, which
stay pending for finance (`rewards.approve_bonus`).

**Withdrawn submissions.** A participant can withdraw a submission before it is decided (Pending, UnderReview or
NeedsCorrection → `Withdrawn`, see `docs/api/campaigns-submissions-review.md`). Nothing is priced or reserved before
approval — the estimate shown to the participant is informational and caps/budget are evaluated only at approval
under the campaign lock — so a withdrawal writes no ledger entries and has nothing to release. A withdrawal and an
approval racing on the same submission are both conditional updates on its status, so either the approval (with its
earnings) or the withdrawal (with none) happens, never both. Withdrawn submissions don't count toward the
per-participant submission limit, the "first approved post" check or any cap.

## Mapping future options to the model

| Option | How it maps |
|---|---|
| Country-specific rates | `RateOverride` with `CountryCode` |
| Tier-specific rates | `RateOverride` with `Tier` (combine with Platform/Country for more specific rates) |
| Platform rates | `RateOverride` with `Platform` |
| Promotional rates for a period | `RateOverride` with `ValidFrom/ValidTo` (replaces the rate) or `TimeLimitedBonus` (adds to it) |
| Campaign budgets | `Campaign.BudgetAmount` / `BudgetCurrency` (= rule currency); enforced as the `campaign_budget` cap |
| Per-participant caps | Daily/weekly/campaign caps on the version |
| Bonuses needing sign-off | `ApprovalMode = ManualApproval` → PendingApproval earning |
| Referral rewards | Not priced by this engine: the referral program (`referral.program` setting) records `ReferralReward` earnings through `ILedgerWriter` with its own idempotency key. A campaign-scoped referral bonus would be a new `RewardRuleType` priced here in the same way as `FirstPostBonus`. |
| Multi-currency | Each campaign's rules use one original currency; `ILedgerWriter` converts every earning to the payout schedule's settlement currency at the rate in force and stores both amounts plus the rate (`ExchangeRate`, `SettlementAmount`). Adding a currency = add it to `Money.SupportedCurrencies` and configure exchange rates. |
| Engagement-based rewards (views/likes) | Would add context fields (e.g. verified view count) and a new rule type; the versioning and ledger model stay unchanged. |
| Per-person / per-group rates | Rate cards assigned to people, rate groups or segments (next section). |
| Affiliate / discount-code sales | Not posts: a separate code-program workflow ([DISCOUNT_CODES.md](DISCOUNT_CODES.md)) prices approved sales with its own calculator and records `SaleCommission` / `SaleTierBonus` earnings through `ILedgerWriter`. It reuses rate groups (shared codes, group payout overrides) and the rate-source ledger fields with the levels `CodePersonOverride`, `CodeGroupOverride`, `CodeProgramTier` and `CodeProgramRules` (outside the post precedence below). |

## Person-level rates (rate cards, rate groups, personal deals)

Different people can be paid different rates: influencers, groups of creators ("Macro", "Micro", "Nano",
"Standard", "VIP") and individually negotiated deals. The campaign rules above stay the default; a **person-level
rate** replaces the campaign's post rate (the `BaseRate` / `RateOverride` the post would otherwise get) for one
person. Code: `Domain/Rewards/PersonalRates.cs` (data + the pure `PersonalRateResolver`), `Api/Modules/Rates`
(services, endpoints). UI: *Manage → Rate cards / Rate groups*, *Admin → Users → (person) → Rates* and the campaign
editor's *Rewards* tab (policy, "Personal & group rates" panel, price simulator). API: [`docs/api/rates.md`](api/rates.md).

### Building blocks

| Concept | Table | What it is |
|---|---|---|
| **Rate card** | `rate_cards` | A named, reusable set of rates in one currency. Draft → Active → Archived. `Kind = Custom` cards are private negotiated rates owned by one person (hidden from the card list). |
| **Card version** | `rate_card_versions` | Immutable, numbered (unique `(RateCardId, Version)`), effective-dated (`EffectiveFrom` may be in the future, never in the past). Holds the currency, the lines, optional per-participant daily/weekly/campaign caps and `StackCampaignBonuses`. Status `Approved`, `PendingApproval` (four-eyes) or `Rejected`. |
| **Line** | `rate_card_lines` | Flat amount per approved post, with optional `Platform`, `Format` (Post, Story, ShortVideo, LongVideo, Carousel) and `CountryCode`. At most one line per (platform, format, country) per version. |
| **Rate group** | `rate_groups` | Named group with a `Priority`. **Manual** (members in `rate_group_members`, history in `rate_group_member_events`) or **Automatic** (a segment rule: participant tiers and/or verified-follower bounds on the post's platform, evaluated at pricing time). Archived, never deleted. |
| **Assignment** | `rate_assignments` | Applies a card to one **person** or one **group**, for **every campaign** (`CampaignId` null) or **one campaign**, optionally within `ValidFrom`/`ValidTo` (e.g. a negotiated deal that expires). Ended assignments keep their row (`EndedAt`, `EndReason`). |
| **Snapshot** | `submission_rates` | The person-level rate locked on a submission when it was created (1:1; absent when the campaign rules priced it). |

**Content format.** Submissions have an optional `Format`. The participant may declare it; otherwise it is read from
the link when the link says so (`/reel/`, `/shorts/`, YouTube `watch`/`youtu.be`, TikTok → short video, `/stories/`).
A declared format that contradicts an unambiguous link is refused (`400 submission.format_mismatch`). Submissions
without a format (all older ones) only match format-agnostic lines.

### Precedence

A post is priced by the first level (lowest rank) with an **applicable** candidate — an active card with an approved
version in force, an assignment whose window contains the post time, the person in the group (or matching the
automatic rule on that platform) and a line matching the post:

| Rank | Level (`RateSourceLevel`) | Source |
|---|---|---|
| 1 | `CampaignPersonalCustom` | negotiated custom rate for this person, this campaign |
| 2 | `CampaignPersonalCard` | rate card assigned to this person for this campaign |
| 3 | `CampaignGroup` | card assigned to a manual group the person is in, for this campaign |
| 4 | `GlobalPersonalCustom` | negotiated custom rate for this person, every campaign |
| 5 | `GlobalPersonalCard` | card assigned to this person, every campaign |
| 6 | `GlobalGroup` | card assigned to a manual group the person is in, every campaign |
| 7 | `CampaignSegment` | card assigned to an automatic group the person matches, this campaign |
| 8 | `GlobalSegment` | card assigned to an automatic group the person matches, every campaign |
| 9 | `CampaignRules` | no person-level rate: the campaign's `RateOverride` / `BaseRate` |

Within a level: the **most specific line** wins (number of set conditions among platform, format, country), then the
**higher priority** (the group's priority for group levels), then the **older assignment** (lower time-ordered id) —
deterministic, never random. A level without a matching line falls through to the next level (a personal YouTube-only
deal doesn't hide the group's Instagram rate). Overlapping assignments of the same kind for the same person or group
and scope are refused (`409 rates.duplicate_assignment`), so ties only happen between groups; group priority decides
them and "explain this rate" names the rule that did. This refines the original proposal in one place: a personal
**custom** rate outranks a personal **card** at the same scope (a negotiated deal is the most specific commercial
decision).

**Campaign policy** (fields of each reward rule-set version, so versioned and captured with the submission):
`PersonalRatesMode = CampaignRatesOnly` ignores every person-level rate; `PersonalRateMaxMultiplier` (e.g. `3`) caps a
person-level rate at that multiple of the campaign rate the post would otherwise get (`personal_rate_limit` in
`AppliedCaps`). The campaign's daily/weekly/campaign caps and its budget always apply to everyone; a card's own caps
apply on top (`rate_card_daily_cap`, `rate_card_weekly_cap`, `rate_card_campaign_cap`, evaluated like the campaign
caps). `StackCampaignBonuses = false` makes a card all-inclusive: first-post and time-limited bonuses are not added (a
reviewer's quality bonus still can be).

### When the price locks

Following the rule-set semantics, the person-level rate is **resolved once, when the submission is created**, and
stored in `submission_rates` (card, version, line, group, assignment, card amount and currency, exchange rate and its
id, converted amount and caps, a textual explanation). Assignment windows are evaluated at the reward window time
`min(PostedAt, SubmittedAt)` (the same instant as rate-override windows); card versions at submission time.
Corrections keep the snapshot; approval prices from it; the engine stays pure (`RewardContext.PersonalRate`). So:

* a new card version, removing the person from the group, ending or expiring the deal, or archiving the card or group
  after the submission never changes that submission's price; new submissions use the new state;
* a deal that expires between submission and approval is honoured for posts submitted before it expired;
* approved earnings are immutable ledger rows and never change.

### Currency

A card's currency may differ from the campaign's. The card amount (and its caps) is converted to the campaign's reward
currency with `IExchangeRateProvider` **at submission time**; the rate and its id are stored on the snapshot, then the
ledger converts to the settlement currency as for every earning. Missing exchange rates are refused up front: creating
an assignment checks the campaign's currency (scoped) or every live campaign's currency (global); a card version or
approval that changes currency re-checks the card's assignments; publishing a campaign checks every rate that can apply
to it (`409 rates.fx_missing`, listing the pairs). If a rate still disappears, the submission is refused with `409
rates.fx_missing` — a post is never priced at 0 or silently at the campaign rate. Amounts are rounded to the currency's
minor unit (JPY 0, KWD 3 decimals).

### Ledger and audit

Post-reward earnings carry `RateSource` (the level; `CampaignRules` when no person-level rate applied),
`RateSourceLabel` (e.g. `Group 'Micro influencers' · card 'Micro creators 2026' v2`), `RateCardId`, `RateCardVersion`,
`RateGroupId`, `RateAssignmentId` — immutable like every ledger column, copied onto reversal legs, shown to finance and
in the ledger CSV export. For a person-level line `RewardRuleId` is null (it identifies campaign rules). Every card,
version, approval, group, membership change (bulk operations: one audit row plus one history row per person) and
assignment change is audited with its reason.

### Four-eyes

Setting `rates.fourEyesIncreasePercent` (0 = off; the Demo seed uses 50): a new card version that raises any rate by
more than this percentage over the rate the same post got before (or changes the card's currency) is saved as
`PendingApproval` and prices nothing until a **different** person with `rates.manage` approves it (not its author and
not the custom rate's owner: `403 rates.self_approval`). One pending version per card at a time. First versions,
assignments and custom-rate creation are not four-eyed (they need `rates.manage` / `rates.assign`, are audited, and
money writes are denied while impersonating).

### Who sees what

`rates.view` (campaign managers, finance, admins; usable in custom roles) sees cards, groups, assignments, a person's
rates and the explanation. `rates.manage` edits cards, groups and custom rates; `rates.assign` manages assignments and
membership. Reviewers see which rate applies to a submission (card and group names only with `rates.view`).
**Participants** see only their own rate ("Your personal rate" for a deal, "Your rate" for a group or segment rate) and
the deal's end date on campaign cards and pages, and whether a submission was priced with it — never card names, group
names or other people's rates: commercial terms are private. Rate cards and groups are organisation-wide (campaigns
are not client-tenant data); test accounts are priced like anyone (they are never paid); suspended people can stay in
groups (they can't submit while suspended); deactivated accounts can't be added.

### Worked examples

Campaign "Launch" (USD): `BaseRate 5`, `RateOverride TikTok 7`, `FirstPostBonus 1`, maximum personal rate 3×.

| Person | Assignments | Post | Result |
|---|---|---|---|
| Ann | none | Instagram | 5 + 1 = **6** (`CampaignRules`) |
| Ben | group *Micro* (card: any 10, Instagram 12) | Instagram | 12 + 1 = **13** (`GlobalGroup`, most specific line) |
| Ben | same | TikTok | 10 + 1 = **11** (the group rate replaces the TikTok override) |
| Cat | *Micro* (priority 30) and *VIP* (priority 50, card: any 20) | Instagram | Micro's Instagram line (1 condition) beats VIP's default (0): 12 + 1 = **13** |
| Cat | same | TikTok | two default lines: VIP's priority 50 wins: 20 + 1 = **21** |
| Dan | *Micro* + custom deal Instagram 40 (every campaign) | Instagram | the deal wins (rank 4 < 6), limited to 3 × 5: 15 + 1 = **16** (`personal_rate_limit`) |
| Dan | + card *Launch special* (any 9) for this campaign | Instagram | the campaign-scoped card (rank 2) wins: 9 + 1 = **10** |
| Eve | GBP card (any 8.99), GBP→USD 1.25 | any | 11.2375 → **11.24** USD (+1); 1.25 is stored on the snapshot |
| Fay | deal until 30 Sep | submitted 29 Sep, approved 2 Oct | deal price (locked at submission) |
