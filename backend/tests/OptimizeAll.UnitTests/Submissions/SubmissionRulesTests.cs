using System.Buffers.Binary;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Submissions;

namespace OptimizeAll.UnitTests.Submissions;

public sealed class PlatformUrlRulesTests
{
    [Theory]
    [InlineData(SocialPlatform.Instagram, "https://www.instagram.com/p/Cabc123/")]
    [InlineData(SocialPlatform.Instagram, "https://instagr.am/p/Cabc123")]
    [InlineData(SocialPlatform.TikTok, "https://www.tiktok.com/@user/video/7234567890")]
    [InlineData(SocialPlatform.TikTok, "https://vm.tiktok.com/ZMabc/")]
    [InlineData(SocialPlatform.X, "https://twitter.com/user/status/1")]
    [InlineData(SocialPlatform.X, "https://x.com/user/status/1")]
    [InlineData(SocialPlatform.Facebook, "https://m.facebook.com/story.php?id=1")]
    [InlineData(SocialPlatform.Facebook, "https://fb.watch/abc/")]
    [InlineData(SocialPlatform.LinkedIn, "https://www.linkedin.com/posts/abc")]
    [InlineData(SocialPlatform.YouTube, "https://youtu.be/dQw4w9WgXcQ")]
    [InlineData(SocialPlatform.YouTube, "https://www.youtube.com/shorts/abc")]
    [InlineData(SocialPlatform.Threads, "https://www.threads.net/@user/post/abc")]
    [InlineData(SocialPlatform.Pinterest, "https://pin.it/abc")]
    [InlineData(SocialPlatform.Snapchat, "https://story.snapchat.com/s/abc")]
    public void Accepts_post_urls_on_the_platforms_hosts(SocialPlatform platform, string url)
    {
        Assert.True(PlatformUrlRules.IsValidPostUrl(platform, url));
        Assert.True(PlatformUrlRules.IsValidPostUrl(platform, Normalization.PostUrl(url)));
    }

    [Theory]
    [InlineData(SocialPlatform.Instagram, "https://www.tiktok.com/@user/video/1")]
    [InlineData(SocialPlatform.Instagram, "https://instagram.com.evil.example/p/abc")]
    [InlineData(SocialPlatform.Instagram, "https://notinstagram.com/p/abc")]
    [InlineData(SocialPlatform.Instagram, "https://www.instagram.com/")]
    [InlineData(SocialPlatform.Instagram, "https://instagram.com")]
    [InlineData(SocialPlatform.X, "ftp://x.com/user/status/1")]
    [InlineData(SocialPlatform.X, "https://user:pw@x.com/user/status/1")]
    [InlineData(SocialPlatform.YouTube, "not a url")]
    [InlineData(SocialPlatform.YouTube, "")]
    [InlineData(SocialPlatform.YouTube, null)]
    [InlineData(SocialPlatform.Instagram, "https://de.instagram.com/p/abc")]
    [InlineData(SocialPlatform.Instagram, "https://evil.www.instagram.com/p/abc")]
    [InlineData(SocialPlatform.Instagram, "https://www.www.instagram.com/p/abc")]
    [InlineData(SocialPlatform.TikTok, "https://vm.tiktok.com/")]
    [InlineData(SocialPlatform.YouTube, "https://attacker.youtube.com/watch?v=abc")]
    [InlineData(SocialPlatform.X, "https://api.x.com/user/status/1")]
    public void Rejects_other_hosts_roots_and_invalid_urls(SocialPlatform platform, string? url) =>
        Assert.False(PlatformUrlRules.IsValidPostUrl(platform, url));

    [Fact]
    public void Every_platform_has_hosts()
    {
        foreach (var platform in Enum.GetValues<SocialPlatform>())
            Assert.NotEmpty(PlatformUrlRules.HostsFor(platform));
    }

    [Theory]
    [InlineData("https://www.instagram.com/p/ABC/?utm_source=ig&igshid=xyz", "https://instagram.com/p/ABC")]
    [InlineData("https://m.instagram.com/p/ABC", "https://instagram.com/p/ABC")]
    [InlineData("http://instagram.com/p/ABC/#comments", "https://instagram.com/p/ABC")]
    [InlineData("https://www.instagram.com./p/ABC/", "https://instagram.com/p/ABC")]
    [InlineData("https://WWW.M.Instagram.COM/p/ABC", "https://instagram.com/p/ABC")]
    public void Url_variants_normalize_to_the_same_value(string raw, string expected) =>
        Assert.Equal(expected, Normalization.PostUrl(raw));
}

