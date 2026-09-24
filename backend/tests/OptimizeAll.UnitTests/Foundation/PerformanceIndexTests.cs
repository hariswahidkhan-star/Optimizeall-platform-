using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.UnitTests.Foundation;

/// <summary>
/// Guards the indexes that list, dashboard and job queries depend on (docs/DATABASE.md § Index catalogue), on both
/// providers, plus two model-wide rules: no redundant index (a non-unique index that is a leading prefix of another index
/// on the same table only costs writes) and every foreign key is covered by an index (InnoDB needs one; joins and cascades
/// use it). Removing or reordering one of these indexes needs a matching change here and in docs/DATABASE.md.
/// </summary>
public sealed class PerformanceIndexTests
{
    public enum Provider { MySql, Sqlite }

    /// <summary>(table, columns in order, unique?) — each row names the query it serves.</summary>
    public static readonly (string Table, string[] Columns, bool Unique)[] Required =
    {
        // Admin audit log: filters by entity / actor / date, newest first; legal record, never pruned.
        ("audit_logs", new[] { "EntityType", "EntityId" }, false),
        ("audit_logs", new[] { "ActorUserId" }, false),
        ("audit_logs", new[] { "CreatedAt" }, false),
        // Notification bell: unread count + list newest first; per-type dedup checks; dispatch job.
        ("notifications", new[] { "UserId", "ReadAt", "CreatedAt" }, false),
        ("notifications", new[] { "UserId", "CreatedAt" }, false),
        ("notifications", new[] { "Type", "CreatedAt" }, false),
        ("notification_deliveries", new[] { "Status", "NextAttemptAt" }, false),
        // Job run log: last run per job, run log newest first, retention oldest first.
        ("job_runs", new[] { "JobName", "StartedAt" }, false),
        ("job_runs", new[] { "StartedAt" }, false),
        // Email: send job candidates and throttle, reports, subscriber lists and activity.
        ("email_campaign_recipients", new[] { "CampaignId", "SubscriberId" }, true),
        ("email_campaign_recipients", new[] { "CampaignId", "Status", "DueAt" }, false),
        ("email_campaign_recipients", new[] { "CampaignId", "SentAt" }, false),
        ("email_events", new[] { "CampaignId", "Type" }, false),
        ("email_events", new[] { "SubscriberId", "Type", "OccurredAt" }, false),
        ("email_events", new[] { "ClientAccountId", "Type", "OccurredAt" }, false),
        ("email_subscribers", new[] { "ScopeKey", "NormalizedEmail" }, true),
        ("email_subscribers", new[] { "ScopeKey", "Status" }, false),
        ("email_subscribers", new[] { "ScopeKey", "CreatedAt" }, false),
        // Review queue, participant lists, live checks, reviewer stats.
        ("submissions", new[] { "NormalizedPostUrl" }, true),
        ("submissions", new[] { "Status", "SubmittedAt" }, false),
        ("submissions", new[] { "UserId", "CampaignId" }, false),
        ("submissions", new[] { "LiveCheckStatus", "Status", "LiveCheckDueAt" }, false),
        ("submissions", new[] { "UserId", "SubmittedAt" }, false),
        ("submissions", new[] { "ClaimedByUserId", "ClaimExpiresAt" }, false),
        ("submissions", new[] { "SubmittedAt" }, false),
        ("submissions", new[] { "Status", "DecidedAt" }, false),
        ("submission_events", new[] { "SubmissionId", "CreatedAt" }, false),
        ("submission_events", new[] { "ActorUserId", "CreatedAt" }, false),
        // Ledger: idempotent writes, participant balances.
        ("earning_entries", new[] { "IdempotencyKey" }, true),
        ("earning_entries", new[] { "UserId", "Status" }, false),
        // Public forms and landing pages: rate limit, unique-view check, lead reports.
        ("form_submissions", new[] { "FormId", "SubmittedAt" }, false),
        ("form_submissions", new[] { "IpHash", "SubmittedAt" }, false),
        ("form_submissions", new[] { "ClientAccountId", "SubmittedAt", "LandingPageId" }, false),
        ("landing_page_views", new[] { "PageId", "VisitorHash", "ViewedAt" }, false),
        ("landing_page_views", new[] { "ClientAccountId", "ViewedAt", "PageId" }, false),
        ("tracking_clicks", new[] { "TrackingLinkId", "ClickedAt" }, false),
        ("tracking_clicks", new[] { "ClickedAt" }, false),
        // Delivery: time reports and the admin dashboard, client health.
        ("time_entries", new[] { "UserId", "Date" }, false),
        ("time_entries", new[] { "ProjectId", "Date" }, false),
        ("time_entries", new[] { "Date" }, false),
        ("thread_messages", new[] { "ClientAccountId", "CreatedAt" }, false),
        // Sessions.
        ("refresh_tokens", new[] { "TokenHash" }, true),
    };

