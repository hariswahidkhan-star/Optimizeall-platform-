using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>"Migrate" (default; applies EF migrations), "EnsureCreated" (throwaway local databases only) or "None".</summary>
    public string InitializationMode { get; set; } = "Migrate";

    /// <summary>Seed profiles to apply after initialization, e.g. ["Baseline"] or ["Baseline","Demo"].</summary>
    public string[] Seed { get; set; } = Array.Empty<string>();

    /// <summary>How long startup waits for the database server to accept connections before failing.</summary>
    public int StartupWaitSeconds { get; set; } = 120;
}

public sealed class BootstrapOptions
{
    public const string Section = "Bootstrap";

    /// <summary>When set, ensures an administrator with this email exists (created verified). Password comes from secrets.</summary>
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminDisplayName { get; set; } = "Platform Admin";
}

/// <summary>A seed profile ("Baseline", "Demo"). Must be idempotent: running twice must not duplicate data.</summary>
public interface ISeeder
{
    string Profile { get; }
    int Order { get; }
    Task SeedAsync(AppDbContext db, CancellationToken ct);
}

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
        var db = sp.GetRequiredService<AppDbContext>();

        if (options.InitializationMode != "None")
            await WaitForDatabaseAsync(db, options.StartupWaitSeconds, logger, ct);

        switch (options.InitializationMode)
        {
            case "Migrate":
                logger.LogInformation("Applying database migrations");
                await db.Database.MigrateAsync(ct);
                break;
            case "EnsureCreated":
                await db.Database.EnsureCreatedAsync(ct);
                break;
            case "None":
                break;
            default:
                throw new InvalidOperationException($"Unknown Database:InitializationMode '{options.InitializationMode}'.");
        }

        await EnsureBootstrapAdminAsync(sp, db, logger, ct);

        var seeders = sp.GetServices<ISeeder>().OrderBy(s => s.Order).ToList();
        foreach (var profile in options.Seed)
        {
            foreach (var seeder in seeders.Where(s => string.Equals(s.Profile, profile, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogInformation("Running seeder {Seeder} ({Profile})", seeder.GetType().Name, profile);
                await seeder.SeedAsync(db, ct);
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// Waits for the database server to accept connections (containers and platforms often start the API before
    /// MySQL is ready), so startup doesn't fail on a transient "connection refused".
    /// </summary>
    private static async Task WaitForDatabaseAsync(AppDbContext db, int maxSeconds, ILogger logger, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(0, maxSeconds));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Connects to the server without requiring the schema to exist yet.
                await using var connection = new MySqlConnector.MySqlConnection(
                    new MySqlConnector.MySqlConnectionStringBuilder(db.Database.GetConnectionString() ?? string.Empty) { Database = string.Empty }.ConnectionString);
                await connection.OpenAsync(ct);
                return;
            }
            catch (MySqlConnector.MySqlException ex) when (DateTime.UtcNow < deadline)
            {
                logger.LogWarning("Database not reachable yet (attempt {Attempt}): {Message}. Retrying in 3s.", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    private static async Task EnsureBootstrapAdminAsync(IServiceProvider sp, AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var bootstrap = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BootstrapOptions>>().Value;
        if (string.IsNullOrWhiteSpace(bootstrap.AdminEmail) || string.IsNullOrWhiteSpace(bootstrap.AdminPassword)) return;

        var normalized = Normalization.Email(bootstrap.AdminEmail);
        if (await db.Set<User>().AnyAsync(u => u.NormalizedEmail == normalized, ct)) return;

        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var admin = new User
        {
            Email = bootstrap.AdminEmail.Trim(),
            NormalizedEmail = normalized,
            DisplayName = bootstrap.AdminDisplayName,
            CountryCode = "US",
            EmailVerifiedAt = now,
            ReferralCode = "ADM" + Random.Shared.Next(10000, 99999),
        };
        admin.PasswordHash = sp.GetRequiredService<IPasswordHasher<User>>().HashPassword(admin, bootstrap.AdminPassword);
        admin.Roles.Add(new UserRole { UserId = admin.Id, Role = Role.Admin, GrantedAt = now });
        db.Set<User>().Add(admin);
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Bootstrap administrator {Email} created", admin.Email);
    }
}
