using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Submissions;

namespace OptimizeAll.UnitTests.Submissions;

/// <summary>Canonical post keys used for duplicate detection (C1).</summary>
public sealed class PostKeyTests
{
    [Theory]
    // Instagram: /p/, /reel/, /reels/, /tv/, with or without a user segment.
    [InlineData(SocialPlatform.Instagram, "https://www.instagram.com/p/Cabc123/", "instagram:Cabc123")]
    [InlineData(SocialPlatform.Instagram, "https://instagram.com/reel/Cabc123", "instagram:Cabc123")]
    [InlineData(SocialPlatform.Instagram, "https://instagram.com/reels/Cabc123/", "instagram:Cabc123")]
    [InlineData(SocialPlatform.Instagram, "https://instagram.com/tv/Cabc123", "instagram:Cabc123")]
    [InlineData(SocialPlatform.Instagram, "https://instagram.com/some.user/p/Cabc123/", "instagram:Cabc123")]
    [InlineData(SocialPlatform.Instagram, "https://instagr.am/p/Cabc123", "instagram:Cabc123")]
    // TikTok
    [InlineData(SocialPlatform.TikTok, "https://www.tiktok.com/@creator/video/7291234567890?is_from_webapp=1", "tiktok:7291234567890")]
    [InlineData(SocialPlatform.TikTok, "https://m.tiktok.com/video/7291234567890", "tiktok:7291234567890")]
    [InlineData(SocialPlatform.TikTok, "https://vm.tiktok.com/ZMabc12/", "tiktok-short:ZMabc12")]
    [InlineData(SocialPlatform.TikTok, "https://vt.tiktok.com/ZMabc12", "tiktok-short:ZMabc12")]
    [InlineData(SocialPlatform.TikTok, "https://www.tiktok.com/t/ZMabc12/", "tiktok-short:ZMabc12")]
    // X / Twitter
    [InlineData(SocialPlatform.X, "https://twitter.com/brand/status/1234567890?s=20&t=abc", "x:1234567890")]
    [InlineData(SocialPlatform.X, "https://mobile.x.com/other_name/status/1234567890/photo/1", "x:1234567890")]
    [InlineData(SocialPlatform.X, "https://x.com/i/web/status/1234567890", "x:1234567890")]
    // YouTube
    [InlineData(SocialPlatform.YouTube, "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s&feature=share", "youtube:dQw4w9WgXcQ")]
    [InlineData(SocialPlatform.YouTube, "https://youtu.be/dQw4w9WgXcQ?si=xyz", "youtube:dQw4w9WgXcQ")]
    [InlineData(SocialPlatform.YouTube, "https://m.youtube.com/shorts/dQw4w9WgXcQ", "youtube:dQw4w9WgXcQ")]
    [InlineData(SocialPlatform.YouTube, "https://youtube.com/live/dQw4w9WgXcQ?feature=share", "youtube:dQw4w9WgXcQ")]
    // Facebook
    [InlineData(SocialPlatform.Facebook, "https://m.facebook.com/story.php?story_fbid=111&id=222&mibextid=x", "facebook:story:222:111")]
    [InlineData(SocialPlatform.Facebook, "https://www.facebook.com/permalink.php?id=222&story_fbid=111", "facebook:story:222:111")]
    [InlineData(SocialPlatform.Facebook, "https://www.facebook.com/brandpage/posts/pfbid02abc?__cft__=1", "facebook:post:pfbid02abc")]
    [InlineData(SocialPlatform.Facebook, "https://www.facebook.com/reel/987654321/?s=single_unit", "facebook:reel:987654321")]
    [InlineData(SocialPlatform.Facebook, "https://web.facebook.com/watch/?v=555&ref=sharing", "facebook:video:555")]
    [InlineData(SocialPlatform.Facebook, "https://www.facebook.com/photo.php?fbid=777&set=a.1", "facebook:photo:777")]
    [InlineData(SocialPlatform.Facebook, "https://www.facebook.com/groups/somegroup/?ref=share&x=1", "https://facebook.com/groups/somegroup")]
    // LinkedIn
    [InlineData(SocialPlatform.LinkedIn, "https://www.linkedin.com/feed/update/urn:li:activity:7100000000000000001/", "linkedin:activity:7100000000000000001")]
    [InlineData(SocialPlatform.LinkedIn, "https://www.linkedin.com/feed/update/urn%3Ali%3Aactivity%3A7100000000000000001", "linkedin:activity:7100000000000000001")]
    [InlineData(SocialPlatform.LinkedIn, "https://www.linkedin.com/posts/jane-doe_launch-day-activity-7100000000000000001-AbCd?utm_source=share", "linkedin:activity:7100000000000000001")]
    // Threads, Pinterest, Snapchat
    [InlineData(SocialPlatform.Threads, "https://www.threads.net/@creator/post/C9xYz?xmt=1", "threads:C9xYz")]
    [InlineData(SocialPlatform.Threads, "https://threads.com/@other/post/C9xYz", "threads:C9xYz")]
    [InlineData(SocialPlatform.Pinterest, "https://www.pinterest.com/pin/123456789/?mt=login", "pinterest:123456789")]
    [InlineData(SocialPlatform.Snapchat, "https://www.snapchat.com/spotlight/W7_EDlXWTBiXAEEniNoMPwAA?share_id=1", "snapchat:spotlight:W7_EDlXWTBiXAEEniNoMPwAA")]
    // Fallback: normalized URL without any query parameter.
    [InlineData(SocialPlatform.Snapchat, "https://story.snapchat.com/s/abc/?locale=en", "https://snapchat.com/s/abc")]
    [InlineData(SocialPlatform.Instagram, "https://www.instagram.com/stories/creator/3141592653/?utm_source=ig", "https://instagram.com/stories/creator/3141592653")]
    public void Computes_the_canonical_key(SocialPlatform platform, string url, string expected)
    {
        var result = PlatformUrlRules.Parse(platform, url);
        Assert.Equal(PostUrlError.None, result.Error);
        Assert.Equal(expected, result.CanonicalKey);
    }

