using System.Net;
using System.Text.RegularExpressions;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// Applies <see cref="PartnerLinkPolicy"/> to the server-rendered HTML (the counterpart of the web app's partnerLinks.ts):
/// every anchor whose target is a partner's website (host or subdomain) gets the partner's UTM tags,
/// <c>rel="sponsored noopener"</c> and <c>target="_blank"</c>; so do links to the partner click counter
/// (<c>/api/v1/public/partners/{slug}/visit</c>), which redirects to the partner. Runs over the whole body — content,
/// Markdown and site chrome — after it is written, so no renderer can forget it. Other anchors are left untouched.
/// </summary>
public static partial class SeoPartnerLinks
{
    public static string Apply(string html, IReadOnlyList<PartnerLinkRule> rules) =>
        AnchorRegex().Replace(html, m =>
        {
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value);
            string? target = null;
            if (VisitRegex().IsMatch(href)) target = href;
            else if (rules.Count > 0 && PartnerLinkPolicy.Apply(href, rules) is { } link) target = link.Href;
            if (target is null) return m.Value;
            var attrs = RelOrTargetRegex().Replace(m.Groups["pre"].Value + m.Groups["post"].Value, string.Empty).Trim();
            return $"<a {(attrs.Length > 0 ? attrs + " " : string.Empty)}href=\"{WebUtility.HtmlEncode(target)}\" rel=\"{PartnerLinkPolicy.Rel}\" target=\"{PartnerLinkPolicy.Target}\">";
        });

    [GeneratedRegex("<a (?<pre>[^>]*?)href=\"(?<href>[^\"]*)\"(?<post>[^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex("\\s*\\b(rel|target)=\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex RelOrTargetRegex();

    [GeneratedRegex("^/api/v1/public/partners/[a-z0-9-]+/visit(\\?|$)")]
    private static partial Regex VisitRegex();
}
