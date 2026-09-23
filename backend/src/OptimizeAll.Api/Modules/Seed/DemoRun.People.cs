using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Social;
using V = OptimizeAll.Domain.Social.SocialAccountVerificationStatus;
using P = OptimizeAll.Domain.Common.SocialPlatform;
using T = OptimizeAll.Domain.Common.ParticipantTier;

namespace OptimizeAll.Api.Modules.Seed;

internal sealed partial class DemoRun
{
    private const string ReferralAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly Dictionary<string, DemoPerson> _people = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DemoPerson> _participants = new();
    private readonly HashSet<string> _referralCodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _handles = new(StringComparer.Ordinal);

    private DemoPerson Admin => _people["admin"];
    private DemoPerson Reviewer1 => _people["reviewer1"];
    private DemoPerson Reviewer2 => _people["reviewer2"];
    private DemoPerson Manager => _people["manager"];
    private DemoPerson Finance1 => _people["finance1"];
    private DemoPerson Finance2 => _people["finance2"];
    private DemoPerson Sara => _people["sara.participant"];
    private DemoPerson Person(string local) => _people[local];

    private sealed record AccountSpec(SocialPlatform Platform, string Handle, int Followers, int AgeDays, V Status, string? Note = null);

    private sealed record ParticipantSpec(
        string Local, string Name, string Country, string Language, string TimeZone, T Tier, string[] Interests,
        int JoinedDaysAgo, AccountSpec[] Accounts, PayoutMethod? Payout, bool EmailVerified = true, int InactiveDays = 0);

    private static AccountSpec A(SocialPlatform p, string handle, int followers, int ageDays, V status = V.Verified, string? note = null) =>
        new(p, handle, followers, ageDays, status, note);

