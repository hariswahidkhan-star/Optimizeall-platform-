using System.Text;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.UnitTests.Website;

public sealed class MarkdownSanitizerTests
{
    [Theory]
    [InlineData("Hello <script>alert(1)</script> world", "Hello  world")]
    [InlineData("<SCRIPT type=\"text/javascript\">\nsteal()\n</SCRIPT>Safe", "Safe")]
    [InlineData("Text <img src=x onerror=alert(1)> more", "Text  more")]
    [InlineData("<a href=\"#\" onclick=\"evil()\">click</a>", "click")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>After", "After")]
    [InlineData("<!-- hidden <script>x</script> -->Visible", "Visible")]
    [InlineData("Unclosed <script>alert(1)", "Unclosed ")]
    public void Strips_raw_html_scripts_and_event_handlers(string input, string expected) =>
        Assert.Equal(expected, MarkdownSanitizer.Sanitize(input));

    [Theory]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("[click](JaVaScRiPt:alert(1))")]
    [InlineData("[click]( javascript:alert(1) )")]
    [InlineData("[click](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("[click](vbscript:msgbox)")]
    [InlineData("[click](//evil.example/path)")]
    [InlineData("<javascript:alert(1)>")]
    public void Removes_unsafe_link_destinations(string input)
    {
        var output = MarkdownSanitizer.Sanitize(input);
        Assert.DoesNotContain("javascript", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", output);
        Assert.DoesNotContain("vbscript", output);
        Assert.DoesNotContain("//evil", output);
    }

    [Fact]
    public void Drops_unsafe_images_and_reference_definitions()
    {
        var output = MarkdownSanitizer.Sanitize("![x](javascript:alert(1))\n\n[ref]: javascript:alert(1)\n\n![ok](/api/v1/files/1)");
        Assert.DoesNotContain("javascript", output);
        Assert.Contains("![ok](/api/v1/files/1)", output);
    }

    [Fact]
    public void Keeps_safe_markdown_and_code()
    {
        const string input = "# Title\n\nSome **bold** text with [a link](https://example.com \"Example\") and [an anchor](#section) and [mail](mailto:a@b.co).\n\n" +
                             "```html\n<script>alert('this is code')</script>\n```\n\nInline `<b>code</b>` stays.";
        var output = MarkdownSanitizer.Sanitize(input);
        Assert.Contains("# Title", output);
        Assert.Contains("[a link](https://example.com \"Example\")", output);
        Assert.Contains("[an anchor](#section)", output);
        Assert.Contains("[mail](mailto:a@b.co)", output);
        Assert.Contains("<script>alert('this is code')</script>", output);
        Assert.Contains("`<b>code</b>`", output);
    }

    [Fact]
    public void Leaves_comparison_operators_in_prose()
    {
        Assert.Equal("If a < b and c > d", MarkdownSanitizer.Sanitize("If a < b and c > d"));
    }

    [Fact]
    public void Computes_reading_time_and_plain_text()
    {
        var words = string.Join(' ', Enumerable.Repeat("word", 450));
        Assert.Equal(2, MarkdownSanitizer.ReadingMinutes(words));
        Assert.Equal(1, MarkdownSanitizer.ReadingMinutes("short"));
        Assert.Equal("Title Hello link", MarkdownSanitizer.ToPlainText("# Title\n\n**Hello** [link](https://x.y)"));
    }
}

public sealed class PdfSignatureTests
{
    private static byte[] Pdf(string body = "1 0 obj<<>>endobj\n") => Encoding.ASCII.GetBytes("%PDF-1.7\n" + body + "trailer<<>>\n%%EOF\n");

    [Fact]
    public void Accepts_real_pdfs() => Assert.True(PdfSignature.IsPdf(Pdf()));

    [Fact]
    public void Rejects_other_files_renamed_to_pdf()
    {
        Assert.False(PdfSignature.IsPdf(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.False(PdfSignature.IsPdf(Encoding.ASCII.GetBytes("<html><body>%PDF-1.4 %%EOF</body></html>")));
        Assert.False(PdfSignature.IsPdf(Encoding.ASCII.GetBytes("%PDF-")));
        Assert.False(PdfSignature.IsPdf(Pdf()[..20])); // truncated: no trailer
        Assert.False(PdfSignature.IsPdf(Encoding.ASCII.GetBytes("%PDF-9.9\nxxxxxxxxxxxx\n%%EOF")));
    }
}

public sealed class SlugsTests
{
    [Theory]
    [InlineData("Local SEO & Google Business Profile", "local-seo-google-business-profile")]
    [InlineData("  Café Déjà Vu  ", "cafe-deja-vu")]
    [InlineData("TikTok/LinkedIn Ads", "tiktok-linkedin-ads")]
    [InlineData("!!!", "")]
    public void Builds_url_slugs(string input, string expected) => Assert.Equal(expected, Slugs.From(input));
}

public sealed class ConsultationSlotsTests
{
    private static ConsultationSettings Settings(string zone = "UTC", int minNotice = 0) => new()
    {
        TimeZone = zone, SlotMinutes = 30, MinNoticeHours = minNotice, MaxDaysAhead = 30, IsEnabled = true,
        WeeklyAvailability = new() { new AvailabilityWindow(DayOfWeek.Monday, "09:00", "10:30") },
    };

    private static readonly DateTime Sunday = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Generates_slots_inside_weekly_windows()
    {
        var slots = ConsultationSlots.Available(Settings(), TimeZoneInfo.Utc, new HashSet<DateOnly>(), new HashSet<string>(), Sunday, Sunday, Sunday.AddDays(2));
        Assert.Equal(new[] { 9, 9, 10 }, slots.Select(s => s.Hour));
        Assert.Equal(new DateTime(2026, 9, 21, 9, 30, 0, DateTimeKind.Utc), slots[1]);
    }

    [Fact]
    public void Excludes_booked_blackout_and_too_soon_slots()
    {
        var booked = new HashSet<string> { ConsultationBooking.KeyFor(new DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc)) };
        var slots = ConsultationSlots.Available(Settings(), TimeZoneInfo.Utc, new HashSet<DateOnly>(), booked, Sunday, Sunday, Sunday.AddDays(2));
        Assert.Equal(2, slots.Count);

        var blackout = new HashSet<DateOnly> { new(2026, 9, 21) };
        Assert.Empty(ConsultationSlots.Available(Settings(), TimeZoneInfo.Utc, blackout, new HashSet<string>(), Sunday, Sunday, Sunday.AddDays(2)));

        // 22 hours of notice required: only the 10:00 slot on Monday qualifies from Sunday 12:00.
        var soon = ConsultationSlots.Available(Settings(minNotice: 22), TimeZoneInfo.Utc, new HashSet<DateOnly>(), new HashSet<string>(), Sunday, Sunday, Sunday.AddDays(2));
        Assert.Equal(new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc), Assert.Single(soon));
    }

    [Fact]
    public void Converts_the_agency_time_zone_to_utc()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi"); // UTC+5, no DST
        var slots = ConsultationSlots.Available(Settings("Asia/Karachi"), zone, new HashSet<DateOnly>(), new HashSet<string>(), Sunday, Sunday, Sunday.AddDays(2));
        Assert.Equal(new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc), slots[0]);
    }

    [Theory]
    [InlineData("09:00", true)]
    [InlineData("24:00", true)]
    [InlineData("24:30", false)]
    [InlineData("9:00", false)]
    [InlineData("12:60", false)]
    public void Parses_24_hour_times(string value, bool valid) => Assert.Equal(valid, ConsultationSlots.TryParseTime(value, out _));
}
