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
| `PostedAtUtc` | Submission `PostedAt` |
| `IsFirstApprovedPostInCampaign` | No other Approved submission of the participant in the campaign **and** no `firstpost:{campaignId}:{userId}` earning exists yet |
| `EarnedTodayInCampaign`, `EarnedThisWeekInCampaign` | Sum of the participant's campaign earnings (status not Reversed/Declined, reversal legs included) whose submission was posted on the same campaign-local day / Monday-based week as this post (campaign `TimeZone`) |
| `EarnedInCampaignTotal` | Same, all time |
| `CampaignBudgetRemaining` | `Campaign.BudgetAmount` − all non-reversed, non-declined campaign earnings; null when no budget (or when the budget currency differs from the rule currency, which validation prevents) |
| `QualityBonusRequested` | Reviewer's proposed quality bonus |

Caps are evaluated against the **posting** day/week rather than the approval day, so a review backlog approved
on one day does not unfairly exhaust a participant's daily cap.

## Algorithm

1. **Validate** the rule set (see below); an invalid set throws `reward.invalid_rules` (400).
2. **Post rate** (`PostReward` line): the most specific `RateOverride` whose conditions all match and whose window
   contains `PostedAt`. Specificity = number of set conditions among Platform/Country/Tier. Ties → higher
   `Priority` → higher `Amount` → lower `Id`. No match → the `BaseRate`.
3. **Time-limited bonuses**: every `TimeLimitedBonus` whose window contains `PostedAt` and whose optional
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
  final guard (409 `reward.version_conflict`). Versions are never updated or deleted.
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
| FirstPostBonus | `firstpost:{campaignId}:{userId}` — at most one per participant per campaign, even under races |
| Appeal overturn | the above with `:appeal:{appealId}` appended (first-post key unchanged) |

`RequiresApproval` on the ledger = `line.RequiresApproval || campaign.MinPostLiveHours > 0`. When a live check is
required, confirming the post is still live approves every pending line except `ManualApproval` bonuses, which
stay pending for finance (`rewards.approve_bonus`).

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
