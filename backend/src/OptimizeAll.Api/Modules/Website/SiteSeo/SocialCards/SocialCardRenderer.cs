using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace OptimizeAll.Api.Modules.Website.SiteSeo.SocialCards;

/// <summary>
/// What a page's social card shows: a small uppercase eyebrow (content type and category), the title, an optional
/// subtitle, a few facts along the bottom ("Free course · 12 lessons · Certificate") and the site's name and host.
/// Pages get one derived from their content (<see cref="SocialCardFactory"/>); a page builder can set
/// <see cref="SeoPage.Card"/> to choose the words itself.
/// </summary>
public sealed record SocialCard(string Eyebrow, string Title, string? Subtitle = null, IReadOnlyList<string>? Facts = null)
{
    /// <summary>Changes whenever the rendered image would (the words, the site name/host, the renderer's design).</summary>
    public string Version(string siteName, string host)
    {
        var key = string.Join('\u001f', SocialCardRenderer.DesignVersion, siteName, host, Eyebrow, Title, Subtitle ?? string.Empty,
            string.Join('\u001e', Facts ?? Array.Empty<string>()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12].ToLowerInvariant();
    }
}

/// <summary>
/// Renders 1200×630 PNG social cards (Open Graph / Twitter) in the brand look — navy gradient, amber accents, the logo
/// mark, Work Sans type — with a fully managed rasterizer (<see cref="Canvas"/>, <see cref="TrueTypeFont"/>): no native
/// libraries or system fonts, so it renders identically in the Alpine Docker image. Rendered cards are kept in a
/// size-bounded memory cache keyed by their version. Singleton (thread-safe).
/// </summary>
public sealed class SocialCardRenderer
{
    public const int Width = 1200;
    public const int Height = 630;

    /// <summary>Bump when the card design changes: every card URL (…?v=) changes with it, so caches refresh.</summary>
    public const string DesignVersion = "2026-09-a";

    private static readonly Rgba Navy = Rgba.Hex("#1f2659");
    private static readonly Rgba NavyDeep = Rgba.Hex("#0e1233");
    private static readonly Rgba Amber = Rgba.Hex("#fcb31e");
    private static readonly Rgba Pale = Rgba.Hex("#c9cff5");
    private static readonly Rgba White = Rgba.Hex("#ffffff");