    private static readonly ParticipantSpec[] ParticipantSpecs =
    {
        // ---------------- Pakistan
        new("sara.participant", "Sara Khan", "PK", "en", "Asia/Karachi", T.Gold, new[] { "fitness", "tech", "travel", "food" }, 170,
            new[] { A(P.Instagram, "sara.creates", 18_400, 1_300), A(P.TikTok, "sarakhanvibes", 42_100, 760), A(P.X, "sarakhan_pk", 3_150, 2_200),
                    A(P.YouTube, "SaraKhanDaily", 9_800, 540, V.PendingReview) }, PayoutMethod.MobileWallet),
        new("hamza.qureshi", "Hamza Qureshi", "PK", "ur", "Asia/Karachi", T.Silver, new[] { "tech", "gaming", "finance" }, 150,
            new[] { A(P.X, "hamzacodes", 5_400, 2_600), A(P.LinkedIn, "hamza-qureshi-dev", 2_300, 3_000), A(P.Instagram, "hamza.q", 1_200, 1_500, V.Unverified) },
            PayoutMethod.BankTransfer),
        new("zainab.malik", "Zainab Malik", "PK", "en", "Asia/Karachi", T.Standard, new[] { "beauty", "fashion", "food" }, 45,
            new[] { A(P.Instagram, "zainabstyles", 8_700, 1_100), A(P.TikTok, "zainab.malik", 15_300, 600, V.PendingReview) }, PayoutMethod.MobileWallet),
        new("usman.tariq", "Usman Tariq", "PK", "ur", "Asia/Karachi", T.Standard, new[] { "gaming", "tech", "fitness" }, 25,
            new[] { A(P.TikTok, "usmanplays", 2_900, 400, V.Unverified), A(P.X, "usman_tariq", 640, 1_800, V.Unverified) }, PayoutMethod.MobileWallet),
        new("bilal.ahmed", "Bilal Ahmed", "PK", "en", "Asia/Karachi", T.Silver, new[] { "fitness", "food", "tech" }, 120,
            new[] { A(P.Instagram, "bilal.eats", 6_200, 900), A(P.TikTok, "bilalahmed", 11_800, 700), A(P.Facebook, "bilal.ahmed.pk", 2_100, 3_200) },
            PayoutMethod.BankTransfer),
        new("ayesha.siddiqui", "Ayesha Siddiqui", "PK", "ur", "Asia/Karachi", T.Standard, new[] { "food", "lifestyle", "beauty" }, 110,
            new[] { A(P.Instagram, "ayeshacooks", 4_300, 1_000), A(P.Facebook, "ayesha.siddiqui", 1_500, 2_500, V.Unverified) }, PayoutMethod.MobileWallet),
        new("fahad.iqbal", "Fahad Iqbal", "PK", "en", "Asia/Karachi", T.Standard, new[] { "finance", "tech" }, 95,
            new[] { A(P.X, "fahadiqbal", 1_900, 1_900), A(P.LinkedIn, "fahad-iqbal", 3_400, 2_800) }, PayoutMethod.BankTransfer),
        new("mariam.javed", "Mariam Javed", "PK", "en", "Asia/Karachi", T.Silver, new[] { "travel", "photography", "food" }, 130,
            new[] { A(P.Instagram, "mariamwanders", 12_600, 1_400), A(P.YouTube, "MariamJaved", 3_100, 800), A(P.TikTok, "mariamjaved", 5_200, 500, V.PendingReview) },
            PayoutMethod.MobileWallet),
        new("new.participant", "Hira Shah", "PK", "en", "Asia/Karachi", T.Standard, new[] { "beauty", "lifestyle" }, 3,
            new[] { A(P.Instagram, "hira.shah.glow", 1_450, 10, V.Unverified) }, null),

        // ---------------- United Arab Emirates
        new("hold.participant", "Aisha Rahman", "AE", "en", "Asia/Dubai", T.Silver, new[] { "beauty", "fitness" }, 140,
            new[] { A(P.Instagram, "aisharahman.ae", 21_000, 1_600), A(P.TikTok, "aisha.rahman", 9_400, 650) }, PayoutMethod.BankTransfer),
        new("layla.haddad", "Layla Haddad", "AE", "ar", "Asia/Dubai", T.Gold, new[] { "beauty", "fashion", "travel" }, 160,
            new[] { A(P.Instagram, "laylahaddad", 56_000, 2_400), A(P.YouTube, "LaylaHaddadBeauty", 23_000, 1_500), A(P.TikTok, "layla.haddad", 31_000, 800) },
            PayoutMethod.BankTransfer),
        new("omar.alsuwaidi", "Omar Al Suwaidi", "AE", "ar", "Asia/Dubai", T.Standard, new[] { "tech", "gaming", "fitness" }, 100,
            new[] { A(P.TikTok, "omar.suwaidi", 3_800, 500), A(P.X, "omar_alsuwaidi", 1_200, 2_000, V.Unverified) }, PayoutMethod.BankTransfer),
        new("priyanka.menon", "Priyanka Menon", "AE", "en", "Asia/Dubai", T.Platinum, new[] { "tech", "lifestyle", "travel" }, 175,
            new[] { A(P.Instagram, "priyankamenon", 88_000, 2_900), A(P.YouTube, "PriyankaMenonTech", 142_000, 2_100), A(P.LinkedIn, "priyanka-menon", 12_400, 3_300) },
            PayoutMethod.BankTransfer),
        new("rashid.alnuaimi", "Rashid Al Nuaimi", "AE", "ar", "Asia/Dubai", T.Standard, new[] { "finance", "travel" }, 70,
            new[] { A(P.Instagram, "rashid.nuaimi", 2_600, 1_300, V.PendingReview),
                    A(P.YouTube, "RashidTravels", 1_700, 900, V.Rejected, "Channel ownership could not be confirmed: the verification code was not found in the channel description.") },
            PayoutMethod.BankTransfer, InactiveDays: 41),
        new("noor.khalil", "Noor Khalil", "AE", "en", "Asia/Dubai", T.Silver, new[] { "food", "beauty", "lifestyle" }, 20,
            new[] { A(P.Instagram, "noorkhalil.eats", 7_100, 1_000), A(P.TikTok, "noor.khalil", 4_400, 450) }, PayoutMethod.PayPal),
        new("karim.nassar", "Karim Nassar", "AE", "ar", "Asia/Dubai", T.Standard, new[] { "fitness", "gaming" }, 85,
            new[] { A(P.TikTok, "karimnassar", 2_200, 380), A(P.Instagram, "karim.nassar", 980, 1_200) }, PayoutMethod.BankTransfer),

        // ---------------- Saudi Arabia
        new("faisal.alharbi", "Faisal Al Harbi", "SA", "ar", "Asia/Riyadh", T.Silver, new[] { "tech", "gaming" }, 125,
            new[] { A(P.X, "faisal_harbi", 7_300, 2_500), A(P.YouTube, "FaisalHarbiTech", 12_900, 1_300), A(P.TikTok, "faisal.harbi", 5_100, 600) },
            PayoutMethod.BankTransfer),
        new("reem.alqahtani", "Reem Al Qahtani", "SA", "ar", "Asia/Riyadh", T.Gold, new[] { "beauty", "fashion", "lifestyle" }, 150,
            new[] { A(P.Instagram, "reemqahtani", 34_000, 1_900), A(P.TikTok, "reem.qahtani", 27_000, 700) }, PayoutMethod.BankTransfer),
        new("abdullah.alotaibi", "Abdullah Al Otaibi", "SA", "ar", "Asia/Riyadh", T.Standard, new[] { "finance", "fitness" }, 60,
            new[] { A(P.Instagram, "abdullah.otaibi", 1_300, 1_500, V.Unverified), A(P.TikTok, "abdullahotaibi", 2_400, 420, V.PendingReview) },
            PayoutMethod.BankTransfer),
        new("lama.alshehri", "Lama Al Shehri", "SA", "ar", "Asia/Riyadh", T.Silver, new[] { "travel", "photography", "food" }, 18,
            new[] { A(P.Instagram, "lamashehri", 9_600, 1_700), A(P.YouTube, "LamaShehri", 4_100, 1_000) }, PayoutMethod.BankTransfer),
        new("yousef.alghamdi", "Yousef Al Ghamdi", "SA", "ar", "Asia/Riyadh", T.Standard, new[] { "gaming", "tech" }, 40,
            new[] { A(P.TikTok, "yousefghamdi", 1_900, 500), A(P.YouTube, "YousefPlays", 2_600, 900, V.Unverified) }, null, InactiveDays: 33),

        // ---------------- United Kingdom
        new("oliver.bennett", "Oliver Bennett", "GB", "en", "Europe/London", T.Silver, new[] { "finance", "tech", "fitness" }, 140,
            new[] { A(P.X, "oliverbennett", 4_200, 3_100), A(P.LinkedIn, "oliver-bennett", 6_800, 3_500), A(P.Instagram, "oliver.bennett", 1_900, 2_000) },
            PayoutMethod.BankTransfer),
        new("amelia.clarke", "Amelia Clarke", "GB", "en", "Europe/London", T.Gold, new[] { "travel", "lifestyle", "photography" }, 165,
            new[] { A(P.Instagram, "ameliaclarke.travels", 41_000, 2_700), A(P.YouTube, "AmeliaClarke", 18_000, 1_800), A(P.TikTok, "amelia.clarke", 12_000, 800) },
            PayoutMethod.BankTransfer),
        new("harry.patel", "Harry Patel", "GB", "en", "Europe/London", T.Standard, new[] { "food", "gaming" }, 28,
            new[] { A(P.Instagram, "harrypatel.food", 2_300, 1_100), A(P.Facebook, "harry.patel", 800, 2_600, V.Unverified) }, PayoutMethod.PayPal),
        new("chloe.evans", "Chloe Evans", "GB", "en", "Europe/London", T.Standard, new[] { "fitness", "beauty" }, 90,
            new[] { A(P.TikTok, "chloe.evans.fit", 8_800, 720), A(P.Instagram, "chloeevansfit", 5_600, 1_300) }, PayoutMethod.BankTransfer),
        new("suspended.participant", "Jack Morrison", "GB", "en", "Europe/London", T.Standard, new[] { "fitness", "gaming" }, 115,
            new[] { A(P.Instagram, "jackmorrison.fit", 3_900, 1_400), A(P.TikTok, "jack.morrison", 6_100, 600) }, PayoutMethod.PayPal),

        // ---------------- United States
        new("emily.carter", "Emily Carter", "US", "en", "America/New_York", T.Gold, new[] { "fitness", "lifestyle", "food" }, 175,
            new[] { A(P.Instagram, "emilycarterfit", 62_000, 2_600), A(P.TikTok, "emily.carter", 88_000, 900), A(P.YouTube, "EmilyCarterFit", 25_000, 1_700) },
            PayoutMethod.PayPal),
        new("marcus.johnson", "Marcus Johnson", "US", "en", "America/Chicago", T.Silver, new[] { "finance", "tech" }, 135,
            new[] { A(P.X, "marcusjfinance", 15_200, 3_000), A(P.LinkedIn, "marcus-johnson-cfa", 9_100, 3_400) }, PayoutMethod.PayPal),
        new("sofia.ramirez", "Sofia Ramirez", "US", "es", "America/Los_Angeles", T.Standard, new[] { "travel", "food", "photography" }, 105,
            new[] { A(P.Instagram, "sofiaramirez.eats", 7_400, 1_500), A(P.TikTok, "sofia.ramirez", 12_300, 650, V.PendingReview) }, PayoutMethod.PayPal),
        new("tyler.brooks", "Tyler Brooks", "US", "en", "America/New_York", T.Standard, new[] { "gaming", "tech" }, 22,
            new[] { A(P.TikTok, "tylerbrooksgg", 4_500, 520), A(P.YouTube, "TylerBrooksGaming", 3_300, 1_100), A(P.X, "tyler_brooks", 900, 1_900, V.Unverified) },
            PayoutMethod.PayPal),
        new("hannah.lee", "Hannah Lee", "US", "en", "America/Los_Angeles", T.Platinum, new[] { "tech", "lifestyle", "beauty" }, 180,
            new[] { A(P.Instagram, "hannahlee", 118_000, 3_200), A(P.YouTube, "HannahLeeTech", 76_000, 2_300), A(P.LinkedIn, "hannah-lee", 5_400, 3_000) },
            PayoutMethod.PayPal),
        new("unverified", "Jordan Mitchell", "US", "en", "America/Denver", T.Standard, new[] { "fitness" }, 2,
            new[] { A(P.Instagram, "jordanmitchell.fit", 700, 900, V.Unverified) }, null, EmailVerified: false),

        // ---------------- India
        new("arjun.sharma", "Arjun Sharma", "IN", "hi", "Asia/Kolkata", T.Silver, new[] { "tech", "finance", "gaming" }, 145,
            new[] { A(P.X, "arjunsharma_dev", 8_800, 2_700), A(P.LinkedIn, "arjun-sharma", 11_200, 3_100), A(P.YouTube, "ArjunSharmaTech", 19_000, 1_200) },
            PayoutMethod.BankTransfer),
        new("kavya.iyer", "Kavya Iyer", "IN", "en", "Asia/Kolkata", T.Standard, new[] { "beauty", "fashion" }, 100,
            new[] { A(P.Instagram, "kavyaiyer.style", 9_300, 1_200), A(P.Facebook, "kavya.iyer", 2_000, 2_800, V.Unverified) }, PayoutMethod.BankTransfer),
        new("rohan.gupta", "Rohan Gupta", "IN", "en", "Asia/Kolkata", T.Standard, new[] { "finance", "fitness" }, 80,
            new[] { A(P.X, "rohangupta", 2_700, 1_600), A(P.LinkedIn, "rohan-gupta", 3_900, 2_400) }, PayoutMethod.BankTransfer),
        new("ananya.reddy", "Ananya Reddy", "IN", "en", "Asia/Kolkata", T.Silver, new[] { "travel", "food", "photography" }, 26,
            new[] { A(P.Instagram, "ananyareddy.travel", 14_400, 1_800), A(P.YouTube, "AnanyaReddy", 6_200, 900, V.PendingReview) }, PayoutMethod.BankTransfer),
        new("vikram.singh", "Vikram Singh", "IN", "hi", "Asia/Kolkata", T.Standard, new[] { "gaming", "tech" }, 65,
            new[] { A(P.YouTube, "VikramPlays", 5_100, 1_100), A(P.X, "vikram_singh", 1_300, 1_400, V.Unverified) }, PayoutMethod.BankTransfer),

        // ---------------- Egypt
        new("mona.farouk", "Mona Farouk", "EG", "ar", "Africa/Cairo", T.Silver, new[] { "beauty", "lifestyle" }, 120,
            new[] { A(P.Instagram, "monafarouk", 16_000, 1_500), A(P.Facebook, "mona.farouk", 7_200, 3_100) }, PayoutMethod.MobileWallet),
        new("ahmed.samir", "Ahmed Samir", "EG", "ar", "Africa/Cairo", T.Standard, new[] { "tech", "gaming", "finance" }, 90,
            new[] { A(P.X, "ahmedsamir", 2_200, 2_300), A(P.Facebook, "ahmed.samir", 3_300, 3_300) }, PayoutMethod.MobileWallet),
        new("yasmin.adel", "Yasmin Adel", "EG", "en", "Africa/Cairo", T.Standard, new[] { "travel", "food" }, 55,
            new[] { A(P.Instagram, "yasminadel", 4_900, 1_000), A(P.TikTok, "yasmin.adel", 3_700, 480, V.Unverified) }, PayoutMethod.MobileWallet),
        new("karim.mostafa", "Karim Mostafa", "EG", "ar", "Africa/Cairo", T.Standard, new[] { "fitness", "food" }, 15,
            new[] { A(P.Instagram, "karim.mostafa", 1_600, 1_300, V.PendingReview), A(P.Facebook, "karim.mostafa.eg", 900, 2_600) }, null),
    };

