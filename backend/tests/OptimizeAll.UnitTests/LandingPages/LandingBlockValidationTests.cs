using System.Text.Json;
using OptimizeAll.Api.Modules.LandingPages;
using OptimizeAll.Api.Modules.LandingPages.Templates;
using OptimizeAll.Domain.LandingPages;

namespace OptimizeAll.UnitTests.LandingPages;

public sealed class LandingBlockValidationTests
{
    private static readonly Guid FormId = Guid.NewGuid();

    private static readonly BlockValidationContext Context = new(
        url => url.StartsWith("/api/v1/files/", StringComparison.Ordinal) || url.StartsWith("https://cdn.allowed.test/", StringComparison.Ordinal),
        id => id == FormId);

    private static IReadOnlyList<LandingBlock> Parse(object blocks) =>
        LandingBlocks.ParseBlocks(JsonSerializer.SerializeToElement(blocks), Context);

    private static LandingValidationException Rejects(object blocks) =>
        Assert.Throws<LandingValidationException>(() => Parse(blocks));

    [Fact]
    public void Every_block_type_accepts_valid_props()
    {
        var blocks = Parse(new object[]
        {
            new { id = "hero", type = "hero", props = new { headline = "Hello", ctaLabel = "Go", ctaHref = "#form", imageUrl = "/api/v1/files/" + Guid.NewGuid(), align = "left", theme = "dark" } },
            new { id = "t", type = "text", props = new { heading = "About", body = "Plain text." } },
            new { id = "i", type = "image", props = new { url = "https://cdn.allowed.test/a.png", alt = "Team photo" } },
            new { id = "v", type = "video", props = new { url = "https://youtu.be/dQw4w9WgXcQ", title = "Demo video" } },
            new { id = "f", type = "features", props = new { items = new[] { new { title = "Fast" } } } },
            new { id = "q", type = "testimonials", props = new { items = new[] { new { quote = "Great", author = "Ann", rating = 5 } } } },
            new { id = "p", type = "pricing", props = new { plans = new[] { new { name = "Pro", price = "$9", features = new[] { "All" }, ctaLabel = "Buy", ctaHref = "https://shop.test/buy" } } } },
            new { id = "faq", type = "faq", props = new { items = new[] { new { question = "Why?", answer = "Because." } } } },
            new { id = "c", type = "countdown", props = new { endsAt = "2030-01-01T10:00:00Z" } },
            new { id = "form", type = "form", props = new { formId = FormId } },
            new { id = "cta", type = "cta", props = new { heading = "Ready?", buttonLabel = "Call", buttonHref = "tel:+44 20 7946 0000" } },
            new { id = "l", type = "logos", props = new { items = new[] { new { name = "Acme", imageUrl = "https://cdn.allowed.test/acme.svg" } } } },
            new { id = "s", type = "spacer", props = new { size = "lg" } },
        });
        Assert.Equal(13, blocks.Count);
        var video = (VideoProps)blocks.Single(b => b.Type == "video").Props;
        Assert.Equal(("youtube", "dQw4w9WgXcQ"), (video.Provider, video.VideoId));
        Assert.Null(video.Url);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Hello <SCRIPT src=//evil.test/x.js></SCRIPT>")]
    [InlineData("<iframe src=\"https://evil.test\"></iframe>")]
    [InlineData("<object data=x>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("java\nscript:alert(1)")]
    public void Script_and_frame_markup_is_rejected_in_any_text(string payload)
    {
        var ex = Rejects(new object[] { new { id = "t", type = "text", props = new { body = payload } } });
        Assert.Contains("blocks[0].props.body", ex.Errors.Keys);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("vbscript:msgbox")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("//evil.test/path")]
    [InlineData("/\\evil.test")]
    [InlineData("ftp://files.test/a")]
    [InlineData("https://user:pw@site.test/")]
    public void Unsafe_links_are_rejected(string href)
    {
        var ex = Rejects(new object[] { new { id = "cta", type = "cta", props = new { heading = "Go", buttonLabel = "Click", buttonHref = href } } });
        Assert.Contains(ex.Errors.Keys, k => k.StartsWith("blocks[0].props", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://evil.test/embed/video")]
    [InlineData("https://www.youtube.com.evil.test/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://vimeo.com/not-a-number")]
    public void Videos_must_be_youtube_or_vimeo(string url)
    {
        var ex = Rejects(new object[] { new { id = "v", type = "video", props = new { url, title = "Video" } } });
        Assert.Contains("blocks[0].props.url", ex.Errors.Keys);
    }

    [Fact]
    public void Raw_embed_html_cannot_be_smuggled_through_unknown_props_or_types()
    {
        Rejects(new object[] { new { id = "x", type = "html", props = new { html = "<p>hi</p>" } } });
        var unknownProp = Rejects(new object[] { new { id = "v", type = "video", props = new { url = "https://vimeo.com/123456789", title = "V", embedHtml = "<div>" } } });
        Assert.Contains(unknownProp.Errors.Keys, k => k.StartsWith("blocks[0].props", StringComparison.Ordinal));
    }

    [Fact]
    public void Images_must_follow_the_image_policy_and_need_alt_text()
    {
        Assert.Contains("blocks[0].props.url", Rejects(new object[] { new { id = "i", type = "image", props = new { url = "https://random-host.test/a.png", alt = "x" } } }).Errors.Keys);
        Assert.Contains("blocks[0].props.alt", Rejects(new object[] { new { id = "i", type = "image", props = new { url = "https://cdn.allowed.test/a.png" } } }).Errors.Keys);
        Parse(new object[] { new { id = "i", type = "image", props = new { url = "https://cdn.allowed.test/a.png", decorative = true } } });
    }

    [Fact]
    public void Forms_must_belong_to_the_client_and_ids_must_be_unique()
    {
        Assert.Contains("blocks[0].props.formId", Rejects(new object[] { new { id = "f", type = "form", props = new { formId = Guid.NewGuid() } } }).Errors.Keys);
        var dup = Rejects(new object[]
        {
            new { id = "a", type = "spacer", props = new { size = "sm" } },
            new { id = "a", type = "spacer", props = new { size = "sm" } },
        });
        Assert.Contains("blocks[1].id", dup.Errors.Keys);
    }

    [Fact]
    public void Variants_require_control_A_and_unique_keys()
    {
        var blocks = new object[] { new { id = "s", type = "spacer", props = new { size = "sm" } } };
        Assert.Throws<LandingValidationException>(() => LandingBlocks.ParseVariants(JsonSerializer.SerializeToElement(new[] { new { key = "B", name = "B", weight = 50, blocks } }), Context));
        Assert.Throws<LandingValidationException>(() => LandingBlocks.ParseVariants(JsonSerializer.SerializeToElement(new[]
        {
            new { key = "A", name = "A", weight = 50, blocks }, new { key = "A", name = "A2", weight = 50, blocks },
        }), Context));
        var ok = LandingBlocks.ParseVariants(JsonSerializer.SerializeToElement(new[]
        {
            new { key = "B", name = "Challenger", weight = 30, blocks }, new { key = "A", name = "Control", weight = 70, blocks },
        }), Context);
        Assert.Equal(new[] { "A", "B" }, ok.Select(v => v.Key));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "youtube", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/shorts/dQw4w9WgXcQ", "youtube", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", "youtube", "dQw4w9WgXcQ")]
    [InlineData("https://vimeo.com/76979871", "vimeo", "76979871")]
    [InlineData("https://player.vimeo.com/video/76979871", "vimeo", "76979871")]
    public void Video_urls_resolve_to_provider_and_id(string url, string provider, string id) =>
        Assert.Equal((provider, id), VideoEmbed.Resolve(url, null, null));

    [Fact]
    public void Every_seeded_landing_template_is_valid_content()
    {
        foreach (var template in TemplateCatalog.Pages)
        {
            var variants = LandingPageService.InstantiateTemplate(template.BlocksJson, FormId, DateTime.UtcNow);
            var parsed = LandingBlocks.ParseVariants(variants, Context);
            Assert.True(parsed[0].Blocks.Count >= 3, template.Key);
            Assert.Contains(parsed[0].Blocks, b => b.Type == "form");
        }
        Assert.Equal(6, TemplateCatalog.Pages.Count);
    }
}
