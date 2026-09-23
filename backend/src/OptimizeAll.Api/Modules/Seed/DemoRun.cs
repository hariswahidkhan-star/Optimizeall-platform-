using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Social;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seed;

/// <summary>A demo person (staff or participant) and the facts the simulation needs about them.</summary>
internal sealed class DemoPerson
{
    public required User User { get; init; }
    public Role Role { get; init; } = Role.Participant;
    public List<SocialAccount> Accounts { get; } = new();
    public bool HasPayoutProfile { get; set; }
    public PayoutMethod? PayoutMethod { get; set; }
    public DateTime? SuspendedAt { get; set; }

    /// <summary>Scripted people get hand-written histories and are excluded from the random submission generator.</summary>
    public bool Scripted { get; set; }

    public Guid Id => User.Id;
    public string Name => User.DisplayName;
}

/// <summary>
/// One execution of the demo seed. Replays platform activity through a time-ordered event queue: each event runs at
/// its simulated time (<see cref="DemoClock"/>), is saved, and the change tracker is cleared so later events always read
/// the current database state (like separate requests would).
/// </summary>
internal sealed partial class DemoRun
{
    private readonly AppDbContext _db;
    private readonly DateTime _now;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IDataProtector _destinationProtector;
    private readonly IFileStorage _storage;
    private readonly ILogger _logger;

    private readonly DemoClock _clock;
    private readonly DemoAuditLogger _audit;
    private readonly LedgerWriter _ledger;
    private readonly RewardQuoteService _quotes;
    private readonly SettingsService _settings;
    private readonly NotificationService _notifications;
    private readonly PayoutScheduleProvider _schedules;
    private readonly DemoRandom _rng = new(DemoSeeder.RandomSeed);

    private readonly PriorityQueue<Func<Task>, (DateTime At, long Seq)> _queue = new();
    private long _seq;
    private readonly List<string> _writtenKeys = new();
    private readonly Dictionary<string, int> _counts = new();

    // Timeline anchors (all relative to "now").
    private PayoutSchedule _schedule = null!;
    private PayoutPeriod _p0 = null!, _p1 = null!, _p2 = null!, _p3 = null!; // p0 = last completed period, p1 = one before, …
    private int _minAccountAgeDays;

    public DemoRun(AppDbContext db, DateTime now, IPasswordHasher<User> hasher, IDataProtectionProvider protection,
        IFileStorage storage, ILogger logger)
    {
        _db = db;
        _now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        _hasher = hasher;
        _destinationProtector = protection.CreateProtector(Accounts.PayoutDestinationProtection.Purpose);
        _storage = storage;
        _logger = logger;

        _clock = new DemoClock(_now.AddDays(-200));
        _audit = new DemoAuditLogger(db, _clock);
        _schedules = new PayoutScheduleProvider(db);
        // The production ledger writer (conversion, rounding, hold periods, idempotency), driven by the simulated clock.
        _ledger = new LedgerWriter(db, _schedules, new ExchangeRateProvider(db), _audit, _clock);
        _quotes = new RewardQuoteService(db);
        _settings = new SettingsService(db, _clock);
        _notifications = new NotificationService(db, _clock);
    }

    private DateTime Now => _clock.Now;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _schedule = await _schedules.GetActiveAsync(_now, ct);
        _p0 = PayoutPeriodCalculator.LastCompletedPeriod(_schedule, _now);
        _p1 = PayoutPeriodCalculator.Previous(_schedule, _p0);
        _p2 = PayoutPeriodCalculator.Previous(_schedule, _p1);
        _p3 = PayoutPeriodCalculator.Previous(_schedule, _p2);
        _minAccountAgeDays = await _settings.MinAccountAgeDaysAsync(ct);

        await CreateStaffAsync(ct);
        await CreateExchangeRatesAsync(ct);
        await CreateParticipantsAsync(ct);
        await CreateCampaignsAsync(ct);
        await CreateReferralsAsync(ct);
        await CreateExperimentsAsync(ct);

        PlanScriptedHistories();
        PlanGeneratedSubmissions();
        PlanReviewClaims();
        EnqueuePlans();
        PlanPayoutOperations();
        PlanLedgerOperations();

        _clock.Now = _now.AddDays(-200); // setup code moved the clock; the replay starts from the beginning
        await RunQueueAsync(ct);

