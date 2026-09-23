using OptimizeAll.Api.Modules.Social;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.UnitTests.Social;

public sealed class SocialProfileRulesTests
{
    [Theory]
    [InlineData(SocialPlatform.Instagram, "https://www.instagram.com/sara", true)]
    [InlineData(SocialPlatform.Instagram, "https://instagram.com/sara", true)]
    [InlineData(SocialPlatform.Instagram, "https://m.instagram.com/sara", true)]
    [InlineData(SocialPlatform.Instagram, "https://instagram.com.evil.io/sara", false)]
    [InlineData(SocialPlatform.Instagram, "https://notinstagram.com/sara", false)]
    [InlineData(SocialPlatform.Instagram, "http://instagram.com/sara", false)]
    [InlineData(SocialPlatform.TikTok, "https://www.tiktok.com/@sara", true)]
    [InlineData(SocialPlatform.X, "https://x.com/sara", true)]
    [InlineData(SocialPlatform.X, "https://twitter.com/sara", true)]
    [InlineData(SocialPlatform.X, "https://mobile.twitter.com/sara", true)]
    [InlineData(SocialPlatform.Facebook, "https://fb.com/sara", true)]
    [InlineData(SocialPlatform.Facebook, "https://www.facebook.com/sara", true)]
    [InlineData(SocialPlatform.LinkedIn, "https://www.linkedin.com/in/sara", true)]
    [InlineData(SocialPlatform.LinkedIn, "https://pk.linkedin.com/in/sara", true)]
    [InlineData(SocialPlatform.YouTube, "https://youtu.be/abc", true)]
    [InlineData(SocialPlatform.YouTube, "https://www.youtube.com/@sara", true)]
    [InlineData(SocialPlatform.Threads, "https://www.threads.net/@sara", true)]
    [InlineData(SocialPlatform.Pinterest, "https://www.pinterest.com/sara", true)]
    [InlineData(SocialPlatform.Snapchat, "https://www.snapchat.com/add/sara", true)]
    [InlineData(SocialPlatform.Snapchat, "https://www.instagram.com/sara", false)]
    [InlineData(SocialPlatform.YouTube, "https://user:pw@youtube.com/@sara", false)]
    [InlineData(SocialPlatform.YouTube, "youtube.com/@sara", false)]
    [InlineData(SocialPlatform.YouTube, "", false)]
    public void Profile_url_host_must_match_the_platform(SocialPlatform platform, string url, bool valid) =>
        Assert.Equal(valid, SocialProfileRules.ValidateProfileUrl(platform, url) is null);

    [Fact]
    public void Every_platform_has_hosts() =>
        Assert.All(Enum.GetValues<SocialPlatform>(), p => Assert.NotEmpty(SocialProfileRules.PlatformHosts[p]));

    [Theory]
    [InlineData("@sara_k", true)]
    [InlineData("sara.k", true)]
    [InlineData("", false)]
    [InlineData("@", false)]
    [InlineData("sara k", false)]
    [InlineData("https://instagram.com/sara", false)]
    public void Handles(string handle, bool valid) => Assert.Equal(valid, SocialProfileRules.ValidateHandle(handle) is null);

    [Fact]
    public void Account_creation_date_bounds()
    {
        var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        Assert.Null(SocialProfileRules.ValidateAccountCreatedAt(new DateTime(2004, 1, 1, 0, 0, 0, DateTimeKind.Utc), now));
        Assert.NotNull(SocialProfileRules.ValidateAccountCreatedAt(new DateTime(2003, 12, 31, 23, 59, 59, DateTimeKind.Utc), now));
        Assert.NotNull(SocialProfileRules.ValidateAccountCreatedAt(now.AddMinutes(1), now));
        Assert.Null(SocialProfileRules.ValidateAccountCreatedAt(now, now));
    }
}