    /// <summary>Every bypass variant from the review maps to one key.</summary>
    [Theory]
    [InlineData("https://www.instagram.com/p/CxYz_9-a/")]
    [InlineData("https://instagram.com/p/CxYz_9-a")]
    [InlineData("https://instagram.com./p/CxYz_9-a/")]
    [InlineData("https://www.instagram.com./p/CxYz_9-a")]
    [InlineData("https://instagram.com/p/CxYz_9-a?x=1")]
    [InlineData("https://instagram.com/p/CxYz_9-a/?igsh=abc&utm_source=ig_web_copy_link&x=1#c")]
    [InlineData("https://m.instagram.com/reel/CxYz_9-a/")]
    [InlineData("http://INSTAGRAM.COM/reels/CxYz_9-a")]
    [InlineData("https://instagram.com/tv/CxYz_9-a")]
    [InlineData("https://instagram.com/anyone/p/CxYz_9-a/")]
    public void Instagram_bypass_variants_collide(string url) =>
        Assert.Equal("instagram:CxYz_9-a", PlatformUrlRules.CanonicalKey(SocialPlatform.Instagram, url));

    [Theory]
    [InlineData("https://youtu.be/AbC-123_xyZ")]
    [InlineData("https://www.youtube.com/watch?v=AbC-123_xyZ")]
    [InlineData("https://youtube.com/watch?feature=share&v=AbC-123_xyZ&t=10")]
    [InlineData("https://m.youtube.com/shorts/AbC-123_xyZ?si=q")]
    [InlineData("https://youtube.com./live/AbC-123_xyZ")]
    public void YouTube_forms_collide(string url) =>
        Assert.Equal("youtube:AbC-123_xyZ", PlatformUrlRules.CanonicalKey(SocialPlatform.YouTube, url));