    private readonly Lazy<TrueTypeFont> _bold = new(() => TrueTypeFont.Embedded("WorkSans-Bold.ttf"));
    private readonly Lazy<TrueTypeFont> _regular = new(() => TrueTypeFont.Embedded("WorkSans-Regular.ttf"));
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 48L * 1024 * 1024 });

    /// <summary>The card as PNG bytes (cached by version).</summary>
    public byte[] Render(SocialCard card, string siteName, string host)
    {
        var version = card.Version(siteName, host);
        if (_cache.TryGetValue(version, out byte[]? cached) && cached is not null) return cached;
        var png = Draw(card, siteName, host).ToPng();
        _cache.Set(version, png, new MemoryCacheEntryOptions { Size = png.Length, SlidingExpiration = TimeSpan.FromHours(12) });
        return png;
    }

    public Canvas Draw(SocialCard card, string siteName, string host)
    {
        var bold = _bold.Value;
        var regular = _regular.Value;
        var c = new Canvas(Width, Height);
        c.DiagonalGradient(Navy, NavyDeep);
        c.Glow(1060, 40, 520, Amber.WithAlpha(0.13f));
        c.Glow(120, 640, 560, Rgba.Hex("#3a4bc4", 0.22f));

        // The logo's two rings, large and faint, as a backdrop on the right.
        var rings = new VectorPath();
        rings.Ring(1085, 138, 190, 46);
        c.Fill(rings, Amber.WithAlpha(0.16f));
        var pale = new VectorPath();
        pale.Ring(930, 10, 140, 36);
        c.Fill(pale, Pale.WithAlpha(0.07f));

        // Header: logo mark + site name.
        const float left = 80;
        DrawLogo(c, left, 58, 60);
        TextLayout.Draw(c, bold, 34, left + 78, 100, siteName, White);

        // Bottom: an amber rule, the facts and the host.
        var bar = new VectorPath();
        bar.Rect(0, Height - 12, Width, 12);
        c.Fill(bar, Amber);
        const float footerBaseline = Height - 52;
        var hostWidth = TextLayout.Measure(bold, 24, host);
        TextLayout.Draw(c, bold, 24, Width - left - hostWidth, footerBaseline, host, Pale);
        var facts = (card.Facts ?? Array.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        if (facts.Count > 0)
        {
            var x = left;
            var maxX = Width - left - hostWidth - 40;
            for (var i = 0; i < facts.Count; i++)
            {
                var text = facts[i].Trim();
                var w = TextLayout.Measure(regular, 26, text);
                if (x + w > maxX) break;
                if (i > 0)
                {
                    var dot = new VectorPath();
                    dot.Circle(x + 10, footerBaseline - 9, 4);
                    c.Fill(dot, Amber);
                    x += 28;
                }
                TextLayout.Draw(c, regular, 26, x, footerBaseline, text, White);
                x += w + 8;
            }
        }

        // Eyebrow, title and subtitle.
        const float contentWidth = Width - 2 * left;
        var y = 214f;
        if (!string.IsNullOrWhiteSpace(card.Eyebrow))
        {
            var eyebrow = TextLayout.Wrap(bold, 24, card.Eyebrow.ToUpperInvariant(), contentWidth, 1, 2.4f)[0];
            var accent = new VectorPath();
            accent.RoundedRect(left, y - 19, 36, 6, 3);
            c.Fill(accent, Amber);
            TextLayout.Draw(c, bold, 24, left + 50, y, eyebrow, Amber, 2.4f);
            y += 30;
        }
        const float footerTop = Height - 110;
        var (size, lines) = FitTitle(bold, card.Title, contentWidth, footerTop - y);
        var lineHeight = size * 1.12f;
        y += size * 0.98f;
        foreach (var line in lines)
        {
            TextLayout.Draw(c, bold, size, left, y, line, White);
            y += lineHeight;
        }
        if (!string.IsNullOrWhiteSpace(card.Subtitle))
        {
            const float subSize = 28;
            var room = (int)((footerTop - (y - lineHeight + size * 0.3f + 22)) / (subSize * 1.35f));
            if (room > 0)
            {
                var subY = y - lineHeight + size * 0.3f + 22 + subSize;
                foreach (var line in TextLayout.Wrap(regular, subSize, card.Subtitle, contentWidth - 60, Math.Min(2, room)))
                {
                    TextLayout.Draw(c, regular, subSize, left, subY, line, Pale);
                    subY += subSize * 1.35f;
                }
            }
        }
        return c;
    }

    /// <summary>The largest title size (72 → 42 px) at which the title fits the space; the smallest one ellipsizes.</summary>
    private static (float Size, IReadOnlyList<string> Lines) FitTitle(TrueTypeFont font, string title, float width, float height)
    {
        float[] sizes = { 72, 64, 58, 52, 46, 42 };
        foreach (var size in sizes)
        {
            var maxLines = Math.Max(1, (int)((height - size * 0.3f) / (size * 1.12f)));
            var lines = TextLayout.Wrap(font, size, title, width, int.MaxValue);
            if (lines.Count <= Math.Min(maxLines, 4)) return (size, Balance(font, size, title, width, lines));
        }
        var last = sizes[^1];
        var fit = Math.Clamp((int)((height - last * 0.3f) / (last * 1.12f)), 1, 4);
        return (last, TextLayout.Wrap(font, last, title, width, fit));
    }

    /// <summary>
    /// The same number of lines at the narrowest width that still needs no more of them, so a title does not end with a
    /// lone short word ("Getting started on Optimize / All" becomes "Getting started on / Optimize All").
    /// </summary>
    private static IReadOnlyList<string> Balance(TrueTypeFont font, float size, string title, float width, IReadOnlyList<string> lines)
    {
        if (lines.Count < 2) return lines;
        float lo = width * 0.4f, hi = width;
        var best = lines;
        for (var i = 0; i < 12; i++)
        {
            var mid = (lo + hi) / 2;
            var attempt = TextLayout.Wrap(font, size, title, mid, int.MaxValue);
            if (attempt.Count <= lines.Count) { best = attempt; hi = mid; }
            else lo = mid;
        }
        return best;
    }

    /// <summary>The logo mark (frontend/public/favicon.svg): amber ring, pale ring and the amber arc over their overlap.</summary>
    private static void DrawLogo(Canvas c, float x, float y, float size)
    {
        var s = size / 88f;
        float X(float v) => x + (v - 8) * s;
        float Y(float v) => y + (v - 4) * s;
        var amber = new VectorPath();
        amber.Ring(X(56), Y(52), 30 * s, 12 * s);
        c.Fill(amber, Amber);
        var pale = new VectorPath();
        pale.Ring(X(38), Y(34), 22 * s, 11 * s);
        c.Fill(pale, Pale);
        var arc = new VectorPath();
        arc.ArcBand(X(56), Y(52), 30 * s, 12 * s, 200 * MathF.PI / 180, 160 * MathF.PI / 180);
        c.Fill(arc, Amber);
    }
}

