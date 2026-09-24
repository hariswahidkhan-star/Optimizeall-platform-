using System.Text.Json;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Settings;

namespace OptimizeAll.Api.Modules.Seed;

/// <summary>
/// Person-level pricing demo data (docs/DEMO.md "Person-level rates"): reusable rate cards, the Macro / Micro / Nano /
/// Standard influencer groups (members chosen by their largest follower count, as a manager would), an automatic
/// Platinum group, negotiated personal deals (Sara's expires in 5 days), a pending four-eyes raise and an archived
/// card. Created 30–40 days back, so the replayed submissions of the last month are priced with them and their
/// ledger lines carry the rate source.
/// </summary>
internal sealed partial class DemoRun
{
    public const decimal DemoFourEyesPercent = 50m;

    /// <summary>Campaign reward-rule policies for person-level rates (applied as each campaign's rule set is created).</summary>
    private static void ApplyRatePolicy(string campaignKey, RewardRuleSet set)
    {
        switch (campaignKey)
        {
            case "eats":
                set.PersonalRatesMode = PersonalRatesMode.CampaignRatesOnly; // festival pays everyone the same
                break;
            case "aurora":
                set.PersonalRateMaxMultiplier = 3m; // negotiated rates at most 3× the campaign rate
                break;
        }
    }