public sealed class RiskRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static RiskSignals Clean() => new()
    {
        PostedAtUtc = Now.AddHours(-1),
        SubmittedAtUtc = Now,
        CampaignStartsAtUtc = Now.AddDays(-5),
        CampaignEndsAtUtc = Now.AddDays(5),
        AccountVerified = true,
        VelocityLimitPer24Hours = 10,
        ParticipantCreatedAtUtc = Now.AddDays(-30),
        NowUtc = Now,
    };

    [Fact]
    public void Clean_submission_has_no_flags()
    {
        var flags = RiskRules.Evaluate(Clean());
        Assert.Empty(flags);
        Assert.Equal(0, RiskRules.Score(flags));
    }

    [Fact]
    public void Each_signal_produces_its_flag_and_weight()
    {
        var flags = RiskRules.Evaluate(Clean() with
        {
            SameScreenshotCount = 2,
            RepeatedContentCount = 1,
            PostedAtUtc = Now.AddDays(-6),
            AccountVerified = false,
            SubmissionsInLast24Hours = 10,
            ParticipantCreatedAtUtc = Now.AddDays(-2),
            IsShortLink = true,
        });
        Assert.Equal(new[]
        {
            SubmissionFlagType.DuplicateScreenshot, SubmissionFlagType.RepeatedContent, SubmissionFlagType.OutsideCampaignWindow,
            SubmissionFlagType.PostedLongBeforeSubmission, SubmissionFlagType.UnresolvedShortLink,
            SubmissionFlagType.AccountNotVerified, SubmissionFlagType.HighSubmissionVelocity, SubmissionFlagType.NewParticipant,
        }, flags.Select(f => f.Type));
        Assert.Equal(40 + 20 + 30 + 15 + 10 + 10 + 15 + 5, RiskRules.Score(flags));
    }

    [Fact]
    public void Unverified_account_is_not_flagged_when_the_campaign_requires_verification()
    {
        var flags = RiskRules.Evaluate(Clean() with { AccountVerified = false, CampaignRequiresVerifiedAccount = true });
        Assert.DoesNotContain(flags, f => f.Type == SubmissionFlagType.AccountNotVerified);
    }

    [Fact]
    public void Velocity_flag_starts_above_the_limit()
    {
        Assert.Empty(RiskRules.Evaluate(Clean() with { SubmissionsInLast24Hours = 9 }));
        Assert.Single(RiskRules.Evaluate(Clean() with { SubmissionsInLast24Hours = 10 }));
    }

    [Fact]
    public void Posted_long_before_submission_is_flagged_only_beyond_48_hours()
    {
        Assert.Empty(RiskRules.Evaluate(Clean() with { PostedAtUtc = Now.AddHours(-48) }));
        var flag = Assert.Single(RiskRules.Evaluate(Clean() with { PostedAtUtc = Now.AddHours(-48).AddMinutes(-1) }));
        Assert.Equal(SubmissionFlagType.PostedLongBeforeSubmission, flag.Type);
        Assert.Equal(15, flag.Weight);
        // Measured against the submission time, not "now" (a later resubmission doesn't raise it by itself).
        Assert.Empty(RiskRules.Evaluate(Clean() with { PostedAtUtc = Now.AddDays(-3), SubmittedAtUtc = Now.AddDays(-2), NowUtc = Now }));
    }

    [Fact]
    public void Posts_after_the_campaign_end_are_outside_the_window()
    {
        var flags = RiskRules.Evaluate(Clean() with { PostedAtUtc = Now.AddDays(6) });
        Assert.Equal(SubmissionFlagType.OutsideCampaignWindow, Assert.Single(flags).Type);
    }
}

public sealed class ImageInspectorTests
{
    public static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        bytes[24] = 8;
        bytes[25] = 2;
        return bytes;
    }

    public static byte[] Jpeg(int width, int height)
    {
        var app0 = new byte[] { 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 };
        var sof = new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1 };
        return new byte[] { 0xFF, 0xD8 }.Concat(app0).Concat(sof).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
    }

    private static byte[] WebP(string chunk, Action<byte[]> fill)
    {
        var bytes = new byte[40];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 32);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(bytes, 12);
        fill(bytes);
        return bytes;
    }

    [Fact]
    public void Reads_png_dimensions()
    {
        var info = ImageInspector.Inspect(Png(640, 480));
        Assert.Equal(new ImageInfo("image/png", ".png", 640, 480), info);
    }

    [Fact]
    public void Reads_jpeg_dimensions_after_app_segments()
    {
        var info = ImageInspector.Inspect(Jpeg(1080, 1920));
        Assert.Equal(new ImageInfo("image/jpeg", ".jpg", 1080, 1920), info);
    }

    [Fact]
    public void Reads_webp_vp8x_vp8_and_vp8l_dimensions()
    {
        var vp8x = WebP("VP8X", b => { b[24] = (800 - 1) & 0xFF; b[25] = (800 - 1) >> 8; b[27] = (600 - 1) & 0xFF; b[28] = (600 - 1) >> 8; });
        Assert.Equal(new ImageInfo("image/webp", ".webp", 800, 600), ImageInspector.Inspect(vp8x));

        var vp8 = WebP("VP8 ", b =>
        {
            b[23] = 0x9D; b[24] = 0x01; b[25] = 0x2A;
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(26), 1024);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(28), 768);
        });
        Assert.Equal(new ImageInfo("image/webp", ".webp", 1024, 768), ImageInspector.Inspect(vp8));

        var vp8l = WebP("VP8L", b =>
        {
            b[20] = 0x2F;
            var bits = (uint)(300 - 1) | ((uint)(250 - 1) << 14);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(21), bits);
        });
        Assert.Equal(new ImageInfo("image/webp", ".webp", 300, 250), ImageInspector.Inspect(vp8l));
    }

    [Theory]
    [InlineData("%PDF-1.7\n1 0 obj")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("GIF89a......")]
    [InlineData("")]
    public void Rejects_non_images(string content) =>
        Assert.Null(ImageInspector.Inspect(System.Text.Encoding.ASCII.GetBytes(content)));

    [Fact]
    public void Rejects_truncated_headers()
    {
        Assert.Null(ImageInspector.Inspect(Png(10, 10).AsSpan(0, 20)));
        Assert.Null(ImageInspector.Inspect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 }));
        Assert.Null(ImageInspector.Inspect(Png(0, 100)));
    }

    [Theory]
    [InlineData("../../etc/passwd.png", ".png", "passwd.png")]
    [InlineData("C:\\Users\\me\\shot 1.PNG", ".png", "shot 1.png")]
    [InlineData("<script>.jpg", ".jpg", "_script_.jpg")]
    [InlineData("", ".webp", "image.webp")]
    [InlineData("evil.html", ".png", "evil.png")]
    public void Sanitizes_file_names(string name, string ext, string expected) =>
        Assert.Equal(expected, FileService.SanitizeFileName(name, ext));
}
