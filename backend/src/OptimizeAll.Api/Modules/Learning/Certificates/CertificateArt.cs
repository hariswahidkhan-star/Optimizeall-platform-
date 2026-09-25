using System.Globalization;
using System.Security;
using System.Text;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.Api.Modules.Learning.Certificates;

/// <summary>
/// Vector artwork for badges and certificates (brand navy #1F2659 + amber #FCB31E, one accent colour per category). The
/// SVGs are static documents (no scripts, no external references) and every text is XML-escaped. The same geometry is
/// drawn into the PDF by <see cref="CertificatePdf"/>.
/// </summary>
public static class CertificateArt
{
    public const string Navy = "#1F2659";
    public const string NavyDark = "#12163A";
    public const string Amber = "#FCB31E";
    public const string Ink = "#1D174C";

    public static string CategoryColor(CourseCategory category) => category switch
    {
        CourseCategory.Sales => "#0E8A6E",
        CourseCategory.Marketing => "#D9480F",
        CourseCategory.Seo => "#2563EB",
        CourseCategory.Ai => "#7C3AED",
        CourseCategory.Business => "#B7791F",
        CourseCategory.Design => "#DB2777",
        CourseCategory.Data => "#0891B2",
        _ => Amber,
    };

    private static string X(string text) => SecurityElement.Escape(text) ?? string.Empty;

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Greedy word wrap into at most <paramref name="maxLines"/> lines of about <paramref name="width"/> characters.</summary>
    public static IReadOnlyList<string> Wrap(string text, int width, int maxLines)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > width)
            {
                lines.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0) lines.Add(current.ToString());
        if (lines.Count > maxLines)
        {
            var kept = lines.Take(maxLines).ToList();
            kept[^1] = kept[^1].TrimEnd('.', ',') + "…";
            return kept;
        }
        return lines;
    }

    /// <summary>Points of a regular hexagon (pointy top) centred on (cx, cy).</summary>
    public static (double X, double Y)[] Hexagon(double cx, double cy, double r) =>
        Enumerable.Range(0, 6).Select(i =>
        {
            var angle = Math.PI / 180 * (60 * i - 90);
            return (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
        }).ToArray();

    /// <summary>Points of a five-pointed star centred on (cx, cy).</summary>
    public static (double X, double Y)[] Star(double cx, double cy, double outer, double inner) =>
        Enumerable.Range(0, 10).Select(i =>
        {
            var r = i % 2 == 0 ? outer : inner;
            var angle = Math.PI / 180 * (36 * i - 90);
            return (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
        }).ToArray();

    private static string Points((double X, double Y)[] points) => string.Join(' ', points.Select(p => $"{F(p.X)},{F(p.Y)}"));

    /// <summary>The badge emblem as SVG elements in a 400×400 box at (x, y) scaled by <paramref name="scale"/>.</summary>
    private static string Emblem(string badgeName, CourseCategory category, CourseLevel level, string issuer, double x, double y, double scale)
    {
        var accent = CategoryColor(category);
        var sb = new StringBuilder();
        sb.Append($"<g transform=\"translate({F(x)} {F(y)}) scale({F(scale)})\">");
        sb.Append($"<polygon points=\"{Points(Hexagon(200, 200, 196))}\" fill=\"{accent}\"/>");
        sb.Append($"<polygon points=\"{Points(Hexagon(200, 200, 178))}\" fill=\"{Navy}\"/>");
        sb.Append($"<polygon points=\"{Points(Hexagon(200, 200, 166))}\" fill=\"none\" stroke=\"{Amber}\" stroke-width=\"3\" stroke-opacity=\"0.9\"/>");
        sb.Append($"<polygon points=\"{Points(Star(200, 92, 26, 11))}\" fill=\"{Amber}\"/>");
        var lines = Wrap(badgeName.ToUpperInvariant(), 15, 3);
        var size = lines.Max(l => l.Length) > 13 ? 25 : 29;
        var top = 205 - (lines.Count - 1) * (size + 6) / 2.0;
        for (var i = 0; i < lines.Count; i++)
            sb.Append($"<text x=\"200\" y=\"{F(top + i * (size + 6))}\" text-anchor=\"middle\" font-family=\"'Work Sans', 'Segoe UI', Arial, sans-serif\" font-weight=\"700\" font-size=\"{size}\" letter-spacing=\"1\" fill=\"#FFFFFF\">{X(lines[i])}</text>");
        sb.Append($"<rect x=\"128\" y=\"282\" width=\"144\" height=\"30\" rx=\"15\" fill=\"{accent}\"/>");
        sb.Append($"<text x=\"200\" y=\"303\" text-anchor=\"middle\" font-family=\"'Work Sans', 'Segoe UI', Arial, sans-serif\" font-weight=\"700\" font-size=\"15\" letter-spacing=\"2\" fill=\"#FFFFFF\">{X(level.ToString().ToUpperInvariant())}</text>");
        var footer = issuer.Length > 26 ? issuer[..25] + "…" : issuer;
        sb.Append($"<text x=\"200\" y=\"340\" text-anchor=\"middle\" font-family=\"'Work Sans', 'Segoe UI', Arial, sans-serif\" font-weight=\"600\" font-size=\"13\" letter-spacing=\"2.5\" fill=\"{Amber}\">{X(footer.ToUpperInvariant())}</text>");
        sb.Append("</g>");
        return sb.ToString();
    }

    /// <summary>A standalone 400×400 SVG badge for a course (Open Badges image, course cards, LinkedIn/OG previews).</summary>
    public static string BadgeSvg(string badgeName, CourseCategory category, CourseLevel level, string issuer)
    {
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 400 400\" width=\"400\" height=\"400\" role=\"img\"");
        sb.Append($" aria-label=\"{X(badgeName)} badge ({X(level.ToString())}) by {X(issuer)}\">");
        sb.Append($"<title>{X(badgeName)} — {X(issuer)}</title>");
        sb.Append(Emblem(badgeName, category, level, issuer, 0, 0, 1));
        sb.Append("</svg>");
        return sb.ToString();
    }

    public sealed record CertificateText(
        string HolderName, string CourseTitle, string BadgeName, CourseCategory Category, CourseLevel Level, IReadOnlyList<string> Skills,
        DateTime IssuedAt, string VerificationCode, string VerifyUrl, string Issuer, bool Revoked);

    /// <summary>The certificate as a 1600×1131 (A4 landscape) SVG image.</summary>
    public static string CertificateSvg(CertificateText t)
    {
        const string sans = "'Work Sans', 'Segoe UI', Arial, sans-serif";
        const string serif = "Lora, Georgia, 'Times New Roman', serif";
        var accent = CategoryColor(t.Category);
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1600 1131\" width=\"1600\" height=\"1131\" role=\"img\"");
        sb.Append($" aria-label=\"Certificate {X(t.VerificationCode)}: {X(t.HolderName)} completed {X(t.CourseTitle)}\">");
        sb.Append($"<title>Certificate — {X(t.HolderName)} — {X(t.CourseTitle)}</title>");
        sb.Append("<rect width=\"1600\" height=\"1131\" fill=\"#FFFFFF\"/>");
        sb.Append($"<rect x=\"28\" y=\"28\" width=\"1544\" height=\"1075\" fill=\"none\" stroke=\"{Navy}\" stroke-width=\"8\"/>");
        sb.Append($"<rect x=\"48\" y=\"48\" width=\"1504\" height=\"1035\" fill=\"none\" stroke=\"{Amber}\" stroke-width=\"2\"/>");
        sb.Append($"<rect x=\"48\" y=\"48\" width=\"360\" height=\"1035\" fill=\"{Navy}\"/>");
        sb.Append($"<rect x=\"408\" y=\"48\" width=\"10\" height=\"1035\" fill=\"{accent}\"/>");
        sb.Append(Emblem(t.BadgeName, t.Category, t.Level, t.Issuer, 78, 150, 0.75));
        sb.Append($"<text x=\"228\" y=\"520\" text-anchor=\"middle\" font-family=\"{sans}\" font-size=\"20\" letter-spacing=\"3\" fill=\"{Amber}\">VERIFIED CREDENTIAL</text>");
        sb.Append($"<text x=\"228\" y=\"900\" text-anchor=\"middle\" font-family=\"{sans}\" font-size=\"18\" fill=\"#C9CEEA\">Credential ID</text>");
        sb.Append($"<text x=\"228\" y=\"934\" text-anchor=\"middle\" font-family=\"{sans}\" font-weight=\"700\" font-size=\"26\" letter-spacing=\"2\" fill=\"#FFFFFF\">{X(t.VerificationCode)}</text>");

        const double left = 500;
        sb.Append($"<text x=\"{left}\" y=\"170\" font-family=\"{sans}\" font-weight=\"700\" font-size=\"24\" letter-spacing=\"6\" fill=\"{Navy}\">{X(t.Issuer.ToUpperInvariant())}</text>");
        sb.Append($"<text x=\"{left}\" y=\"260\" font-family=\"{serif}\" font-weight=\"700\" font-size=\"64\" fill=\"{Ink}\">Certificate of Achievement</text>");
        sb.Append($"<rect x=\"{left}\" y=\"290\" width=\"120\" height=\"6\" fill=\"{Amber}\"/>");
        sb.Append($"<text x=\"{left}\" y=\"370\" font-family=\"{sans}\" font-size=\"26\" fill=\"#555A70\">This certifies that</text>");
        var holder = t.HolderName.Length > 38 ? t.HolderName[..37] + "…" : t.HolderName;
        sb.Append($"<text x=\"{left}\" y=\"460\" font-family=\"{serif}\" font-style=\"italic\" font-size=\"72\" fill=\"{Navy}\">{X(holder)}</text>");
        sb.Append($"<text x=\"{left}\" y=\"530\" font-family=\"{sans}\" font-size=\"26\" fill=\"#555A70\">has successfully completed the course and passed its final assessment</text>");
        var titleLines = Wrap(t.CourseTitle, 42, 2);
        for (var i = 0; i < titleLines.Count; i++)
            sb.Append($"<text x=\"{left}\" y=\"{600 + i * 54}\" font-family=\"{sans}\" font-weight=\"700\" font-size=\"44\" fill=\"{Ink}\">{X(titleLines[i])}</text>");
        var y = 600 + titleLines.Count * 54 + 10;
        sb.Append($"<text x=\"{left}\" y=\"{y}\" font-family=\"{sans}\" font-size=\"26\" fill=\"{accent}\" font-weight=\"700\">Awarded the {X(t.BadgeName)} badge</text>");
        if (t.Skills.Count > 0)
        {
            var skills = Wrap("Skills: " + string.Join(" · ", t.Skills), 80, 2);
            for (var i = 0; i < skills.Count; i++)
                sb.Append($"<text x=\"{left}\" y=\"{y + 50 + i * 34}\" font-family=\"{sans}\" font-size=\"22\" fill=\"#555A70\">{X(skills[i])}</text>");
        }

        sb.Append($"<line x1=\"{left}\" y1=\"930\" x2=\"{left + 360}\" y2=\"930\" stroke=\"{Navy}\" stroke-width=\"2\"/>");
        sb.Append($"<text x=\"{left}\" y=\"966\" font-family=\"{sans}\" font-size=\"20\" fill=\"#555A70\">Date of issue</text>");
        sb.Append($"<text x=\"{left}\" y=\"915\" font-family=\"{sans}\" font-weight=\"700\" font-size=\"26\" fill=\"{Ink}\">{X(t.IssuedAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture))}</text>");
        sb.Append($"<line x1=\"{left + 460}\" y1=\"930\" x2=\"{left + 1000}\" y2=\"930\" stroke=\"{Navy}\" stroke-width=\"2\"/>");
        sb.Append($"<text x=\"{left + 460}\" y=\"966\" font-family=\"{sans}\" font-size=\"20\" fill=\"#555A70\">Verify at</text>");
        var url = t.VerifyUrl.Length > 60 ? t.VerifyUrl[..59] + "…" : t.VerifyUrl;
        sb.Append($"<text x=\"{left + 460}\" y=\"915\" font-family=\"{sans}\" font-size=\"19\" fill=\"{Navy}\">{X(url)}</text>");
        if (t.Revoked)
        {
            sb.Append("<g transform=\"rotate(-18 1000 560)\">");
            sb.Append("<rect x=\"640\" y=\"480\" width=\"720\" height=\"150\" fill=\"none\" stroke=\"#B42318\" stroke-width=\"10\" rx=\"12\"/>");
            sb.Append($"<text x=\"1000\" y=\"585\" text-anchor=\"middle\" font-family=\"{sans}\" font-weight=\"700\" font-size=\"96\" letter-spacing=\"10\" fill=\"#B42318\">REVOKED</text>");
            sb.Append("</g>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}
