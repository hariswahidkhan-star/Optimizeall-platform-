using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.UnitTests.Accounts;

public sealed class FieldRulesTests
{
    [Theory]
    [InlineData("+923001234567", true)]
    [InlineData("+14155552671", true)]
    [InlineData("+4420123456", true)]
    [InlineData("+12345678", true)]          // 8 digits: minimum
    [InlineData("+123456789012345", true)]   // 15 digits: maximum
    [InlineData("+1234567", false)]          // too short
    [InlineData("+1234567890123456", false)] // too long
    [InlineData("923001234567", false)]      // missing '+'
    [InlineData("+0923001234567", false)]    // country code can't start with 0
    [InlineData("+92 300 1234567", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void E164(string? value, bool valid) => Assert.Equal(valid, FieldRules.IsE164(value));

    [Theory]
    [InlineData("GB82WEST12345698765432", true)]
    [InlineData("DE89370400440532013000", true)]
    [InlineData("PK36SCBL0000001123456702", true)]
    [InlineData("GB00WEST12345698765432", false)]
    [InlineData("DE89370400440532013001", false)]
    public void Iban_checksum(string iban, bool valid) => Assert.Equal(valid, FieldRules.IsValidIban(iban));

    [Theory]
    [InlineData("12345678", true)]
    [InlineData("001234567890", true)]
    [InlineData("1234567", false)]
    [InlineData("GB82WEST12345698765432", true)]
    [InlineData("GB00WEST12345698765432", false)]
    [InlineData("1234-5678", false)] // must be normalized first
    public void Bank_account(string value, bool valid) => Assert.Equal(valid, FieldRules.IsBankAccount(value));

    [Fact]
    public void Masking_never_reveals_more_than_the_tail()
    {
        Assert.Equal("••••1234", FieldRules.MaskTail("GB82WEST00001234"));
        Assert.Equal("••••", FieldRules.MaskTail("1234"));
        Assert.Equal("j•••@gmail.com", FieldRules.MaskEmail("jane.doe@gmail.com"));
    }

    [Theory]
    [InlineData(PayoutMethod.PayPal, "Jane.Doe@Gmail.com", "jane.doe@gmail.com", "j•••@gmail.com")]
    [InlineData(PayoutMethod.BankTransfer, "gb82 west 1234 5698 7654 32", "GB82WEST12345698765432", "••••5432")]
    [InlineData(PayoutMethod.BankTransfer, "0012-3456-7890", "001234567890", "••••7890")]
    [InlineData(PayoutMethod.MobileWallet, "+92 300-1234567", "+923001234567", "••••4567")]
    [InlineData(PayoutMethod.Other, "Western Union to Lahore 5555", "Western Union to Lahore 5555", "••••5555")]
    public void Payout_destinations_are_normalized_and_masked(PayoutMethod method, string raw, string normalized, string hint)
    {
        var (n, h, error) = FieldRules.NormalizePayoutDestination(method, raw);
        Assert.Null(error);
        Assert.Equal(normalized, n);
        Assert.Equal(hint, h);
    }

    [Theory]
    [InlineData(PayoutMethod.PayPal, "jane")]
    [InlineData(PayoutMethod.BankTransfer, "12AB")]
    [InlineData(PayoutMethod.MobileWallet, "03001234567")]
    [InlineData(PayoutMethod.Other, "ab")]
    public void Invalid_payout_destinations_are_rejected(PayoutMethod method, string raw)
    {
        var (n, h, error) = FieldRules.NormalizePayoutDestination(method, raw);
        Assert.NotNull(error);
        Assert.Null(n);
        Assert.Null(h);
    }

    [Theory]
    [InlineData("/app/campaigns", true)]
    [InlineData("https://cdn.example.com/banner.png", true)]
    [InlineData("http://example.com", false)]
    [InlineData("//evil.example.com/x", false)]
    [InlineData("/\\evil.example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html;base64,AAAA", false)]
    [InlineData("app/campaigns", false)]
    [InlineData("", false)]
    public void Content_urls(string url, bool valid) => Assert.Equal(valid, FieldRules.IsSafeContentUrl(url));

    [Theory]
    [InlineData("/api/v1/files/0f8fad5b-d9cb-469f-a165-70867728950e", true)]
    [InlineData("https://images.example.com/hero.png", true)]
    [InlineData("https://IMAGES.example.com/a/b.jpg?x=1", true)]
    [InlineData("https://placehold.co/1200x630/png", false)]               // not in the allowlist
    [InlineData("https://images.example.com.evil.test/a.png", false)]
    [InlineData("https://evil.test/@images.example.com", false)]
    [InlineData("https://user@images.example.com/a.png", false)]           // userinfo
    [InlineData("https://images.example.com:8443/a.png", false)]           // non-default port (CSP host-source)
    [InlineData("http://images.example.com/a.png", false)]
    [InlineData("//images.example.com/a.png", false)]
    [InlineData("/api/v1/files/not-a-guid", false)]
    [InlineData("/api/v1/files/0f8fad5b-d9cb-469f-a165-70867728950e/../x", false)]
    [InlineData("/app/logo.png", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void Image_urls_are_uploads_or_allowlisted_https_hosts(string url, bool valid) =>
        Assert.Equal(valid, FieldRules.IsAllowedImageUrl(url, new[] { "images.example.com", " cdn.example.org " }));

    [Fact]
    public void Image_urls_default_to_uploads_only()
    {
        Assert.True(FieldRules.IsAllowedImageUrl("/api/v1/files/0f8fad5b-d9cb-469f-a165-70867728950e", Array.Empty<string>()));
        Assert.False(FieldRules.IsAllowedImageUrl("https://images.example.com/hero.png", Array.Empty<string>()));
    }

    [Fact]
    public void Interests_are_normalized_and_limited()
    {
        var (tags, error) = FieldRules.NormalizeInterests(new[] { " Tech ", "tech", "Food", "", null });
        Assert.Null(error);
        Assert.Equal(new[] { "tech", "food" }, tags);
        Assert.NotNull(FieldRules.NormalizeInterests(Enumerable.Range(0, 21).Select(i => $"t{i}")).Error);
        Assert.NotNull(FieldRules.NormalizeInterests(new[] { new string('x', 41) }).Error);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("pt-BR", true)]
    [InlineData("zh-Hant", true)]
    [InlineData("english", false)]
    [InlineData("e", false)]
    public void Language_codes(string code, bool valid) => Assert.Equal(valid, FieldRules.IsLanguageCode(code));

    [Theory]
    [InlineData("Asia/Karachi", true)]
    [InlineData("Europe/London", true)]
    [InlineData("UTC", true)]
    [InlineData("Mars/Olympus", false)]
    public void Time_zones(string tz, bool valid) => Assert.Equal(valid, FieldRules.IsTimeZone(tz));
}
