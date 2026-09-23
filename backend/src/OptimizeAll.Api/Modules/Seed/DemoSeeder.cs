using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seed;

/// <summary>Well-known demo accounts (see docs/DEMO.md). STAGING / DEMO DATA ONLY — never seed this in production.</summary>
public static class DemoAccounts
{
    public const string Domain = "demo.optimizeall.app";

    /// <summary>Password of every demo account (staff and participants).</summary>
    public const string Password = "Demo#2026!pass";

    public static string Email(string local) => $"{local}@{Domain}";

    public static readonly string Admin = Email("admin");
    public static readonly string Reviewer1 = Email("reviewer1");
    public static readonly string Reviewer2 = Email("reviewer2");
    public static readonly string Manager = Email("manager");
    public static readonly string Finance1 = Email("finance1");
    public static readonly string Finance2 = Email("finance2");

    public static readonly string Sara = Email("sara.participant");
    public static readonly string NewParticipant = Email("new.participant");
    public static readonly string Unverified = Email("unverified");
    public static readonly string Suspended = Email("suspended.participant");
    public static readonly string OnHold = Email("hold.participant");
    public static readonly string Rapid = Email("bilal.ahmed");
}

/// <summary>
/// "Demo" seed profile: a realistic staging dataset covering every journey (participant, reviewer, campaign manager,
/// finance, admin). It replays about three months of activity in chronological order relative to "now" and goes through
/// the same rules the real services enforce (eligibility, URL normalization, captured reward rule versions, ledger
/// idempotency keys and conversion, payout batch invariants), so reconciliation balances for every batch.
/// Idempotent: does nothing when the <see cref="MarkerKey"/> setting or the Sara demo account already exists.
/// Everything is written in one transaction; screenshots written to storage are deleted again if it fails.
/// </summary>
public sealed class DemoSeeder(
    TimeProvider clock,
    IPasswordHasher<User> passwordHasher,
    IDataProtectionProvider dataProtection,
    IFileStorage storage,
    ILogger<DemoSeeder> logger) : ISeeder
{
    public const string MarkerKey = "demo.seeded";

    /// <summary>Fixed pseudo-random seed: the same "now" always produces the same dataset.</summary>
    public const int RandomSeed = 20260923;

    public string Profile => "Demo";
    public int Order => 100;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await IsSeededAsync(db, ct))
        {
            logger.LogInformation("Demo data already present; skipping the Demo seed");
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var run = new DemoRun(db, now, passwordHasher, dataProtection, storage, logger);
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct))
        {
            try
            {
                await run.ExecuteAsync(ct);
                db.Set<SystemSetting>().Add(new SystemSetting
                {
                    Key = MarkerKey,
                    ValueJson = JsonSerializer.Serialize(new { seededAt = now, profile = "Demo", version = 1 }),
                    Description = "Marker written by the Demo seed profile (staging/demo data only).",
                    UpdatedAt = now,
                });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                run.DeleteWrittenFiles();
                throw;
            }
        }
        db.ChangeTracker.Clear();
        logger.LogWarning("Demo seed created {Summary}. Demo/staging data only.", run.Summary());
    }

    public static async Task<bool> IsSeededAsync(AppDbContext db, CancellationToken ct = default)
    {
        var sara = Normalization.Email(DemoAccounts.Sara);
        return await db.Set<SystemSetting>().AnyAsync(s => s.Key == MarkerKey, ct) ||
               await db.Set<User>().AnyAsync(u => u.NormalizedEmail == sara, ct);
    }
}