    [Fact]
    public void Codes_differing_only_by_case_are_different_posts()
    {
        var upper = PlatformUrlRules.CanonicalKey(SocialPlatform.Instagram, "https://instagram.com/p/CAbc123/");
        var lower = PlatformUrlRules.CanonicalKey(SocialPlatform.Instagram, "https://instagram.com/p/cabc123/");
        Assert.NotNull(upper);
        Assert.NotNull(lower);
        Assert.NotEqual(upper, lower);
        Assert.NotEqual(PlatformUrlRules.CanonicalKey(SocialPlatform.YouTube, "https://youtu.be/AbCdEfGhIjK"),
            PlatformUrlRules.CanonicalKey(SocialPlatform.YouTube, "https://youtu.be/abcdefghijk"));
    }

    [Theory]
    [InlineData(SocialPlatform.Instagram, "https://de.instagram.com/p/Cabc123/")]
    [InlineData(SocialPlatform.Instagram, "https://anything.instagram.com/p/Cabc123/")]
    [InlineData(SocialPlatform.Instagram, "https://instagram.com.evil.example/p/Cabc123/")]
    [InlineData(SocialPlatform.TikTok, "https://vm.tiktok.com.evil.example/ZMabc/")]
    [InlineData(SocialPlatform.X, "https://evil.twitter.com/brand/status/1")]
    [InlineData(SocialPlatform.YouTube, "https://instagram.com/p/abc")]
    [InlineData(SocialPlatform.Facebook, "https://facebook.com/")]
    public void Unknown_subdomains_other_hosts_and_roots_are_platform_mismatches(SocialPlatform platform, string url) =>
        Assert.Equal(PostUrlError.PlatformMismatch, PlatformUrlRules.Parse(platform, url).Error);