/// <summary>Measuring, wrapping and drawing a line of text with a <see cref="TrueTypeFont"/> (with pair kerning).</summary>
public static class TextLayout
{
    private static IEnumerable<int> CodePoints(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else yield return text[i];
        }
    }

    /// <summary>Glyphs of the text, with characters the font lacks (emoji, other scripts) left out.</summary>
    private static List<int> Glyphs(TrueTypeFont font, string text) =>
        CodePoints(text).Where(cp => font.HasGlyph(cp) || cp == ' ').Select(font.GlyphIndex).ToList();

    public static float Measure(TrueTypeFont font, float size, string text, float letterSpacing = 0)
    {
        var glyphs = Glyphs(font, text);
        var scale = size / font.UnitsPerEm;
        float w = 0;
        for (var i = 0; i < glyphs.Count; i++)
        {
            w += font.AdvanceWidth(glyphs[i]) * scale;
            if (i + 1 < glyphs.Count) w += font.Kerning(glyphs[i], glyphs[i + 1]) * scale + letterSpacing;
        }
        return w;
    }

    /// <summary>
    /// Greedy word wrap into at most <paramref name="maxLines"/> lines; a word wider than a line is broken, and text that
    /// does not fit ends with an ellipsis.
    /// </summary>
    public static IReadOnlyList<string> Wrap(TrueTypeFont font, float size, string text, float width, int maxLines, float letterSpacing = 0)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;
        var overflow = false;
        for (var wi = 0; wi < words.Length; wi++)
        {
            var word = words[wi];
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (Measure(font, size, candidate, letterSpacing) <= width) { current = candidate; continue; }
            if (current.Length > 0)
            {
                lines.Add(current);
                current = string.Empty;
                if (lines.Count == maxLines) { overflow = true; break; }
                wi--; // retry the word on the new line
                continue;
            }
            // A single word wider than the line: break it.
            var cut = word.Length;
            while (cut > 1 && Measure(font, size, word[..cut], letterSpacing) > width) cut--;
            lines.Add(word[..cut]);
            if (lines.Count == maxLines) { overflow = cut < word.Length || wi < words.Length - 1; break; }
            words[wi] = word[cut..];
            wi--;
        }
        if (!overflow && current.Length > 0)
        {
            if (lines.Count < maxLines) lines.Add(current);
            else overflow = true;
        }
        if (overflow && lines.Count > 0)
        {
            var last = lines[^1].TrimEnd(',', ';', ':', '—', '-', ' ', '.');
            while (last.Length > 0 && Measure(font, size, last + "…", letterSpacing) > width)
            {
                var space = last.LastIndexOf(' ');
                last = space > 0 ? last[..space].TrimEnd(',', ';', ':', '—', '-', ' ', '.') : last[..^1];
            }
            lines[^1] = last + "…";
        }
        if (lines.Count == 0) lines.Add(string.Empty);
        return lines;
    }

    /// <summary>Draws one line of text with its baseline at <paramref name="baseline"/>.</summary>
    public static void Draw(Canvas canvas, TrueTypeFont font, float size, float x, float baseline, string text, Rgba color, float letterSpacing = 0)
    {
        var glyphs = Glyphs(font, text);
        var scale = size / font.UnitsPerEm;
        var path = new VectorPath();
        var pen = x;
        for (var i = 0; i < glyphs.Count; i++)
        {
            AppendGlyph(path, font.Outline(glyphs[i]), pen, baseline, scale);
            pen += font.AdvanceWidth(glyphs[i]) * scale;
            if (i + 1 < glyphs.Count) pen += font.Kerning(glyphs[i], glyphs[i + 1]) * scale + letterSpacing;
        }
        canvas.Fill(path, color);
    }

    private static void AppendGlyph(VectorPath path, IReadOnlyList<TrueTypeFont.GlyphContour> contours, float ox, float baseline, float scale)
    {
        foreach (var contour in contours)
        {
            var pts = contour.Points;
            var n = pts.Count;
            if (n < 2) continue;
            (float X, float Y) P(int i) => (ox + pts[i].X * scale, baseline - pts[i].Y * scale);
            (float X, float Y) start;
            int first, count;
            if (pts[0].OnCurve) { start = P(0); first = 1; count = n - 1; }
            else if (pts[n - 1].OnCurve) { start = P(n - 1); first = 0; count = n - 1; }
            else
            {
                var (ax, ay) = P(n - 1);
                var (bx, by) = P(0);
                start = ((ax + bx) / 2, (ay + by) / 2);
                first = 0;
                count = n;
            }
            path.MoveTo(start.X, start.Y);
            (float X, float Y)? control = null;
            for (var k = 0; k < count; k++)
            {
                var i = (first + k) % n;
                var q = P(i);
                if (pts[i].OnCurve)
                {
                    if (control is { } ctl) path.QuadTo(ctl.X, ctl.Y, q.X, q.Y);
                    else path.LineTo(q.X, q.Y);
                    control = null;
                }
                else
                {
                    if (control is { } ctl)
                    {
                        var mid = ((ctl.X + q.X) / 2, (ctl.Y + q.Y) / 2);
                        path.QuadTo(ctl.X, ctl.Y, mid.Item1, mid.Item2);
                    }
                    control = q;
                }
            }
            if (control is { } last) path.QuadTo(last.X, last.Y, start.X, start.Y);
            path.Close();
        }
    }
}

/// <summary>Fact helpers shared by card builders.</summary>
public static class SocialCardText
{
    public static string Count(int n, string singular, string plural) =>
        n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? singular : plural);
}
