using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using P = OptimizeAll.Domain.Common.SocialPlatform;

namespace OptimizeAll.Api.Modules.Seed;

/// <summary>A demo campaign plus the copy participants use when they post about it.</summary>
internal sealed class DemoCampaign
{
    public required string Key { get; init; }
    public required Campaign Campaign { get; init; }
    public required string[] Openers { get; init; }
    public required string Caption { get; init; }
    public List<RewardRuleSet> RuleSets { get; } = new();
    public DateTime? PausedAt { get; set; }

    /// <summary>Random submissions to generate (scripted ones come on top).</summary>
    public int Generated { get; init; }

    public Guid Id => Campaign.Id;
    public string Title => Campaign.Title;
    public string Currency => RuleSets.Count == 0 ? "USD" : RuleSets[0].Currency;

    /// <summary>Last moment a participant could submit: deadline, pause or "now".</summary>
    public DateTime SubmissionsUntil(DateTime now) =>
        new[] { Campaign.SubmissionDeadline, PausedAt ?? DateTime.MaxValue, now }.Min();
}

internal sealed partial class DemoRun
{
    private readonly Dictionary<string, DemoCampaign> _campaigns = new();
    private DemoCampaign C(string key) => _campaigns[key];

    private async Task CreateCampaignsAsync(CancellationToken ct)
    {
        var categories = await _db.Set<CampaignCategory>().AsNoTracking().ToDictionaryAsync(c => c.Slug, c => c.Id, ct);
        Guid? Category(string slug) => categories.TryGetValue(slug, out var id) ? id : null;

        // ---- Nimbus Fitness (Active, USD, two reward versions) -------------------------------------------------
        var nimbus = await AddCampaignAsync(new DemoCampaign
        {
            Key = "nimbus",
            Generated = 40,
            Openers = new[]
            {
                "Three weeks into the Nimbus 30-day plan and I finally enjoy morning workouts.",
                "My new training partner lives on my phone.",
                "Tried the Nimbus Fitness app for my home workouts this week.",
                "The guided HIIT sessions on Nimbus are no joke!",
                "Swapped my gym playlist for Nimbus audio coaching and I'm hooked.",
                "Rest day, but still checking my Nimbus streak.",
                "Short on time? Nimbus has 12-minute workouts that actually work.",
                "Week two with Nimbus: 5 workouts done, zero excuses.",
            },
            Caption = "Nimbus Fitness is live on iOS and Android — personalised workouts, audio coaching and a 30-day plan that fits a busy week. Get your first month free with my link.",
            Campaign = new Campaign
            {
                Slug = "nimbus-fitness-app-launch",
                Title = "Nimbus Fitness App launch",
                Summary = "Share the launch of Nimbus Fitness, the AI workout coach, with your fitness community.",
                Description = "Nimbus Fitness is launching its new app with personalised plans and audio coaching. Post about your first workouts with the app using our approved captions and visuals. Posts must include the paid-partnership disclosure.",
                CategoryId = Category("health-fitness"),
                Topics = new() { "fitness", "health", "apps" },
                Status = CampaignStatus.Active,
                StartsAt = Day(-56, 6), EndsAt = Day(20, 20), SubmissionDeadline = Day(23, 20),
                TimeZone = "Asia/Dubai",
                PostingInstructions = "1. Download Nimbus Fitness and complete at least one workout.\n2. Post one of the approved visuals or a photo of your own workout with the app visible.\n3. Use an approved caption (you may add a personal sentence), the hashtags below and the disclosure for your platform.\n4. Keep the post public for at least 30 days.\n5. Submit the public post link and a screenshot within 3 days of posting.",
                DefaultDisclosureText = "#ad Paid partnership with Nimbus Fitness",
                RequiredHashtags = "#NimbusFitness #TrainSmarter",
                RequiredMentions = "@nimbusfitness",
                BudgetAmount = 15_000m, BudgetCurrency = "USD",
                MaxSubmissionsPerParticipant = 8,
                MinPostLiveHours = 0,
                RequireScreenshot = true,
                Eligibility = new CampaignEligibility { MinFollowers = 500 },
                Platforms = Platforms(P.Instagram, P.TikTok, P.X, P.Facebook),
                LandingHeadline = "Get paid to share the Nimbus Fitness launch",
                LandingBody = "Join creators across the Gulf, South Asia and the UK sharing the Nimbus Fitness launch. Earn a fixed reward for every approved post.",
                HeroImageUrl = "https://placehold.co/1200x630/png?text=Nimbus+Fitness",
                TrackingDestinationUrl = "https://nimbusfitness.example/download?src=creators",
                UtmCampaign = "nimbus-launch",
            },
        }, createdDaysAgo: 60, publishedDaysAgo: 58, ct,
            assets: new[]
            {
                (CampaignAssetType.Image, "Launch visual (square)", "https://placehold.co/1080x1080/png?text=Nimbus+Fitness+Launch", (string?)null, (SocialPlatform?)null),
                (CampaignAssetType.Image, "Story visual (9:16)", "https://placehold.co/1080x1920/png?text=Nimbus+Story", null, P.Instagram),
                (CampaignAssetType.Caption, "Main caption", null, "Nimbus Fitness is live on iOS and Android — personalised workouts, audio coaching and a 30-day plan that fits a busy week. Get your first month free with my link. #NimbusFitness #TrainSmarter", null),
                (CampaignAssetType.Caption, "Short caption for X", null, "My new workout coach lives in my pocket 💪 Nimbus Fitness is out now. #NimbusFitness #TrainSmarter", P.X),
                (CampaignAssetType.Link, "App download link", "https://nimbusfitness.example/download", null, null),
            },
            disclosures: new[]
            {
                ((SocialPlatform?)P.Instagram, (string?)null, "Paid partnership with Nimbus Fitness #ad"),
                (P.TikTok, null, "#ad #sponsored — Nimbus Fitness"),
                (null, "AE", "#إعلان | Ad — paid partnership with Nimbus Fitness"),
                (null, "GB", "#ad — I'm paid by Nimbus Fitness for this post"),
            });

        AddRuleSet(nimbus, 1, "USD", nimbus.Campaign.CreatedAt, "Initial reward rules",
            daily: 20m, weekly: 45m, campaign: 90m,
            new RewardRule { Type = RewardRuleType.BaseRate, Amount = 6.00m },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 7.50m, Platform = P.TikTok, Label = "TikTok video rate" },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 6.50m, CountryCode = "GB", Label = "UK rate" },
            new RewardRule
            {
                Type = RewardRuleType.TimeLimitedBonus, Amount = 2.00m, Label = "Launch week bonus",
                ValidFrom = nimbus.Campaign.StartsAt, ValidTo = nimbus.Campaign.StartsAt.AddDays(7),
            },
            new RewardRule { Type = RewardRuleType.FirstPostBonus, Amount = 1.50m, Label = "First post bonus" },
            new RewardRule { Type = RewardRuleType.QualityBonus, Amount = 5.00m, ApprovalMode = BonusApprovalMode.ManualApproval, Label = "Standout post bonus" });
        AddRuleSet(nimbus, 2, "USD", Day(-21, 9), "Raised the base rate after strong week-one results; added a Pakistan rate, a Platinum rate and a weekend push bonus.",
            daily: 20m, weekly: 45m, campaign: 90m,
            new RewardRule { Type = RewardRuleType.BaseRate, Amount = 7.00m },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 8.50m, Platform = P.TikTok, Label = "TikTok video rate" },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 6.00m, CountryCode = "PK", Label = "Pakistan rate" },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 9.00m, Tier = ParticipantTier.Platinum, Label = "Platinum creator rate" },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 10.00m, Platform = P.TikTok, Tier = ParticipantTier.Platinum, Label = "Platinum TikTok rate" },
            new RewardRule
            {
                Type = RewardRuleType.TimeLimitedBonus, Amount = 1.50m, Label = "Weekend push bonus",
                ValidFrom = Day(-11, 0), ValidTo = Day(-8, 0),
            },
            new RewardRule { Type = RewardRuleType.FirstPostBonus, Amount = 1.50m, Label = "First post bonus" },
            new RewardRule { Type = RewardRuleType.QualityBonus, Amount = 5.00m, ApprovalMode = BonusApprovalMode.ManualApproval, Label = "Standout post bonus" });

        // ---- Desert Bloom Skincare (Active, AED, Gulf only, 72h live check) -----------------------------------
        var bloom = await AddCampaignAsync(new DemoCampaign
        {
            Key = "bloom",
            Generated = 18,
            Openers = new[]
            {
                "My skin has never felt this hydrated in the Dubai heat.",
                "Evening routine update: Desert Bloom oasis serum is now a staple.",
                "Unboxing the new Desert Bloom autumn collection!",
                "Finally a sunscreen that doesn't feel heavy — thank you Desert Bloom.",
                "Two weeks with Desert Bloom and the glow is real.",
                "Skincare that's made for our climate.",
            },
            Caption = "Desert Bloom's new Autumn Glow collection is made for Gulf weather — lightweight hydration, SPF 50 and no white cast. Use code GLOW15 for 15% off.",
            Campaign = new Campaign
            {
                Slug = "desert-bloom-autumn-glow",
                Title = "Desert Bloom Skincare — Autumn Glow",
                Summary = "Show your routine with Desert Bloom's Autumn Glow collection (UAE and Saudi Arabia).",
                Description = "Desert Bloom is a UAE skincare brand. Share your routine with the Autumn Glow collection. Posts must stay live for at least 72 hours before rewards become payable.",
                CategoryId = Category("fashion-beauty"),
                Topics = new() { "beauty", "skincare" },
                Status = CampaignStatus.Active,
                StartsAt = Day(-30, 5), EndsAt = Day(25, 20), SubmissionDeadline = Day(28, 20),
                TimeZone = "Asia/Dubai",
                PostingInstructions = "Show at least one Desert Bloom product in use. Use the approved caption or your own words plus the discount code, the hashtags and the disclosure. Posts must remain public for 72 hours; a reviewer checks before the reward is released.",
                DefaultDisclosureText = "#ad Paid partnership with Desert Bloom",
                RequiredHashtags = "#DesertBloom #AutumnGlow",
                RequiredMentions = "@desertbloom.skin",
                BudgetAmount = 25_000m, BudgetCurrency = "AED",
                MaxSubmissionsPerParticipant = 3,
                MinPostLiveHours = 72,
                RequireScreenshot = true,
                Eligibility = new CampaignEligibility { MinFollowers = 1_000, Countries = new() { "AE", "SA" } },
                Platforms = Platforms(P.Instagram, P.TikTok, P.YouTube),
                HeroImageUrl = "https://placehold.co/1200x630/png?text=Desert+Bloom",
            },
        }, createdDaysAgo: 34, publishedDaysAgo: 31, ct,
            assets: new[]
            {
                (CampaignAssetType.Image, "Product flat lay", "https://placehold.co/1080x1350/png?text=Desert+Bloom+Autumn+Glow", (string?)null, (SocialPlatform?)null),
                (CampaignAssetType.Caption, "English caption", null, "Desert Bloom's new Autumn Glow collection is made for Gulf weather — lightweight hydration, SPF 50 and no white cast. Use code GLOW15 for 15% off. #DesertBloom #AutumnGlow", null),
                (CampaignAssetType.Caption, "Arabic caption", null, "مجموعة Autumn Glow الجديدة من Desert Bloom مصممة لطقس الخليج — ترطيب خفيف وحماية SPF 50. استخدموا كود GLOW15 للحصول على خصم 15٪. #DesertBloom", null),
            },
            disclosures: new[]
            {
                ((SocialPlatform?)null, (string?)"AE", "#إعلان | Paid partnership with Desert Bloom"),
                (null, "SA", "#إعلان مدفوع | Desert Bloom"),
                (P.YouTube, null, "Includes paid promotion — Desert Bloom"),
            });
        AddRuleSet(bloom, 1, "AED", bloom.Campaign.CreatedAt, "Initial reward rules",
            daily: null, weekly: 120m, campaign: null,
            new RewardRule { Type = RewardRuleType.BaseRate, Amount = 25m },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 30m, Platform = P.TikTok, Label = "TikTok rate" },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 22m, CountryCode = "SA", Label = "Saudi Arabia rate" },
            new RewardRule { Type = RewardRuleType.FirstPostBonus, Amount = 5m, Label = "First post bonus" },
            new RewardRule { Type = RewardRuleType.QualityBonus, Amount = 20m, ApprovalMode = BonusApprovalMode.ManualApproval, Label = "Creative excellence bonus" });

        // ---- Karachi Eats (Active, PKR, Pakistan only, 48h live check, tracking) ------------------------------
        var eats = await AddCampaignAsync(new DemoCampaign
        {
            Key = "eats",
            Generated = 18,
            Openers = new[]
            {
                "Karachi Eats is back and the nihari stall alone is worth the trip!",
                "Weekend plans sorted: Karachi Eats food festival at Port Grand.",
                "Best bun kebab of my life at Karachi Eats.",
                "Took the whole family to Karachi Eats — 60 stalls, zero regrets.",
                "Dessert street at Karachi Eats deserves its own post.",
            },
            Caption = "Karachi Eats food festival is on at Port Grand — 60+ stalls, live music and family deals. Get early-bird passes with my link.",
            Campaign = new Campaign
            {
                Slug = "karachi-eats-food-festival",
                Title = "Karachi Eats food festival",
                Summary = "Promote the Karachi Eats food festival at Port Grand to your followers in Pakistan.",
                Description = "Karachi Eats brings 60+ food stalls, live music and family activities to Port Grand. Share your festival visit or the official poster. Posts must stay live for 48 hours.",
                CategoryId = Category("food-drink"),
                Topics = new() { "food", "events", "karachi" },
                Status = CampaignStatus.Active,
                StartsAt = Day(-35, 4), EndsAt = Day(10, 18), SubmissionDeadline = Day(12, 18),
                TimeZone = "Asia/Karachi",
                PostingInstructions = "Post a photo or video from the festival (or the official poster) with the approved caption, the hashtags and the disclosure. Include your tracking link in your bio or story link sticker. Keep the post live for 48 hours.",
                DefaultDisclosureText = "#ad Paid partnership with Karachi Eats",
                RequiredHashtags = "#KarachiEats #PortGrand",
                BudgetAmount = 2_500_000m, BudgetCurrency = "PKR",
                MaxSubmissionsPerParticipant = 6,
                MinPostLiveHours = 48,
                RequireScreenshot = true,
                Eligibility = new CampaignEligibility { MinFollowers = 500, Countries = new() { "PK" } },
                Platforms = Platforms(P.Instagram, P.Facebook, P.TikTok),
                HeroImageUrl = "https://placehold.co/1200x630/png?text=Karachi+Eats",
                TrackingDestinationUrl = "https://karachieats.example/passes?utm_source=partner&lang=en",
                UtmCampaign = "karachi-eats-2026",
            },
        }, createdDaysAgo: 38, publishedDaysAgo: 36, ct,
            assets: new[]
            {
                (CampaignAssetType.Image, "Festival poster", "https://placehold.co/1080x1350/png?text=Karachi+Eats", (string?)null, (SocialPlatform?)null),
                (CampaignAssetType.Caption, "English caption", null, "Karachi Eats food festival is on at Port Grand — 60+ stalls, live music and family deals. Get early-bird passes with my link. #KarachiEats #PortGrand", null),
                (CampaignAssetType.Caption, "Urdu caption", null, "کراچی ایٹس فوڈ فیسٹیول پورٹ گرینڈ پر — 60 سے زیادہ اسٹالز اور لائیو میوزک۔ #KarachiEats", null),
                (CampaignAssetType.Link, "Passes page", "https://karachieats.example/passes", null, null),
            },
            disclosures: new[] { ((SocialPlatform?)P.Facebook, (string?)"PK", "Paid partnership with Karachi Eats #ad") });
        AddRuleSet(eats, 1, "PKR", eats.Campaign.CreatedAt, "Initial reward rules",
            daily: 4_000m, weekly: null, campaign: null,
            new RewardRule { Type = RewardRuleType.BaseRate, Amount = 1_500m },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 1_800m, Platform = P.Instagram, Label = "Instagram rate" },
            new RewardRule { Type = RewardRuleType.FirstPostBonus, Amount = 300m, Label = "First post bonus" });

        // ---- LedgerLeaf (Ended, USD, tracking + completed experiment) -----------------------------------------
        var leaf = await AddCampaignAsync(new DemoCampaign
        {
            Key = "leaf",
            Generated = 34,
            Openers = new[]
            {
                "I finally know where my salary goes every month.",
                "Budgeting used to stress me out. LedgerLeaf made it boring (in a good way).",
                "Saved my first emergency fund milestone with LedgerLeaf.",
                "If you're starting your first job, set up a budget app now.",
                "My monthly spending review now takes ten minutes.",
                "Round-ups into savings are my favourite LedgerLeaf feature.",
            },
            Caption = "LedgerLeaf helps you budget in minutes: automatic categories, savings goals and bill reminders. Start free with my link.",
            Campaign = new Campaign
            {
                Slug = "ledgerleaf-save-smarter",
                Title = "LedgerLeaf budgeting app — Save smarter",
                Summary = "Share how LedgerLeaf helps you budget and save.",
                Description = "LedgerLeaf is a personal budgeting app. Share your experience setting up a budget or savings goal. Financial promotions must be balanced: no promises of returns.",
                CategoryId = Category("finance"),
                Topics = new() { "finance", "budgeting", "apps" },
                Status = CampaignStatus.Ended,
                StartsAt = Day(-75, 5), EndsAt = Day(-15, 20), SubmissionDeadline = Day(-12, 20),
                TimeZone = "UTC",
                PostingInstructions = "Share a genuine tip about budgeting with LedgerLeaf. Do not promise returns or financial outcomes. Include the disclosure and your tracking link.",
                DefaultDisclosureText = "#ad Sponsored by LedgerLeaf",
                RequiredHashtags = "#LedgerLeaf #SaveSmarter",
                BudgetAmount = 8_000m, BudgetCurrency = "USD",
                MaxSubmissionsPerParticipant = 5,
                MinPostLiveHours = 0,
                RequireScreenshot = true,
                Eligibility = new CampaignEligibility { MinFollowers = 300, Countries = new() { "US", "GB", "IN", "PK", "AE", "EG" } },
                Platforms = Platforms(P.X, P.LinkedIn, P.Facebook, P.Instagram),
                HeroImageUrl = "https://placehold.co/1200x630/png?text=LedgerLeaf",
                TrackingDestinationUrl = "https://ledgerleaf.example/signup",
                UtmCampaign = "ledgerleaf-save-smarter",
            },
        }, createdDaysAgo: 80, publishedDaysAgo: 78, ct,
            assets: new[]
            {
                (CampaignAssetType.Image, "App screenshot", "https://placehold.co/1080x1080/png?text=LedgerLeaf", (string?)null, (SocialPlatform?)null),
                (CampaignAssetType.Caption, "Main caption", null, "LedgerLeaf helps you budget in minutes: automatic categories, savings goals and bill reminders. Start free with my link. #LedgerLeaf #SaveSmarter", null),
                (CampaignAssetType.Caption, "LinkedIn caption", null, "Early-career tip: automate your budget before your first raise. I use LedgerLeaf for categories and savings goals. #LedgerLeaf #PersonalFinance", P.LinkedIn),
                (CampaignAssetType.Link, "Sign-up link", "https://ledgerleaf.example/signup", null, null),
            },
            disclosures: new[]
            {
                ((SocialPlatform?)P.LinkedIn, (string?)null, "Sponsored post — LedgerLeaf"),
                (null, "GB", "#ad — paid promotion for LedgerLeaf. Capital at risk does not apply: LedgerLeaf is a budgeting tool, not an investment."),
            });
        AddRuleSet(leaf, 1, "USD", leaf.Campaign.CreatedAt, "Initial reward rules",
            daily: 20m, weekly: null, campaign: 40m,
            new RewardRule { Type = RewardRuleType.BaseRate, Amount = 7.50m },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 10.00m, Platform = P.LinkedIn, Label = "LinkedIn rate" },
            new RewardRule { Type = RewardRuleType.FirstPostBonus, Amount = 2.00m, Label = "First post bonus" });

        // ---- Wanderly Travel (Paused, USD, interest targeting) ------------------------------------------------
        var wander = await AddCampaignAsync(new DemoCampaign
        {
            Key = "wander",
            Generated = 14,
            Openers = new[]
            {
                "Found this hidden beach through Wanderly and I can't stop thinking about it.",
                "Planning my next weekend escape with Wanderly's hidden gems list.",
                "A 200-year-old caravanserai turned boutique hotel? Only on Wanderly.",
                "Travel tip: book small, local stays — Wanderly makes it easy.",
            },
            Caption = "Wanderly curates hidden-gem stays and experiences off the usual tourist trail. Get 10% off your first booking with my link.",
            Campaign = new Campaign
            {
                Slug = "wanderly-hidden-gems",
                Title = "Wanderly Travel — Hidden Gems",
                Summary = "Share a hidden-gem destination and Wanderly's curated stays.",
                Description = "Wanderly is a travel marketplace for boutique stays. Share a place you discovered through Wanderly.",
                CategoryId = Category("travel"),
                Topics = new() { "travel", "hotels" },
                Status = CampaignStatus.Paused,
                StartsAt = Day(-40, 5), EndsAt = Day(15, 20), SubmissionDeadline = Day(18, 20),
                TimeZone = "Europe/London",
                PostingInstructions = "Share a photo or reel of a destination with the approved caption and disclosure. Tag @wanderly.travel.",
                DefaultDisclosureText = "#ad Paid partnership with Wanderly",
                RequiredHashtags = "#Wanderly #HiddenGems",
                RequiredMentions = "@wanderly.travel",
                BudgetAmount = 6_000m, BudgetCurrency = "USD",
                MaxSubmissionsPerParticipant = 4,
                RequireScreenshot = true,
                Eligibility = new CampaignEligibility { MinFollowers = 1_000, Interests = new() { "travel", "photography", "lifestyle" } },
                Platforms = Platforms(P.Instagram, P.YouTube, P.TikTok, P.Facebook),
                HeroImageUrl = "https://placehold.co/1200x630/png?text=Wanderly",
            },
        }, createdDaysAgo: 44, publishedDaysAgo: 42, ct,
            assets: new[]
            {
                (CampaignAssetType.Image, "Hidden gem visual", "https://placehold.co/1080x1350/png?text=Wanderly+Hidden+Gems", (string?)null, (SocialPlatform?)null),
                (CampaignAssetType.Caption, "Main caption", null, "Wanderly curates hidden-gem stays and experiences off the usual tourist trail. Get 10% off your first booking with my link. #Wanderly #HiddenGems", null),
            },
            disclosures: new[] { ((SocialPlatform?)P.YouTube, (string?)null, "Includes paid promotion — Wanderly") });
        AddRuleSet(wander, 1, "USD", wander.Campaign.CreatedAt, "Initial reward rules",
            daily: null, weekly: 30m, campaign: null,
            new RewardRule { Type = RewardRuleType.BaseRate, Amount = 5.50m },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 12.00m, Platform = P.YouTube, Label = "YouTube video rate" },
            new RewardRule { Type = RewardRuleType.FirstPostBonus, Amount = 1.00m, Label = "First post bonus" });
        wander.PausedAt = Day(-4, 11);

        // ---- Aurora Pro (Active, invite-only, Gold/Platinum, verified profiles) --------------------------------
        var aurora = await AddCampaignAsync(new DemoCampaign
        {
            Key = "aurora",
            Generated = 8,
            Openers = new[]
            {
                "Spent a week editing with the Aurora Pro on — the noise cancelling is unreal.",
                "Studio-level sound, 40-hour battery. Aurora Pro review time.",
                "My desk setup just got an upgrade: Aurora Pro headphones.",
            },
            Caption = "Aurora Pro: adaptive noise cancelling, 40-hour battery and studio-tuned sound. Creators circle members get early access.",
            Campaign = new Campaign
            {
                Slug = "aurora-pro-creators-circle",
                Title = "Aurora Pro headphones — creators circle",
                Summary = "Invite-only: review the Aurora Pro headphones (Gold and Platinum creators).",
                Description = "Aurora Audio invites established tech and lifestyle creators to review the Aurora Pro. Review units are shipped after acceptance. Honest opinions are welcome; disclosure is mandatory.",
                CategoryId = Category("technology"),
                Topics = new() { "tech", "audio", "review" },
                Status = CampaignStatus.Active,
                Visibility = CampaignVisibility.InviteOnly,
                StartsAt = Day(-20, 5), EndsAt = Day(30, 20), SubmissionDeadline = Day(33, 20),
                TimeZone = "UTC",
                PostingInstructions = "Publish an honest review (video preferred) featuring the Aurora Pro. Mention adaptive noise cancelling and battery life. Use the disclosure for your platform.",
                DefaultDisclosureText = "#ad Aurora Pro provided by Aurora Audio (paid partnership)",
                RequiredHashtags = "#AuroraPro",
                BudgetAmount = 12_000m, BudgetCurrency = "USD",
                MaxSubmissionsPerParticipant = 3,
                RequireScreenshot = true,
                Eligibility = new CampaignEligibility
                {
                    MinAccountAgeDays = 180, MinFollowers = 5_000, RequireVerifiedAccount = true,
                    Tiers = new() { ParticipantTier.Gold, ParticipantTier.Platinum },
                },
                Platforms = Platforms(P.YouTube, P.Instagram, P.LinkedIn),
                LandingHeadline = "You're invited: the Aurora Pro creators circle",
                LandingBody = "A small group of creators gets early access to the Aurora Pro and a premium reward for an honest review.",
                HeroImageUrl = "https://placehold.co/1200x630/png?text=Aurora+Pro",
            },
        }, createdDaysAgo: 24, publishedDaysAgo: 22, ct,
            assets: new[]
            {
                (CampaignAssetType.Image, "Product hero", "https://placehold.co/1080x1080/png?text=Aurora+Pro", (string?)null, (SocialPlatform?)null),
                (CampaignAssetType.Caption, "Review caption", null, "Aurora Pro: adaptive noise cancelling, 40-hour battery and studio-tuned sound. #AuroraPro #ad", null),
                (CampaignAssetType.Link, "Product page", "https://aurora-audio.example/pro", null, null),
            },
            disclosures: new[] { ((SocialPlatform?)P.YouTube, (string?)null, "Includes paid promotion. Aurora Pro provided by Aurora Audio.") });
        AddRuleSet(aurora, 1, "USD", aurora.Campaign.CreatedAt, "Initial reward rules",
            daily: null, weekly: null, campaign: 120m,
            new RewardRule { Type = RewardRuleType.BaseRate, Amount = 25m },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 40m, Platform = P.YouTube, Label = "YouTube review rate" },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 35m, Tier = ParticipantTier.Platinum, Label = "Platinum creator rate" },
            new RewardRule { Type = RewardRuleType.QualityBonus, Amount = 15m, ApprovalMode = BonusApprovalMode.ManualApproval, Label = "In-depth review bonus" });

        // ---- Orbit Arena (Scheduled) ----------------------------------------------------------------------------
        var orbit = await AddCampaignAsync(new DemoCampaign
        {
            Key = "orbit",
            Openers = Array.Empty<string>(),
            Caption = "Orbit Arena season 3 drops soon — new maps, ranked mode and cross-play. Watch the trailer!",
            Campaign = new Campaign
            {
                Slug = "orbit-arena-season-3-trailer",
                Title = "Orbit Arena season 3 trailer",
                Summary = "Share the Orbit Arena season 3 trailer with your gaming audience.",
                Description = "Orbit Arena's third season introduces ranked mode and cross-play. Share the official trailer on launch week.",
                CategoryId = Category("gaming"),
                Topics = new() { "gaming", "esports" },
                Status = CampaignStatus.Scheduled,
                StartsAt = Day(5, 12), EndsAt = Day(40, 20), SubmissionDeadline = Day(43, 20),
                TimeZone = "UTC",
                PostingInstructions = "Share the official trailer (YouTube) or a clip (TikTok/X) with the approved caption and disclosure.",
                DefaultDisclosureText = "#ad Sponsored by Orbit Arena",
                RequiredHashtags = "#OrbitArena #Season3",
                BudgetAmount = 10_000m, BudgetCurrency = "USD",
                MaxSubmissionsPerParticipant = 2,
                Eligibility = new CampaignEligibility { MinFollowers = 1_000, Interests = new() { "gaming" } },
                Platforms = Platforms(P.YouTube, P.TikTok, P.X),
                HeroImageUrl = "https://placehold.co/1200x630/png?text=Orbit+Arena+S3",
            },
        }, createdDaysAgo: 6, publishedDaysAgo: 2, ct,
            assets: new[]
            {
                (CampaignAssetType.Video, "Season 3 trailer", "https://www.youtube.com/watch?v=orbitS3demo", (string?)null, (SocialPlatform?)null),
                (CampaignAssetType.Caption, "Launch caption", null, "Orbit Arena season 3 drops soon — new maps, ranked mode and cross-play. Watch the trailer! #OrbitArena #Season3", null),
            },
            disclosures: Array.Empty<(SocialPlatform?, string?, string)>());
        AddRuleSet(orbit, 1, "USD", orbit.Campaign.CreatedAt, "Initial reward rules",
            daily: null, weekly: null, campaign: 30m,
            new RewardRule { Type = RewardRuleType.BaseRate, Amount = 6m },
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 7m, Platform = P.TikTok, Label = "TikTok rate" },
            new RewardRule
            {
                Type = RewardRuleType.TimeLimitedBonus, Amount = 3m, Label = "Launch day bonus",
                ValidFrom = Day(5, 12), ValidTo = Day(6, 12),
            });

        // ---- CodeSprout (Draft) -------------------------------------------------------------------------------
        var sprout = await AddCampaignAsync(new DemoCampaign
        {
            Key = "sprout",
            Openers = Array.Empty<string>(),
            Caption = "CodeSprout Kids coding week: free beginner lessons for ages 8–14.",
            Campaign = new Campaign
            {
                Slug = "codesprout-kids-coding-week",
                Title = "CodeSprout Kids coding week",
                Summary = "Draft: promote CodeSprout's free coding week for children.",
                Description = "Draft campaign — awaiting final creative from CodeSprout.",
                CategoryId = Category("education"),
                Topics = new() { "education", "kids", "coding" },
                Status = CampaignStatus.Draft,
                StartsAt = Day(20, 5), EndsAt = Day(50, 20), SubmissionDeadline = Day(53, 20),
                TimeZone = "Europe/London",
                PostingInstructions = "To be confirmed with the client.",
                DefaultDisclosureText = "#ad Sponsored by CodeSprout",
                BudgetCurrency = "USD",
                MaxSubmissionsPerParticipant = 1,
                Eligibility = new CampaignEligibility { Interests = new() { "education", "tech" } },
                Platforms = Platforms(P.Facebook, P.Instagram, P.LinkedIn),
            },
        }, createdDaysAgo: 3, publishedDaysAgo: null, ct,
            assets: new[]
            {
                (CampaignAssetType.Caption, "Draft caption", (string?)null, (string?)"CodeSprout Kids coding week: free beginner lessons for ages 8–14. #CodeSprout", (SocialPlatform?)null),
            },
            disclosures: Array.Empty<(SocialPlatform?, string?, string)>());
        AddRuleSet(sprout, 1, "USD", sprout.Campaign.CreatedAt, "Initial reward rules",
            daily: null, weekly: null, campaign: null,
            new RewardRule { Type = RewardRuleType.BaseRate, Amount = 4m });

        await SaveAsync(ct);

        // Status changes after publishing.
        _clock.Now = wander.PausedAt!.Value;
        _audit.As(Manager.Id, Role.CampaignManager).Record("campaign.paused", nameof(Campaign), wander.Id,
            new { Status = "Active" }, new { Status = "Paused" }, "Client asked to pause while partner hotels update their autumn availability.");
        _clock.Now = leaf.Campaign.SubmissionDeadline.AddMinutes(7);
        _audit.RecordSystem("campaign.ended", nameof(Campaign), leaf.Id, new { From = "Active", To = "Ended" }, "Campaign schedule");
        await SaveAsync(ct);
    }

    private static List<CampaignPlatform> Platforms(params SocialPlatform[] platforms) =>
        platforms.Select(p => new CampaignPlatform { Platform = p }).ToList();

    /// <summary>A UTC time <paramref name="days"/> from today at <paramref name="hour"/>:00.</summary>
    private DateTime Day(int days, int hour) => DateTime.SpecifyKind(_now.Date.AddDays(days).AddHours(hour), DateTimeKind.Utc);

    private async Task<DemoCampaign> AddCampaignAsync(DemoCampaign demo, int createdDaysAgo, int? publishedDaysAgo, CancellationToken ct,
        (CampaignAssetType Type, string Title, string? Url, string? Body, SocialPlatform? Platform)[] assets,
        (SocialPlatform? Platform, string? Country, string Text)[] disclosures)
    {
        var c = demo.Campaign;
        var created = Day(-createdDaysAgo, 10);
        c.Id = IdGenerator.NewId(created);
        if (await _db.Set<Campaign>().AnyAsync(x => x.Slug == c.Slug, ct)) c.Slug += "-demo";
        c.CreatedAt = created;
        c.CreatedByUserId = Manager.Id;
        c.PublishedAt = publishedDaysAgo is { } p ? Day(-p, 9) : null;
        foreach (var platform in c.Platforms) platform.CampaignId = c.Id;
        var order = 0;
        foreach (var a in assets)
        {
            c.Assets.Add(new CampaignAsset
            {
                CampaignId = c.Id, Type = a.Type, Title = a.Title, Url = a.Url, Body = a.Body, Platform = a.Platform,
                SortOrder = order += 10, CreatedAt = created,
            });
        }
        foreach (var d in disclosures)
            c.Disclosures.Add(new CampaignDisclosure { CampaignId = c.Id, Platform = d.Platform, CountryCode = d.Country, Text = d.Text });

        // The simulation keeps a detached copy of the campaign (with platforms and eligibility) for eligibility checks.
        _db.Set<Campaign>().Add(c);
        _campaigns[demo.Key] = demo;

        _clock.Now = created;
        _audit.As(Manager.Id, Role.CampaignManager).Record("campaign.created", nameof(Campaign), c.Id,
            after: new { c.Slug, c.Title, Status = "Draft", c.Visibility });
        if (c.PublishedAt is { } published)
        {
            _clock.Now = published;
            _audit.Record("campaign.published", nameof(Campaign), c.Id,
                before: new { Status = "Draft" },
                after: new { Status = (c.StartsAt > published ? CampaignStatus.Scheduled : CampaignStatus.Active).ToString(), PublishedAt = published });
        }
        Count("campaigns");
        return demo;
    }

    /// <summary>Adds an immutable reward rule version (validated by the reward engine) and its audit record.</summary>
    private void AddRuleSet(DemoCampaign demo, int version, string currency, DateTime effectiveFrom, string reason,
        decimal? daily, decimal? weekly, decimal? campaign, params RewardRule[] rules)
    {
        var set = new RewardRuleSet
        {
            Id = IdGenerator.NewId(effectiveFrom),
            CampaignId = demo.Id,
            Version = version,
            Currency = currency,
            DailyCapPerParticipant = daily,
            WeeklyCapPerParticipant = weekly,
            CampaignCapPerParticipant = campaign,
            EffectiveFrom = effectiveFrom,
            CreatedAt = effectiveFrom,
            CreatedByUserId = Manager.Id,
            ChangeReason = reason,
        };
        foreach (var rule in rules)
        {
            rule.RuleSetId = set.Id;
            set.Rules.Add(rule);
        }
        RewardEngine.EnsureValid(set);
        if (demo.Campaign.BudgetAmount.HasValue && demo.Campaign.BudgetCurrency != set.Currency)
            throw new InvalidOperationException($"Demo campaign {demo.Key}: budget and reward currency differ.");

        var previous = demo.RuleSets.LastOrDefault();
        _db.Set<RewardRuleSet>().Add(set);
        demo.RuleSets.Add(set);
        _clock.Now = effectiveFrom;
        _audit.As(Manager.Id, Role.CampaignManager).Record("campaign.reward_rules_changed", nameof(Campaign), demo.Id,
            before: previous is null ? null : RewardRuleSetFactory.Snapshot(previous),
            after: RewardRuleSetFactory.Snapshot(set), reason: reason);
        Count("reward rule versions");
    }
}
