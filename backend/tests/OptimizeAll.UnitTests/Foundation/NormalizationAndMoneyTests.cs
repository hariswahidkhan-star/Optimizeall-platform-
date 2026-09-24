using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Auth;
using OptimizeAll.Domain.Common;
using Xunit;

namespace OptimizeAll.UnitTests.Foundation;

public sealed class PostUrlNormalizationTests
{
    [Theory]
    [InlineData("https://www.instagram.com/p/Cx1abc/", "https://instagram.com/p/Cx1abc")]
    [InlineData("http://instagram.com/p/Cx1abc?igshid=xyz&utm_source=ig", "https://instagram.com/p/Cx1abc")]
    [InlineData("https://m.facebook.com/story.php?story_fbid=1&id=2&fbclid=abc", "https://facebook.com/story.php?id=2&story_fbid=1")]
    [InlineData("https://twitter.com/brand/status/123?s=20&t=abc", "https://x.com/brand/status/123")]
    [InlineData("https://www.tiktok.com/@creator/video/7291#comments", "https://tiktok.com/@creator/video/7291")]
    [InlineData("  https://WWW.YouTube.com/watch?v=abc123&feature=share  ", "https://youtube.com/watch?v=abc123")]
    [InlineData("https://instagram.com./p/Cx1abc/", "https://instagram.com/p/Cx1abc")]
    [InlineData("https://www.m.instagram.com:443/p/Cx1abc", "https://instagram.com/p/Cx1abc")]
    [InlineData("https://mobile.twitter.com./brand/status/1", "https://x.com/brand/status/1")]
    public void Variants_of_the_same_post_normalize_identically(string raw, string expected)
    {
        Assert.Equal(expected, Normalization.PostUrl(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://instagram.com/p/1")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("https://user:secret@instagram.com/p/1")]
    public void Invalid_urls_return_null(string? raw)
    {
        Assert.Null(Normalization.PostUrl(raw));
    }

    [Fact]
    public void Content_hash_ignores_case_whitespace_and_punctuation()
    {
        var a = Normalization.ContentHash("Loving the new #Brand app!! 🚀 #ad");
        var b = Normalization.ContentHash("loving the new brand app   ad");
        Assert.NotNull(a);
        Assert.Equal(a, b);
        Assert.Null(Normalization.ContentHash("  !!! "));
    }

    [Fact]
    public void Handles_and_emails_are_normalized()
    {
        Assert.Equal("creator.one", Normalization.Handle("  @Creator.One "));
        Assert.Equal("SARA@EXAMPLE.COM", Normalization.Email(" sara@Example.com "));
    }
}

public sealed class MoneyTests
{
    [Theory]
    [InlineData(10.005, "USD", 10.01)]
    [InlineData(10.004, "USD", 10.00)]
    [InlineData(-10.005, "USD", -10.01)]
    [InlineData(1234.5, "JPY", 1235)]
    [InlineData(1.2345, "KWD", 1.235)]
    [InlineData(1.2344, "BHD", 1.234)]
    public void Rounds_to_currency_minor_units_away_from_zero(decimal amount, string currency, decimal expected)
    {
        Assert.Equal(expected, Money.Round(amount, currency));
    }

    [Fact]
    public void Convert_applies_rate_then_rounds_in_target_currency()
    {
        Assert.Equal(16.28m, Money.Convert(15m, 1.0853m, "USD"));
        Assert.Equal(2244m, Money.Convert(15m, 149.63m, "JPY"));
    }

    [Theory]
    [InlineData(24.5, "USD", "24.50 USD")]
    [InlineData(12.345, "KWD", "12.345 KWD")]
    [InlineData(1500, "JPY", "1,500 JPY")]
    [InlineData(1234.565, "GBP", "1,234.57 GBP")]
    public void Format_shows_the_currency_minor_units(decimal amount, string currency, string expected) =>
        Assert.Equal(expected, Money.Format(amount, currency));

    [Theory]
    [InlineData("USD", true)]
    [InlineData("usd", true)]
    [InlineData("XYZ", false)]
    [InlineData("US", false)]
    [InlineData(null, false)]
    public void Supported_currencies(string? code, bool supported) => Assert.Equal(supported, Money.IsSupported(code));
}

public sealed class IdGeneratorTests
{
    [Fact]
    public void Ids_are_version_7_and_time_ordered()
    {
        var first = IdGenerator.NewId(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));
        var second = IdGenerator.NewId(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_001));

        Assert.Equal('7', first.ToString()[14]);
        Assert.True(string.CompareOrdinal(first.ToString(), second.ToString()) < 0);
        Assert.NotEqual(IdGenerator.NewId(), IdGenerator.NewId());
    }
}

public sealed class CsvTests
{
    [Fact]
    public void Escapes_separators_quotes_and_newlines()
    {
        var csv = Csv.Write(new[] { "name", "note" }, new[] { new object?[] { "Khan, Sara", "said \"hi\"\nthere" } });
        Assert.Equal("name,note\r\n\"Khan, Sara\",\"said \"\"hi\"\"\nthere\"\r\n", csv);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "'=HYPERLINK(\"http://evil\")")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("  =1+1", "'  =1+1")]
    [InlineData(" @SUM(A1)", "' @SUM(A1)")]
    [InlineData("-10.50", "-10.50")]
    [InlineData(" -3", " -3")]
    [InlineData("plain", "plain")]
    public void Neutralizes_formula_injection_but_keeps_negative_numbers(string value, string expected)
    {
        Assert.Equal(expected, Csv.Escape(value).Trim('"').Replace("\"\"", "\""));
    }

    [Fact]
    public void Formats_values_invariantly()
    {
        var csv = Csv.Write(new[] { "amount", "at" },
            new[] { new object?[] { 1234.5m, new DateTime(2026, 9, 20, 23, 59, 59, DateTimeKind.Utc) } });
        Assert.Contains("1234.5,2026-09-20T23:59:59Z", csv);
    }
}

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("Horizon-Tulip-42")]
    [InlineData("correct horse battery staple")]
    public void Accepts_strong_passwords(string password) => PasswordPolicy.Validate(password, "sara@example.com");

    [Theory]
    [InlineData("password123")]
    [InlineData("aaaaaaaaaaaa")]
    [InlineData("short")]
    [InlineData("sarakhan-2026!")]
    public void Rejects_weak_passwords(string password)
    {
        var ex = Assert.Throws<DomainException>(() => PasswordPolicy.Validate(password, "sarakhan@example.com"));
        Assert.Equal("auth.weak_password", ex.Code);
        Assert.NotNull(ex.Errors);
    }
}