    /// <summary>Participants with hand-written histories (not used by the random submission generator).</summary>
    private static readonly HashSet<string> ScriptedLocals = new(StringComparer.OrdinalIgnoreCase)
    {
        "sara.participant", "new.participant", "unverified", "suspended.participant", "hold.participant", "bilal.ahmed",
        "zainab.malik", "usman.tariq", "karim.mostafa", "hamza.qureshi",
    };

    // ------------------------------------------------------------------ staff

    private async Task CreateStaffAsync(CancellationToken ct)
    {
        (await _db.Set<User>().AsNoTracking().Select(u => u.ReferralCode).ToListAsync(ct)).ForEach(c => _referralCodes.Add(c));
        foreach (var (platform, handle) in await _db.Set<SocialAccount>().AsNoTracking().Select(a => new { a.Platform, a.NormalizedHandle })
                     .Select(x => new ValueTuple<SocialPlatform, string>(x.Platform, x.NormalizedHandle)).ToListAsync(ct))
            _handles.Add($"{platform}:{handle}");

        var staff = new (string Local, string Name, string Country, string Tz, Role Role, int Days)[]
        {
            ("admin", "Nadia Rahman", "AE", "Asia/Dubai", Role.Admin, 200),
            ("reviewer1", "Omar Siddiqui", "PK", "Asia/Karachi", Role.Reviewer, 190),
            ("reviewer2", "Priya Nair", "IN", "Asia/Kolkata", Role.Reviewer, 185),
            ("manager", "Daniel Brooks", "GB", "Europe/London", Role.CampaignManager, 195),
            ("finance1", "Fatima Al-Mansoori", "AE", "Asia/Dubai", Role.Finance, 190),
            ("finance2", "James Whitfield", "GB", "Europe/London", Role.Finance, 180),
        };
        foreach (var s in staff)
        {
            var created = _now.AddDays(-s.Days);
            var user = NewUser(DemoAccounts.Email(s.Local), s.Name, s.Country, "en", s.Tz, ParticipantTier.Standard,
                Array.Empty<string>(), created, emailVerified: true);
            user.LastLoginAt = _now.AddHours(-_rng.Next(2, 30));
            user.LastActiveAt = user.LastLoginAt;
            user.Roles.Add(new UserRole { UserId = user.Id, Role = s.Role, GrantedAt = created });
            _db.Set<User>().Add(user);
            _people[s.Local] = new DemoPerson { User = user, Role = s.Role, Scripted = true };
            Count("staff users");
        }
        _clock.Now = _now.AddDays(-180);
        _audit.As(Admin.Id, Role.Admin);
        foreach (var s in staff.Where(s => s.Local != "admin"))
            _audit.Record("admin.staff_created", nameof(User), _people[s.Local].Id,
                after: new { Email = DemoAccounts.Email(s.Local), Roles = new[] { s.Role.ToString() } });
        await SaveAsync(ct);
    }

