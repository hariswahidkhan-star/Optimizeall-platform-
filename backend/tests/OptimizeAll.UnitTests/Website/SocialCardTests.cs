using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using OptimizeAll.Api.Modules.Website.SiteSeo;
using OptimizeAll.Api.Modules.Website.SiteSeo.SocialCards;
using Xunit.Abstractions;

namespace OptimizeAll.UnitTests.Website;

/// <summary>The managed social-card renderer (SiteSeo/SocialCards): fonts, rasterizer, PNG output and card derivation.</summary>
public sealed class SocialCardTests(ITestOutputHelper output)
{
    private static readonly SocialCardRenderer Renderer = new();

    /// <summary>Decodes an 8-bit RGB PNG (as written by <see cref="Canvas.ToPng"/>) to check its size and pixels.</summary>
    private static (int Width, int Height, byte[] Rgb) DecodePng(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        int width = 0, height = 0;
        using var idat = new MemoryStream();
        var p = 8;
        while (p < png.Length)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(p));
            var type = System.Text.Encoding.ASCII.GetString(png, p + 4, 4);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(p + 8));
                height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(p + 12));
                Assert.Equal(8, png[p + 16]);
                Assert.Equal(2, png[p + 17]);
            }
            if (type == "IDAT") idat.Write(png, p + 8, len);
            p += 12 + len;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var data = raw.ToArray();
        var stride = width * 3;
        var rgb = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = data[y * (stride + 1)];
            for (var i = 0; i < stride; i++)
            {
                var v = data[y * (stride + 1) + 1 + i];
                int left = i >= 3 ? rgb[y * stride + i - 3] : 0, up = y > 0 ? rgb[(y - 1) * stride + i] : 0,
                    upLeft = i >= 3 && y > 0 ? rgb[(y - 1) * stride + i - 3] : 0;
                int pred = filter switch
                {
                    0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2,
                    _ => Math.Abs(up - upLeft) <= Math.Abs(left - upLeft) && Math.Abs(up - upLeft) <= Math.Abs(left + up - 2 * upLeft) ? left
                        : Math.Abs(left - upLeft) <= Math.Abs(left + up - 2 * upLeft) ? up : upLeft,
                };
                rgb[y * stride + i] = (byte)(v + pred);
            }
        }
        return (width, height, rgb);
    }

    [Fact]
    public void A_card_is_a_valid_1200_by_630_png_in_the_brand_colours()
    {
        var card = new SocialCard("Academy · SEO", "Technical SEO fundamentals for growing websites", "Crawling, indexing and site speed explained.",
            new[] { "Free course", "12 lessons", "Certificate" });
        var sw = Stopwatch.StartNew();
        var png = Renderer.Render(card, "Optimize All", "optimizeall.com");
        output.WriteLine($"first render {sw.ElapsedMilliseconds} ms, {png.Length} bytes");
        sw.Restart();
        var drawn = Renderer.Draw(card with { Title = "Another title to avoid the cache" }, "Optimize All", "optimizeall.com");
        output.WriteLine($"second draw {sw.ElapsedMilliseconds} ms");
        sw.Restart();
        drawn.ToPng();
        output.WriteLine($"second encode {sw.ElapsedMilliseconds} ms");
        var (w, h, rgb) = DecodePng(png);
        Assert.Equal((1200, 630), (w, h));
        Assert.InRange(png.Length, 10_000, 400_000);
        byte[] Px(int x, int y) { var i = (y * w + x) * 3; return rgb[i..(i + 3)]; }
        // Amber rule along the bottom; navy background in the top-left corner.
        Assert.Equal(new byte[] { 0xfc, 0xb3, 0x1e }, Px(600, 625));
        var corner = Px(2, 2);
        Assert.True(corner[2] > corner[0] && corner[0] < 60, "the background is navy");
        // Some white title pixels exist in the title band.
        var white = 0;
        for (var y = 230; y < 420; y++) for (var x = 80; x < 1100; x++) if (Px(x, y).All(c => c > 240)) white++;
        Assert.True(white > 5_000, $"expected title text, found {white} white pixels");
        // The same card renders from the cache (same bytes).
        Assert.Same(png, Renderer.Render(card, "Optimize All", "optimizeall.com"));
        if (Environment.GetEnvironmentVariable("OA_CARD_OUT") is { Length: > 0 } dir) File.WriteAllBytes(Path.Combine(dir, "card-test.png"), png);
    }

    [Fact]
    public void The_version_changes_with_the_words_and_the_host()
    {
        var card = new SocialCard("Blog", "Title");
        Assert.Equal(card.Version("Optimize All", "a.com"), new SocialCard("Blog", "Title").Version("Optimize All", "a.com"));
        Assert.NotEqual(card.Version("Optimize All", "a.com"), card.Version("Optimize All", "b.com"));
        Assert.NotEqual(card.Version("Optimize All", "a.com"), (card with { Title = "Other" }).Version("Optimize All", "a.com"));
        Assert.Matches("^[0-9a-f]{12}$", card.Version("Optimize All", "a.com"));
    }

    [Fact]
    public void Fonts_map_characters_measure_and_kern()
    {
        var font = TrueTypeFont.Embedded("WorkSans-Bold.ttf");
        Assert.True(font.UnitsPerEm > 0);
        Assert.NotEqual(0, font.GlyphIndex('A'));
        Assert.NotEqual(0, font.GlyphIndex('é'));
        Assert.Equal(0, font.GlyphIndex(0x1F600)); // emoji: not in the font
        Assert.NotEmpty(font.Outline(font.GlyphIndex('é'))); // a composite glyph (e + acute)
        Assert.True(TextLayout.Measure(font, 40, "WWW") > TextLayout.Measure(font, 40, "iii"));
        // "AV" is kerned tighter than the sum of its letters.
        var sum = TextLayout.Measure(font, 100, "A") + TextLayout.Measure(font, 100, "V");
        Assert.True(TextLayout.Measure(font, 100, "AV") < sum, "GPOS pair kerning applies to AV");
    }

    [Fact]
    public void Long_titles_wrap_and_end_with_an_ellipsis()
    {
        var font = TrueTypeFont.Embedded("WorkSans-Bold.ttf");
        var text = string.Join(' ', Enumerable.Repeat("optimization", 30));
        var lines = TextLayout.Wrap(font, 60, text, 1040, 3);
        Assert.Equal(3, lines.Count);
        Assert.EndsWith("…", lines[^1]);
        Assert.All(lines, l => Assert.True(TextLayout.Measure(font, 60, l) <= 1040));
        Assert.Equal(new[] { "Short" }, TextLayout.Wrap(font, 60, "Short", 1040, 3));
        // A single word longer than a line is broken rather than overflowing.
        Assert.All(TextLayout.Wrap(font, 60, new string('W', 60), 500, 5), l => Assert.True(TextLayout.Measure(font, 60, l) <= 500));
        // Emoji and other characters the font lacks are skipped, not drawn as boxes.
        var canvas = new Canvas(200, 80);
        TextLayout.Draw(canvas, font, 40, 5, 60, "Hi 😀", new Rgba(255, 255, 255));
    }

    [Fact]
    public void The_rasterizer_covers_shapes_exactly_and_clips_at_the_edges()
    {
        var canvas = new Canvas(20, 20);
        var path = new VectorPath();
        path.Rect(-10, 5, 20, 10); // half outside on the left
        canvas.Fill(path, new Rgba(255, 0, 0));
        Assert.Equal(new byte[] { 255, 0, 0 }, canvas.Pixel(0, 10));
        Assert.Equal(new byte[] { 255, 0, 0 }, canvas.Pixel(9, 10));
        Assert.Equal(new byte[] { 0, 0, 0 }, canvas.Pixel(10, 10));
        Assert.Equal(new byte[] { 0, 0, 0 }, canvas.Pixel(5, 4));
        var half = new Canvas(4, 4);
        var p2 = new VectorPath();
        p2.Rect(0, 0, 1.5f, 4);
        half.Fill(p2, new Rgba(200, 200, 200));
        Assert.InRange(half.Pixel(1, 2)[0], 95, 105); // half-covered pixel
        // A ring leaves its middle empty.
        var ring = new Canvas(100, 100);
        var r = new VectorPath();
        r.Ring(50, 50, 30, 10);
        ring.Fill(r, new Rgba(255, 255, 255));
        Assert.Equal(new byte[] { 0, 0, 0 }, ring.Pixel(50, 50));
        Assert.Equal(new byte[] { 255, 255, 255 }, ring.Pixel(80, 50));
    }

    private static SeoPage Page(string path, string source, params ContentNode[] content)
    {
        var page = new SeoPage { Path = path, Title = "Page title | Optimize All", Description = "A description of the page.", Source = source };
        page.Content.AddRange(content);
        return page;
    }

    [Fact]
    public void Cards_are_derived_from_what_the_page_renders()
    {
        var course = Page("/learn/seo", "Course", new ParagraphNode("SEO · Beginner · 90 minutes · free"), new HeadingNode(1, "SEO basics"),
            new LinkListNode(new[] { new LinkItem("One", "/learn/seo/one"), new LinkItem("Two", "/learn/seo/two") }));
        course.Section = "SEO";
        var c = SocialCardFactory.From(course, "Optimize All");
        Assert.Equal("Academy · SEO", c.Eyebrow);
        Assert.Equal("SEO basics", c.Title);
        Assert.Equal(new[] { "Free course", "2 lessons", "Beginner", "90 minutes", "Certificate" }, c.Facts);

        var lesson = Page("/learn/seo/one", "Lesson", new ParagraphNode("SEO basics · Module 1 · lesson 1 of 2 · 8 min"), new HeadingNode(1, "One"));
        lesson.Breadcrumbs.AddRange(new[] { new Crumb("Home", "/"), new Crumb("Academy", "/learn"), new Crumb("SEO basics", "/learn/seo"), new Crumb("One", "/learn/seo/one") });
        var l = SocialCardFactory.From(lesson, "Optimize All");
        Assert.Equal("Lesson · SEO basics", l.Eyebrow);
        Assert.Equal(new[] { "Lesson 1 of 2", "8 min", "Free" }, l.Facts);

        var post = Page("/blog/x", "Blog post", new HeadingNode(1, "Post"), new ParagraphNode("Jane Doe · 3 May 2026 · 6 min read"));
        post.Section = "SEO";
        var b = SocialCardFactory.From(post, "Optimize All");
        Assert.Equal("Blog · SEO", b.Eyebrow);
        Assert.Equal(new[] { "Jane Doe", "3 May 2026", "6 min read" }, b.Facts);

        var generic = Page("/about", "CMS page");
        generic.Breadcrumbs.AddRange(new[] { new Crumb("Home", "/"), new Crumb("About us", "/about") });
        var g = SocialCardFactory.From(generic, "Optimize All");
        Assert.Equal("Page title", g.Title); // no h1: the title without the site name
        Assert.Equal("About us", g.Eyebrow);
    }

    [Theory]
    [InlineData("/", "/og/index.png")]
    [InlineData("/services/seo", "/og/services/seo.png")]
    public void Card_paths_round_trip(string page, string card)
    {
        Assert.Equal(card, SocialCardFactory.CardPath(page));
        Assert.Equal(page, SocialCardFactory.PagePath(card));
    }

    [Theory]
    [InlineData("/og/")]
    [InlineData("/og/.png")]
    [InlineData("/og/x.jpg")]
    [InlineData("/other/x.png")]
    public void Other_paths_are_not_cards(string path) => Assert.Null(SocialCardFactory.PagePath(path));

    [Fact]
    public void Only_pages_without_a_raster_image_get_a_card()
    {
        var defaults = new[] { "https://x.com/og-default.png" };
        Assert.True(SocialCardFactory.NeedsCard(new SeoPage { Path = "/", OgImage = null }, defaults));
        Assert.True(SocialCardFactory.NeedsCard(new SeoPage { Path = "/", OgImage = "https://x.com/og-default.png" }, defaults));
        Assert.True(SocialCardFactory.NeedsCard(new SeoPage { Path = "/", OgImage = "https://x.com/api/v1/public/learning/courses/a/badge.svg?x=1" }, defaults));
        Assert.False(SocialCardFactory.NeedsCard(new SeoPage { Path = "/", OgImage = "https://x.com/api/v1/files/abc" }, defaults));
    }
}
