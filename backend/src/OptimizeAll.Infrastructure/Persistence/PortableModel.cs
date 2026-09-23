using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OptimizeAll.Infrastructure.Persistence;

/// <summary>EF provider names used to pick provider-specific model details and dialects.</summary>
public static class DatabaseProviders
{
    public const string MySql = "Pomelo.EntityFrameworkCore.MySql";
    public const string Sqlite = "Microsoft.EntityFrameworkCore.Sqlite";

    /// <summary>The assembly that holds the SQLite migrations (the MySQL migrations live in this Infrastructure assembly).</summary>
    public const string SqliteMigrationsAssembly = "OptimizeAll.Infrastructure.Sqlite";

    public static bool IsMySql(string? providerName) => providerName == MySql;
    public static bool IsSqlite(string? providerName) => providerName == Sqlite;
}

/// <summary>
/// Turns the MySQL-flavoured entity configurations into a provider-neutral model (used for SQLite):
/// <list type="bullet">
/// <item>explicit column types (<c>json</c>, <c>text</c>, <c>char(36)</c>) are removed so the provider picks its own
/// (SQLite: <c>TEXT</c>);</item>
/// <item>collations are removed: SQLite's default <c>BINARY</c> collation is already case-sensitive, which is what the
/// MySQL <c>utf8mb4_bin</c> columns need;</item>
/// <item>check-constraint SQL is rewritten: backtick identifiers become double-quoted, <c>CHAR_LENGTH</c> becomes
/// <c>LENGTH</c>, and decimal columns (stored as TEXT on SQLite, where a comparison with a number literal would be a
/// text comparison) are compared numerically via <c>CAST(… AS REAL)</c>, which is exact for the sign/zero checks the
/// constraints make.</item>
/// <item>decimals read back with their configured scale, as on MySQL (see <see cref="DecimalScaleConverter"/>).</item>
/// </list>
/// </summary>
public static class PortableModel
{
    private static readonly Regex Identifier = new("`([A-Za-z0-9_]+)`", RegexOptions.Compiled);
    private static readonly Regex CharLength = new(@"\bCHAR_LENGTH\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.GetColumnType() is not null) property.SetColumnType(null);
                if (property.GetCollation() is not null) property.SetCollation(null);
                RemoveMySqlAnnotations(property);
                if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(decimal) && property.GetValueConverter() is null)
                    property.SetValueConverter(DecimalScaleConverter(property.GetScale() ?? 4));
            }
            RemoveMySqlAnnotations(entityType);

            var decimals = entityType.GetProperties()
                .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(decimal))
                .Select(p => p.GetColumnName())
                .ToHashSet(StringComparer.Ordinal);

            foreach (var check in entityType.GetCheckConstraints().ToList())
            {
                var sql = RewriteCheckSql(check.Sql, decimals);
                if (sql == check.Sql) continue;
                var modelName = check.ModelName;
                var name = check.Name;
                entityType.RemoveCheckConstraint(modelName);
                var replacement = entityType.AddCheckConstraint(modelName, sql);
                if (name is not null && name != replacement.Name) replacement.Name = name;
            }
        }
        RemoveMySqlAnnotations(modelBuilder.Model);
    }

    /// <summary>Rewrites MySQL check-constraint SQL into portable SQL (see the class remarks).</summary>
    public static string RewriteCheckSql(string sql, IReadOnlySet<string> decimalColumns)
    {
        var rewritten = CharLength.Replace(sql, "LENGTH(");
        return Identifier.Replace(rewritten, m =>
        {
            var column = m.Groups[1].Value;
            return decimalColumns.Contains(column) ? $"CAST(\"{column}\" AS REAL)" : $"\"{column}\"";
        });
    }

    /// <summary>
    /// SQLite stores decimals as TEXT, so a value reads back with the scale its text had (11.5), where MySQL's
    /// DECIMAL(p,s) always returns s places (11.5000). Adding a zero with scale s restores the MySQL behaviour, so values
    /// (and anything formatting them) are identical on both providers. The value itself is unchanged.
    /// </summary>
    private static ValueConverter<decimal, decimal> DecimalScaleConverter(int scale)
    {
        var zero = new decimal(0, 0, 0, false, (byte)Math.Clamp(scale, 0, 28));
        return new ValueConverter<decimal, decimal>(v => v, v => v + zero);
    }

    private static void RemoveMySqlAnnotations(IMutableAnnotatable annotatable)
    {
        foreach (var annotation in annotatable.GetAnnotations().Where(a => a.Name.StartsWith("MySql:", StringComparison.Ordinal)).ToList())
            annotatable.RemoveAnnotation(annotation.Name);
    }
}