    private User NewUser(string email, string name, string country, string language, string timeZone, ParticipantTier tier,
        IEnumerable<string> interests, DateTime createdAt, bool emailVerified)
    {
        var user = new User
        {
            Id = IdGenerator.NewId(createdAt),
            Email = email,
            NormalizedEmail = Normalization.Email(email),
            DisplayName = name,
            CountryCode = country,
            LanguageCode = language,
            TimeZone = timeZone,
            Interests = interests.ToList(),
            Tier = tier,
            EmailVerifiedAt = emailVerified ? createdAt.AddMinutes(_rng.Next(3, 90)) : null,
            ReferralCode = NewReferralCode(name),
            MarketingEmailOptIn = _rng.Chance(0.6),
            CreatedAt = createdAt,
            LastLoginAt = createdAt,
            LastActiveAt = createdAt,
        };
        user.PasswordHash = _hasher.HashPassword(user, DemoAccounts.Password);
        return user;
    }

    private string NewReferralCode(string name)
    {
        var prefix = new string(name.ToUpperInvariant().Where(c => c is >= 'A' and <= 'Z' && ReferralAlphabet.Contains(c)).Take(4).ToArray());
        for (var i = 0; ; i++)
        {
            var code = prefix + _rng.Chars(ReferralAlphabet, 8 - prefix.Length);
            if (_referralCodes.Add(code)) return code;
        }
    }

