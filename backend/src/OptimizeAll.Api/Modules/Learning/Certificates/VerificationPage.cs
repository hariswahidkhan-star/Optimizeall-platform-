using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OptimizeAll.Api.Modules.Learning.Certificates;

/// <summary>
/// The server-rendered certificate verification page (<c>/verify/certificates/{id}</c> through the web server, see
/// docs/LEARNING.md § "Verification page"): a complete, script-free HTML document with the title/description, canonical,
/// Open Graph and Twitter tags that LinkedIn and search engines read, schema.org JSON-LD, and the verification itself
/// (holder, course, badge, skills, dates, valid/revoked). Every value is HTML-encoded; JSON-LD is serialized with the
/// HTML-safe encoder (no "&lt;/script" can appear).
/// </summary>
public static class VerificationPage
{
    private static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>The page's Content-Security-Policy: inline styles only, images from this origin.</summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'unsafe-inline'; img-src 'self' data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public static string Render(CertificateVerificationDto v, string baseUrl)
    {
        var url = v.Links.VerificationUrl;
        var title = v.Seo.Title + " | " + v.IssuerName;
        var issued = v.IssuedAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append($"<title>{H(title)}</title>");
        sb.Append($"<meta name=\"description\" content=\"{H(v.Seo.Description)}\">");
        sb.Append($"<link rel=\"canonical\" href=\"{H(url)}\">");
        if (v.Seo.NoIndex) sb.Append("<meta name=\"robots\" content=\"noindex, nofollow\">");
        sb.Append("<meta property=\"og:type\" content=\"website\">");
        sb.Append($"<meta property=\"og:title\" content=\"{H(v.Seo.Title)}\">");
        sb.Append($"<meta property=\"og:description\" content=\"{H(v.Seo.Description)}\">");
        sb.Append($"<meta property=\"og:url\" content=\"{H(url)}\">");
        sb.Append($"<meta property=\"og:image\" content=\"{H(baseUrl.TrimEnd('/') + "/og-image.png")}\">");
        sb.Append($"<meta property=\"og:site_name\" content=\"{H(v.IssuerName)}\">");
        sb.Append("<meta name=\"twitter:card\" content=\"summary_large_image\">");
        sb.Append($"<meta name=\"twitter:title\" content=\"{H(v.Seo.Title)}\">");
        sb.Append($"<meta name=\"twitter:description\" content=\"{H(v.Seo.Description)}\">");
        foreach (var ld in v.JsonLd)
            sb.Append("<script type=\"application/ld+json\">")
              .Append(JsonSerializer.Serialize(ld, new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default }))
              .Append("</script>");
        sb.Append("<style>").Append(Css).Append("</style></head><body>");
        sb.Append("<header class=\"top\"><a class=\"brand\" href=\"/learn\">").Append(H(v.IssuerName)).Append("</a>");
        sb.Append("<nav aria-label=\"Main\"><a href=\"/learn\">Free courses</a><a href=\"/register\">Create a free account</a></nav></header>");
        sb.Append("<main id=\"main\"><article class=\"card\" aria-labelledby=\"cert-title\">");
        sb.Append(v.IsValid
            ? "<p class=\"status ok\" role=\"status\"><span aria-hidden=\"true\">✓</span> Valid certificate</p>"
            : "<p class=\"status bad\" role=\"status\"><span aria-hidden=\"true\">✕</span> Revoked — this certificate is no longer valid</p>");
        sb.Append("<div class=\"grid\"><img class=\"badge\" width=\"200\" height=\"200\" src=\"")
          .Append(H(v.Links.BadgeImageUrl)).Append("\" alt=\"").Append(H(v.BadgeName)).Append(" badge\">");
        sb.Append("<div><p class=\"eyebrow\">Certificate of achievement</p>");
        sb.Append($"<h1 id=\"cert-title\">{H(v.HolderName)}</h1>");
        sb.Append($"<p class=\"lead\">earned the <strong>{H(v.BadgeName)}</strong> credential for completing <a href=\"/learn/{H(v.CourseSlug)}\">{H(v.CourseTitle)}</a> and passing its final assessment.</p>");
        sb.Append("<dl>");
        sb.Append($"<div><dt>Issued by</dt><dd>{H(v.IssuerName)}</dd></div>");
        sb.Append($"<div><dt>Issue date</dt><dd><time datetime=\"{v.IssuedAt:yyyy-MM-dd}\">{H(issued)}</time></dd></div>");
        sb.Append($"<div><dt>Credential ID</dt><dd><code>{H(v.VerificationCode)}</code></dd></div>");
        if (v.RevokedAt is { } revoked)
            sb.Append($"<div><dt>Revoked on</dt><dd><time datetime=\"{revoked:yyyy-MM-dd}\">{H(revoked.ToString("d MMMM yyyy", CultureInfo.InvariantCulture))}</time></dd></div>");
        sb.Append("</dl>");
        if (v.Skills.Count > 0)
        {
            sb.Append("<h2>Skills</h2><ul class=\"skills\">");
            foreach (var skill in v.Skills) sb.Append($"<li>{H(skill)}</li>");
            sb.Append("</ul>");
        }
        if (!string.IsNullOrWhiteSpace(v.BadgeDescription)) sb.Append($"<h2>What the holder demonstrated</h2><p>{H(v.BadgeDescription)}</p>");
        if (!string.IsNullOrWhiteSpace(v.Criteria)) sb.Append($"<h2>Criteria</h2><p>{H(v.Criteria)}</p>");
        sb.Append("<p class=\"actions\">");
        if (v.IsValid)
        {
            sb.Append($"<a class=\"btn primary\" href=\"{H(v.Links.PdfUrl)}\">Download certificate (PDF)</a>");
            sb.Append($"<a class=\"btn\" href=\"{H(v.Links.LinkedInShareUrl)}\" rel=\"noopener noreferrer\" target=\"_blank\">Share on LinkedIn<span class=\"sr\"> (opens in a new tab)</span></a>");
            sb.Append($"<a class=\"btn\" href=\"{H(v.Links.OpenBadgeAssertionUrl)}\">Open Badge (JSON)</a>");
        }
        sb.Append($"<a class=\"btn\" href=\"/learn/{H(v.CourseSlug)}\">Take this free course</a></p>");
        sb.Append("</div></div></article></main>");
        sb.Append($"<footer><p>Verification by {H(v.IssuerName)}. Questions about a certificate? Contact the issuer.</p></footer>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private const string Css = """
        :root{color-scheme:light dark;--bg:#f7f8fa;--card:#fff;--text:#2d3148;--muted:#555a70;--head:#12152e;--navy:#1f2659;--link:#343f87;--border:#e5e7ed}
        @media (prefers-color-scheme:dark){:root{--bg:#0a0c14;--card:#12151f;--text:#dfe2ec;--muted:#a4a9bb;--head:#f3f4f8;--navy:#bcc3e5;--link:#b3bbf0;--border:#232837}}
        *{box-sizing:border-box}body{margin:0;font:16px/1.6 system-ui,-apple-system,'Segoe UI',Roboto,Arial,sans-serif;background:var(--bg);color:var(--text)}
        a{color:var(--link)}.top{display:flex;flex-wrap:wrap;gap:12px;justify-content:space-between;align-items:center;padding:16px;max-width:1040px;margin:0 auto}
        .brand{font-weight:700;color:var(--head);text-decoration:none;font-size:18px}.top nav{display:flex;gap:16px}
        main{padding:16px;max-width:1040px;margin:0 auto}.card{background:var(--card);border:1px solid var(--border);border-radius:16px;padding:clamp(16px,4vw,40px)}
        .grid{display:grid;grid-template-columns:200px 1fr;gap:32px;align-items:start}@media (max-width:640px){.grid{grid-template-columns:1fr}.badge{width:140px;height:140px}}
        .status{display:inline-flex;gap:8px;font-weight:700;border-radius:999px;padding:6px 14px;margin:0 0 24px}
        .ok{background:#e7f6ec;color:#146c2e}.bad{background:#fde8e8;color:#9b1c1c}
        @media (prefers-color-scheme:dark){.ok{background:#0f2e1a;color:#8fe0a8}.bad{background:#3a1212;color:#fca5a5}}
        .eyebrow{text-transform:uppercase;letter-spacing:.12em;font-size:13px;color:var(--muted);margin:0}
        h1{font-size:clamp(28px,5vw,44px);line-height:1.15;margin:4px 0 8px;color:var(--head)}h2{font-size:18px;color:var(--head);margin:24px 0 8px}
        .lead{font-size:18px}dl{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px;margin:24px 0}
        dt{font-size:13px;color:var(--muted)}dd{margin:0;font-weight:600;color:var(--head)}code{font-size:15px}
        .skills{display:flex;flex-wrap:wrap;gap:8px;list-style:none;padding:0;margin:0}.skills li{border:1px solid var(--border);border-radius:999px;padding:2px 12px;font-size:14px}
        .actions{display:flex;flex-wrap:wrap;gap:12px;margin-top:28px}.btn{display:inline-block;padding:10px 16px;border-radius:10px;border:1px solid var(--border);text-decoration:none;font-weight:600}
        .primary{background:#1f2659;color:#fff;border-color:#1f2659}@media (prefers-color-scheme:dark){.primary{background:#bcc3e5;color:#141a36}}
        a:focus-visible{outline:3px solid #fcb31e;outline-offset:2px}.sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}
        footer{max-width:1040px;margin:0 auto;padding:16px;color:var(--muted);font-size:14px}
        """;
}
