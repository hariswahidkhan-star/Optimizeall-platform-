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
    /// <summary>Amber-toned text on white (5.1:1); amber itself is too faint for text on light backgrounds.</summary>
    public const string AmberText = "#9A6200";

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

    /// <summary>Font stack of the artwork. SVG images cannot load web fonts, so the metrics below target Helvetica/Arial.</summary>
    public const string SansStack = "'Inter Tight', Inter, 'Helvetica Neue', Helvetica, Arial, sans-serif";

    // Advance widths of Arial/Helvetica Bold (1/1000 em) for the characters badge texts use; anything else counts as wide.
    private static readonly Dictionary<char, int> BoldWidths = BuildBoldWidths();

    private static Dictionary<char, int> BuildBoldWidths()
    {
        var w = new Dictionary<char, int>();
        void Set(string chars, int width)
        {
            foreach (var c in chars) w[c] = width;
        }
        Set("ABCDHKNRUÄÅÀÁÂÃÇÑÜÚÙÛ&", 722);
        Set("EPSVXYÉÈÊË", 667);
        Set("FTZL", 611);
        Set("GOQÖÓÒÔÕØ", 778);
        Set("M", 833);
        Set("W", 944);
        Set("IÍÌÎÏ", 278);
        Set("J", 556);
        Set("0123456789–$#?", 556);
        Set(" .,:;!|'’·", 278);
        Set("-()/[]", 333);
        Set("+=<>", 584);
        Set("@", 975);
        Set("%", 889);
        Set("…", 1000);
        return w;
    }

    /// <summary>
    /// Estimated rendered width of upper-case bold text: glyph advances (Arial Bold metrics, +6% headroom for fallback fonts
    /// such as DejaVu or Segoe UI) plus the letter spacing between glyphs. Every emblem text is sized and cut with it.
    /// </summary>
    public static double MeasureCaps(string text, double fontSize, double letterSpacing = 0)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var units = text.Sum(c => BoldWidths.TryGetValue(char.ToUpperInvariant(c), out var v) ? v : 760);
        return units / 1000.0 * fontSize * 1.06 + letterSpacing * (text.Length - 1);
    }

    /// <summary>Cuts <paramref name="text"/> with an ellipsis until it measures at most <paramref name="maxWidth"/>.</summary>
    public static string Truncate(string text, double fontSize, double letterSpacing, double maxWidth)
    {
        if (MeasureCaps(text, fontSize, letterSpacing) <= maxWidth) return text;
        var cut = text;
        while (cut.Length > 1 && MeasureCaps(cut + "…", fontSize, letterSpacing) > maxWidth) cut = cut[..^1];
        return cut.TrimEnd(' ', '.', ',', '&', '-', '–', '·') + "…";
    }

    /// <summary>The badge name laid out for the emblem: one to four balanced lines at the largest size that fits.</summary>
    public sealed record BadgeNameLayout(IReadOnlyList<string> Lines, double FontSize, double LineHeight);

    /// <summary>Width available to each badge-name line inside the emblem's inner hairline (400-unit box).</summary>
    public const double BadgeNameMaxWidth = 226;

    /// <summary>Height available to the badge-name block (cap top of the first line to baseline of the last).</summary>
    public const double BadgeNameMaxHeight = 92;

    public const double BadgeNameMaxSize = 27;
    public const double BadgeNameMinSize = 14;
    private const double BadgeNameSpacing = 0.6;
    private const double BadgeNameLeading = 1.16;
    private const double CapHeight = 0.72;

    /// <summary>Visual height of a block of <paramref name="lines"/> upper-case lines at <paramref name="size"/>.</summary>
    public static double BadgeNameBlockHeight(int lines, double size) => size * (BadgeNameLeading * (lines - 1) + CapHeight);

    public static BadgeNameLayout LayoutBadgeName(string badgeName)
    {
        var text = string.Join(' ', (badgeName ?? string.Empty).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0) text = "BADGE";
        IReadOnlyList<string>? bestLines = null;
        double bestSize = 0, bestScore = double.MinValue;
        for (var n = 1; n <= 4; n++)
        {
            var lines = Balance(text, n);
            if (lines.Count != n) break;
            var size = BadgeNameMaxSize;
            while (size > BadgeNameMinSize
                   && (lines.Any(l => MeasureCaps(l, size, BadgeNameSpacing) > BadgeNameMaxWidth) || BadgeNameBlockHeight(n, size) > BadgeNameMaxHeight))
                size -= 0.5;
            if (lines.Any(l => MeasureCaps(l, size, BadgeNameSpacing) > BadgeNameMaxWidth)) continue;
            // Fewer lines read better: an extra line has to buy a clearly larger type size.
            // Type under 18 units gets hard to read on small badge thumbnails, so it counts extra against a layout.
            var score = size - (n - 1) * 2.5 - Math.Max(0, 18 - size) * 1.5;
            if (score > bestScore)
            {
                bestScore = score;
                bestLines = lines;
                bestSize = size;
            }
        }
        if (bestLines is null)
        {
            // Very long names: four balanced lines at the minimum size, each cut to the width with an ellipsis.
            bestSize = BadgeNameMinSize;
            bestLines = Balance(text, 4).Select(l => Truncate(l, bestSize, BadgeNameSpacing, BadgeNameMaxWidth)).ToList();
        }
        return new BadgeNameLayout(bestLines, bestSize, Math.Round(bestSize * BadgeNameLeading, 2));
    }

    /// <summary>Splits the words into (at most) <paramref name="lines"/> lines, minimising the widest line.</summary>
    private static List<string> Balance(string text, int lines)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (lines <= 1 || words.Length <= 1) return new List<string> { string.Join(' ', words) };
        lines = Math.Min(lines, words.Length);
        List<string>? best = null;
        var bestWidth = double.MaxValue;
        // Badge names are a handful of words, so trying every split is cheap (and deterministic).
        void Search(int start, int remaining, List<string> acc)
        {
            if (remaining == 1)
            {
                var candidate = new List<string>(acc) { string.Join(' ', words[start..]) };
                // A line should not start with a dash (it reads as a list marker): such splits count as much wider.
                var width = candidate.Max(l => MeasureCaps(l, 1)) + (candidate.Skip(1).Any(l => l[0] is '–' or '-' or '—') ? 1.5 : 0);
                if (width < bestWidth - 1e-9)
                {
                    bestWidth = width;
                    best = candidate;
                }
                return;
            }
            for (var end = start + 1; end <= words.Length - remaining + 1; end++)
            {
                acc.Add(string.Join(' ', words[start..end]));
                Search(end, remaining - 1, acc);
                acc.RemoveAt(acc.Count - 1);
            }
        }
        Search(0, lines, new List<string>());
        return best!;
    }

    /// <summary>A darker shade of a #RRGGBB colour.</summary>
    private static string Shade(string hex, double factor)
    {
        int C(int i) => (int)Math.Round(Convert.ToInt32(hex.Substring(i, 2), 16) * factor);
        return $"#{C(1):X2}{C(3):X2}{C(5):X2}";
    }

    /// <summary>True when white text on <paramref name="hex"/> would be too faint (light accents such as amber).</summary>
    private static bool IsLight(string hex)
    {
        double L(int i)
        {
            var c = Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * L(1) + 0.7152 * L(3) + 0.0722 * L(5) > 0.4;
    }

    // Emblem geometry (400-unit box): the inner hairline hexagon has radius 156, so its straight sides span y 122–278 at
    // x 65–335 and the slanted sides narrow to the tips at y 44 and 356. Every text sits inside those bounds.
    private const double HairlineRadius = 156;
    public const double IssuerMaxWidth = 212;

    /// <summary>
    /// The badge emblem as SVG elements in a 400×400 box at (x, y) scaled by <paramref name="scale"/>: a soft-cornered
    /// hexagon with a category-colour rim, navy face and amber hairline; a star, the issuer line, the badge name (sized to
    /// fit, 1–4 lines) and a level ribbon. <paramref name="idPrefix"/> keeps gradient ids unique within a document.
    /// </summary>
    private static string Emblem(string badgeName, CourseCategory category, CourseLevel level, string issuer, double x, double y, double scale,
        string idPrefix = "oa-badge")
    {
        var accent = CategoryColor(category);
        var sb = new StringBuilder();
        sb.Append($"<g transform=\"translate({F(x)} {F(y)}) scale({F(scale)})\">");
        sb.Append("<defs>");
        sb.Append($"<linearGradient id=\"{idPrefix}-rim\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"{accent}\"/><stop offset=\"1\" stop-color=\"{Shade(accent, 0.7)}\"/></linearGradient>");
        sb.Append($"<linearGradient id=\"{idPrefix}-face\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"#2B3577\"/><stop offset=\"0.5\" stop-color=\"{Navy}\"/><stop offset=\"1\" stop-color=\"{NavyDark}\"/></linearGradient>");
        sb.Append("</defs>");
        // Rim and face; round joins soften the corners.
        sb.Append($"<polygon points=\"{Points(Hexagon(200, 200, 182))}\" fill=\"url(#{idPrefix}-rim)\" stroke=\"url(#{idPrefix}-rim)\" stroke-width=\"22\" stroke-linejoin=\"round\"/>");
        sb.Append($"<polygon points=\"{Points(Hexagon(200, 200, 170))}\" fill=\"url(#{idPrefix}-face)\" stroke=\"#FFFFFF\" stroke-opacity=\"0.16\" stroke-width=\"1.5\" stroke-linejoin=\"round\"/>");
        sb.Append($"<polygon points=\"{Points(Hexagon(200, 200, HairlineRadius))}\" fill=\"none\" stroke=\"{Amber}\" stroke-opacity=\"0.55\" stroke-width=\"1.5\" stroke-linejoin=\"round\"/>");
        sb.Append($"<polygon points=\"{Points(Star(200, 90, 17, 7.2))}\" fill=\"{Amber}\" stroke=\"{Amber}\" stroke-width=\"1.5\" stroke-linejoin=\"round\"/>");

        // Issuer: small caps line in the straight-sided band, never on the slanted edges.
        const double issuerSpacing = 2.2;
        var issuerText = string.Join(' ', (issuer ?? string.Empty).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var issuerSize = 12.0;
        while (issuerSize > 9 && MeasureCaps(issuerText, issuerSize, issuerSpacing) > IssuerMaxWidth) issuerSize -= 0.5;
        issuerText = Truncate(issuerText, issuerSize, issuerSpacing, IssuerMaxWidth);
        sb.Append($"<text x=\"200\" y=\"136\" text-anchor=\"middle\" font-family=\"{SansStack}\" font-weight=\"600\" font-size=\"{F(issuerSize)}\" letter-spacing=\"{F(issuerSpacing)}\" fill=\"{Amber}\">{X(issuerText)}</text>");
        sb.Append($"<line x1=\"186\" y1=\"152\" x2=\"214\" y2=\"152\" stroke=\"{Amber}\" stroke-opacity=\"0.7\" stroke-width=\"1.5\" stroke-linecap=\"round\"/>");

        // Badge name: its block (cap top to last baseline, at most 92 high) is centred on y 214, between the divider
        // (y 152) and the ribbon (y 279).
        var name = LayoutBadgeName(badgeName);
        var blockHeight = BadgeNameBlockHeight(name.Lines.Count, name.FontSize);
        var firstBaseline = 214 - blockHeight / 2 + name.FontSize * CapHeight;
        for (var i = 0; i < name.Lines.Count; i++)
            sb.Append($"<text x=\"200\" y=\"{F(firstBaseline + i * name.LineHeight)}\" text-anchor=\"middle\" font-family=\"{SansStack}\" font-weight=\"700\" font-size=\"{F(name.FontSize)}\" letter-spacing=\"{F(BadgeNameSpacing)}\" fill=\"#FFFFFF\">{X(name.Lines[i])}</text>");

        // Level ribbon with notched ends (fits the narrowing hexagon: half-width 76 at y 305, where the hairline allows 88).
        const double ry = 292, rh = 26, rw = 128;
        var l = 200 - rw / 2;
        var r = 200 + rw / 2;
        sb.Append($"<polygon points=\"{F(l - 10)},{F(ry - rh / 2)} {F(r + 10)},{F(ry - rh / 2)} {F(r + 2)},{F(ry)} {F(r + 10)},{F(ry + rh / 2)} {F(l - 10)},{F(ry + rh / 2)} {F(l - 2)},{F(ry)}\" fill=\"{accent}\" stroke=\"{accent}\" stroke-width=\"2\" stroke-linejoin=\"round\"/>");
        var levelText = level.ToString().ToUpperInvariant();
        var levelSize = 12.0;
        while (levelSize > 9 && MeasureCaps(levelText, levelSize, 2.4) > rw - 16) levelSize -= 0.5;
        var levelFill = IsLight(accent) ? Navy : "#FFFFFF";
        sb.Append($"<text x=\"200\" y=\"{F(ry + levelSize * 0.36)}\" text-anchor=\"middle\" font-family=\"{SansStack}\" font-weight=\"700\" font-size=\"{F(levelSize)}\" letter-spacing=\"2.4\" fill=\"{levelFill}\">{X(levelText)}</text>");
        sb.Append($"<polygon points=\"200,320 205,326 200,332 195,326\" fill=\"{Amber}\" fill-opacity=\"0.85\"/>");
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
        sb.Append($"<text x=\"{left}\" y=\"{y}\" font-family=\"{sans}\" font-size=\"26\" fill=\"{(IsLight(accent) ? AmberText : accent)}\" font-weight=\"700\">Awarded the {X(t.BadgeName)} badge</text>");
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
