using System.Text;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.Ads;

public sealed record NamingCheck(bool Compliant, string? Expected, IReadOnlyList<string> Problems);

/// <summary>
/// Campaign naming convention per client. A template is literal text with tokens:
/// <c>{client}</c> (client slug), <c>{platform}</c>, <c>{objective}</c>, <c>{country}</c>, <c>{yyyy}</c>, <c>{mm}</c>,
/// <c>{yyyymm}</c>, <c>{audience}</c>, <c>{name}</c> (free text). <see cref="Build"/> renders a name from values;
/// <see cref="Check"/> validates a name: fixed tokens must equal their value, free tokens must be a non-empty
/// slug (letters, digits, hyphens) and every literal separator must be present.
/// </summary>
public static partial class NamingConvention
{
    public static readonly IReadOnlyList<string> Tokens = new[] { "client", "platform", "objective", "country", "yyyy", "mm", "yyyymm", "audience", "name" };

    private static readonly HashSet<string> FreeTokens = new(StringComparer.Ordinal) { "objective", "audience", "name" };

    public static IReadOnlyList<string> TemplateProblems(string template)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(template)) problems.Add("The template is empty.");
        foreach (Match m in TokenRegex().Matches(template ?? string.Empty))
            if (!Tokens.Contains(m.Groups[1].Value)) problems.Add($"Unknown token {{{m.Groups[1].Value}}}.");
        if (!TokenRegex().IsMatch(template ?? string.Empty)) problems.Add("The template has no tokens.");
        return problems;
    }

    public static string Slug(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    public static string Build(string template, IReadOnlyDictionary<string, string?> values) =>
        TokenRegex().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) && !string.IsNullOrWhiteSpace(v)
            ? (m.Groups[1].Value is "yyyy" or "mm" or "yyyymm" ? v : Slug(v))
            : m.Value);

    /// <summary>Validates <paramref name="name"/> against the template with the fixed token values supplied.</summary>
    public static NamingCheck Check(string template, string name, IReadOnlyDictionary<string, string?> fixedValues)
    {
        var problems = new List<string>();
        var pattern = new StringBuilder("^");
        var last = 0;
        foreach (Match m in TokenRegex().Matches(template))
        {
            pattern.Append(Regex.Escape(template[last..m.Index]));
            var token = m.Groups[1].Value;
            if (!FreeTokens.Contains(token) && fixedValues.TryGetValue(token, out var fixedValue) && !string.IsNullOrWhiteSpace(fixedValue))
            {
                var expected = token is "yyyy" or "mm" or "yyyymm" ? fixedValue : Slug(fixedValue);
                pattern.Append($"(?<{token}>{Regex.Escape(expected)})");
            }
            else if (token is "yyyy")
                pattern.Append($"(?<{token}>\\d{{4}})");
            else if (token is "mm")
                pattern.Append($"(?<{token}>0[1-9]|1[0-2])");
            else if (token is "yyyymm")
                pattern.Append($"(?<{token}>\\d{{4}}(?:0[1-9]|1[0-2]))");
            else
                pattern.Append($"(?<{token}>[a-z0-9]+(?:-[a-z0-9]+)*)");
            last = m.Index + m.Length;
        }
        pattern.Append(Regex.Escape(template[last..])).Append('$');

        var ok = Regex.IsMatch(name, pattern.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (!ok)
        {
            problems.Add($"\"{name}\" does not follow the naming template \"{template}\".");
            foreach (var (token, value) in fixedValues)
            {
                if (string.IsNullOrWhiteSpace(value) || !template.Contains("{" + token + "}")) continue;
                var expected = token is "yyyy" or "mm" or "yyyymm" ? value : Slug(value);
                if (!name.Contains(expected, StringComparison.Ordinal)) problems.Add($"Expected {{{token}}} to be \"{expected}\".");
            }
            if (name.Any(char.IsUpper)) problems.Add("Use lower case.");
            if (name.Contains(' ')) problems.Add("Spaces are not allowed; use hyphens inside a segment.");
        }
        var example = Build(template, AddExamples(fixedValues.ToDictionary(kv => kv.Key, kv => kv.Value)));
        return new NamingCheck(ok, ok ? null : example, problems);
    }

    private static Dictionary<string, string?> AddExamples(Dictionary<string, string?> values)
    {
        foreach (var free in FreeTokens) values.TryAdd(free, free == "name" ? "summer-sale" : free == "objective" ? "conversions" : "prospecting");
        values.TryAdd("country", "us");
        values.TryAdd("yyyy", "2026");
        values.TryAdd("mm", "09");
        values.TryAdd("yyyymm", "202609");
        return values;
    }

    [GeneratedRegex(@"\{([a-z]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}

public sealed record UtmParameters(string Source, string Medium, string Campaign, string? Term = null, string? Content = null, string? Id = null);

/// <summary>
/// Builds UTM-tagged URLs: keeps the path, fragment and every non-UTM query parameter, replaces existing utm_* values,
/// and (optionally) lower-cases and hyphenates values so reports don't split "Facebook" and "facebook".
/// </summary>
public static class UtmBuilder
{
    private static readonly string[] UtmKeys = { "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id" };

    public static string Build(string url, UtmParameters p, bool normalize = true)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new Common.DomainException("utm.invalid_url", "Enter an absolute http(s) URL.");
        if (string.IsNullOrWhiteSpace(p.Source) || string.IsNullOrWhiteSpace(p.Medium) || string.IsNullOrWhiteSpace(p.Campaign))
            throw new Common.DomainException("utm.missing", "Source, medium and campaign are required.");

        var kept = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !UtmKeys.Contains(Uri.UnescapeDataString(pair.Split('=', 2)[0]).ToLowerInvariant()))
            .ToList();
        string V(string value) => Uri.EscapeDataString(normalize ? Normalize(value) : value.Trim());
        var utm = new List<string>
        {
            "utm_source=" + V(p.Source),
            "utm_medium=" + V(p.Medium),
            "utm_campaign=" + V(p.Campaign),
        };
        if (!string.IsNullOrWhiteSpace(p.Term)) utm.Add("utm_term=" + V(p.Term));
        if (!string.IsNullOrWhiteSpace(p.Content)) utm.Add("utm_content=" + V(p.Content));
        if (!string.IsNullOrWhiteSpace(p.Id)) utm.Add("utm_id=" + V(p.Id));

        var builder = new UriBuilder(uri) { Query = string.Join('&', kept.Concat(utm)) };
        var result = builder.Uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
        // UriBuilder adds the default port only when it was explicit; keep the original authority form.
        return result;
    }

    public static string Normalize(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}

public sealed record CopyLimit(string Field, int Max, bool Hard, int MinCount, int MaxCount);

public sealed record CopyIssue(string Field, int Index, string Severity, string Message);

/// <summary>Per-platform ad copy limits (hard limits are errors, recommended lengths are warnings).</summary>
public static class AdCopyLimits
{
    public static IReadOnlyList<CopyLimit> For(AdPlatform platform) => platform switch
    {
        AdPlatform.GoogleAds or AdPlatform.MicrosoftAds => new[]
        {
            new CopyLimit("headline", 30, true, 3, 15),
            new CopyLimit("description", 90, true, 2, 4),
        },
        AdPlatform.MetaAds => new[]
        {
            new CopyLimit("headline", 40, false, 1, 5),
            new CopyLimit("description", 30, false, 0, 5),
            new CopyLimit("primaryText", 125, false, 0, 1),
        },
        AdPlatform.TikTokAds => new[]
        {
            new CopyLimit("headline", 100, true, 1, 5),
            new CopyLimit("description", 100, true, 0, 5),
        },
        AdPlatform.LinkedInAds => new[]
        {
            new CopyLimit("headline", 200, true, 1, 5),
            new CopyLimit("description", 300, true, 0, 5),
            new CopyLimit("primaryText", 600, true, 0, 1),
        },
        AdPlatform.SnapchatAds => new[]
        {
            new CopyLimit("headline", 34, true, 1, 5),
            new CopyLimit("description", 34, true, 0, 5),
        },
        _ => Array.Empty<CopyLimit>(),
    };

    public static IReadOnlyList<CopyIssue> Validate(AdPlatform platform, IReadOnlyList<string> headlines, IReadOnlyList<string> descriptions, string? primaryText)
    {
        var issues = new List<CopyIssue>();
        foreach (var limit in For(platform))
        {
            IReadOnlyList<string> values = limit.Field switch
            {
                "headline" => headlines,
                "description" => descriptions,
                _ => string.IsNullOrWhiteSpace(primaryText) ? Array.Empty<string>() : new[] { primaryText },
            };
            var nonEmpty = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            if (nonEmpty.Count < limit.MinCount)
                issues.Add(new CopyIssue(limit.Field, -1, "Error", $"{platform}: at least {limit.MinCount} {limit.Field}(s) required."));
            if (nonEmpty.Count > limit.MaxCount)
                issues.Add(new CopyIssue(limit.Field, -1, "Error", $"{platform}: at most {limit.MaxCount} {limit.Field}(s)."));
            for (var i = 0; i < values.Count; i++)
            {
                var length = new System.Globalization.StringInfo(values[i]).LengthInTextElements;
                if (length > limit.Max)
                    issues.Add(new CopyIssue(limit.Field, i, limit.Hard ? "Error" : "Warning",
                        $"{platform} {limit.Field} {i + 1} is {length} characters; {(limit.Hard ? "the limit" : "the recommended maximum")} is {limit.Max}."));
            }
        }
        return issues;
    }
}
