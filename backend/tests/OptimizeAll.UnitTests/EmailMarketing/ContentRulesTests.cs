using OptimizeAll.Domain.EmailMarketing;

namespace OptimizeAll.UnitTests.EmailMarketing;

public sealed class MergeTagsTests
{
    private static readonly Dictionary<string, string?> Values = new()
    {
        ["first_name"] = "<script>alert(1)</script>",
        ["last_name"] = null,
        ["email"] = "a+b@example.com",
        ["custom.plan"] = "Pro & Team",
        ["unsubscribe_url"] = "https://app.test/e/u/abc.def",
    };

    [Fact]
    public void Uses_the_fallback_when_the_value_is_missing_or_blank()
    {
        Assert.Equal("Hi friend", MergeTags.Render("Hi {{last_name|friend}}", Values, MergeContext.Text));
        Assert.Equal("Hi ", MergeTags.Render("Hi {{last_name}}", Values, MergeContext.Text));
        Assert.Equal("Plan: Pro & Team", MergeTags.Render("Plan: {{ custom.plan | none }}", Values, MergeContext.Text));
    }

    [Fact]
    public void Html_context_encodes_values_and_fallbacks()
    {
        var html = MergeTags.Render("<p>Hi {{first_name|there}}</p><p>{{last_name|<b>you</b>}}</p>", Values, MergeContext.Html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("&lt;b&gt;you&lt;/b&gt;", html);
    }

    [Fact]
    public void Url_context_percent_encodes_contact_values_but_keeps_system_urls()
    {
        Assert.Equal("https://shop.example/?e=a%2Bb%40example.com", MergeTags.Render("https://shop.example/?e={{email}}", Values, MergeContext.Url));
        Assert.Equal("https://app.test/e/u/abc.def", MergeTags.Render("{{unsubscribe_url}}", Values, MergeContext.Url));
    }

    [Fact]
    public void Only_whitelisted_tags_are_allowed()
    {
        Assert.Empty(MergeTags.Unknown("{{first_name}} {{custom.plan}} {{event.cart_url}} {{org_address}}"));
        Assert.Equal(new[] { "password" }, MergeTags.Unknown("{{password}} {{custom.plan}}").ToArray());
        Assert.True(MergeTags.HasMalformedTags("{{custom.Bad}}"));
        // Unknown tags never leak data even if they reach rendering.
        Assert.Equal("x", MergeTags.Render("x{{password|}}", new Dictionary<string, string?> { ["password"] = "secret" }, MergeContext.Text));
    }

    [Fact]
    public void Detects_malformed_tags()
    {
        Assert.True(MergeTags.HasMalformedTags("Hi {{first_name"));
        Assert.True(MergeTags.HasMalformedTags("Hi first_name}}"));
        Assert.False(MergeTags.HasMalformedTags("Hi {{first_name|there}}"));
    }
}

public sealed class HtmlSanitizerTests
{
    [Theory]
    [InlineData("<p>Hello<script>alert(1)</script></p>", "<p>Hello</p>")]
    [InlineData("<img src=x onerror=alert(1)>Hi", "Hi")]
    [InlineData("<p onclick=\"steal()\" style=\"color:red\">Hi</p>", "<p>Hi</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\"//evil.test\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\"https://ok.test/?a=1&amp;b=2\">x</a>", "<a href=\"https://ok.test/?a=1&amp;b=2\">x</a>")]
    [InlineData("<a href=\"{{unsubscribe_url}}\">Unsubscribe</a>", "<a href=\"{{unsubscribe_url}}\">Unsubscribe</a>")]
    [InlineData("<a href=\"https://{{email}}.evil.test\">x</a>", "<a>x</a>")]
    [InlineData("<strong>bold <em>both</strong>", "<strong>bold <em>both</em></strong>")]
    [InlineData("1 < 2 & 3 > 2", "1 &lt; 2 &amp; 3 &gt; 2")]
    [InlineData("<iframe src=\"https://evil.test\"></iframe>ok", "ok")]
    [InlineData("<!-- hidden --><p>shown</p>", "<p>shown</p>")]
    [InlineData("<svg><script>alert(1)</script></svg>after", "after")]
    [InlineData("<p>unclosed", "<p>unclosed</p>")]
    public void Keeps_only_safe_markup(string input, string expected) => Assert.Equal(expected, HtmlSanitizer.Sanitize(input));