    // ------------------------------------------------------------------ exchange rates

    private async Task CreateExchangeRatesAsync(CancellationToken ct)
    {
        var rates = new (string Base, string Quote, decimal Rate, int DaysAgo)[]
        {
            ("AED", "USD", 0.27229000m, 120),
            ("PKR", "USD", 0.00358420m, 120),
            ("PKR", "USD", 0.00359710m, 30),
            ("SAR", "USD", 0.26660000m, 120),
            ("GBP", "USD", 1.26450000m, 120),
        };
        foreach (var r in rates)
        {
            var at = _now.Date.AddDays(-r.DaysAgo);
            if (await _db.Set<ExchangeRate>().AnyAsync(x => x.BaseCurrency == r.Base && x.QuoteCurrency == r.Quote && x.EffectiveAt == at, ct))
                continue;
            var rate = new ExchangeRate
            {
                BaseCurrency = r.Base, QuoteCurrency = r.Quote, Rate = r.Rate, EffectiveAt = at, Source = "demo",
                CreatedAt = at, CreatedByUserId = Finance1.Id,
            };
            _db.Set<ExchangeRate>().Add(rate);
            _clock.Now = at;
            _audit.As(Finance1.Id, Role.Finance).Record("fx.rate_created", nameof(ExchangeRate), rate.Id,
                after: new { rate.BaseCurrency, rate.QuoteCurrency, rate.Rate, rate.EffectiveAt, rate.Source });
            Count("exchange rates");
        }
        await SaveAsync(ct);
    }

