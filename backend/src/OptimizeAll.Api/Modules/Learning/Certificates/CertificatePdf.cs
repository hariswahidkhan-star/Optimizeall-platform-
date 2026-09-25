using System.Globalization;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.Api.Modules.Learning.Certificates;

/// <summary>
/// Fonts for PDFsharp from embedded resources (Work Sans and Lora, SIL Open Font License 1.1; licence texts next to the
/// files in Modules/Learning/Fonts). No system fonts are needed, so PDFs render identically on Render's Linux containers.
/// </summary>
public sealed class EmbeddedFontResolver : IFontResolver
{
    public const string Sans = "Work Sans";
    public const string Serif = "Lora";

    private static readonly Dictionary<string, string> Faces = new(StringComparer.Ordinal)
    {
        ["WorkSans-Regular"] = "WorkSans-Regular.ttf",
        ["WorkSans-Bold"] = "WorkSans-Bold.ttf",
        ["Lora-Bold"] = "Lora-Bold.ttf",
        ["Lora-Italic"] = "Lora-Italic.ttf",
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (familyName.Equals(Serif, StringComparison.OrdinalIgnoreCase))
            return new FontResolverInfo(italic && !bold ? "Lora-Italic" : "Lora-Bold");
        return new FontResolverInfo(bold ? "WorkSans-Bold" : "WorkSans-Regular");
    }

    public byte[]? GetFont(string faceName)
    {
        if (!Faces.TryGetValue(faceName, out var file)) return null;
        using var stream = typeof(EmbeddedFontResolver).Assembly.GetManifestResourceStream("OptimizeAll.Learning.Fonts." + file);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static readonly object Gate = new();

    /// <summary>Installs the resolver once per process (PDFsharp's font settings are global).</summary>
    public static void EnsureInstalled()
    {
        if (GlobalFontSettings.FontResolver is EmbeddedFontResolver) return;
        lock (Gate)
        {
            if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
        }
    }
}

/// <summary>
/// The certificate as a vector PDF (A4 landscape) with PDFsharp (MIT licence, pure managed code): same layout as
/// <see cref="CertificateArt.CertificateSvg"/>, embedded fonts, document metadata (title, subject with the verification
/// code, keywords with the skills).
/// </summary>
public static class CertificatePdf
{
    private static XColor Hex(string hex) =>
        XColor.FromArgb(Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));

    private static XPoint[] Pts((double X, double Y)[] points, double x, double y, double scale) =>
        points.Select(p => new XPoint(x + p.X * scale, y + p.Y * scale)).ToArray();