    [Fact]
    public void Converts_rich_text_to_plain_text()
    {
        var text = HtmlSanitizer.ToText("<p>Hi <strong>Ann</strong></p><ul><li>One</li><li>Two</li></ul><p><a href=\"https://x.test/a\">Shop</a></p>");
        Assert.Contains("Hi Ann", text);
        Assert.Contains("- One", text);
        Assert.Contains("Shop (https://x.test/a)", text);
    }
}

public sealed class DesignRulesTests
{
    private static EmailDesign Valid() => new()
    {
        Blocks =
        {
            new DesignBlock { Type = "header", Title = "Hello {{first_name|there}}" },
            new DesignBlock { Type = "text", Html = "<p>Body <a href=\"https://shop.test/a?e={{email}}\">link</a></p>" },
            new DesignBlock { Type = "image", Src = "https://cdn.test/hero.png", Alt = "Hero", Href = "https://shop.test/hero" },
            new DesignBlock { Type = "button", Text = "Shop", Href = "https://shop.test/b" },
            new DesignBlock
            {
                Type = "columns",
                Columns = new List<DesignColumn>
                {
                    new() { Blocks = { new DesignBlock { Type = "text", Html = "<p>Left</p>" } } },
                    new() { Blocks = { new DesignBlock { Type = "button", Text = "Right", Href = "https://shop.test/b" } } },
                },
            },
            new DesignBlock { Type = "social", Links = new List<SocialLink> { new() { Network = "instagram", Url = "https://instagram.com/x" } } },
            new DesignBlock { Type = "footer" },
        },
    };

    [Fact]
    public void A_valid_design_has_no_errors_and_a_footer()
    {
        var design = Valid();
        Assert.Empty(DesignRules.Validate(design));
        Assert.True(DesignRules.HasFooter(design));
        design.Blocks.RemoveAt(design.Blocks.Count - 1);
        Assert.False(DesignRules.HasFooter(design));
    }

    [Fact]
    public void Rejects_unsafe_or_invalid_blocks()
    {
        var design = new EmailDesign
        {
            Settings = new DesignSettings { BackgroundColor = "red;background:url(x)" },
            Blocks =
            {
                new DesignBlock { Type = "image", Src = "http://insecure.test/x.png" },
                new DesignBlock { Type = "button", Text = "x", Href = "javascript:alert(1)" },
                new DesignBlock { Type = "script" },
                new DesignBlock { Type = "columns", Columns = new List<DesignColumn> { new() { Blocks = { new DesignBlock { Type = "footer" } } }, new() } },
                new DesignBlock { Type = "footer" },
                new DesignBlock { Type = "footer" },
            },
        };
        var errors = DesignRules.Validate(design);
        Assert.Contains(errors, e => e.Contains("backgroundColor"));
        Assert.Contains(errors, e => e.Contains("https image URL"));
        Assert.Contains(errors, e => e.Contains("href must be"));
        Assert.Contains(errors, e => e.Contains("unknown block type"));
        Assert.Contains(errors, e => e.Contains("cannot be placed inside columns"));
        Assert.Contains(errors, e => e.Contains("only one footer"));
    }

    [Fact]
    public void Renders_responsive_html_with_footer_unsubscribe_and_plain_text()
    {
        var values = new Dictionary<string, string?>
        {
            ["first_name"] = "Ann", ["email"] = "ann@x.test", ["org_name"] = "Acme", ["org_address"] = "1 Main St", ["unsubscribe_url"] = "https://app.test/e/u/tok",
            ["preferences_url"] = "https://app.test/email/preferences/tok",
        };
        var rendered = EmailRenderer.Render(Valid(), new RenderOptions { Subject = "Hi", PreviewText = "Preview", Values = values });
        Assert.Contains("@media only screen", rendered.Html);
        Assert.Contains("Hello Ann", rendered.Html);
        Assert.Contains("href=\"https://app.test/e/u/tok\"", rendered.Html);
        Assert.Contains("1 Main St", rendered.Html);
        Assert.Contains("https://shop.test/a?e=ann%40x.test", rendered.Html);
        Assert.Contains("Unsubscribe: https://app.test/e/u/tok", rendered.Text);
        Assert.Contains("Shop: https://shop.test/b", rendered.Text);
        Assert.DoesNotContain("{{", rendered.Html);
    }

