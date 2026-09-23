using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Ledger;

namespace OptimizeAll.Infrastructure.Persistence;

/// <summary>
/// Single EF Core context for the platform. Entity mappings live in per-module
/// IEntityTypeConfiguration classes under Persistence/Configurations; access sets with <c>db.Set&lt;T&gt;()</c>.
/// </summary>
public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    private readonly TimeProvider _clock;

    public AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider clock) : base(options)
    {
        _clock = clock;
    }

    /// <summary>ASP.NET Data Protection key ring (shared across instances; encrypts payout destinations).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>True when this context runs on MySQL (Pomelo); false for SQLite.</summary>
    public bool IsMySql => DatabaseProviders.IsMySql(Database.ProviderName);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var mySql = IsMySql;
        if (mySql) modelBuilder.HasCharSet("utf8mb4");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.Entity<DataProtectionKey>(b =>
        {
            b.ToTable("data_protection_keys");
            b.Property(k => k.FriendlyName).HasMaxLength(200);
            b.Property(k => k.Xml).HasColumnType("text");
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IConcurrencyStamped).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IConcurrencyStamped.ConcurrencyStamp))
                    .IsConcurrencyToken();
            }
        }

        // Configurations are written with MySQL column types/collations/check SQL; other providers get a portable model.
        if (!mySql) PortableModel.Apply(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HavePrecision(6);
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>().HavePrecision(6);
        if (IsMySql) configurationBuilder.Properties<Guid>().HaveColumnType("char(36)");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyRules()
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case AuditLog when entry.State is EntityState.Modified or EntityState.Deleted:
                    throw new InvalidOperationException("Audit log entries are append-only.");

                case EarningEntry when entry.State == EntityState.Deleted:
                    throw new InvalidOperationException("Earning entries cannot be deleted; record a reversal instead.");

                case EarningEntry when entry.State == EntityState.Modified:
                    foreach (var name in EarningEntry.ImmutableProperties)
                    {
                        if (entry.Property(name).IsModified &&
                            !Equals(entry.Property(name).OriginalValue, entry.Property(name).CurrentValue))
                        {
                            throw new InvalidOperationException(
                                $"EarningEntry.{name} is immutable; record an adjustment or reversal instead.");
                        }
                    }
                    break;
            }

            if (entry.Entity is IAuditedEntity audited)
            {
                if (entry.State == EntityState.Added)
                {
                    if (audited.CreatedAt == default) audited.CreatedAt = now;
                    audited.UpdatedAt = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    audited.UpdatedAt = now;
                    entry.Property(nameof(IAuditedEntity.CreatedAt)).IsModified = false;
                }
            }

            if (entry.Entity is IConcurrencyStamped stamped && entry.State == EntityState.Modified)
            {
                stamped.ConcurrencyStamp = Guid.NewGuid();
            }
        }
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}

/// <summary>Helpers for mapping list-valued properties to JSON text columns (MySQL <c>json</c>, SQLite <c>TEXT</c>).</summary>
public static class JsonColumn
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static ValueConverter<List<T>, string> ListConverter<T>() => new(
        v => System.Text.Json.JsonSerializer.Serialize(v, Options),
        v => string.IsNullOrEmpty(v) ? new List<T>() : System.Text.Json.JsonSerializer.Deserialize<List<T>>(v, Options) ?? new List<T>());

    public static ValueComparer<List<T>> ListComparer<T>() => new(
        (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item == null ? 0 : item.GetHashCode())),
        v => v.ToList());
}
