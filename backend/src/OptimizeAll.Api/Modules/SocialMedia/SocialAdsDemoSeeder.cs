using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Ads;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

/// <summary>
/// "Demo" profile, part M4b (Order 300): social media + paid ads demo data for the four canonical demo clients.
/// STAGING / DEMO DATA ONLY. Idempotent: clients and staff are looked up (by slug / email) and only created when missing;
/// the content part is skipped when the Nimbus Fitness Instagram profile already exists. Social metrics are labelled as a
/// platform export and ad metrics as a CSV import (demo files), never as API data; no post claims an API publication.
/// </summary>
public sealed class SocialAdsDemoSeeder(
    TimeProvider clock, IPasswordHasher<User> passwordHasher, IDatabaseDialect dialect, IServiceProvider services, ILogger<SocialAdsDemoSeeder> logger) : ISeeder
{
    public const string Password = DeliveryDemoData.Password;
    public const string MarkerHandle = "nimbusfitness";

    public string Profile => "Demo";
    public int Order => 300;

    /// <summary>The canonical demo clients (<see cref="DeliveryDemoData"/>), so the result does not depend on seeder order.</summary>
    internal static IReadOnlyList<DeliveryDemoData.DemoClient> Clients => DeliveryDemoData.Clients;

    private DateTime _now;
    private Random _rng = new(20260923);

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        _now = clock.GetUtcNow().UtcDateTime;
        _rng = new Random(20260923);
        var clients = await EnsureClientsAsync(db, ct);
        var social = await EnsureStaffAsync(db, DeliveryDemoData.Social, ct);
        var ads = await EnsureStaffAsync(db, DeliveryDemoData.Ads, ct);

        var nimbus = clients[DeliveryDemoData.Nimbus.Slug];
        if (await db.Set<BrandProfile>().AnyAsync(p => p.ClientAccountId == nimbus.Id && p.Handle == MarkerHandle, ct))
        {
            logger.LogInformation("Social/ads demo data already present; skipping");
            return;
        }

        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            await EnsureRatesAsync(db, ct);
            SeedSocial(db, clients, social.Id);
            SeedAds(db, clients, ads.Id, social.Id);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();

        // Budget alerts from the real evaluation (deduped by the job itself).
        using var scope = services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<AdsAlertJob>();
        var summary = await job.EvaluateAsync(notify: false, ct);
        logger.LogWarning("Social/ads demo data created ({Summary}). Demo/staging data only.", summary);
    }

    // ------------------------------------------------------------------ clients & staff

    private async Task<Dictionary<string, ClientAccount>> EnsureClientsAsync(AppDbContext db, CancellationToken ct)
    {
        var result = new Dictionary<string, ClientAccount>();
        foreach (var c in Clients)
        {
            var existing = await db.Set<ClientAccount>().FirstOrDefaultAsync(x => x.Slug == c.Slug, ct);
            if (existing is null)
            {
                existing = new ClientAccount
                {
                    Slug = c.Slug, Name = c.Name, Industry = c.Industry, CountryCode = c.CountryCode, Currency = c.Currency, TimeZone = c.TimeZone,
                    Status = c.Status, Website = c.Website, Summary = c.Summary, Notes = "Demo client (staging data only).",
                };
                db.Set<ClientAccount>().Add(existing);
                await db.SaveChangesAsync(ct);
            }
            result[c.Slug] = existing;
        }
        return result;
    }

    private async Task<User> EnsureStaffAsync(AppDbContext db, DeliveryDemoData.DemoStaff staff, CancellationToken ct)
    {
        var (email, name, role) = (staff.Email, staff.DisplayName, staff.Role);
        var normalized = Normalization.Email(email);
        var user = await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is not null)
        {
            if (!user.HasRole(role))
            {
                user.Roles.Add(new UserRole { UserId = user.Id, Role = role, GrantedAt = _now });
                await db.SaveChangesAsync(ct);
            }
            return user;
        }
        user = new User
        {
            Email = email, NormalizedEmail = normalized, DisplayName = name, CountryCode = "US", LanguageCode = "en", TimeZone = "UTC",
            EmailVerifiedAt = _now, ReferralCode = "M4B" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant(),
        };
        user.PasswordHash = passwordHasher.HashPassword(user, Password);
        user.Roles.Add(new UserRole { UserId = user.Id, Role = role, GrantedAt = _now });
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>Adds a demo FX rate for a pair only when finance has none in either direction (needed to convert AED/GBP/PKR).</summary>
    private async Task EnsureRatesAsync(AppDbContext db, CancellationToken ct)
    {
        var rates = new (string Base, string Quote, decimal Rate)[] { ("GBP", "USD", 1.2645m), ("AED", "USD", 0.27229m), ("PKR", "USD", 0.0035971m) };
        foreach (var r in rates)
        {
            if (await db.Set<ExchangeRate>().AnyAsync(x => (x.BaseCurrency == r.Base && x.QuoteCurrency == r.Quote) || (x.BaseCurrency == r.Quote && x.QuoteCurrency == r.Base), ct))
                continue;
            db.Set<ExchangeRate>().Add(new ExchangeRate
            {
                BaseCurrency = r.Base, QuoteCurrency = r.Quote, Rate = r.Rate, EffectiveAt = _now.Date.AddDays(-120), Source = "demo", CreatedAt = _now,
            });
        }
    }

    // ------------------------------------------------------------------ social

    private void SeedSocial(AppDbContext db, Dictionary<string, ClientAccount> clients, Guid staffId)
    {
        var nimbus = clients[DeliveryDemoData.Nimbus.Slug];
        var wanderly = clients[DeliveryDemoData.Wanderly.Slug];
        var aurora = clients[DeliveryDemoData.Aurora.Slug];
        var karachi = clients[DeliveryDemoData.KarachiEats.Slug];

        db.Set<SocialClientSettings>().AddRange(
            new SocialClientSettings { ClientAccountId = nimbus.Id, RequireClientApproval = true, UpdatedAt = _now },
            new SocialClientSettings { ClientAccountId = aurora.Id, RequireClientApproval = true, UpdatedAt = _now });

        BrandProfile P(ClientAccount c, SocialNetwork n, string handle, string name, string url) =>
            Add(db, new BrandProfile { ClientAccountId = c.Id, Network = n, Handle = handle, DisplayName = name, ProfileUrl = url });

        var nIg = P(nimbus, SocialNetwork.Instagram, MarkerHandle, "Nimbus Fitness", "https://www.instagram.com/nimbusfitness/");
        var nFb = P(nimbus, SocialNetwork.Facebook, "nimbusfitnessapp", "Nimbus Fitness", "https://www.facebook.com/nimbusfitnessapp");
        var nX = P(nimbus, SocialNetwork.X, "nimbusfit", "Nimbus Fitness", "https://x.com/nimbusfit");
        var nLi = P(nimbus, SocialNetwork.LinkedIn, "nimbus-fitness", "Nimbus Fitness", "https://www.linkedin.com/company/nimbus-fitness/");
        var wIg = P(wanderly, SocialNetwork.Instagram, "wanderlytravel", "Wanderly Travel", "https://www.instagram.com/wanderlytravel/");
        var wTt = P(wanderly, SocialNetwork.TikTok, "wanderly", "Wanderly", "https://www.tiktok.com/@wanderly");
        var wYt = P(wanderly, SocialNetwork.YouTube, "wanderlytravel", "Wanderly Travel", "https://www.youtube.com/@wanderlytravel");
        var aIg = P(aurora, SocialNetwork.Instagram, "auroraskincare", "Aurora Skincare", "https://www.instagram.com/auroraskincare/");
        var aTt = P(aurora, SocialNetwork.TikTok, "auroraskin", "Aurora Skincare", "https://www.tiktok.com/@auroraskin");
        var aPin = P(aurora, SocialNetwork.Pinterest, "auroraskincare", "Aurora Skincare", "https://www.pinterest.com/auroraskincare/");
        var kFb = P(karachi, SocialNetwork.Facebook, "karachieats", "Karachi Eats", "https://www.facebook.com/karachieats");
        var kIg = P(karachi, SocialNetwork.Instagram, "karachi.eats", "Karachi Eats", "https://www.instagram.com/karachi.eats/");
        var kGbp = P(karachi, SocialNetwork.GoogleBusiness, "karachi-eats-clifton", "Karachi Eats Clifton", "https://g.page/karachi-eats-clifton");

        foreach (var (profile, slots) in new[] { (nIg, new[] { "Tue 11:00", "Thu 10:00", "Sat 09:00" }), (nX, new[] { "Mon 09:00", "Wed 12:00", "Fri 09:00" }),
                     (wIg, new[] { "Wed 18:00", "Sun 10:00" }), (kIg, new[] { "Thu 19:00", "Fri 19:00", "Sat 13:00" }) })
            foreach (var slot in slots.Select(NetworkPresets.ParseTime))
                db.Set<SocialQueueSlot>().Add(new SocialQueueSlot { ClientAccountId = profile.ClientAccountId, ProfileId = profile.Id, DayOfWeek = slot!.Value.Day, MinuteOfDay = slot.Value.Minute });

        var summer = Add(db, new SocialCampaign { ClientAccountId = nimbus.Id, Name = "Autumn challenge", UtmCampaign = "autumn-challenge-2026", UtmMedium = "organic-social" });
        Add(db, new SocialCampaign { ClientAccountId = wanderly.Id, Name = "Winter escapes", UtmCampaign = "winter-escapes" });
        db.Set<SocialHashtagSet>().AddRange(
            new SocialHashtagSet { ClientAccountId = nimbus.Id, Name = "Core fitness", Hashtags = new() { "#NimbusFitness", "#HomeWorkout", "#FitnessApp", "#StrongerEveryDay" } },
            new SocialHashtagSet { ClientAccountId = wanderly.Id, Name = "Travel inspiration", Hashtags = new() { "#Wanderly", "#TravelTips", "#CityBreak" } },
            new SocialHashtagSet { ClientAccountId = aurora.Id, Name = "Skincare", Hashtags = new() { "#AuroraSkincare", "#Skincare", "#GlowRoutine" } },
            new SocialHashtagSet { ClientAccountId = karachi.Id, Name = "Food", Hashtags = new() { "#KarachiEats", "#KarachiFood", "#Biryani" } });
        db.Set<SocialCaptionSnippet>().AddRange(
            new SocialCaptionSnippet { ClientAccountId = nimbus.Id, Name = "Free trial CTA", Body = "Start your 14-day free trial — link in bio." },
            new SocialCaptionSnippet { ClientAccountId = karachi.Id, Name = "Order online", Body = "Order online for delivery across Karachi." });

        SocialMediaAsset Img(ClientAccount c, string title, string seed, int w, int h, string alt) => Add(db, new SocialMediaAsset
        {
            ClientAccountId = c.Id, Kind = MediaKind.Image, Title = title, ExternalUrl = $"https://picsum.photos/seed/{seed}/{w}/{h}", Width = w, Height = h,
            AltText = alt, IsPublic = true, CreatedByUserId = staffId, Tags = new() { "demo" },
        });
        var nImg1 = Img(nimbus, "Morning HIIT", "nimbus-hiit", 1080, 1350, "Woman doing a HIIT workout at home with the Nimbus app on a tablet");
        var nImg2 = Img(nimbus, "Challenge badge", "nimbus-badge", 1080, 1080, "Nimbus autumn challenge badge");
        var wImg = Img(wanderly, "Lisbon at dusk", "wanderly-lisbon", 1080, 1350, "Tram on a steep Lisbon street at dusk");
        var aImg = Img(aurora, "Serum flat lay", "aurora-serum", 1000, 1500, "Aurora vitamin C serum bottle on a marble surface");
        var kImg = Img(karachi, "Biryani platter", "karachi-biryani", 1080, 1080, "Plate of chicken biryani with raita");
        var wVideo = Add(db, new SocialMediaAsset
        {
            ClientAccountId = wanderly.Id, Kind = MediaKind.Video, Title = "48 hours in Porto", ExternalUrl = "https://videos.example.com/wanderly/porto-48h.mp4",
            Width = 1080, Height = 1920, DurationSeconds = 58, IsPublic = true, CreatedByUserId = staffId, Tags = new() { "demo", "reel" },
        });

        var day = _now.Date;
        SocialPost Post(ClientAccount c, string title, SocialPostStatus status, DateTime at, params SocialPostVariant[] variants)
        {
            var post = new SocialPost { ClientAccountId = c.Id, Title = title, Status = status, ScheduledAt = at, CreatedByUserId = staffId };
            foreach (var v in variants)
            {
                v.PostId = post.Id;
                v.ClientAccountId = c.Id;
                post.Variants.Add(v);
            }
            if (status >= SocialPostStatus.Approved) { post.ApprovedAt = at.AddDays(-2); post.ApprovedByUserId = staffId; }
            return Add(db, post);
        }
        SocialPostVariant V(BrandProfile p, string text, SocialMediaAsset? media = null, string? link = null, params string[] tags) => new()
        {
            ProfileId = p.Id, Network = p.Network, Text = text, MediaIds = media is null ? new() : new() { media.Id },
            AltTexts = media?.AltText is { } alt ? new() { alt } : new(), Link = link, Hashtags = tags.ToList(),
        };

        // Nimbus: every workflow state.
        Post(nimbus, "Autumn challenge teaser", SocialPostStatus.Draft, day.AddDays(6).AddHours(15),
            V(nIg, "Something big is coming this autumn 🍂 Are you ready for 30 days of stronger?", nImg2, null, "#NimbusFitness", "#AutumnChallenge"));
        var review = Post(nimbus, "5-minute core routine", SocialPostStatus.InternalReview, day.AddDays(4).AddHours(15),
            V(nIg, "No gym, no excuses: a 5-minute core routine you can do before coffee ☕", nImg1, null, "#HomeWorkout", "#CoreWorkout"),
            V(nFb, "No gym, no excuses: try this 5-minute core routine before your morning coffee.", nImg1, "https://nimbus.example.com/blog/core"));
        var clientApproval = Post(nimbus, "Autumn challenge launch", SocialPostStatus.ClientApproval, day.AddDays(3).AddHours(15),
            V(nIg, "The Nimbus Autumn Challenge starts Monday! 30 days, 15 minutes a day. Join free in the app.", nImg2, null, "#AutumnChallenge"),
            V(nX, "The Nimbus Autumn Challenge starts Monday: 30 days, 15 minutes a day. Join free →", null, "https://nimbus.example.com/challenge"),
            V(nLi, "We're launching the Nimbus Autumn Challenge: 30 days of short, science-backed workouts for busy teams.", null, "https://nimbus.example.com/challenge"));
        clientApproval.CampaignId = summer.Id;
        clientApproval.AutoAppendUtm = true;
        Post(nimbus, "Member story: Priya", SocialPostStatus.Approved, day.AddDays(2).AddHours(14),
            V(nIg, "Priya lost 8 kg and found a routine she loves. Her story in her words 💪", nImg1, null, "#StrongerEveryDay"));
        Post(nimbus, "Tip Tuesday: warm-ups", SocialPostStatus.Scheduled, day.AddDays(1).AddHours(15),
            V(nX, "Tip Tuesday: 3 warm-up moves that cut injury risk. Thread 🧵", null, null, "#FitnessTips"),
            V(nFb, "Tip Tuesday: three warm-up moves that cut injury risk — full guide on our blog.", null, "https://nimbus.example.com/blog/warm-ups"));
        var publishing = Post(nimbus, "Weekend reset", SocialPostStatus.Publishing, day.AddHours(-1),
            V(nFb, "Weekend reset: 20 minutes of mobility to undo a week at your desk.", nImg1));
        publishing.PublishClaimId = Guid.NewGuid();
        publishing.PublishClaimedAt = _now;
        publishing.Variants[0].PublishStatus = VariantPublishStatus.Publishing;
        publishing.Variants[0].Attempts = 1;
        var failed = Post(nimbus, "Hiring: product designer", SocialPostStatus.Failed, day.AddDays(-1).AddHours(16),
            V(nLi, "We're hiring a product designer to shape the future of home fitness. Remote-friendly.", null, "https://nimbus.example.com/careers"));
        failed.Variants[0].PublishStatus = VariantPublishStatus.Failed;
        failed.Variants[0].Attempts = 1;
        failed.Variants[0].FailureKind = PublishFailureKind.NotConfigured;
        failed.Variants[0].FailureReason = "LinkedIn publishing is not configured in this installation (no API adapter). Publish it on the network yourself, then use \"Mark as published\" with the live URL.";
        failed.FailureReason = "LinkedIn: " + failed.Variants[0].FailureReason;

        // Published history (marked as published manually with the live URL) + metrics.
        var published = new List<(SocialPost Post, SocialPostVariant Variant, BrandProfile Profile)>();
        var publishedProfiles = new[] { nIg, nFb, nX, wIg, wTt, aIg, aTt, kIg, kFb };
        for (var i = 0; i < 36; i++)
        {
            var profile = publishedProfiles[i % publishedProfiles.Length];
            var client = clients.Values.First(c => c.Id == profile.ClientAccountId);
            var at = day.AddDays(-2 - i * 2).AddHours(new[] { 9, 12, 15, 18, 20 }[i % 5]);
            var media = client.Id == nimbus.Id ? nImg1 : client.Id == wanderly.Id ? (profile.Network == SocialNetwork.TikTok ? wVideo : wImg)
                : client.Id == aurora.Id ? aImg : kImg;
            if (profile.Network is SocialNetwork.X or SocialNetwork.Facebook && i % 2 == 0) media = null!;
            var variant = V(profile, $"{client.Name} — story #{36 - i}: behind the scenes and tips from our team.", media);
            var post = Post(client, $"{client.Name} update #{36 - i}", SocialPostStatus.Published, at, variant);
            variant.PublishStatus = VariantPublishStatus.Published;
            variant.PublishedManually = true;
            variant.MarkedPublishedByUserId = staffId;
            variant.PublishedAt = at;
            variant.PublishedUrl = profile.Network switch
            {
                SocialNetwork.Instagram => $"https://www.instagram.com/p/demo{i:000}/",
                SocialNetwork.X => $"https://x.com/{profile.Handle}/status/19000000000000{i:000}",
                SocialNetwork.TikTok => $"https://www.tiktok.com/@{profile.Handle}/video/73000000000000{i:000}",
                _ => $"https://www.facebook.com/{profile.Handle}/posts/demo{i:000}",
            };
            variant.ExternalPostId = $"demo-{profile.Network.ToString().ToLowerInvariant()}-{i:000}";
            post.PublishedAt = at;
            published.Add((post, variant, profile));
            if (i % 6 == 0)
            {
                post.IsEvergreen = true;
                post.EvergreenIntervalDays = 45;
                post.EvergreenMaxRepeats = 2;
            }
        }

        // Comments on the approval posts.
        db.Set<SocialPostComment>().AddRange(
            new SocialPostComment { PostId = review.Id, ClientAccountId = nimbus.Id, AuthorName = DeliveryDemoData.Social.DisplayName, AuthorUserId = staffId, IsInternal = true, Kind = PostCommentKind.Submitted, Body = "Submitted for internal review.", CreatedAt = _now.AddHours(-5) },
            new SocialPostComment { PostId = clientApproval.Id, ClientAccountId = nimbus.Id, AuthorName = DeliveryDemoData.Social.DisplayName, AuthorUserId = staffId, Kind = PostCommentKind.Approved, Body = "Approved internally; sent to the client for approval.", CreatedAt = _now.AddHours(-3) });

        // Metrics: an export-style daily profile series (90 days) and per-post lifetime snapshots.
        var batch = Add(db, new SocialMetricImport
        {
            ClientAccountId = nimbus.Id, ProfileId = nIg.Id, Kind = "profile-daily", FileName = "demo-platform-export.csv", Source = MetricSource.PlatformExport,
            CreatedByUserId = staffId, CreatedAt = _now, Errors = new() { "Demo data: synthetic export generated by the Demo seed profile." },
        });
        foreach (var profile in publishedProfiles.Append(kGbp).Append(nLi).Append(wYt).Append(aPin))
        {
            var followers = 4000 + _rng.Next(0, 40000);
            for (var d = 90; d >= 1; d--)
            {
                followers += _rng.Next(-5, 40);
                var impressions = 800 + _rng.Next(0, 4000);
                db.Set<SocialProfileMetric>().Add(new SocialProfileMetric
                {
                    ClientAccountId = profile.ClientAccountId, ProfileId = profile.Id, Date = DateOnly.FromDateTime(day.AddDays(-d)), Followers = followers,
                    Impressions = impressions, Reach = (long)(impressions * 0.7), Engagements = (long)(impressions * (0.02 + _rng.NextDouble() * 0.05)),
                    Clicks = _rng.Next(5, 90), VideoViews = profile.Network is SocialNetwork.TikTok or SocialNetwork.YouTube ? _rng.Next(200, 3000) : 0,
                    Source = MetricSource.PlatformExport, ImportBatchId = batch.Id, UpdatedAt = _now,
                });
            }
        }
        foreach (var (post, variant, profile) in published)
        {
            var hourBoost = post.ScheduledAt!.Value.Hour is 12 or 18 ? 1.6 : 1.0;
            var impressions = (long)((1500 + _rng.Next(0, 9000)) * hourBoost);
            db.Set<SocialPostMetric>().Add(new SocialPostMetric
            {
                ClientAccountId = profile.ClientAccountId, ProfileId = profile.Id, Network = profile.Network, PostId = post.Id, VariantId = variant.Id,
                PostKey = variant.ExternalPostId!, Date = DateOnly.FromDateTime(day.AddDays(-1)), PublishedAt = variant.PublishedAt,
                Impressions = impressions, Reach = (long)(impressions * 0.75), Engagements = (long)(impressions * (0.03 + _rng.NextDouble() * 0.06) * hourBoost),
                Clicks = _rng.Next(10, 250), VideoViews = profile.Network == SocialNetwork.TikTok ? impressions / 2 : 0, Source = MetricSource.PlatformExport,
                ImportBatchId = batch.Id, UpdatedAt = _now,
            });
        }

        // Listening, inbox, competitors.
        var q1 = Add(db, new SocialListeningQuery { ClientAccountId = nimbus.Id, Kind = ListeningQueryKind.Keyword, Term = "nimbus app", Networks = new() { SocialNetwork.X, SocialNetwork.Instagram } });
        Add(db, new SocialListeningQuery { ClientAccountId = karachi.Id, Kind = ListeningQueryKind.Hashtag, Term = "#KarachiEats" });
        var mentions = new[]
        {
            (nimbus, SocialNetwork.X, "fitmom_jess", "Loving the new Nimbus app update, the workouts are amazing!", (Sentiment?)null),
            (nimbus, SocialNetwork.X, "runner_dan", "Nimbus app keeps crashing after the update. Really annoying.", null),
            (nimbus, SocialNetwork.Instagram, "gymrat.lee", "Is Nimbus worth it compared to other apps?", null),
            (karachi, SocialNetwork.Instagram, "foodie.khi", "Best biryani in Clifton, hands down 😍", null),
            (karachi, SocialNetwork.Facebook, "ahmed.r", "Delivery was late and the food was cold.", (Sentiment?)Sentiment.Negative),
        };
        var m = 0;
        foreach (var (client, network, author, text, manual) in mentions)
        {
            var estimate = SentimentScorer.Score(text);
            db.Set<SocialMention>().Add(new SocialMention
            {
                ClientAccountId = client.Id, QueryId = client.Id == nimbus.Id ? q1.Id : null, Network = network, AuthorHandle = author, Text = text,
                PostedAt = _now.AddHours(-6 * ++m), Source = IngestSource.Manual, DedupeKey = $"demo:{m}", CreatedByUserId = staffId,
                Sentiment = manual ?? estimate.Sentiment, SentimentSource = manual is null ? SentimentSource.Automatic : SentimentSource.Manual,
                SentimentScore = manual is null ? estimate.Score : null,
            });
        }
        var inbox1 = Add(db, new SocialInboxItem
        {
            ClientAccountId = nimbus.Id, ProfileId = nIg.Id, Network = SocialNetwork.Instagram, Kind = InboxItemKind.Comment, AuthorHandle = "newbie_fit",
            Text = "Does the free trial need a credit card?", ReceivedAt = _now.AddHours(-2), Source = IngestSource.Manual, DedupeKey = "demo:inbox:1",
            Status = InboxItemStatus.Assigned, AssignedToUserId = staffId, Sentiment = Sentiment.Neutral,
        });
        Add(db, new SocialInboxItem
        {
            ClientAccountId = karachi.Id, ProfileId = kFb.Id, Network = SocialNetwork.Facebook, Kind = InboxItemKind.DirectMessage, AuthorHandle = "sara.k",
            Text = "Do you cater for weddings of 200 guests?", ReceivedAt = _now.AddHours(-20), Source = IngestSource.Manual, DedupeKey = "demo:inbox:2",
            Status = InboxItemStatus.Replied, Sentiment = Sentiment.Neutral,
        });
        db.Set<SocialInboxReply>().Add(new SocialInboxReply { ItemId = inbox1.Id, ClientAccountId = nimbus.Id, Body = "No card needed for the 14-day trial!", ByUserId = staffId, CreatedAt = _now.AddHours(-1) });

        foreach (var (client, name, network, handle, followers) in new[]
                 {
                     (nimbus, "FitStream", SocialNetwork.Instagram, "fitstream", 182_000), (nimbus, "PulseGym App", SocialNetwork.Instagram, "pulsegym", 96_000),
                     (karachi, "Biryani House", SocialNetwork.Instagram, "biryanihouse.pk", 41_000),
                 })
        {
            var comp = Add(db, new SocialCompetitor { ClientAccountId = client.Id, Name = name, Network = network, Handle = handle });
            for (var w = 12; w >= 0; w--)
                db.Set<SocialCompetitorSnapshot>().Add(new SocialCompetitorSnapshot
                {
                    CompetitorId = comp.Id, ClientAccountId = client.Id, Date = DateOnly.FromDateTime(day.AddDays(-7 * w)), Followers = followers - w * _rng.Next(200, 900),
                    EngagementRate = Math.Round(0.015m + (decimal)_rng.NextDouble() * 0.03m, 4), PostsLast30Days = _rng.Next(8, 30), Source = MetricSource.Manual, UpdatedAt = _now,
                });
        }
    }

    // ------------------------------------------------------------------ ads

    private void SeedAds(AppDbContext db, Dictionary<string, ClientAccount> clients, Guid adsStaffId, Guid socialStaffId)
    {
        var nimbus = clients[DeliveryDemoData.Nimbus.Slug];
        var wanderly = clients[DeliveryDemoData.Wanderly.Slug];
        var aurora = clients[DeliveryDemoData.Aurora.Slug];
        var karachi = clients[DeliveryDemoData.KarachiEats.Slug];

        db.Set<AdsClientSettings>().Add(new AdsClientSettings
        {
            ClientAccountId = nimbus.Id, CampaignNamingTemplate = "{client}_{platform}_{objective}_{yyyymm}_{name}", UpdatedAt = _now,
        });

        var specs = new (ClientAccount Client, AdPlatform Platform, string External, string Currency, string Zone, (string Name, string Objective, decimal DailySpend, decimal Cvr, decimal Aov)[] Campaigns)[]
        {
            (nimbus, AdPlatform.GoogleAds, "123-456-7890", "USD", "America/New_York", new[]
            {
                ("nimbus-fitness_google_conversions_202609_brand-search", "conversions", 140m, 0.09m, 60m),
                ("nimbus-fitness_google_conversions_202609_generic-search", "conversions", 260m, 0.035m, 60m),
                ("nimbus-fitness_google_awareness_202609_youtube", "awareness", 90m, 0.004m, 60m),
            }),
            (nimbus, AdPlatform.MetaAds, "act_1029384756", "USD", "America/New_York", new[]
            {
                ("nimbus-fitness_meta_conversions_202609_trial-lookalike", "conversions", 180m, 0.03m, 60m),
                ("nimbus-fitness_meta_traffic_202609_retargeting", "traffic", 70m, 0.05m, 60m),
            }),
            (wanderly, AdPlatform.GoogleAds, "234-567-8901", "GBP", "Europe/London", new[]
            {
                ("Wanderly | Search | City breaks", "conversions", 210m, 0.025m, 420m),
                ("Wanderly | PMax | Winter sun", "conversions", 150m, 0.02m, 610m),
            }),
            (wanderly, AdPlatform.MetaAds, "act_2233445566", "USD", "Europe/London", new[] { ("Wanderly | Reels | Porto", "awareness", 60m, 0.006m, 450m) }),
            (aurora, AdPlatform.MetaAds, "act_3344556677", "AED", "Asia/Dubai", new[]
            {
                ("Aurora | Advantage+ Shopping", "sales", 900m, 0.04m, 240m),
                ("Aurora | Retargeting | Cart", "sales", 350m, 0.07m, 260m),
            }),
            (aurora, AdPlatform.TikTokAds, "7123456789012345678", "USD", "Asia/Dubai", new[] { ("Aurora | TikTok | Glow challenge (awareness)", "awareness", 120m, 0m, 0m) }),
            (karachi, AdPlatform.MetaAds, "act_4455667788", "PKR", "Asia/Karachi", new[]
            {
                ("KE | Delivery | Clifton", "conversions", 28000m, 0.06m, 4200m),
                ("KE | Catering | Leads", "leads", 12000m, 0.03m, 0m),
            }),
        };

        var today = DateOnly.FromDateTime(_now);
        var accounts = new List<AdAccount>();
        foreach (var spec in specs)
        {
            var account = Add(db, new AdAccount
            {
                ClientAccountId = spec.Client.Id, Platform = spec.Platform, ExternalAccountId = spec.External, Currency = spec.Currency, TimeZone = spec.Zone,
                Name = $"{spec.Client.Name} — {spec.Platform}", ManagerUserId = adsStaffId, Status = AdAccountStatus.NotConnected,
                LastSyncMessage = "Demo account: metrics come from a demo CSV import, not from the platform API.",
            });
            accounts.Add(account);
            var batch = Add(db, new AdImportBatch
            {
                ClientAccountId = spec.Client.Id, AdAccountId = account.Id, Template = spec.Platform == AdPlatform.GoogleAds ? "google-ads" : spec.Platform == AdPlatform.MetaAds ? "meta-ads" : "generic",
                FileName = "demo-90-days.csv", ContentSha256 = new string('0', 64), CreatedByUserId = adsStaffId, CreatedAt = _now,
                FromDate = today.AddDays(-90), ToDate = today.AddDays(-1), Errors = new() { "Demo data: synthetic export generated by the Demo seed profile." },
            });
            var ci = 0;
            foreach (var c in spec.Campaigns)
            {
                ci++;
                var campaign = Add(db, new AdCampaign
                {
                    ClientAccountId = spec.Client.Id, AdAccountId = account.Id, ExternalId = $"{spec.External.Replace("-", "").Replace("act_", "")}{ci:00}",
                    Name = c.Name, Objective = c.Objective, Status = AdEntityStatus.Active, BudgetType = BudgetType.Daily, BudgetAmount = Money.Round(c.DailySpend * 1.1m, spec.Currency),
                    Currency = spec.Currency, BidStrategy = c.Objective == "awareness" ? "Maximize reach" : "Maximize conversions",
                    StartDate = today.AddDays(-120), TargetingSummary = $"{spec.Client.CountryCode}; 18–54; interests matched to {spec.Client.Industry}",
                    TargetCpa = c.Cvr > 0 ? Money.Round(c.DailySpend / 6m, spec.Currency) : null, Source = AdEntitySource.Imported,
                    NamingCompliant = spec.Client.Id != nimbus.Id || c.Name.StartsWith("nimbus-fitness_", StringComparison.Ordinal),
                });
                var cpc = spec.Currency switch { "PKR" => 42m, "AED" => 3.1m, "GBP" => 0.95m, _ => 1.15m };
                for (var d = 90; d >= 1; d--)
                {
                    var date = today.AddDays(-d);
                    var weekday = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0.85m : 1.05m;
                    // The Nimbus generic search campaign accelerates this month (drives an over-pacing alert).
                    var ramp = spec.Client.Id == nimbus.Id && ci == 2 && date.Month == today.Month ? 1.6m : 1m;
                    var spend = Money.Round(c.DailySpend * weekday * ramp * (0.8m + (decimal)_rng.NextDouble() * 0.4m), spec.Currency);
                    var clicks = (long)Math.Max(1, spend / (cpc * (0.8m + (decimal)_rng.NextDouble() * 0.4m)));
                    var impressions = clicks * _rng.Next(35, 90);
                    var conversions = Math.Round(clicks * c.Cvr * (0.7m + (decimal)_rng.NextDouble() * 0.6m), 2);
                    db.Set<AdDailyMetric>().Add(new AdDailyMetric
                    {
                        ClientAccountId = spec.Client.Id, AdAccountId = account.Id, Platform = spec.Platform, Level = AdLevel.Campaign, EntityKey = campaign.ExternalId!,
                        EntityName = campaign.Name, CampaignId = campaign.Id, Date = date, Currency = spec.Currency, Spend = spend, Impressions = impressions, Clicks = clicks,
                        Conversions = conversions, ConversionValue = Money.Round(conversions * c.Aov, spec.Currency), Reach = (long)(impressions * 0.62m),
                        VideoViews = c.Objective == "awareness" ? impressions / 4 : null, Source = AdMetricSource.CsvImport, ImportBatchId = batch.Id, UpdatedAt = _now,
                    });
                    batch.RowsTotal++;
                    batch.RowsImported++;
                }
            }
        }

        // Monthly budgets (this month) with pacing thresholds and targets.
        var month = new DateOnly(today.AddDays(-1).Year, today.AddDays(-1).Month, 1);
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        db.Set<AdBudget>().AddRange(
            new AdBudget { ClientAccountId = nimbus.Id, Month = month, Platform = AdPlatform.GoogleAds, Amount = Money.Round(490m * daysInMonth, "USD"), Currency = "USD", TargetCpa = 55m, Notes = "Search + YouTube" },
            new AdBudget { ClientAccountId = nimbus.Id, Month = month, Platform = AdPlatform.MetaAds, Amount = Money.Round(250m * daysInMonth, "USD"), Currency = "USD", TargetRoas = 1.5m },
            new AdBudget { ClientAccountId = wanderly.Id, Month = month, Amount = Money.Round(430m * daysInMonth, "GBP"), Currency = "GBP", TargetRoas = 3m },
            new AdBudget { ClientAccountId = aurora.Id, Month = month, Platform = AdPlatform.MetaAds, Amount = Money.Round(1300m * daysInMonth, "AED"), Currency = "AED", TargetRoas = 4m },
            new AdBudget { ClientAccountId = karachi.Id, Month = month, Amount = Money.Round(60000m * daysInMonth, "PKR"), Currency = "PKR", Notes = "Includes catering leads" });

        // Media plans, creatives, experiments, UTM links.
        var plan = new MediaPlan { ClientAccountId = nimbus.Id, Name = $"Nimbus {month:MMMM yyyy}", Month = month, Currency = "USD", Status = MediaPlanStatus.Approved };
        plan.Lines.AddRange(new[]
        {
            new MediaPlanLine { PlanId = plan.Id, ClientAccountId = nimbus.Id, Platform = AdPlatform.GoogleAds, Channel = "Search", Objective = "Trials", PlannedBudget = 400m * daysInMonth, FlightStart = month, FlightEnd = month.AddDays(daysInMonth - 1), KpiName = "CPA", KpiTarget = 55m },
            new MediaPlanLine { PlanId = plan.Id, ClientAccountId = nimbus.Id, Platform = AdPlatform.MetaAds, Channel = "Reels + Feed", Objective = "Trials", PlannedBudget = 250m * daysInMonth, FlightStart = month, FlightEnd = month.AddDays(daysInMonth - 1), KpiName = "ROAS", KpiTarget = 1.5m },
        });
        Add(db, plan);
        var plan2 = new MediaPlan { ClientAccountId = aurora.Id, Name = $"Aurora {month:MMMM yyyy}", Month = month, Currency = "AED", Status = MediaPlanStatus.Draft };
        plan2.Lines.Add(new MediaPlanLine { PlanId = plan2.Id, ClientAccountId = aurora.Id, Platform = AdPlatform.MetaAds, Channel = "Advantage+", Objective = "Sales", PlannedBudget = 1250m * daysInMonth, FlightStart = month, FlightEnd = month.AddDays(daysInMonth - 1), KpiName = "ROAS", KpiTarget = 4m });
        Add(db, plan2);

        db.Set<AdCreative>().AddRange(
            new AdCreative
            {
                ClientAccountId = nimbus.Id, Name = "RSA — free trial", Platform = AdPlatform.GoogleAds, Format = AdCreativeFormat.ResponsiveSearch,
                Headlines = new() { "Nimbus Fitness App", "Try 14 Days Free", "Workouts in 15 Minutes", "No Gym Needed" }, Descriptions = new() { "Short, science-backed home workouts. Start your free trial today.", "Plans for every level. Cancel anytime." },
                FinalUrl = "https://nimbus.example.com/trial", Status = SocialPostStatus.Approved, ApprovedAt = _now.AddDays(-10), ApprovedByUserId = adsStaffId, CreatedByUserId = adsStaffId,
            },
            new AdCreative
            {
                ClientAccountId = aurora.Id, Name = "Vitamin C serum — carousel", Platform = AdPlatform.MetaAds, Format = AdCreativeFormat.Carousel,
                Headlines = new() { "Glow in 14 days" }, Descriptions = new() { "Free delivery in the UAE" }, PrimaryText = "Our best-selling vitamin C serum, now in a travel size.",
                CallToAction = "Shop now", FinalUrl = "https://aurora.example.com/serum", Status = SocialPostStatus.ClientApproval, CreatedByUserId = adsStaffId,
            },
            new AdCreative
            {
                ClientAccountId = karachi.Id, Name = "Catering lead form", Platform = AdPlatform.MetaAds, Format = AdCreativeFormat.SingleImage,
                Headlines = new() { "Weddings & events catering" }, PrimaryText = "Planning an event? Get a catering quote from Karachi Eats in minutes.",
                CallToAction = "Get quote", Status = SocialPostStatus.Draft, CreatedByUserId = adsStaffId,
            });

        var experiment = new AdExperiment
        {
            ClientAccountId = nimbus.Id, AdAccountId = accounts[0].Id, Platform = AdPlatform.GoogleAds, Name = "Headline: price vs benefit",
            Hypothesis = "Leading with the free trial increases conversion rate versus leading with workout length.", Metric = "ConversionRate",
            StartDate = today.AddDays(-40), EndDate = today.AddDays(-10), Status = AdExperimentStatus.Concluded, WinnerVariant = "B — Free trial",
            Result = "Variant B converted better; rolled out to all ad groups.",
        };
        experiment.Variants.AddRange(new[]
        {
            new AdExperimentVariant { ExperimentId = experiment.Id, Name = "A — 15-minute workouts", IsControl = true, Impressions = 48000, Clicks = 2100, Conversions = 84, Spend = 2415m },
            new AdExperimentVariant { ExperimentId = experiment.Id, Name = "B — Free trial", Impressions = 47500, Clicks = 2180, Conversions = 118, Spend = 2470m },
        });
        Add(db, experiment);

        db.Set<AdUtmLink>().Add(new AdUtmLink
        {
            ClientAccountId = nimbus.Id, BaseUrl = "https://nimbus.example.com/trial", Source = "google", Medium = "cpc", Campaign = "autumn-challenge-2026",
            TaggedUrl = UtmBuilder.Build("https://nimbus.example.com/trial", new UtmParameters("google", "cpc", "autumn-challenge-2026")), CreatedByUserId = adsStaffId, CreatedAt = _now,
        });
    }

    private static T Add<T>(AppDbContext db, T entity) where T : class
    {
        db.Set<T>().Add(entity);
        return entity;
    }
}
