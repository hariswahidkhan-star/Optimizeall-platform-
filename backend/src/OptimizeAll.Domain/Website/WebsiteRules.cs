using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.Website;

/// <summary>
/// Sanitizes Markdown written in the CMS so it can be stored and rendered safely:
/// <list type="bullet">
/// <item>raw HTML is removed (script/style/iframe/object/embed/template/textarea blocks including their content, then
/// every remaining tag and comment), which also removes inline event handlers such as <c>onerror=</c>;</item>
/// <item>links and images whose destination is not <c>https://</c>, <c>http://</c>, <c>mailto:</c>, an app path
/// (<c>/…</c>) or an in-page anchor (<c>#…</c>) are reduced to their text (images are dropped), so
/// <c>javascript:</c>, <c>data:</c>, <c>vbscript:</c> and protocol-relative links never survive;</item>
/// <item>fenced and indented code is kept verbatim (the renderer escapes it).</item>
/// </list>
/// The web app renders the result with its own Markdown renderer that emits React elements (no HTML injection), so
/// this is defence in depth. Pure and unit-tested.
/// </summary>
public static partial class MarkdownSanitizer
{
    public static string Sanitize(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;
        var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');

        var output = new StringBuilder(text.Length);
        var inFence = false;
        var fence = string.Empty;
        var prose = new StringBuilder();

        void FlushProse()
        {
            if (prose.Length == 0) return;
            output.Append(SanitizeProse(prose.ToString()));
            prose.Clear();
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            var fenceMatch = FenceRegex().Match(trimmed);
            if (!inFence && fenceMatch.Success)
            {
                FlushProse();
                inFence = true;
                fence = fenceMatch.Groups[1].Value;
                output.Append(line).Append('\n');
                continue;
            }
            if (inFence)
            {
                output.Append(line).Append('\n');
                if (trimmed.StartsWith(fence, StringComparison.Ordinal) && trimmed.Trim() == new string(fence[0], trimmed.Trim().Length))
                    inFence = false;
                continue;
            }
            prose.Append(line).Append('\n');
        }
        FlushProse();

        var result = output.ToString();
        // Collapse the blank lines left behind by removed blocks and drop the trailing newline we added.
        result = ExtraBlankLinesRegex().Replace(result, "\n\n");
        return result.Trim('\n');
    }

    private static string SanitizeProse(string prose)
    {
        // Inline code spans are shown literally (escaped by the renderer), so keep them out of tag and link rewriting.
        var spans = new List<string>();
        var s = InlineCodeRegex().Replace(prose.Replace("\u0000", string.Empty), m =>
        {
            spans.Add(m.Value);
            return $"\u0000{spans.Count - 1}\u0000";
        });

        s = DangerousBlockRegex().Replace(s, string.Empty);
        s = CommentRegex().Replace(s, string.Empty);
        s = UnclosedDangerousRegex().Replace(s, string.Empty);
        s = TagRegex().Replace(s, string.Empty);

        s = ImageRegex().Replace(s, m => IsSafeUrl(m.Groups["url"].Value) ? m.Value : string.Empty);
        s = LinkRegex().Replace(s, m => IsSafeUrl(m.Groups["url"].Value) ? m.Value : m.Groups["text"].Value);
        s = ReferenceDefinitionRegex().Replace(s, m => IsSafeUrl(m.Groups["url"].Value) ? m.Value : string.Empty);

        s = PlaceholderRegex().Replace(s, m => spans[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]);
        return s;
    }