    private async Task CreateRatesAsync(CancellationToken ct)
    {
        var manager = Manager;
        var created = Day(-40, 10);
        _clock.Now = created;
        _audit.As(manager.Id, Role.CampaignManager);

        RateCard Card(string name, string description, string currency, DateTime at, params RateCardLine[] lines)
        {
            var card = new RateCard
            {
                Id = IdGenerator.NewId(at), Name = name, Description = description, Kind = RateCardKind.Standard,
                Status = RateCardStatus.Active, Currency = currency, CurrentVersion = 1, CreatedByUserId = manager.Id, CreatedAt = at, UpdatedAt = at,
            };
            AddVersion(card, 1, at, "Initial rates", RateCardVersionStatus.Approved, lines);
            _db.Set<RateCard>().Add(card);
            Count("rate cards");
            return card;
        }

        RateCardLine L(decimal amount, SocialPlatform? platform = null, ContentFormat? format = null, string? country = null, string? label = null) =>
            new() { Amount = amount, Platform = platform, Format = format, CountryCode = country, Label = label };

        var macro = Card("Macro creators 2026", "50k+ followers. Premium per-post fee; long YouTube videos pay the most.", "USD", created,
            L(15m), L(18m, SocialPlatform.Instagram), L(22m, SocialPlatform.Instagram, ContentFormat.ShortVideo, label: "Reel fee"),
            L(20m, SocialPlatform.TikTok), L(30m, SocialPlatform.YouTube, ContentFormat.LongVideo, label: "YouTube video fee"));
        var micro = Card("Micro creators 2026", "10k–50k followers.", "USD", created,
            L(10m), L(12m, SocialPlatform.Instagram), L(14m, SocialPlatform.Instagram, ContentFormat.ShortVideo, label: "Reel fee"),
            L(12m, SocialPlatform.TikTok));
        var nano = Card("Nano creators 2026", "2k–10k followers.", "USD", created, L(8m), L(9m, SocialPlatform.TikTok));
        var standard = Card("Standard participants", "Everyone else: the regular per-post fee.", "USD", created, L(7m));
        var platinum = Card("Platinum tier bonus rate", "Automatic for Platinum-tier participants.", "USD", created, L(11m), L(13m, SocialPlatform.YouTube));
        var summer = Card("Summer 2026 creators (retired)", "Replaced by the 2026 cards.", "USD", Day(-120, 10), L(6m));
        summer.Status = RateCardStatus.Archived;
        summer.ArchivedAt = Day(-41, 9);
        summer.ArchivedByUserId = manager.Id;
        summer.ArchiveReason = "Replaced by the 2026 creator cards.";

        // Macro v2 (effective 10 days ago) raised Instagram; submissions made before keep v1 prices.
        AddVersion(macro, 2, Day(-10, 9), "Instagram demand is up: raised the Instagram rates by 2 USD.", RateCardVersionStatus.Approved,
            L(15m), L(20m, SocialPlatform.Instagram), L(24m, SocialPlatform.Instagram, ContentFormat.ShortVideo, label: "Reel fee"),
            L(20m, SocialPlatform.TikTok), L(30m, SocialPlatform.YouTube, ContentFormat.LongVideo, label: "YouTube video fee"));
        macro.CurrentVersion = 2;
        // A raise above the four-eyes threshold, waiting for a second person.
        var pending = AddVersion(macro, 3, Day(-1, 15), "Proposed TikTok raise for Q4 (needs a second approval).", RateCardVersionStatus.PendingApproval,
            L(15m), L(20m, SocialPlatform.Instagram), L(24m, SocialPlatform.Instagram, ContentFormat.ShortVideo, label: "Reel fee"),
            L(32m, SocialPlatform.TikTok), L(30m, SocialPlatform.YouTube, ContentFormat.LongVideo, label: "YouTube video fee"));
        pending.MaxIncreasePercent = 60m;
        pending.EffectiveFrom = Day(-1, 15);

        _db.Set<SystemSetting>().Add(new SystemSetting
        {
            Key = SettingKeys.RatesFourEyesIncreasePercent, ValueJson = JsonSerializer.Serialize((int)DemoFourEyesPercent),
            UpdatedAt = created, UpdatedByUserId = Admin.Id,
        });

        // Groups, with members by largest follower count on any profile.
        RateGroup Group(string name, string description, int priority, RateGroupMembershipMode mode = RateGroupMembershipMode.Manual)
        {
            var g = new RateGroup
            {
                Id = IdGenerator.NewId(created), Name = name, Description = description, Priority = priority, MembershipMode = mode,
                CreatedByUserId = manager.Id, CreatedAt = created, UpdatedAt = created,
            };
            _db.Set<RateGroup>().Add(g);
            _audit.Record("rate_group.created", nameof(RateGroup), g.Id, after: new { g.Name, g.Priority, MembershipMode = mode.ToString() });
            Count("rate groups");
            return g;
        }

        var macroGroup = Group("Macro influencers", "50,000+ followers on their largest profile.", 40);
        var microGroup = Group("Micro influencers", "10,000–49,999 followers.", 30);
        var nanoGroup = Group("Nano influencers", "2,000–9,999 followers.", 20);
        var standardGroup = Group("Standard", "Under 2,000 followers.", 10);
        var platinumGroup = Group("Platinum tier (automatic)", "Everyone on the Platinum tier.", 50, RateGroupMembershipMode.Automatic);
        platinumGroup.AutoTiers = new List<ParticipantTier> { ParticipantTier.Platinum };
        platinumGroup.AutoRequireVerified = true;

        foreach (var person in _participants.Where(p => p.User.Status != UserStatus.Deactivated))
        {
            var followers = person.Accounts.Count == 0 ? 0 : person.Accounts.Max(a => a.FollowerCount);
            var group = followers >= 50_000 ? macroGroup : followers >= 10_000 ? microGroup : followers >= 2_000 ? nanoGroup : standardGroup;
            var at = Day(-35, 11);
            _db.Set<RateGroupMember>().Add(new RateGroupMember { GroupId = group.Id, UserId = person.Id, AddedAt = at, AddedByUserId = manager.Id, Note = "Initial segmentation" });
            _db.Set<RateGroupMemberEvent>().Add(new RateGroupMemberEvent
            {
                GroupId = group.Id, UserId = person.Id, Action = RateGroupMemberAction.Added, At = at, ActorUserId = manager.Id, Source = "csv",
                Reason = "Initial segmentation by follower count",
            });
            Count("rate group members");
        }
        _clock.Now = Day(-35, 11);
        _audit.Record("rate_group.members_added", nameof(RateGroup), macroGroup.Id, after: new { Source = "csv" }, reason: "Initial segmentation by follower count");

        var from = Day(-18, 0); // after Nimbus rules v2 (Day -21): v1-priced posts keep their campaign rate
        void Assign(RateCard card, RateAssignmentTarget target, Guid? user, RateGroup? group, Guid? campaignId, DateTime? validFrom, DateTime? validTo,
            string note, DateTime at, bool custom = false)
        {
            var a = new RateAssignment
            {
                Id = IdGenerator.NewId(at), RateCardId = card.Id, Target = target, UserId = user, GroupId = group?.Id, CampaignId = campaignId,
                IsCustom = custom, ValidFrom = validFrom, ValidTo = validTo, Note = note, CreatedByUserId = manager.Id, CreatedAt = at, UpdatedAt = at,
            };
            _db.Set<RateAssignment>().Add(a);
            _clock.Now = at;
            _audit.Record("rate_assignment.created", nameof(RateAssignment), a.Id,
                after: new { CardName = card.Name, Target = target.ToString(), user, GroupName = group?.Name, campaignId, validFrom, validTo }, reason: note);
            Count("rate assignments");
        }

        Assign(macro, RateAssignmentTarget.Group, null, macroGroup, null, from, null, "2026 macro creator rates", Day(-19, 9));
        Assign(micro, RateAssignmentTarget.Group, null, microGroup, null, from, null, "2026 micro creator rates", Day(-19, 9));
        Assign(nano, RateAssignmentTarget.Group, null, nanoGroup, null, from, null, "2026 nano creator rates", Day(-19, 9));
        Assign(standard, RateAssignmentTarget.Group, null, standardGroup, null, from, null, "Regular per-post fee", Day(-19, 9));
        Assign(platinum, RateAssignmentTarget.Group, null, platinumGroup, null, from, null, "Platinum tier perk", Day(-19, 9));

        // Personal deals.
        var sara = Sara;
        var saraDeal = new RateCard
        {
            Id = IdGenerator.NewId(Day(-20, 12)), Name = "Custom rate — Sara Khan", Kind = RateCardKind.Custom, Status = RateCardStatus.Active,
            OwnerUserId = sara.Id, Currency = "USD", CurrentVersion = 1, CreatedByUserId = manager.Id, CreatedAt = Day(-20, 12), UpdatedAt = Day(-20, 12),
        };
        AddVersion(saraDeal, 1, Day(-20, 12), "Negotiated ambassador deal for Q3.", RateCardVersionStatus.Approved,
            L(11m), L(16m, SocialPlatform.Instagram, ContentFormat.ShortVideo, label: "Ambassador reel fee"), L(13m, SocialPlatform.TikTok));
        _db.Set<RateCard>().Add(saraDeal);
        Assign(saraDeal, RateAssignmentTarget.Person, sara.Id, null, null, Day(-20, 12), _now.AddDays(5),
            "Ambassador deal (Q3) — expires in 5 days unless renewed.", Day(-20, 12), custom: true);

        var priyanka = Person("priyanka.menon");
        var priyankaDeal = new RateCard
        {
            Id = IdGenerator.NewId(Day(-15, 12)), Name = "Custom rate — Priyanka Menon (Aurora)", Kind = RateCardKind.Custom,
            Status = RateCardStatus.Active, OwnerUserId = priyanka.Id, Currency = "USD", CurrentVersion = 1, CreatedByUserId = manager.Id,
            CreatedAt = Day(-15, 12), UpdatedAt = Day(-15, 12),
        };
        AddVersion(priyankaDeal, 1, Day(-15, 12), "Flagship YouTube review deal for Aurora Pro.", RateCardVersionStatus.Approved,
            L(60m), L(90m, SocialPlatform.YouTube, label: "Flagship review fee"));
        _db.Set<RateCard>().Add(priyankaDeal);
        Assign(priyankaDeal, RateAssignmentTarget.Person, priyanka.Id, null, C("aurora").Id, Day(-15, 12), null,
            "Flagship review deal, Aurora Pro only.", Day(-15, 12), custom: true);

        var amelia = Person("amelia.clarke");
        Assign(macro, RateAssignmentTarget.Person, amelia.Id, null, C("nimbus").Id, Day(-12, 10), Day(20, 0),
            "Upgraded to macro rates for the Nimbus launch.", Day(-12, 10));

        _clock.Now = summer.ArchivedAt!.Value;
        _audit.Record("rate_card.archived", nameof(RateCard), summer.Id, new { Status = "Active" }, new { Status = "Archived" }, summer.ArchiveReason);
        await SaveAsync(ct);
    }

    private RateCardVersion AddVersion(RateCard card, int version, DateTime at, string reason, RateCardVersionStatus status, params RateCardLine[] lines)
    {
        var v = new RateCardVersion
        {
            Id = IdGenerator.NewId(at), RateCardId = card.Id, Version = version, Currency = card.Currency, EffectiveFrom = at, CreatedAt = at,
            CreatedByUserId = Manager.Id, ChangeReason = reason, Status = status,
        };
        foreach (var l in lines)
        {
            v.Lines.Add(new RateCardLine
            {
                VersionId = v.Id, Amount = l.Amount, Platform = l.Platform, Format = l.Format, CountryCode = l.CountryCode, Label = l.Label,
            });
        }
        RateCardRules.EnsureValid(v);
        card.Versions.Add(v);
        _clock.Now = at;
        _audit.As(Manager.Id, Role.CampaignManager).Record(
            status == RateCardVersionStatus.PendingApproval ? "rate_card.version_pending_approval" : version == 1 ? "rate_card.created" : "rate_card.version_created",
            nameof(RateCard), card.Id, after: new { card.Name, v.Version, v.Currency, Status = status.ToString(), Lines = lines.Length }, reason: reason);
        return v;
    }
}
