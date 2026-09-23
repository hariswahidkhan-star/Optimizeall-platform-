using Microsoft.Extensions.Options;
using OptimizeAll.Api.Modules.Payouts.Providers;
using OptimizeAll.Domain.Payouts;

namespace OptimizeAll.UnitTests.Payouts;

public sealed class PaymentProviderTests
{
    [Fact]
    public async Task Manual_provider_always_requires_manual_action_and_never_succeeds()
    {
        var provider = new ManualPaymentProvider();
        Assert.Equal("manual", provider.Key);
        Assert.False(provider.Capabilities.SupportsAutomaticTransfer);
        Assert.False(provider.Capabilities.SupportsWebhooks);
        var result = await provider.DispatchAsync(new PaymentDispatchRequest(Guid.NewGuid(), Guid.NewGuid(), 25m, "USD",
            "payout-item:x:dispatch", null, "••••1234"), CancellationToken.None);
        Assert.Equal(PaymentAttemptStatus.RequiresManualAction, result.Status);
        Assert.Null(result.ProviderReference);
        Assert.Equal("Pay manually and record the payment reference", result.Message);
    }

    [Fact]
    public void Registry_resolves_the_configured_provider_and_rejects_unknown_keys()
    {
        var providers = new IPaymentProvider[] { new ManualPaymentProvider() };
        Assert.Equal("manual", new PaymentProviderRegistry(providers, Options.Create(new PaymentOptions())).Active.Key);
        Assert.Equal("manual", new PaymentProviderRegistry(providers, Options.Create(new PaymentOptions { Provider = "MANUAL" })).Active.Key);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PaymentProviderRegistry(providers, Options.Create(new PaymentOptions { Provider = "wise" })));
        Assert.Contains("wise", ex.Message);
    }
}