    // ------------------------------------------------------------------ participants

    private async Task CreateParticipantsAsync(CancellationToken ct)
    {
        foreach (var spec in ParticipantSpecs)
        {
            var joined = _now.AddDays(-spec.JoinedDaysAgo).AddHours(-_rng.Next(1, 20));
            var user = NewUser(DemoAccounts.Email(spec.Local), spec.Name, spec.Country, spec.Language, spec.TimeZone, spec.Tier,
                spec.Interests, joined, spec.EmailVerified);
            user.Roles.Add(new UserRole { UserId = user.Id, Role = Role.Participant, GrantedAt = joined });
            user.WhatsAppOptIn = spec.Country is "PK" or "AE" or "SA" or "EG" && _rng.Chance(0.5);
            user.WhatsAppNumber = user.WhatsAppOptIn ? WalletNumber(spec.Country) : null;
            var lastActive = spec.InactiveDays > 0 ? _now.AddDays(-spec.InactiveDays) : _now.AddHours(-_rng.Next(1, 60));
            if (lastActive < joined) lastActive = joined.AddHours(1);
            user.LastActiveAt = lastActive;
            user.LastLoginAt = lastActive;
            _db.Set<User>().Add(user);

            var person = new DemoPerson { User = user, Scripted = ScriptedLocals.Contains(spec.Local) };
            _people[spec.Local] = person;
            _participants.Add(person);
            Count("participants");

            foreach (var a in spec.Accounts)
            {
                var added = joined.AddHours(_rng.Next(1, 48));
                if (added > _now) added = _now.AddMinutes(-30);
                var normalized = Normalization.Handle(a.Handle);
                if (!_handles.Add($"{a.Platform}:{normalized}"))
                {
                    _logger.LogWarning("Demo seed: {Platform} handle {Handle} already exists; skipping this demo profile", a.Platform, a.Handle);
                    continue;
                }
                var reviewer = _rng.Chance(0.5) ? Reviewer1 : Reviewer2;
                var decided = a.Status is V.Verified or V.Rejected;
                var account = new SocialAccount
                {
                    Id = IdGenerator.NewId(added),
                    UserId = user.Id,
                    Platform = a.Platform,
                    Handle = a.Handle,
                    NormalizedHandle = normalized,
                    ProfileUrl = ProfileUrl(a.Platform, a.Handle),
                    AccountCreatedAt = _now.AddDays(-a.AgeDays).Date.AddHours(_rng.Next(6, 22)),
                    FollowerCount = a.Followers,
                    PrimaryLanguage = spec.Language,
                    AudienceCountryCode = spec.Country,
                    VerificationStatus = a.Status,
                    VerifiedAt = decided ? added.AddHours(_rng.Next(4, 40)) : null,
                    VerifiedByUserId = decided ? reviewer.Id : null,
                    VerificationNote = a.Note ?? (a.Status == V.Verified ? "Ownership confirmed via bio code; follower count matches the profile." : null),
                    CreatedAt = added,
                };
                if (account.VerifiedAt > _now) account.VerifiedAt = _now.AddMinutes(-10);
                _db.Set<SocialAccount>().Add(account);
                person.Accounts.Add(account);
                Count("social accounts");
            }

            if (spec.Payout is { } method)
            {
                var profileAt = joined.AddHours(_rng.Next(2, 72));
                AddPayoutProfile(person, spec, method, profileAt > _now ? _now.AddMinutes(-20) : profileAt);
                person.HasPayoutProfile = true;
                person.PayoutMethod = method;
            }
        }
        await SaveAsync(ct);

        // Social verification audit trail for a handful of decisions (the rest are summarized by VerifiedBy/VerifiedAt).
        foreach (var account in _participants.SelectMany(p => p.Accounts).Where(a => a.VerifiedAt.HasValue).Take(12))
        {
            _clock.Now = account.VerifiedAt!.Value;
            _audit.As(account.VerifiedByUserId, Role.Reviewer).Record(
                account.VerificationStatus == V.Verified ? "social.account_verified" : "social.account_rejected",
                nameof(SocialAccount), account.Id,
                before: new { VerificationStatus = V.PendingReview.ToString() },
                after: new { VerificationStatus = account.VerificationStatus.ToString(), account.FollowerCount }, reason: account.VerificationNote);
        }
        await SaveAsync(ct);
    }