    [Theory]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://instagram.com/p/abc")]
    [InlineData("https://user:pw@instagram.com/p/abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Malformed_urls_are_invalid(string? url) =>
        Assert.Equal(PostUrlError.InvalidUrl, PlatformUrlRules.Parse(SocialPlatform.Instagram, url).Error);

    [Fact]
    public void Overlong_keys_are_invalid()
    {
        var url = "https://instagram.com/stories/" + new string('a', 800);
        Assert.Equal(PostUrlError.InvalidUrl, PlatformUrlRules.Parse(SocialPlatform.Instagram, url).Error);
    }

    [Theory]
    [InlineData(SocialPlatform.TikTok, "https://vm.tiktok.com/ZMabc/", true)]
    [InlineData(SocialPlatform.TikTok, "https://tiktok.com/t/ZMabc/", true)]
    [InlineData(SocialPlatform.Facebook, "https://fb.watch/abc/", true)]
    [InlineData(SocialPlatform.Pinterest, "https://pin.it/abc", true)]
    [InlineData(SocialPlatform.LinkedIn, "https://lnkd.in/abc", true)]
    [InlineData(SocialPlatform.TikTok, "https://www.tiktok.com/@a/video/123", false)]
    [InlineData(SocialPlatform.YouTube, "https://youtu.be/abc", false)]
    public void Short_links_are_marked_for_reviewers(SocialPlatform platform, string url, bool isShort)
    {
        var result = PlatformUrlRules.Parse(platform, url);
        Assert.True(result.IsValid);
        Assert.Equal(isShort, result.IsShortLink);
    }

    [Fact]
    public void Fallback_keeps_only_identity_query_parameters()
    {
        Assert.Equal("https://youtube.com/playlist",
            PlatformUrlRules.CanonicalKey(SocialPlatform.YouTube, "https://www.youtube.com/playlist?list=PL1&si=x"));
        Assert.Equal("https://facebook.com/some/page?fbid=9&id=8",
            PlatformUrlRules.CanonicalKey(SocialPlatform.Facebook, "https://www.facebook.com/some/page?zz=1&id=8&fbid=9&fbclid=q"));
        Assert.Equal("https://x.com/brand/likes",
            PlatformUrlRules.CanonicalKey(SocialPlatform.X, "https://twitter.com/brand/likes?ref=1"));
    }

    [Fact]
    public void Host_rules_accept_only_allow_listed_subdomains_and_ignore_a_trailing_dot()
    {
        Assert.True(PlatformUrlRules.HostBelongsTo(SocialPlatform.Instagram, "instagram.com."));
        Assert.True(PlatformUrlRules.HostBelongsTo(SocialPlatform.Instagram, "WWW.Instagram.com"));
        Assert.True(PlatformUrlRules.HostBelongsTo(SocialPlatform.TikTok, "vt.tiktok.com"));
        Assert.False(PlatformUrlRules.HostBelongsTo(SocialPlatform.Instagram, "de.instagram.com"));
        Assert.False(PlatformUrlRules.HostBelongsTo(SocialPlatform.Instagram, "vm.instagram.com"));
        Assert.False(PlatformUrlRules.HostBelongsTo(SocialPlatform.Instagram, "."));
    }
}

public sealed class SubmissionTimingTests
{
    private static readonly DateTime Submitted = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Posted_at_may_be_at_most_seven_days_before_the_submission()
    {
        Assert.False(SubmissionTiming.IsPostedAtTooOld(Submitted.AddDays(-7), Submitted));
        Assert.True(SubmissionTiming.IsPostedAtTooOld(Submitted.AddDays(-7).AddTicks(-1), Submitted));
        Assert.False(SubmissionTiming.IsPostedAtTooOld(Submitted.AddDays(1), Submitted));
    }

    [Fact]
    public void Future_posted_at_is_allowed_only_within_the_clock_skew()
    {
        Assert.False(SubmissionTiming.IsPostedAtInFuture(Submitted.AddMinutes(10), Submitted));
        Assert.True(SubmissionTiming.IsPostedAtInFuture(Submitted.AddMinutes(10).AddTicks(1), Submitted));
    }

    [Fact]
    public void Reward_windows_use_the_earlier_of_posted_and_submitted()
    {
        Assert.Equal(Submitted.AddDays(-2), SubmissionTiming.RewardWindowTime(Submitted.AddDays(-2), Submitted));
        Assert.Equal(Submitted, SubmissionTiming.RewardWindowTime(Submitted.AddMinutes(5), Submitted));
    }

    [Fact]
    public void Caps_use_the_submission_time()
    {
        Assert.Equal(Submitted, SubmissionTiming.CapTime(Submitted));
    }

    [Fact]
    public void Live_check_is_due_after_the_later_of_posted_and_submitted()
    {
        Assert.Equal(Submitted.AddHours(48), SubmissionTiming.LiveCheckDueAt(Submitted.AddHours(-47), Submitted, 48));
        Assert.Equal(Submitted.AddMinutes(5).AddHours(24), SubmissionTiming.LiveCheckDueAt(Submitted.AddMinutes(5), Submitted, 24));
    }
}

public sealed class ReasonTextTests
{
    [Fact]
    public void Short_text_and_null_are_unchanged()
    {
        Assert.Null(ReasonText.Fit(null));
        Assert.Equal("ok", ReasonText.Fit("ok"));
        var exact = new string('a', 1000);
        Assert.Same(exact, ReasonText.Fit(exact));
    }

    [Fact]
    public void Long_text_is_cut_with_an_ellipsis_within_the_limit()
    {
        var fitted = ReasonText.Fit(new string('a', 1500))!;
        Assert.Equal(1000, fitted.Length);
        Assert.EndsWith("…", fitted);
        Assert.Equal(20, ReasonText.Fit(new string('b', 50), 20)!.Length);
    }

    [Fact]
    public void Surrogate_pairs_are_never_split()
    {
        var text = new string('a', 998) + "😀" + "tail";
        var fitted = ReasonText.Fit(text)!;
        Assert.True(fitted.Length <= 1000);
        Assert.False(char.IsHighSurrogate(fitted[^2]));
        Assert.EndsWith("…", fitted);
    }
}
