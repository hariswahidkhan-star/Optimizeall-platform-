# API: person-level rates (rate cards, rate groups, assignments, custom rates)

Base path `/api/v1`. Conventions as in [campaigns-submissions-review.md](campaigns-submissions-review.md): camelCase
JSON, string enums (an undefined value is a 400), UTC ISO-8601 timestamps, RFC 7807 problems with `code`, `traceId`
and `errors`. Concepts, precedence and locking rules: [REWARD_ENGINE.md § Person-level rates](../REWARD_ENGINE.md#person-level-rates-rate-cards-rate-groups-personal-deals).

Permissions: reads `rates.view`; card/group/custom-rate writes `rates.manage`; assignment and membership writes
`rates.assign`. Every write is denied while impersonating (`403 auth.impersonation_forbidden_action`) and audited.
Writes that carry `concurrencyStamp` answer `409 concurrency.conflict` when it is stale.

Enums: `ContentFormat` Post|Story|ShortVideo|LongVideo|Carousel; `RateCardStatus` Draft|Active|Archived;
`RateCardKind` Standard|Custom; `RateCardVersionStatus` Approved|PendingApproval|Rejected;
`RateGroupMembershipMode` Manual|Automatic; `RateAssignmentTarget` Person|Group; `PersonalRatesMode`
Allowed|CampaignRatesOnly; `RateSourceLevel` CampaignPersonalCustom|CampaignPersonalCard|CampaignGroup|
GlobalPersonalCustom|GlobalPersonalCard|GlobalGroup|CampaignSegment|GlobalSegment|CampaignRules (precedence order);
`RateOutcome` Won|NotApplicable|Outranked.

---

## Rate cards

### `GET /admin/rate-cards` — `rates.view`
Query: `search`, `status` (default: all but Archived), `currency`, paging. `Paged<RateCardListItem>`:
`{ id, name, description, kind, status, currency, currentVersion, lineCount, minAmount, maxAmount, activeAssignments, pendingApproval, updatedAt }`
(standard cards only; custom rates are listed on the person).

### `GET /admin/rate-cards/{id}` — `rates.view`
`RateCard`: `{ id, name, description, kind, status, currency, currentVersion, owner, createdAt, updatedAt, archivedAt,
archiveReason, concurrencyStamp, versions: RateCardVersion[] (newest first), assignments: RateAssignment[],
usedBySubmissions, fourEyesRequiredAbovePercent, fourEyesThresholdPercent }`.
`RateCardVersion`: `{ id, version, currency, dailyCapPerParticipant, weeklyCapPerParticipant, campaignCapPerParticipant,
stackCampaignBonuses, effectiveFrom, createdAt, createdBy, reason, status, decidedBy, decidedAt, decisionNote,
maxIncreasePercent, isCurrent, lines: [{ id, platform, format, countryCode, amount, label }] }`.

### `POST /admin/rate-cards` — `rates.manage` → **201** `RateCard`
```json
{ "name": "Micro creators 2026", "description": "10k–50k followers", "currency": "USD",
  "lines": [ { "amount": 10 }, { "platform": "Instagram", "format": "ShortVideo", "amount": 14, "label": "Reel fee" } ],
  "dailyCapPerParticipant": null, "weeklyCapPerParticipant": null, "campaignCapPerParticipant": null,
  "stackCampaignBonuses": true, "activate": true, "reason": "Q4 pricing" }
```
Creates version 1 (Draft unless `activate`). Errors: 400 `rate_card.invalid` (per-line messages in `errors.lines`:
negative/too large amount, bad country, duplicate platform+format+country, no lines, caps ≤ 0, unsupported currency),
400 validation (name 2–120, reason 5–500, ≤ 100 lines), 409 `rate_card.name_taken`.

### `PUT /admin/rate-cards/{id}` — `rates.manage`
`{ name, description, concurrencyStamp }` → `RateCard` (metadata only; rates change through versions).
409 `concurrency.conflict`, `rate_card.name_taken`, `rate_card.archived`.

### `POST /admin/rate-cards/{id}/versions` — `rates.manage` → **201** `RateCard`
Rates body as above plus `{ "reason": "≥ 5", "effectiveFrom": "optional, ≥ now", "baseVersion": 2, "confirm": true }`.
A raise above `rates.fourEyesIncreasePercent` (or a currency change while it is on) is saved `PendingApproval`.
Errors: 400 `confirmation.required`, `rate_card.effective_in_past`, `rate_card.invalid`; 409
`rate_card.version_conflict` (stale `baseVersion` or concurrent save), `rate_card.pending_approval`,
`rate_card.archived`, `rates.fx_missing` (new currency can't be converted into a campaign the card is assigned to).

### `POST /admin/rate-cards/{id}/versions/{version}/approve` — `rates.manage`
`{ "note": "optional" }` → `RateCard`. The version takes effect at `max(effectiveFrom, now)`. 403
`rates.self_approval` (the author, or the custom rate's owner); 409 `rate_card.not_pending`, `rates.fx_missing`.

### `POST /admin/rate-cards/{id}/versions/{version}/reject` — `rates.manage`
`{ "reason": "≥ 5" }` → `RateCard`. 409 `rate_card.not_pending`.

### `POST /admin/rate-cards/{id}/activate` — `rates.manage`
`{ concurrencyStamp }` → `RateCard`. 409 `rate_card.not_draft`, `rate_card.no_version`.

### `POST /admin/rate-cards/{id}/archive` — `rates.manage`
`{ "reason": "≥ 5", "endAssignments": false, "concurrencyStamp": "…" }` → `RateCard`. With active assignments and
`endAssignments: false` → 409 `rate_card.in_use`; with `true` they are ended (reason recorded). Submissions the card
already priced keep their price.

### `POST /admin/rate-cards/{id}/duplicate` — `rates.manage` → **201** `RateCard`
`{ "name": "…" }` → a Draft standard card with the latest approved version's rates.

---

## Rate groups

### `GET /admin/rate-groups` — `rates.view`
Query: `search`, `mode` (Manual|Automatic), `includeArchived`, paging. `Paged<RateGroupListItem>`:
`{ id, name, description, priority, membershipMode, autoRule, memberCount, cards: RateCardRef[], archivedAt, updatedAt }`.

### `GET /admin/rate-groups/{id}` — `rates.view`
`RateGroup`: `{ id, name, description, priority, membershipMode, autoTiers, autoMinFollowers, autoMaxFollowers,
autoRequireVerified, autoRule, memberCount, createdAt, updatedAt, archivedAt, archiveReason, concurrencyStamp, assignments }`.

### `POST /admin/rate-groups` — `rates.manage` → **201**; `PUT /admin/rate-groups/{id}` — `rates.manage`
`{ name, description, priority (−1000..1000), membershipMode, autoTiers, autoMinFollowers, autoMaxFollowers,
autoRequireVerified }` (+ `concurrencyStamp`, optional `reason` on PUT). 400 `rate_group.invalid_rule` (automatic
without a rule, max ≤ min); 409 `rate_group.name_taken`, `rate_group.has_members` (Manual → Automatic with members),
`rate_group.archived`, `concurrency.conflict`.

### `POST /admin/rate-groups/{id}/archive` — `rates.manage`
`{ "reason": "≥ 5", "force": false, "concurrencyStamp": "…" }`. Members or active assignments and no `force` → 409
`rate_group.in_use`; with `force` every member is removed (history source `group_archived`) and assignments ended.

### `GET /admin/rate-groups/{id}/members` — `rates.view`
Query `search`, paging. Manual groups: members (`addedAt`, `addedBy`, `note`); automatic groups: participants matching
the rule on at least one platform (`followers`). `Paged<RateGroupMember>`:
`{ userId, displayName, email, countryCode, tier, status, isTestAccount, addedAt, addedBy, note, followers }`.

### `POST /admin/rate-groups/{id}/members` — `rates.assign`
`{ "userIds": ["…"] (1–10,000), "note": "optional" }` → `BulkMembersResult`
`{ requested, added, unchanged, removed, rejected: BulkIssue[], warnings: BulkIssue[] }`,
`BulkIssue = { row, value, userId, code, message }`. Rejected: `user.not_found`, `rates.not_participant`,
`rates.user_deactivated`; warnings: `duplicate`, `user.suspended`, `user.test_account`. Existing members are left as
they are. Batched in chunks of 500 in one transaction; one audit row per operation, one history row per person.
409 `rate_group.automatic`, `rate_group.archived`.

### `POST /admin/rate-groups/{id}/members/remove` — `rates.assign`
`{ "userIds": [...], "reason": "≥ 5" }` → `BulkMembersResult` (`removed`, `unchanged` = not members).

### `POST /admin/rate-groups/{id}/members/import?dryRun=true|false&note=` — `rates.assign`
Multipart field `file` (CSV ≤ 2 MB, ≤ 10,000 rows; a column named `email` or `userId`, else the first column).
→ `CsvImportResult { dryRun, rows, valid, added, alreadyMembers, rejected, warnings }` with row numbers (header = row 1).
Extra codes: `csv.blank`, `csv.invalid_value`; 400 `csv.empty`, `csv.too_large`, `csv.too_many_rows`.

### `GET /admin/rate-groups/{id}/members/export.csv` — `rates.view`
`userId,email,displayName,country,tier,status,addedAt,note` (formula-injection safe).

### `GET /admin/rate-groups/{id}/history` — `rates.view`
`Paged<{ userId, displayName, action: Added|Removed, at, actor, source: manual|bulk|csv|group_archived, reason }>`, newest first.

---

## Assignments

### `GET /admin/rate-assignments` — `rates.view`
Query: `userId`, `groupId`, `campaignId`, `rateCardId`, `activeOnly`, paging. `Paged<RateAssignment>`:
`{ id, level, levelLabel, target, card: RateCardRef, person, group, campaign, isCustom, validFrom, validTo, endedAt,
endReason, note, isActive, createdAt, createdBy, concurrencyStamp }`. `GET /admin/rate-assignments/{id}` returns one.

### `POST /admin/rate-assignments` — `rates.assign` → **201** `RateAssignment`
```json
{ "rateCardId": "…", "target": "Person", "userId": "…", "groupId": null, "campaignId": null,
  "validFrom": null, "validTo": "2026-10-31T00:00:00Z", "reason": "Deal DL-7" }
```
400 `rates.target_required`, `rates.target_ambiguous`, `rates.invalid_window` (end ≤ start, end in the past, start more
than a day back), `rates.not_participant`, `rates.custom_card_not_assignable`; 404 card/user/group/campaign; 409
`rate_card.not_active`, `rate_group.archived`, `campaign.closed`, `rates.user_deactivated`,
`rates.duplicate_assignment` (overlapping window, same target, scope and kind), `rates.fx_missing`.

### `PUT /admin/rate-assignments/{id}` — `rates.assign`
`{ validFrom, validTo, reason, concurrencyStamp }` → `RateAssignment` (extend or shorten; a start that has passed can't
move: 400 `rates.window_started`). 409 `rates.assignment_ended`, `rates.duplicate_assignment`, `concurrency.conflict`.

### `POST /admin/rate-assignments/{id}/end` — `rates.assign`
`{ reason, concurrencyStamp }` → `RateAssignment`. 409 `rates.assignment_ended`.

---

## A person's rates

### `GET /admin/users/{userId}/rates?campaignId=` — `rates.view`
`PersonRates`: `{ user, countryCode, tier, status, isTestAccount, campaign, groups: [{ id, name, membershipMode,
priority, addedAt, matchedPlatforms }], assignments: RateAssignment[], effective: [{ platform, format, level,
levelLabel, sourceLabel, amount, currency, assignmentId, rateCardId, rateCardVersion, validTo }], precedence }`.
Without `campaignId`: global rates only (card currency); with it: that campaign's scoped rates and policy, and the
campaign rate where no person-level rate applies.

### `GET /admin/users/{userId}/rates/explain?platform=&format=&campaignId=&at=` — `rates.view`
`RateExplanation`: `{ platform, format, countryCode, tier, followers, evaluatedAt, campaign, campaignPolicy,
maxMultiplier, winner, summary, candidates: [{ level, levelLabel, outcome, reason, assignmentId, rateCardId, cardName,
version, groupId, groupName, priority, lineId, lineConditions, amount, currency, validFrom, validTo }], conversion,
conversionError, quote: RewardQuote|null, precedence }` — every candidate with the rule that decided it.

### `POST /admin/users/{userId}/custom-rates` — `rates.manage` → **201** `RateAssignment`
Rates body (currency, lines, caps, `stackCampaignBonuses`) plus `{ campaignId, validFrom, validTo, name, reason }`:
creates a private custom card (version 1) and its assignment. 403 `rates.self_assignment`; other errors as for
assignments. Edit it with `POST /admin/rate-cards/{cardId}/versions`.

---

## Campaign view

### `GET /admin/campaigns/{campaignId}/rates` — `rates.view`
`{ campaignId, currency, personalRatesMode, personalRateMaxMultiplier, campaignAssignments, globalAssignments,
fxProblems: ["USD→QAR"], peopleWithPersonalRates }`.

### `POST /admin/campaigns/{campaignId}/rates/simulate` — `rates.view`
`{ "userId": "…", "platform": "Instagram", "format": null, "postedAt": null, "isFirstApprovedPost": false }` →
`RateExplanation` with `quote` computed from the campaign's current rule set and the person's real caps and budget.

### Changes to existing endpoints
* `POST /admin/campaigns/{id}/reward-rules` and campaign create: `rewardRules.personalRatesMode`,
  `rewardRules.personalRateMaxMultiplier` (0.01–100); returned on `RewardRuleSet`.
* `POST /admin/campaigns/{id}/publish`: 409 `rates.fx_missing` when a rate that can apply can't be converted.
* `POST /me/submissions`: optional `format`; 400 `submission.format_mismatch`; 409 `rates.fx_missing`.
  `GET /me/submissions/{id}`: `format`, `rateKind` (`Personal`|`Special`|null).
* `GET /campaigns`, `/campaigns/recommended`, `/campaigns/{slug}`: `yourRate` `{ currency, minAmount, maxAmount,
  kind: Personal|Special, validTo, entries: [{ platform, format, amount }] }` or null — the caller's own rate only.
* Quotes (`reward`, `rewardQuote`, previews): `rateSource` `{ level, levelLabel, label, campaignRateAmount,
  personalAmount, limited, ignoredReason, cardAmount, cardCurrency, exchangeRate, validTo, rateCardId, rateCardVersion,
  rateGroupId, rateAssignmentId }` (names and ids only with `rates.view`); lines have `fromPersonalRate`.
* `GET /finance/ledger` rows and `export.csv`: `rateSource`, `rateSourceLabel`, `rateCardId`, `rateCardVersion`,
  `rateGroupId`, `rateAssignmentId` (CSV: "Rate source", "Rate source detail", "Rate card ID", "Rate card version",
  "Rate group ID", "Rate assignment ID").
* Setting `rates.fourEyesIncreasePercent` (integer 0–1000, default 0) under `/admin/settings`.