    [Fact]
    public void Link_rewriting_tracks_content_links_but_not_system_links()
    {
        var links = EmailRenderer.ExtractLinks(Valid());
        Assert.Contains("https://shop.test/a?e={{email}}", links);
        Assert.Contains("https://shop.test/b", links);
        Assert.DoesNotContain(links, l => l.Contains("unsubscribe", StringComparison.Ordinal));
        Assert.Equal(links.Count, links.Distinct().Count());

        var rendered = EmailRenderer.Render(Valid(), new RenderOptions
        {
            Values = new Dictionary<string, string?> { ["unsubscribe_url"] = "https://app.test/e/u/tok" },
            LinkRewriter = url => "https://track.test/c/" + links.ToList().IndexOf(url),
            OpenPixelUrl = "https://track.test/o/1.gif",
        });
        Assert.Contains("https://track.test/c/0", rendered.Html);
        Assert.Contains("https://track.test/o/1.gif", rendered.Html);
        Assert.Contains("href=\"https://app.test/e/u/tok\"", rendered.Html);
        Assert.Contains("https://track.test/c/", rendered.Text);
    }

    [Fact]
    public void Text_blocks_are_sanitized_at_render_time_too()
    {
        var design = new EmailDesign { Blocks = { new DesignBlock { Type = "text", Html = "<p>x</p><script>alert(1)</script><img src=x onerror=y>" }, new DesignBlock { Type = "footer" } } };
        var html = EmailRenderer.Render(design, new RenderOptions()).Html;
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("onerror", html);
    }
}

public sealed class ContentChecksTests
{
    [Fact]
    public void Finds_spam_phrases_and_subject_problems()
    {
        Assert.Contains("act now", ContentChecks.FindSpamPhrases("ACT NOW for a free gift", null));
        Assert.Contains("free gift", ContentChecks.FindSpamPhrases("ACT NOW for a free gift"));
        Assert.Empty(ContentChecks.FindSpamPhrases("Your September update"));
        Assert.NotEmpty(ContentChecks.SubjectWarnings("BUY EVERYTHING TODAY NOW!!"));
    }
}

public sealed class ContactRulesTests
{
    [Theory]
    [InlineData(" Ann@Example.COM ", "ann@example.com")]
    [InlineData("not-an-email", null)]
    [InlineData("a@b", null)]
    [InlineData("\"quoted\"@x.test", null)]
    public void Normalizes_emails(string input, string? expected) => Assert.Equal(expected, ContactRules.NormalizeEmail(input));

    [Theory]
    [InlineData("+1 (415) 555-0123", "+14155550123")]
    [InlineData("0092 300 1234567", "+923001234567")]
    [InlineData("03001234567", null)]
    [InlineData("+0123", null)]
    public void Normalizes_phones_to_e164(string input, string? expected) => Assert.Equal(expected, ContactRules.NormalizePhone(input));

    [Fact]
    public void Validates_custom_fields()
    {
        var errors = new List<string>();
        var json = System.Text.Json.JsonDocument.Parse("""{"plan":"pro","visits":3,"vip":true,"email":"x","Bad Key":"y","nested":{"a":1}}""").RootElement;
        var fields = ContactRules.ParseCustomFields(json, errors);
        Assert.Equal("pro", fields["plan"]);
        Assert.Equal("3", fields["visits"]);
        Assert.Equal("true", fields["vip"]);
        Assert.Equal(3, errors.Count); // built-in key, invalid key, nested object
    }
}

public sealed class CsvParserTests
{
    [Fact]
    public void Parses_quotes_new_lines_bom_and_semicolons()
    {
        var rows = CsvParser.Parse("﻿email,name\r\n\"a@x.test\",\"Smith, \"\"Jo\"\"\"\r\nb@x.test,\"multi\nline\"\r\n\r\n");
        Assert.Equal(3, rows.Count);
        Assert.Equal("Smith, \"Jo\"", rows[1][1]);
        Assert.Equal("multi\nline", rows[2][1]);
        Assert.Equal(';', CsvParser.DetectDelimiter("email;name\na;b"));
        Assert.Throws<FormatException>(() => CsvParser.Parse("a,\"b\nc"));
    }

    [Fact]
    public void Records_know_the_file_line_they_start_on_across_blank_lines_and_multi_line_values()
    {
        var records = CsvParser.ParseRecords("email,note\r\na@x.test,one\r\n\r\nb@x.test,\"two\nlines\"\r\n\r\n\r\nc@x.test,three\r\nd@x.test,four");
        Assert.Equal(new[] { 1, 2, 4, 8, 9 }, records.Select(r => r.Line).ToArray());
        Assert.Equal("c@x.test", records[3].Fields[0]);
        Assert.Equal(records.Select(r => r.Fields), CsvParser.Parse("email,note\r\na@x.test,one\r\n\r\nb@x.test,\"two\nlines\"\r\n\r\n\r\nc@x.test,three\r\nd@x.test,four"));
    }
}
