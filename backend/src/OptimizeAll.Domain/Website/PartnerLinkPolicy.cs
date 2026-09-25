using OptimizeAll.Domain.Marketing;

namespace OptimizeAll.Domain.Website;

/// <summary>An active partner's link rule: its website host and the UTM tags for its links.</summary>
public sealed record PartnerLinkRule(string Slug, string Host, string UtmSource, string UtmMedium, string? UtmCampaign);

/// <summary>The attributes an outbound link to a partner must be rendered with.</summary>
public sealed record PartnerLink(string Href, string Rel, string Target, string PartnerSlug);

/// <summary>
/// Google's link-spam policy: every paid or partnership link is qualified with <c>rel="sponsored"</c>. This is the one rule
/// for links to partners, shared by everything that renders HTML or anchors — the web app's Markdown renderer mirrors it
/// in frontend/src/features/public/partners/partnerLinks.ts (same tests), and any server-side renderer (prerendered
/// public pages) must pass each outbound href through <see cref="Apply"/>. A link matches a partner when its host is the
/// partner's website host or a subdomain of it ("www." ignored); the link then gets <c>rel="sponsored noopener"</c>,
/// <c>target="_blank"</c> and the partner's UTM tags (existing <c>utm_*</c> parameters of the link are kept).
/// </summary>
public static class PartnerLinkPolicy
{
    public const string Rel = "sponsored noopener";
    public const string Target = "_blank";

    /// <summary>utm_campaign of partner links written into editorial content (blog posts, pages, courses).</summary>
    public const string EditorialCampaign = "editorial";

    /// <summary>The host partner links are matched on ("www." removed, lower case), or null for an unusable URL.</summary>
    public static string? HostOf(string? url)
    {
        if (!TrackingUrl.IsValidDestination(url)) return null;
        var host = new Uri(url!).IdnHost.ToLowerInvariant().TrimEnd('.');
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>The partner whose website the link points to (host equal or a subdomain), or null.</summary>
    public static PartnerLinkRule? Match(string? href, IEnumerable<PartnerLinkRule> rules)
    {
        if (HostOf(href) is not { } host) return null;
        return rules.Where(r => host == r.Host || host.EndsWith("." + r.Host, StringComparison.Ordinal))
            .OrderByDescending(r => r.Host.Length).FirstOrDefault();
    }

    /// <summary>The href with the partner's UTM tags (utm_* already on the link win: an editor's explicit tag is kept).</summary>
    public static string WithUtm(string href, PartnerLinkRule rule, string campaign)
    {
        var existing = new Uri(href).Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Uri.UnescapeDataString(p.Split('=', 2)[0].Replace('+', ' ')).ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var tags = TrackingUrl.Utm(rule.UtmSource, rule.UtmMedium, rule.UtmCampaign ?? campaign)
            .Where(t => !existing.Contains(t.Key));
        return TrackingUrl.MergeQuery(href, tags);
    }

    /// <summary>How to render <paramref name="href"/>: null when it is not a partner link (render it as usual).</summary>
    public static PartnerLink? Apply(string? href, IEnumerable<PartnerLinkRule> rules, string campaign = EditorialCampaign) =>
        Match(href, rules) is { } rule ? new PartnerLink(WithUtm(href!, rule, campaign), Rel, Target, rule.Slug) : null;
}