    private void AddPayoutProfile(DemoPerson person, ParticipantSpec spec, PayoutMethod method, DateTime at)
    {
        var raw = method switch
        {
            PayoutMethod.PayPal => $"{spec.Local.Replace('.', '_')}.payouts@example.com",
            PayoutMethod.MobileWallet => WalletNumber(spec.Country),
            _ => spec.Country switch
            {
                "PK" => DemoIban.Create("PK", _rng.Pick(new[] { "MEZN", "HABB", "UNIL", "SCBL" }) + _rng.Digits(16)),
                "AE" => DemoIban.Create("AE", _rng.Pick(new[] { "033", "026", "035" }) + _rng.Digits(16)),
                "SA" => DemoIban.Create("SA", _rng.Pick(new[] { "80", "10", "45" }) + _rng.Digits(18)),
                "GB" => DemoIban.Create("GB", _rng.Pick(new[] { "NWBK", "BARC", "LOYD" }) + _rng.Digits(6) + _rng.Digits(8)),
                _ => _rng.Digits(12), // Indian account number (not IBAN-shaped)
            },
        };
        // Same normalization, masking and encryption as ProfileService.UpdatePayoutProfileAsync.
        var (normalized, hint, error) = FieldRules.NormalizePayoutDestination(method, raw);
        if (error is not null) throw new InvalidOperationException($"Demo payout destination for {spec.Local} is invalid: {error}");
        var currency = spec.Country switch { "PK" => "PKR", "AE" => "AED", "SA" => "SAR", "GB" => "GBP", "EG" => "EGP", "IN" => "INR", _ => "USD" };
        var profile = new PayoutProfile
        {
            UserId = person.Id,
            Method = method,
            AccountHolderName = person.Name,
            EncryptedDestination = _destinationProtector.Protect(normalized!),
            MaskedDestination = hint!,
            PreferredCurrency = currency,
            CountryCode = spec.Country,
            CreatedAt = at,
        };
        _db.Set<PayoutProfile>().Add(profile);
        _clock.Now = at;
        _audit.As(person.Id, Role.Participant).Record("account.payout_profile_updated", nameof(PayoutProfile), profile.Id, null,
            new { profile.Method, destinationHint = profile.MaskedDestination, profile.PreferredCurrency, profile.CountryCode, accountHolderInitial = person.Name[..1] });
        Count("payout profiles");
    }

    private string WalletNumber(string country) => country switch
    {
        "PK" => "+923" + _rng.Digits(9),
        "EG" => "+201" + _rng.Digits(9),
        "AE" => "+9715" + _rng.Digits(8),
        "SA" => "+9665" + _rng.Digits(8),
        _ => "+1" + _rng.Digits(10),
    };

    private static string ProfileUrl(SocialPlatform platform, string handle) => platform switch
    {
        P.Instagram => $"https://www.instagram.com/{handle}/",
        P.TikTok => $"https://www.tiktok.com/@{handle}",
        P.X => $"https://x.com/{handle}",
        P.YouTube => $"https://www.youtube.com/@{handle}",
        P.LinkedIn => $"https://www.linkedin.com/in/{handle}/",
        P.Facebook => $"https://www.facebook.com/{handle}",
        _ => $"https://example.com/{handle}",
    };

    // ------------------------------------------------------------------ referrals

    private readonly Dictionary<Guid, Guid> _referralByReferred = new();

