using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Persistence;

/// <summary>
/// Remembers which built-in rows an insert-only baseline seeder has already created, so a row an editor later deleted or
/// renamed (changing the key the seeder matches on) is not inserted again on the next start. The keys are kept as a JSON
/// array in <c>system_settings</c> under <c>seed.&lt;name&gt;</c> (not a <see cref="SettingKeys"/> entry, so it never shows in
/// the admin settings list).
/// <para>
/// A database that predates the ledger has no entry: the seeder then behaves as before (inserting what is missing) and the
/// ledger records the whole catalog, so from the next start on only catalog entries added in a later release are seeded.
/// </para>
/// </summary>
public sealed class SeedLedger
{
    public const string KeyPrefix = "seed.";

    private readonly SystemSetting _row;
    private readonly HashSet<string> _keys;
    private bool _changed;

    private SeedLedger(SystemSetting row, HashSet<string> keys)
    {
        _row = row;
        _keys = keys;
    }

    public static async Task<SeedLedger> LoadAsync(AppDbContext db, string name, CancellationToken ct)
    {
        var key = KeyPrefix + name;
        var row = await db.Set<SystemSetting>().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            row = new SystemSetting
            {
                Key = key, ValueJson = "[]", UpdatedAt = DateTime.UtcNow,
                Description = "Built-in rows the baseline seeder already created (deleted or renamed ones are not re-created).",
            };
            db.Set<SystemSetting>().Add(row);
        }
        HashSet<string> keys;
        try
        {
            keys = new HashSet<string>(JsonSerializer.Deserialize<List<string>>(row.ValueJson) ?? new List<string>(), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            keys = new HashSet<string>(StringComparer.Ordinal);
        }
        return new SeedLedger(row, keys);
    }

    /// <summary>True when the seeder created this entry before (whether or not the row still exists).</summary>
    public bool WasSeeded(string key) => _keys.Contains(key);

    /// <summary>Records that the entry exists (inserted now or found already present). Saved with the seeder's SaveChanges.</summary>
    public void Record(string key)
    {
        if (!_keys.Add(key)) return;
        _changed = true;
        _row.ValueJson = JsonSerializer.Serialize(_keys.OrderBy(k => k, StringComparer.Ordinal));
        _row.UpdatedAt = DateTime.UtcNow;
    }

    public bool HasChanges => _changed;
}