    private static IModel Model(Provider provider)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (provider == Provider.MySql)
            builder.UseMySql("Server=localhost;Database=model;User=model;Password=model", new MySqlServerVersion(new Version(8, 0, 36)));
        else
            builder.UseSqlite("Data Source=:memory:");
        using var db = new AppDbContext(builder.Options, TimeProvider.System);
        return db.GetService<IDesignTimeModel>().Model;
    }

    private sealed record Ix(string Name, string[] Columns, bool Unique, bool Filtered);

    private static Dictionary<string, List<Ix>> Indexes(IModel model)
    {
        var result = new Dictionary<string, List<Ix>>();
        foreach (var entity in model.GetEntityTypes().Where(e => e.GetTableName() is not null))
        {
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var list = result.TryGetValue(table.Name, out var l) ? l : result[table.Name] = new List<Ix>();
            foreach (var index in entity.GetIndexes())
            {
                var name = index.GetDatabaseName(table) ?? index.Name ?? "?";
                if (list.Any(x => x.Name == name)) continue;
                list.Add(new Ix(name, index.Properties.Select(p => p.GetColumnName(table)!).ToArray(), index.IsUnique, index.GetFilter() is not null));
            }
        }
        return result;
    }

    [Theory]
    [InlineData(Provider.MySql)]
    [InlineData(Provider.Sqlite)]
    public void Performance_critical_indexes_exist(Provider provider)
    {
        var indexes = Indexes(Model(provider));
        var missing = Required
            .Where(r => !indexes.TryGetValue(r.Table, out var list) ||
                        !list.Any(i => i.Columns.SequenceEqual(r.Columns) && i.Unique == r.Unique))
            .Select(r => $"{r.Table}({string.Join(", ", r.Columns)}){(r.Unique ? " UNIQUE" : "")}")
            .ToList();
        Assert.True(missing.Count == 0, "Missing indexes: " + string.Join("; ", missing));
    }

    [Fact]
    public void No_index_is_a_redundant_prefix_of_another()
    {
        var redundant = new List<string>();
        foreach (var (table, list) in Indexes(Model(Provider.MySql)))
        foreach (var a in list.Where(i => !i.Unique && !i.Filtered))
        foreach (var b in list.Where(i => i != a && !i.Filtered))
        {
            var prefix = b.Columns.Length > a.Columns.Length && b.Columns.Take(a.Columns.Length).SequenceEqual(a.Columns);
            var duplicate = b.Columns.SequenceEqual(a.Columns) && (b.Unique || string.CompareOrdinal(b.Name, a.Name) < 0);
            if (prefix || duplicate) redundant.Add($"{table}.{a.Name} is covered by {b.Name}");
        }
        Assert.True(redundant.Count == 0, "Redundant indexes: " + string.Join("; ", redundant));
    }

    [Fact]
    public void Every_foreign_key_is_covered_by_an_index()
    {
        var model = Model(Provider.MySql);
        var indexes = Indexes(model);
        var uncovered = new List<string>();
        foreach (var entity in model.GetEntityTypes().Where(e => e.GetTableName() is not null))
        {
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var pk = entity.FindPrimaryKey()?.Properties.Select(p => p.GetColumnName(table)!).ToArray() ?? Array.Empty<string>();
            foreach (var fk in entity.GetForeignKeys())
            {
                var cols = fk.Properties.Select(p => p.GetColumnName(table)!).ToArray();
                var covered = pk.Take(cols.Length).SequenceEqual(cols) ||
                              indexes[table.Name].Any(i => i.Columns.Take(cols.Length).SequenceEqual(cols));
                if (!covered) uncovered.Add($"{table.Name}({string.Join(", ", cols)})");
            }
        }
        Assert.True(uncovered.Count == 0, "Foreign keys without an index: " + string.Join("; ", uncovered));
    }
}
