using System.Text;

namespace OptimizeAll.Domain.Seo;

/// <summary>
/// robots.txt parser and matcher following RFC 9309: records are grouped by consecutive <c>User-agent</c> lines;
/// the crawler obeys the group whose user-agent is the longest match of its product token (merging groups with the
/// same user-agent), else the <c>*</c> group; within the group the rule with the longest matching path wins and
/// <c>Allow</c> wins a tie. Patterns support <c>*</c> (any sequence) and a trailing <c>$</c> (end of URL).
/// <c>/robots.txt</c> itself is always allowed. Also collects <c>Sitemap</c> URLs, <c>Crawl-delay</c> and invalid lines.
/// </summary>
public sealed class RobotsTxt
{
    private sealed record Rule(bool Allow, string Pattern);

    private sealed class Group
    {
        public List<string> Agents { get; } = new();
        public List<Rule> Rules { get; } = new();
        public double? CrawlDelay { get; set; }
    }

    private readonly List<Group> _groups;

    public IReadOnlyList<string> Sitemaps { get; }

    /// <summary>Lines that could not be parsed ("line N: text").</summary>
    public IReadOnlyList<string> InvalidLines { get; }

    private RobotsTxt(List<Group> groups, List<string> sitemaps, List<string> invalid)
    {
        _groups = groups;
        Sitemaps = sitemaps;
        InvalidLines = invalid;
    }

    /// <summary>A robots.txt that allows everything (used when the file is missing: 4xx means "no restrictions").</summary>
    public static RobotsTxt AllowAll { get; } = new(new List<Group>(), new List<string>(), new List<string>());

    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "user-agent", "allow", "disallow", "sitemap", "crawl-delay", "host", "clean-param", "noindex", "request-rate", "visit-time",
    };

    public static RobotsTxt Parse(string? content)
    {
        var groups = new List<Group>();
        var sitemaps = new List<string>();
        var invalid = new List<string>();
        if (string.IsNullOrEmpty(content)) return new RobotsTxt(groups, sitemaps, invalid);

        Group? current = null;
        var lastWasAgent = false;
        var lineNumber = 0;
        foreach (var rawLine in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            lineNumber++;
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim().TrimStart('﻿');
            if (line.Length == 0) continue;

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                invalid.Add($"line {lineNumber}: {Truncate(rawLine.Trim())}");
                continue;
            }
            var field = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            if (!KnownFields.Contains(field))
            {
                invalid.Add($"line {lineNumber}: {Truncate(rawLine.Trim())}");
                continue;
            }

            switch (field)
            {
                case "user-agent":
                    if (current is null || !lastWasAgent)
                    {
                        current = new Group();
                        groups.Add(current);
                    }
                    current.Agents.Add(value.ToLowerInvariant());
                    lastWasAgent = true;
                    continue;
                case "sitemap":
                    if (Uri.TryCreate(value, UriKind.Absolute, out _)) sitemaps.Add(value);
                    else invalid.Add($"line {lineNumber}: {Truncate(rawLine.Trim())}");
                    break;
                case "allow":
                case "disallow":
                    if (current is null)
                    {
                        invalid.Add($"line {lineNumber}: {Truncate(rawLine.Trim())} (rule outside a User-agent group)");
                        break;
                    }
                    // An empty Disallow means "allow everything" and adds no rule.
                    if (value.Length > 0) current.Rules.Add(new Rule(field == "allow", value));
                    break;
                case "crawl-delay":
                    if (current is not null && double.TryParse(value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var delay) && delay >= 0)
                        current.CrawlDelay = delay;
                    else invalid.Add($"line {lineNumber}: {Truncate(rawLine.Trim())}");
                    break;
            }
            lastWasAgent = false;
        }
        return new RobotsTxt(groups, sitemaps, invalid);
    }

    /// <summary>Whether <paramref name="userAgentToken"/> may fetch <paramref name="pathAndQuery"/> (e.g. "/a/b?x=1").</summary>
    public bool IsAllowed(string userAgentToken, string pathAndQuery)
    {
        if (string.IsNullOrEmpty(pathAndQuery)) pathAndQuery = "/";
        if (pathAndQuery.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase)) return true;
        var rules = RulesFor(userAgentToken);
        if (rules.Count == 0) return true;

        var path = NormalizePath(pathAndQuery);
        Rule? best = null;
        var bestLength = -1;
        foreach (var rule in rules)
        {
            var pattern = NormalizePath(rule.Pattern);
            if (!Matches(pattern, path)) continue;
            var length = pattern.Length;
            if (length > bestLength || (length == bestLength && rule.Allow && best is { Allow: false }))
            {
                best = rule;
                bestLength = length;
            }
        }
        return best?.Allow ?? true;
    }

    /// <summary>Crawl-delay (seconds) of the group that applies to the crawler, if any.</summary>
    public double? CrawlDelayFor(string userAgentToken) => SelectGroups(userAgentToken).Select(g => g.CrawlDelay).FirstOrDefault(d => d.HasValue);

    private List<Rule> RulesFor(string userAgentToken) => SelectGroups(userAgentToken).SelectMany(g => g.Rules).ToList();

    private List<Group> SelectGroups(string userAgentToken)
    {
        var token = userAgentToken.ToLowerInvariant();
        var bestLength = 0;
        var matched = new List<Group>();
        foreach (var group in _groups)
        {
            foreach (var agent in group.Agents)
            {
                if (agent == "*" || agent.Length == 0 || !token.Contains(agent, StringComparison.Ordinal)) continue;
                if (agent.Length > bestLength)
                {
                    bestLength = agent.Length;
                    matched.Clear();
                }
                if (agent.Length == bestLength && !matched.Contains(group)) matched.Add(group);
            }
        }
        if (matched.Count > 0) return matched;
        return _groups.Where(g => g.Agents.Contains("*")).ToList();
    }

    /// <summary>Wildcard match anchored at the start: '*' matches any sequence, a final '$' anchors the end.</summary>
    internal static bool Matches(string pattern, string path)
    {
        // Unanchored patterns match a prefix, i.e. behave as if they ended with '*'.
        pattern = pattern.EndsWith('$') ? pattern[..^1] : pattern + "*";
        return WildcardMatch(pattern, path);
    }

    /// <summary>Full-string wildcard match in O(pattern × path) (greedy with backtracking to the last '*').</summary>
    private static bool WildcardMatch(string pattern, string text)
    {
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] != '*' && pattern[p] == text[t])
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    /// <summary>Decodes percent-encoded unreserved characters so "/%7Ejoe" and "/~joe" compare equal.</summary>
    private static string NormalizePath(string value)
    {
        if (!value.Contains('%')) return value;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length &&
                int.TryParse(value.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var code) &&
                code < 128 && (char.IsAsciiLetterOrDigit((char)code) || "-._~".Contains((char)code)))
            {
                sb.Append((char)code);
                i += 2;
            }
            else sb.Append(value[i]);
        }
        return sb.ToString();
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200];
}
