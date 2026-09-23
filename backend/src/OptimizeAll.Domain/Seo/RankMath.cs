namespace OptimizeAll.Domain.Seo;

public sealed record ShareOfVoiceEntry(string Domain, double Visibility, double Share);

public sealed record RankMove(Guid KeywordId, int? Previous, int? Current, int Change);

/// <summary>Ranking statistics (pure).</summary>
public static class RankMath
{
    /// <summary>
    /// Expected organic click-through rate by position (industry average curve, positions 1–20; 0 beyond).
    /// Used to weight visibility, so ranking #1 counts far more than ranking #9.
    /// </summary>
    private static readonly double[] CtrCurve =
    {
        0.284, 0.157, 0.110, 0.080, 0.072, 0.051, 0.040, 0.032, 0.028, 0.025,
        0.020, 0.018, 0.016, 0.014, 0.012, 0.010, 0.009, 0.008, 0.007, 0.006,
    };

    public static double ExpectedCtr(int? position) =>
        position is >= 1 and <= 20 ? CtrCurve[position.Value - 1] : 0;

    /// <summary>
    /// Share of voice: for each domain, Σ over keywords of expected CTR(position) × (search volume, or 1 when unknown),
    /// divided by the total across all domains. Input rows are (domain, keyword volume, position).
    /// </summary>
    public static IReadOnlyList<ShareOfVoiceEntry> ShareOfVoice(IEnumerable<(string Domain, int? Volume, int? Position)> rows)
    {
        var visibility = rows
            .GroupBy(r => r.Domain.ToLowerInvariant())
            .Select(g => (Domain: g.Key, Value: g.Sum(r => ExpectedCtr(r.Position) * Math.Max(1, r.Volume ?? 1))))
            .ToList();
        var total = visibility.Sum(v => v.Value);
        return visibility
            .Select(v => new ShareOfVoiceEntry(v.Domain, Math.Round(v.Value, 4), total == 0 ? 0 : Math.Round(v.Value / total, 4)))
            .OrderByDescending(v => v.Share).ThenBy(v => v.Domain, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Position change (positive = improved). Not ranking counts as position 101 so entering/leaving the top 100 shows
    /// as a move.
    /// </summary>
    public static int Change(int? previous, int? current) => (previous ?? 101) - (current ?? 101);

    /// <summary>Biggest winners and losers between two snapshots per keyword (only keywords that moved).</summary>
    public static (IReadOnlyList<RankMove> Winners, IReadOnlyList<RankMove> Losers) Movers(
        IEnumerable<(Guid KeywordId, int? Previous, int? Current)> pairs, int take = 10)
    {
        var moves = pairs.Select(p => new RankMove(p.KeywordId, p.Previous, p.Current, Change(p.Previous, p.Current)))
            .Where(m => m.Change != 0).ToList();
        return (moves.Where(m => m.Change > 0).OrderByDescending(m => m.Change).Take(take).ToList(),
            moves.Where(m => m.Change < 0).OrderBy(m => m.Change).Take(take).ToList());
    }

    /// <summary>Normalizes a domain or URL to a bare host without "www." (for comparing SERP result domains).</summary>
    public static string BareHost(string domainOrUrl)
    {
        var value = domainOrUrl.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Host.Length > 0) value = uri.Host;
        else
        {
            var slash = value.IndexOf('/');
            if (slash >= 0) value = value[..slash];
            var colon = value.IndexOf(':');
            if (colon >= 0) value = value[..colon];
        }
        value = value.TrimEnd('.').ToLowerInvariant();
        return value.StartsWith("www.", StringComparison.Ordinal) ? value[4..] : value;
    }
}