    /// <summary>
    /// Referrals are created as they would be at registration (fraud signals evaluated with the real rules). They are
    /// qualified later by the simulation when the referred participant's first submission is approved.
    /// </summary>
    private async Task CreateReferralsAsync(CancellationToken ct)
    {
        var deviceShared = _rng.Hash64();
        var links = new (string Referrer, string Referred, string Ip, string Device)[]
        {
            ("sara.participant", "zainab.malik", _rng.Hash64(), deviceShared),
            ("sara.participant", "usman.tariq", _rng.Hash64(), deviceShared), // same device as Zainab's registration → flagged
            ("sara.participant", "karim.mostafa", _rng.Hash64(), _rng.Hash64()),
            ("layla.haddad", "lama.alshehri", _rng.Hash64(), _rng.Hash64()),
            ("emily.carter", "tyler.brooks", _rng.Hash64(), _rng.Hash64()),
        };
        var prior = new Dictionary<Guid, List<PriorReferral>>();
        foreach (var link in links)
        {
            var referrer = Person(link.Referrer);
            var referred = Person(link.Referred);
            var at = referred.User.CreatedAt;
            var priors = prior.TryGetValue(referrer.Id, out var list) ? list : prior[referrer.Id] = new List<PriorReferral>();
            var signals = ReferralFraudRules.Evaluate(new ReferralFraudContext(referrer.User.Email, referred.User.Email, link.Ip, link.Device,
                priors, at));
            var referral = new Referral
            {
                ReferrerUserId = referrer.Id,
                ReferredUserId = referred.Id,
                CodeUsed = referrer.User.ReferralCode,
                Status = ReferralStatus.Registered,
                QualifyingAction = ReferralQualifyingAction.FirstApprovedSubmission,
                QualifyBy = at.AddDays(60),
                RegistrationIpHash = link.Ip,
                DeviceHash = link.Device,
                FraudSignals = ReferralFraudRules.Join(signals),
                CreatedAt = at,
            };
            priors.Add(new PriorReferral(link.Ip, link.Device, at));
            _db.Set<Referral>().Add(referral);
            _referralByReferred[referred.Id] = referral.Id;
            _clock.Now = at;
            _audit.RecordSystem("referral.created", nameof(Referral), referral.Id,
                after: new { referral.ReferrerUserId, referral.ReferredUserId, referral.FraudSignals });
            Count("referrals");
        }
        await SaveAsync(ct);
    }

    /// <summary>Same transition as ReferralService.QualifyAsync (program defaults: 5 USD, manual approval).</summary>
    private async Task QualifyReferralAsync(Guid referredUserId)
    {
        if (!_referralByReferred.TryGetValue(referredUserId, out var referralId)) return;
        var referral = await _db.Set<Referral>().FirstAsync(r => r.Id == referralId);
        if (referral.Status != ReferralStatus.Registered || Now > referral.QualifyBy) return;

        var program = await _settings.GetAsync(Domain.Settings.SettingKeys.ReferralProgram, new Domain.Settings.ReferralProgramSettings());
        referral.Status = ReferralStatus.Qualified;
        referral.QualifiedAt = Now;
        if (program.Enabled && program.ReferrerRewardAmount > 0)
        {
            var signals = ReferralFraudRules.Split(referral.FraudSignals);
            var entry = await _ledger.RecordAsync(new Common.Ledger.NewEarning(
                referral.ReferrerUserId, EarningType.ReferralReward, program.ReferrerRewardAmount, program.Currency,
                $"referral:{referral.Id}", "Referral reward",
                RequiresApproval: program.RequireManualApproval || signals.Count > 0, ReferralId: referral.Id));
            referral.EarningEntryId = entry.Id;
            Count("referral rewards");

            // Finance reviews clean referral rewards within a couple of days; flagged ones wait for a decision.
            if (signals.Count == 0 && entry.Status == EarningStatus.PendingApproval)
            {
                var entryId = entry.Id;
                At(Now.AddHours(_rng.Next(20, 50)), async () =>
                {
                    var e = await _db.Set<EarningEntry>().FirstAsync(x => x.Id == entryId);
                    if (e.Status != EarningStatus.PendingApproval) return;
                    _audit.As(Finance1.Id, Role.Finance);
                    await _ledger.ApproveAsync(e, Finance1.Id);
                });
            }
        }
        _audit.RecordSystem("referral.qualified", nameof(Referral), referral.Id,
            after: new { referral.QualifyingAction, referral.EarningEntryId, referral.RejectionReason });
        await NotifyAsync(referral.ReferrerUserId, NotificationTypes.ReferralQualified, "Your referral qualified",
            $"Someone you invited completed their qualifying step. A referral reward of {program.ReferrerRewardAmount:0.00} {program.Currency} is pending approval.",
            "/referrals");
    }
}