    public static byte[] Render(CertificateArt.CertificateText t)
    {
        EmbeddedFontResolver.EnsureInstalled();
        var document = new PdfDocument();
        document.Info.Title = $"{t.BadgeName} — {t.CourseTitle}";
        document.Info.Author = t.Issuer;
        document.Info.Subject = $"Certificate {t.VerificationCode} issued to {t.HolderName}";
        document.Info.Keywords = string.Join(", ", t.Skills);
        document.Info.Creator = "Optimize All Academy";

        var page = document.AddPage();
        page.Size = PageSize.A4;
        page.Orientation = PageOrientation.Landscape;
        var w = page.Width.Point;
        var h = page.Height.Point;
        // Layout units follow the 1600-wide SVG; k maps them to points.
        var k = w / 1600.0;

        using (var g = XGraphics.FromPdfPage(page))
        {
            var navy = Hex(CertificateArt.Navy);
            var amber = Hex(CertificateArt.Amber);
            var ink = Hex(CertificateArt.Ink);
            var muted = Hex("#555A70");
            var accent = Hex(CertificateArt.CategoryColor(t.Category));

            g.DrawRectangle(XBrushes.White, 0, 0, w, h);
            g.DrawRectangle(new XPen(navy, 8 * k), 28 * k, 28 * k, 1544 * k, h - 56 * k);
            g.DrawRectangle(new XPen(amber, 2 * k), 48 * k, 48 * k, 1504 * k, h - 96 * k);
            g.DrawRectangle(new XSolidBrush(navy), 48 * k, 48 * k, 360 * k, h - 96 * k);
            g.DrawRectangle(new XSolidBrush(accent), 408 * k, 48 * k, 10 * k, h - 96 * k);

            // Emblem (400-unit box at (78,150) scaled 0.75, as in the SVG).
            const double ex = 78, ey = 150, es = 0.75;
            g.DrawPolygon(new XSolidBrush(accent), Pts(CertificateArt.Hexagon(200, 200, 196), ex * k, ey * k, es * k), XFillMode.Winding);
            g.DrawPolygon(new XSolidBrush(navy), Pts(CertificateArt.Hexagon(200, 200, 178), ex * k, ey * k, es * k), XFillMode.Winding);
            g.DrawPolygon(new XPen(amber, 3 * es * k), Pts(CertificateArt.Hexagon(200, 200, 166), ex * k, ey * k, es * k));
            g.DrawPolygon(new XSolidBrush(amber), Pts(CertificateArt.Star(200, 92, 26, 11), ex * k, ey * k, es * k), XFillMode.Winding);
            var badgeLines = CertificateArt.Wrap(t.BadgeName.ToUpperInvariant(), 15, 3);
            var badgeSize = (badgeLines.Max(l => l.Length) > 13 ? 25 : 29) * es * k;
            var badgeFont = new XFont(EmbeddedFontResolver.Sans, badgeSize, XFontStyleEx.Bold);
            var lineGap = badgeSize + 6 * es * k;
            var top = (ey + 205 * es) * k - (badgeLines.Count - 1) * lineGap / 2;
            for (var i = 0; i < badgeLines.Count; i++)
                g.DrawString(badgeLines[i], badgeFont, XBrushes.White, new XPoint((ex + 200 * es) * k, top + i * lineGap), XStringFormats.BaseLineCenter);
            g.DrawRoundedRectangle(new XSolidBrush(accent), (ex + 128 * es) * k, (ey + 282 * es) * k, 144 * es * k, 30 * es * k, 30 * es * k, 30 * es * k);
            g.DrawString(t.Level.ToString().ToUpperInvariant(), new XFont(EmbeddedFontResolver.Sans, 15 * es * k, XFontStyleEx.Bold), XBrushes.White,
                new XPoint((ex + 200 * es) * k, (ey + 303 * es) * k), XStringFormats.BaseLineCenter);

            var sidebarCenter = 228 * k;
            g.DrawString("VERIFIED CREDENTIAL", new XFont(EmbeddedFontResolver.Sans, 20 * k, XFontStyleEx.Regular), new XSolidBrush(amber),
                new XPoint(sidebarCenter, 520 * k), XStringFormats.BaseLineCenter);
            g.DrawString("Credential ID", new XFont(EmbeddedFontResolver.Sans, 18 * k, XFontStyleEx.Regular), new XSolidBrush(Hex("#C9CEEA")),
                new XPoint(sidebarCenter, 900 * k), XStringFormats.BaseLineCenter);
            g.DrawString(t.VerificationCode, new XFont(EmbeddedFontResolver.Sans, 26 * k, XFontStyleEx.Bold), XBrushes.White,
                new XPoint(sidebarCenter, 934 * k), XStringFormats.BaseLineCenter);

            var left = 500 * k;
            void Text(string text, double size, XFontStyleEx style, XColor color, double y, string family = EmbeddedFontResolver.Sans) =>
                g.DrawString(text, new XFont(family, size * k, style), new XSolidBrush(color), new XPoint(left, y * k), XStringFormats.BaseLineLeft);

            Text(t.Issuer.ToUpperInvariant(), 24, XFontStyleEx.Bold, navy, 170);
            Text("Certificate of Achievement", 64, XFontStyleEx.Bold, ink, 260, EmbeddedFontResolver.Serif);
            g.DrawRectangle(new XSolidBrush(amber), left, 290 * k, 120 * k, 6 * k);
            Text("This certifies that", 26, XFontStyleEx.Regular, muted, 370);
            Text(Fit(g, t.HolderName, new XFont(EmbeddedFontResolver.Serif, 72 * k, XFontStyleEx.Italic), 1000 * k), 72, XFontStyleEx.Italic, navy, 460,
                EmbeddedFontResolver.Serif);
            Text("has successfully completed the course and passed its final assessment", 26, XFontStyleEx.Regular, muted, 530);
            var titleLines = CertificateArt.Wrap(t.CourseTitle, 42, 2);
            for (var i = 0; i < titleLines.Count; i++) Text(titleLines[i], 44, XFontStyleEx.Bold, ink, 600 + i * 54);
            var y = 600 + titleLines.Count * 54 + 10;
            Text($"Awarded the {t.BadgeName} badge", 26, XFontStyleEx.Bold, accent, y);
            if (t.Skills.Count > 0)
            {
                var skills = CertificateArt.Wrap("Skills: " + string.Join(" · ", t.Skills), 80, 2);
                for (var i = 0; i < skills.Count; i++) Text(skills[i], 22, XFontStyleEx.Regular, muted, y + 50 + i * 34);
            }

            var linePen = new XPen(navy, 2 * k);
            g.DrawLine(linePen, left, 930 * k, left + 360 * k, 930 * k);
            Text(t.IssuedAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture), 26, XFontStyleEx.Bold, ink, 915);
            Text("Date of issue", 20, XFontStyleEx.Regular, muted, 966);
            g.DrawLine(linePen, left + 460 * k, 930 * k, left + 1000 * k, 930 * k);
            g.DrawString(Fit(g, t.VerifyUrl, new XFont(EmbeddedFontResolver.Sans, 19 * k, XFontStyleEx.Regular), 540 * k),
                new XFont(EmbeddedFontResolver.Sans, 19 * k, XFontStyleEx.Regular), new XSolidBrush(navy),
                new XPoint(left + 460 * k, 915 * k), XStringFormats.BaseLineLeft);
            g.DrawString("Verify at", new XFont(EmbeddedFontResolver.Sans, 20 * k, XFontStyleEx.Regular), new XSolidBrush(muted),
                new XPoint(left + 460 * k, 966 * k), XStringFormats.BaseLineLeft);

            if (t.Revoked)
            {
                var red = Hex("#B42318");
                var state = g.Save();
                g.RotateAtTransform(-18, new XPoint(1000 * k, 560 * k));
                g.DrawRoundedRectangle(new XPen(red, 10 * k), 640 * k, 480 * k, 720 * k, 150 * k, 24 * k, 24 * k);
                g.DrawString("REVOKED", new XFont(EmbeddedFontResolver.Sans, 96 * k, XFontStyleEx.Bold), new XSolidBrush(red),
                    new XPoint(1000 * k, 585 * k), XStringFormats.BaseLineCenter);
                g.Restore(state);
            }
        }

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    /// <summary>Shortens <paramref name="text"/> with an ellipsis until it fits <paramref name="maxWidth"/>.</summary>
    private static string Fit(XGraphics g, string text, XFont font, double maxWidth)
    {
        if (g.MeasureString(text, font).Width <= maxWidth) return text;
        var cut = text;
        while (cut.Length > 1 && g.MeasureString(cut + "…", font).Width > maxWidth) cut = cut[..^1];
        return cut.TrimEnd() + "…";
    }
}