        _clock.Now = _now;
        await CreateGrowthDataAsync(ct);
        await CreateContentAndSupportAsync(ct);
        await AwardAchievementsAsync(ct);
        await FinishNotificationsAsync(ct);
        await SaveAsync(ct);
    }

    // ------------------------------------------------------------------ event queue

    /// <summary>Schedules an action at a simulated time (clamped to the past: nothing happens after "now").</summary>
    private void At(DateTime at, Func<Task> action)
    {
        if (at > _now) at = _now.AddSeconds(-1);
        _queue.Enqueue(action, (at, _seq++));
    }

    private async Task RunQueueAsync(CancellationToken ct)
    {
        while (_queue.TryDequeue(out var action, out var key))
        {
            // Events never go back in time, even when scheduled "now" from an earlier event's handler.
            if (key.At > _clock.Now) _clock.Now = key.At;
            await action();
            await SaveAsync(ct);
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    // ------------------------------------------------------------------ helpers

    private void Count(string what, int n = 1) => _counts[what] = _counts.GetValueOrDefault(what) + n;

    public string Summary() => string.Join(", ", _counts.OrderBy(k => k.Key).Select(k => $"{k.Value} {k.Key}"));

    public void DeleteWrittenFiles()
    {
        foreach (var key in _writtenKeys)
        {
            try { _storage.Delete(key); } catch (IOException) { } catch (InvalidOperationException) { }
        }
    }

    private async Task<StoredFile> StoreScreenshotAsync(Guid ownerId, byte[] png, string fileName, CancellationToken ct = default)
    {
        var info = Domain.Files.ImageInspector.Inspect(png)
                   ?? throw new InvalidOperationException("Generated demo screenshot is not a valid image.");
        if (info.Width < FileService.MinDimension || info.Height < FileService.MinDimension)
            throw new InvalidOperationException("Generated demo screenshot is too small.");
        var key = _storage.NewKey(Now, info.Extension);
        await _storage.WriteAsync(key, png, ct);
        _writtenKeys.Add(key);
        var file = new StoredFile
        {
            OwnerUserId = ownerId,
            Purpose = FilePurpose.SubmissionScreenshot,
            StorageKey = key,
            ContentType = info.ContentType,
            SizeBytes = png.Length,
            Sha256 = Normalization.Sha256Hex(png),
            OriginalFileName = FileService.SanitizeFileName(fileName, info.Extension),
            Width = info.Width,
            Height = info.Height,
            CreatedAt = Now,
            IsPublic = false,
        };
        _db.Set<StoredFile>().Add(file);
        Count("screenshots");
        return file;
    }

    /// <summary>
    /// Writes a generated public creative (campaign hero/asset or homepage banner) through <see cref="IFileStorage"/>
    /// like an admin upload, so the web app shows it from <c>/api/v1/files/{id}</c> (allowed by its CSP img-src).
    /// </summary>
    private async Task<StoredFile> StorePublicImageAsync(Guid ownerId, FilePurpose purpose, int width, int height, string fileName,
        CancellationToken ct = default)
    {
        var png = DemoPng.Creative(width, height, _creativeVariant++);
        var info = Domain.Files.ImageInspector.Inspect(png)
                   ?? throw new InvalidOperationException("Generated demo image is not a valid image.");
        var key = _storage.NewKey(Now, info.Extension);
        await _storage.WriteAsync(key, png, ct);
        _writtenKeys.Add(key);
        var file = new StoredFile
        {
            OwnerUserId = ownerId,
            Purpose = purpose,
            StorageKey = key,
            ContentType = info.ContentType,
            SizeBytes = png.Length,
            Sha256 = Normalization.Sha256Hex(png),
            OriginalFileName = FileService.SanitizeFileName(fileName, info.Extension),
            Width = info.Width,
            Height = info.Height,
            CreatedAt = Now,
            IsPublic = true,
        };
        _db.Set<StoredFile>().Add(file);
        Count("public images");
        return file;
    }

    private int _creativeVariant;

    private Task NotifyAsync(Guid userId, string type, string title, string body, string? link) =>
        _notifications.StageAsync(new NotificationRequest(userId, type, title, body, link));

    private static string Money2(decimal amount, string currency) =>
        $"{Money.Round(amount, currency).ToString("N" + Money.MinorUnitDigits(currency), System.Globalization.CultureInfo.InvariantCulture)} {currency}";
}
