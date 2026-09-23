using Microsoft.Extensions.Options;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Payouts;

namespace OptimizeAll.Api.Modules.Payouts.Providers;

/// <summary>What a payment provider can do. The manual provider can do neither.</summary>
public sealed record PaymentProviderCapabilities(bool SupportsAutomaticTransfer, bool SupportsWebhooks);

/// <summary>One payout item to send. <see cref="IdempotencyKey"/> must be passed to the provider unchanged.</summary>
public sealed record PaymentDispatchRequest(
    Guid PayoutItemId,
    Guid UserId,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    PayoutMethod? DestinationMethod,
    string? MaskedDestination);

/// <summary>
/// Outcome of a dispatch. <c>Succeeded</c> means the provider confirmed the money was sent (the item is then recorded
/// as paid through the same atomic path as a manual payment); <c>Submitted</c> means accepted and awaiting a webhook;
/// <c>RequiresManualAction</c> means a person must pay and record the reference.
/// </summary>
public sealed record PaymentDispatchResult(PaymentAttemptStatus Status, string? ProviderReference, string Message);

/// <summary>A payment rail (manual, Wise, PayPal Payouts, ...). Implementations must be idempotent per IdempotencyKey.</summary>
public interface IPaymentProvider
{
    string Key { get; }
    PaymentProviderCapabilities Capabilities { get; }
    Task<PaymentDispatchResult> DispatchAsync(PaymentDispatchRequest request, CancellationToken ct);
}

/// <summary>
/// The default provider: it cannot move money. Every dispatch asks finance to pay outside the platform and record
/// the payment reference. It never reports <see cref="PaymentAttemptStatus.Succeeded"/>.
/// </summary>
public sealed class ManualPaymentProvider : IPaymentProvider
{
    public const string ProviderKey = "manual";
    public const string ManualActionMessage = "Pay manually and record the payment reference";

    public string Key => ProviderKey;

    public PaymentProviderCapabilities Capabilities { get; } = new(SupportsAutomaticTransfer: false, SupportsWebhooks: false);

    public Task<PaymentDispatchResult> DispatchAsync(PaymentDispatchRequest request, CancellationToken ct) =>
        Task.FromResult(new PaymentDispatchResult(PaymentAttemptStatus.RequiresManualAction, null, ManualActionMessage));
}

public sealed class PaymentOptions
{
    public const string Section = "Payments";

    /// <summary>Key of the active <see cref="IPaymentProvider"/>. Default "manual".</summary>
    public string Provider { get; set; } = ManualPaymentProvider.ProviderKey;
}

public interface IPaymentProviderRegistry
{
    /// <summary>The provider configured in <c>Payments:Provider</c>.</summary>
    IPaymentProvider Active { get; }

    IPaymentProvider Get(string key);
}

public sealed class PaymentProviderRegistry : IPaymentProviderRegistry
{
    private readonly Dictionary<string, IPaymentProvider> _providers;

    public PaymentProviderRegistry(IEnumerable<IPaymentProvider> providers, IOptions<PaymentOptions> options)
    {
        _providers = providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
        var key = options.Value.Provider;
        if (string.IsNullOrWhiteSpace(key) || !_providers.TryGetValue(key, out var active))
            throw new InvalidOperationException(
                $"Payments:Provider '{key}' is not a registered payment provider. Known: {string.Join(", ", _providers.Keys)}.");
        Active = active;
    }

    public IPaymentProvider Active { get; }

    public IPaymentProvider Get(string key) =>
        _providers.TryGetValue(key, out var provider)
            ? provider
            : throw new InvalidOperationException($"Payment provider '{key}' is not registered.");
}