    /// <summary>Allowed link/image destinations: https/http URLs with a host, mailto:, app paths and in-page anchors.</summary>
    public static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var u = url.Trim().Trim('<', '>');
        if (u.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) || u.Contains('\\')) return false;
        if (u.StartsWith('#')) return true;
        if (u.StartsWith('/')) return !u.StartsWith("//", StringComparison.Ordinal);
        if (u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return u.Length > 7 && u.Contains('@');
        return Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
               !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    }

    /// <summary>Plain text of a Markdown document (for search, excerpts and reading time).</summary>
    public static string ToPlainText(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;
        var s = markdown.Replace("\r\n", "\n");
        s = FenceLineRegex().Replace(s, string.Empty);
        s = ImageRegex().Replace(s, m => m.Groups["text"].Value);
        s = LinkRegex().Replace(s, m => m.Groups["text"].Value);
        s = TagRegex().Replace(s, string.Empty);
        s = MarkupRegex().Replace(s, string.Empty);
        s = WhitespaceRegex().Replace(s, " ");
        return s.Trim();
    }

    /// <summary>Estimated reading time at 225 words per minute, at least one minute.</summary>
    public static int ReadingMinutes(string? markdown)
    {
        var words = ToPlainText(markdown).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Ceiling(words / 225.0));
    }

    [GeneratedRegex(@"^(`{3,}|~{3,})")]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"^\s*(`{3,}|~{3,}).*$", RegexOptions.Multiline)]
    private static partial Regex FenceLineRegex();

    [GeneratedRegex(@"<(script|style|iframe|object|embed|template|textarea|noscript|svg|math)\b[^>]*>.*?</\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DangerousBlockRegex();

    [GeneratedRegex(@"<(script|style|iframe|object|embed|template|textarea|noscript|svg|math)\b.*", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UnclosedDangerousRegex();

    [GeneratedRegex(@"<!--.*?(-->|$)", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    /// <summary>Any HTML-looking tag, including autolinks like &lt;javascript:…&gt; and tags split over lines.</summary>
    [GeneratedRegex(@"</?[a-zA-Z!?][^<>]*>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"`+[^`\n]*`+")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\u0000(\d+)\u0000")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"!\[(?<text>[^\]]*)\]\(\s*(?<url>[^)\s]*)(\s+""[^""]*"")?\s*\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\[(?<text>[^\]]*)\]\(\s*(?<url>[^)\s]*)(\s+""[^""]*"")?\s*\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"^\s{0,3}\[[^\]]+\]:\s*(?<url>\S+).*$", RegexOptions.Multiline)]
    private static partial Regex ReferenceDefinitionRegex();

    [GeneratedRegex(@"[#>*_`~|]+")]
    private static partial Regex MarkupRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLinesRegex();
}

/// <summary>PDF content check by magic bytes (never by file name or client MIME type).</summary>
public static class PdfSignature
{
    private static ReadOnlySpan<byte> Header => "%PDF-"u8;
    private static ReadOnlySpan<byte> Eof => "%%EOF"u8;

    /// <summary>
    /// True when the bytes start with <c>%PDF-1.x</c>/<c>%PDF-2.x</c> and contain the <c>%%EOF</c> trailer marker in
    /// the last 2 KB (truncated uploads and renamed files fail).
    /// </summary>
    public static bool IsPdf(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data[..5].SequenceEqual(Header)) return false;
        if (data[5] is not ((byte)'1' or (byte)'2') || data[6] != (byte)'.') return false;
        var tail = data[Math.Max(0, data.Length - 2048)..];
        return tail.IndexOf(Eof) >= 0;
    }
}

/// <summary>Slug helpers for CMS titles.</summary>
public static partial class Slugs
{
    /// <summary>"Local SEO &amp; Google Business Profile" → "local-seo-google-business-profile".</summary>
    public static string From(string? text, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = char.ToLowerInvariant(ch);
            var folded = Folds.IndexOf(c);
            if (folded >= 0) c = FoldTargets[folded];
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }
        var slug = DashesRegex().Replace(sb.ToString(), "-").Trim('-');
        if (slug.Length > maxLength) slug = slug[..maxLength].TrimEnd('-');
        return slug;
    }

    // Fallback for runtimes in invariant-globalization mode, where Normalize(FormD) does not decompose accents.
    private const string Folds = "àáâãäåāçćčèéêëēěìíîïīñńňòóôõöøōùúûüūýÿžźżšśßł";
    private const string FoldTargets = "aaaaaaaccceeeeeeiiiiinnnooooooouuuuuyyzzzsssl";

    [GeneratedRegex("-{2,}")]
    private static partial Regex DashesRegex();
}

/// <summary>
/// Computes bookable consultation slots from weekly availability (in the agency's time zone), blackout days, existing
/// bookings, minimum notice and the booking horizon. All returned instants are UTC. Pure and unit-tested; handles DST
/// (local times that do not exist are skipped).
/// </summary>
public static class ConsultationSlots
{
    public static IReadOnlyList<DateTime> Available(
        ConsultationSettings settings, TimeZoneInfo zone, IReadOnlySet<DateOnly> blackouts, IReadOnlySet<string> bookedKeys,
        DateTime nowUtc, DateTime fromUtc, DateTime toUtc)
    {
        var result = new List<DateTime>();
        if (!settings.IsEnabled || settings.SlotMinutes <= 0) return result;
        var earliest = nowUtc.AddHours(settings.MinNoticeHours);
        var latest = nowUtc.AddDays(settings.MaxDaysAhead);
        var start = fromUtc > earliest ? fromUtc : earliest;
        var end = toUtc < latest ? toUtc : latest;
        if (end <= start) return result;

        var firstLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, zone)).AddDays(-1);
        var lastLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(end, zone)).AddDays(1);
        var slot = TimeSpan.FromMinutes(settings.SlotMinutes);

        for (var day = firstLocal; day <= lastLocal; day = day.AddDays(1))
        {
            if (blackouts.Contains(day)) continue;
            foreach (var window in settings.WeeklyAvailability.Where(w => w.Day == day.DayOfWeek).OrderBy(w => w.Start, StringComparer.Ordinal))
            {
                if (!TryParseTime(window.Start, out var ws) || !TryParseTime(window.End, out var we) || we <= ws) continue;
                for (var t = ws; t + slot <= we; t += slot)
                {
                    var local = day.ToDateTime(TimeOnly.FromTimeSpan(t), DateTimeKind.Unspecified);
                    if (zone.IsInvalidTime(local)) continue;
                    var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
                    if (utc < start || utc >= end) continue;
                    if (bookedKeys.Contains(ConsultationBooking.KeyFor(utc))) continue;
                    result.Add(utc);
                }
            }
        }
        return result.Distinct().OrderBy(x => x).ToList();
    }

    /// <summary>Parses "HH:mm" (00:00–24:00).</summary>
    public static bool TryParseTime(string? value, out TimeSpan time)
    {
        time = default;
        if (value is null || value.Length != 5 || value[2] != ':') return false;
        if (!int.TryParse(value.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(value.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)) return false;
        if (h > 24 || m > 59 || (h == 24 && m != 0)) return false;
        time = new TimeSpan(h, m, 0);
        return true;
    }
}
