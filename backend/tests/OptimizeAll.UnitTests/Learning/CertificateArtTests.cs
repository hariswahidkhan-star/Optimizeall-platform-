using System.Globalization;
using System.Xml.Linq;
using OptimizeAll.Api.Modules.Learning;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.UnitTests.Learning;

/// <summary>
/// Badge artwork layout: every text of the 400×400 emblem (issuer, badge name, level) must stay inside the inner hairline
/// hexagon (radius 156, pointy top), whatever the badge name, level or issuer length.
/// </summary>
public sealed class CertificateArtTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static readonly string[] LongNames =
    {
        "Certified Multimodal & Reasoning AI Practitioner",
        "Certified Digital PR & Brand Authority Specialist",
        "Certified AI Search Optimization Specialist",
        "Certified Digital Marketer – Foundations",
        "Entrepreneurship & Business Models",
        "Supercalifragilisticexpialidocious Internationalization Wizard Extraordinaire Grandmaster of Everything Else",
        "X",
    };

    /// <summary>Half-width of the hairline hexagon at height <paramref name="y"/> (0 outside it).</summary>
    private static double HairlineHalfWidth(double y)
    {
        const double r = 156, cy = 200;
        var flat = r * Math.Sqrt(3) / 2; // 135.1
        var top = cy - r / 2; // 122: the straight sides span 122–278
        var bottom = cy + r / 2;
        if (y < cy - r || y > cy + r) return 0;
        if (y >= top && y <= bottom) return flat;
        return y < top ? flat * (y - (cy - r)) / (r / 2) : flat * (cy + r - y) / (r / 2);
    }

    private static double Attr(XElement e, string name, double fallback = 0) =>
        e.Attribute(name) is { } a ? double.Parse(a.Value, CultureInfo.InvariantCulture) : fallback;

    public static IEnumerable<object[]> BadgeCases()
    {
        var names = CoursePackLibrary.All.Where(f => f.Pack?.Badge is not null).Select(f => f.Pack!.Badge!.Name).Concat(LongNames).Distinct();
        foreach (var name in names)
            yield return new object[] { name };
    }

    [Theory]
    [MemberData(nameof(BadgeCases))]
    public void Every_badge_text_stays_inside_the_hexagon(string badgeName)
    {
        foreach (var level in Enum.GetValues<CourseLevel>())
        {
            var svg = CertificateArt.BadgeSvg(badgeName, CourseCategory.Ai, level, "Optimize All Academy");
            var texts = XDocument.Parse(svg).Descendants(Svg + "text").ToList();
            Assert.True(texts.Count >= 3, "issuer, name and level texts");
            foreach (var t in texts)
            {
                var size = Attr(t, "font-size");
                var baseline = Attr(t, "y");
                var width = CertificateArt.MeasureCaps(t.Value, size, Attr(t, "letter-spacing"));
                Assert.Equal(200, Attr(t, "x"));
                Assert.Equal("middle", t.Attribute("text-anchor")?.Value);
                // Cap top and baseline both keep a margin of at least 6 units from the hairline.
                foreach (var y in new[] { baseline - size * 0.72, baseline })
                    Assert.True(width / 2 + 6 <= HairlineHalfWidth(y),
                        $"\"{t.Value}\" ({width:0.#} wide at y {y:0.#}) crosses the hexagon edge (half-width {HairlineHalfWidth(y):0.#})");
            }
        }
    }

    [Fact]
    public void The_issuer_line_sits_in_the_straight_band_above_the_name_and_long_issuers_are_shrunk_or_cut()
    {
        foreach (var issuer in new[] { "Optimize All Academy", "The Very Long Name Of An Issuing Organisation International" })
        {
            var svg = CertificateArt.BadgeSvg("Certified Creator", CourseCategory.Platform, CourseLevel.Beginner, issuer);
            var texts = XDocument.Parse(svg).Descendants(Svg + "text").ToList();
            var issuerText = texts[0];
            var size = Attr(issuerText, "font-size");
            Assert.InRange(Attr(issuerText, "y") - size * 0.72, 122, 150);
            Assert.True(CertificateArt.MeasureCaps(issuerText.Value, size, Attr(issuerText, "letter-spacing")) <= CertificateArt.IssuerMaxWidth);
            // The name comes below the issuer; the level ribbon below the name.
            Assert.True(Attr(texts[1], "y") > Attr(issuerText, "y") + 20);
            Assert.Equal("BEGINNER", texts[^1].Value);
        }
    }

    [Theory]
    [MemberData(nameof(BadgeCases))]
    public void Badge_names_use_balanced_lines_at_a_readable_size(string badgeName)
    {
        var layout = CertificateArt.LayoutBadgeName(badgeName);
        Assert.InRange(layout.Lines.Count, 1, 4);
        Assert.InRange(layout.FontSize, CertificateArt.BadgeNameMinSize, CertificateArt.BadgeNameMaxSize);
        Assert.True(CertificateArt.BadgeNameBlockHeight(layout.Lines.Count, layout.FontSize) <= CertificateArt.BadgeNameMaxHeight);
        Assert.All(layout.Lines, l => Assert.True(CertificateArt.MeasureCaps(l, layout.FontSize, 0.6) <= CertificateArt.BadgeNameMaxWidth, l));
        Assert.All(layout.Lines.Skip(1), l => Assert.False(l.StartsWith('–'), "no line starts with a dash"));
    }

    [Fact]
    public void Every_course_pack_badge_name_is_shown_in_full_and_not_tiny()
    {
        foreach (var name in CoursePackLibrary.All.Where(f => f.Pack?.Badge is not null).Select(f => f.Pack!.Badge!.Name))
        {
            var layout = CertificateArt.LayoutBadgeName(name);
            Assert.Equal(string.Join(' ', name.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)), string.Join(' ', layout.Lines));
            Assert.True(layout.FontSize >= 18, $"{name}: {layout.FontSize}");
        }
    }

    [Fact]
    public void Short_names_get_the_largest_type_and_overlong_words_are_cut_with_an_ellipsis()
    {
        var shortName = CertificateArt.LayoutBadgeName("SEO Pro");
        Assert.Single(shortName.Lines);
        Assert.Equal(CertificateArt.BadgeNameMaxSize, shortName.FontSize);

        var overlong = CertificateArt.LayoutBadgeName(LongNames[5]);
        Assert.Contains(overlong.Lines, l => l.EndsWith('…'));
    }

    [Fact]
    public void Badge_svg_is_deterministic_valid_and_readable_on_light_accents()
    {
        var a = CertificateArt.BadgeSvg("Optimize All Certified Creator", CourseCategory.Platform, CourseLevel.Beginner, "Optimize All Academy");
        var b = CertificateArt.BadgeSvg("Optimize All Certified Creator", CourseCategory.Platform, CourseLevel.Beginner, "Optimize All Academy");
        Assert.Equal(a, b);
        var root = XDocument.Parse(a).Root!;
        Assert.Equal("0 0 400 400", root.Attribute("viewBox")?.Value);
        Assert.Equal("400", root.Attribute("width")?.Value);
        Assert.DoesNotContain("<script", a);
        Assert.DoesNotContain("href", a); // no external references
        // Level text on the amber ribbon is navy (white on amber is unreadable); on dark accents it is white.
        var level = root.Descendants(Svg + "text").Last();
        Assert.Equal(CertificateArt.Navy, level.Attribute("fill")?.Value);
        var ai = XDocument.Parse(CertificateArt.BadgeSvg("X", CourseCategory.Ai, CourseLevel.Advanced, "Academy")).Root!;
        Assert.Equal("#FFFFFF", ai.Descendants(Svg + "text").Last().Attribute("fill")?.Value);
    }
}
