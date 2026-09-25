using System.Text.RegularExpressions;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.UnitTests.Foundation;

/// <summary>
/// The web app formats money with the API's minor units (JPY 0, KWD 3, ...), from its own table in
/// <c>frontend/src/lib/format/money.ts</c>. That table must list exactly <see cref="Money.NonDefaultMinorUnits"/>: a
/// currency added on one side only would show amounts with a different number of decimals than the API rounds them to.
/// </summary>
public sealed partial class MinorUnitsSyncTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray())))
            dir = dir.Parent;
        Assert.True(dir is not null, $"{string.Join('/', parts)} was not found above the test directory.");
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    /// <summary>Parses the <c>MINOR_UNITS</c> object literal of money.ts (<c>CODE: digits</c> pairs).</summary>
    internal static Dictionary<string, int> ParseFrontendTable(string source)
    {
        var block = TableRegex().Match(source);
        Assert.True(block.Success, "const MINOR_UNITS = { ... }; was not found in money.ts: update this test with the new shape.");
        var body = block.Groups["body"].Value;
        var entries = EntryRegex().Matches(body).Select(m => (Code: m.Groups["code"].Value, Digits: int.Parse(m.Groups["digits"].Value))).ToList();
        // Anything else in the literal (a comment is fine, a spread or a computed key is not) would be missed silently.
        var rest = EntryRegex().Replace(CommentRegex().Replace(body, string.Empty), string.Empty);
        Assert.True(string.IsNullOrWhiteSpace(rest.Replace(",", string.Empty)),
            $"MINOR_UNITS holds entries this test cannot read: '{rest.Trim()}'. Keep it a plain CODE: digits list.");
        var duplicates = entries.GroupBy(e => e.Code).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate currencies in MINOR_UNITS: " + string.Join(", ", duplicates));
        return entries.ToDictionary(e => e.Code, e => e.Digits, StringComparer.Ordinal);
    }

    [Fact]
    public void Frontend_table_matches_the_backend_minor_units()
    {
        var frontend = ParseFrontendTable(File.ReadAllText(RepoFile("frontend", "src", "lib", "format", "money.ts")));
        var backend = Money.NonDefaultMinorUnits.ToDictionary(kv => kv.Key.ToUpperInvariant(), kv => kv.Value, StringComparer.Ordinal);

        var differences = backend.Keys.Union(frontend.Keys).Order(StringComparer.Ordinal)
            .Where(code => !frontend.TryGetValue(code, out var f) || !backend.TryGetValue(code, out var b) || f != b)
            .Select(code => $"{code}: backend {(backend.TryGetValue(code, out var b) ? b : 2)}, frontend {(frontend.TryGetValue(code, out var f) ? f : 2)}")
            .ToList();
        Assert.True(differences.Count == 0,
            "Money.MinorUnits (backend/src/OptimizeAll.Domain/Common/Money.cs) and MINOR_UNITS (frontend/src/lib/format/money.ts) differ: "
            + string.Join("; ", differences));
    }

    [Fact]
    public void Every_listed_currency_differs_from_the_default_of_two()
    {
        // A "USD: 2" entry would be harmless but means the table is no longer the list of exceptions.
        Assert.All(Money.NonDefaultMinorUnits, kv => Assert.NotEqual(2, kv.Value));
    }

    [Fact]
    public void The_parser_reports_a_changed_or_missing_currency()
    {
        const string source = """
            const MINOR_UNITS: Readonly<Record<string, number>> = {
              BHD: 3, JPY: 0, // comment
              KRW: 0,
            };
            """;
        Assert.Equal(new Dictionary<string, int> { ["BHD"] = 3, ["JPY"] = 0, ["KRW"] = 0 }, ParseFrontendTable(source));
    }

    [GeneratedRegex(@"const\s+MINOR_UNITS[^=]*=\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"(?<code>[A-Z]{3})\s*:\s*(?<digits>\d+)")]
    private static partial Regex EntryRegex();

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();
}
