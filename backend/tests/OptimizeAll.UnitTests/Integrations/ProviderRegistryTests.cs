using OptimizeAll.Api.Modules.Integrations;

namespace OptimizeAll.UnitTests.Integrations;

public sealed class ProviderRegistryTests
{
    private static readonly Dictionary<string, string> None = new();

    [Fact]
    public void Registry_lists_every_required_provider_with_unique_keys()
    {
        var expected = new[]
        {
            "meta", "x", "linkedin", "tiktok", "youtube", "pinterest", "google-business", "google-ads", "meta-ads", "tiktok-ads", "linkedin-ads",
            "microsoft-ads", "twilio", "whatsapp-cloud", "sendgrid", "mailgun", "dataforseo", "google-search-console", "stripe", "paypal", "hcaptcha", "turnstile",
        };
        Assert.Equal(expected.OrderBy(k => k), ProviderRegistry.All.Select(p => p.Key).OrderBy(k => k));
        Assert.All(ProviderRegistry.All, p =>
        {
            Assert.NotEmpty(p.HelpText);
            Assert.NotEmpty(p.Secrets);
            Assert.True(p.AgencyWide || p.PerClient, p.Key);
            Assert.Equal(p.Settings.Count + p.Secrets.Count, p.Settings.Concat(p.Secrets).Select(f => f.Key).Distinct().Count());
        });
    }

    [Fact]
    public void Required_fields_patterns_and_unknown_keys_are_enforced()
    {
        var twilio = ProviderRegistry.Find("twilio")!;
        var errors = ProviderRegistry.Validate(twilio,
            new Dictionary<string, string> { ["accountSid"] = "not-a-sid", ["colour"] = "blue" },
            new Dictionary<string, string> { ["password"] = "x" }, new HashSet<string>());
        Assert.Contains("settings.accountSid", errors.Keys);
        Assert.Contains("settings.fromNumber", errors.Keys);
        Assert.Contains("settings.colour", errors.Keys);
        Assert.Contains("secrets.password", errors.Keys);
        Assert.Contains("secrets.authToken", errors.Keys);

        var ok = ProviderRegistry.Validate(twilio,
            new Dictionary<string, string> { ["accountSid"] = "AC" + new string('a', 32), ["fromNumber"] = "+14155550100" },
            new Dictionary<string, string> { ["authToken"] = "token" }, new HashSet<string>());
        Assert.Empty(ok);
    }

    [Fact]
    public void Saved_secrets_satisfy_required_fields_on_update()
    {
        var dfs = ProviderRegistry.Find("dataforseo")!;
        Assert.NotEmpty(ProviderRegistry.Validate(dfs, None, None, new HashSet<string>()));
        Assert.Empty(ProviderRegistry.Validate(dfs, None, None, new HashSet<string> { "login", "password" }));
    }

    [Fact]
    public void Only_implemented_verifiers_claim_verification()
    {
        Assert.True(IntegrationVerifier.CanVerify("dataforseo"));
        Assert.True(IntegrationVerifier.CanVerify("stripe"));
        Assert.False(IntegrationVerifier.CanVerify("meta"));
        Assert.False(IntegrationVerifier.CanVerify("hcaptcha"));
    }
}
