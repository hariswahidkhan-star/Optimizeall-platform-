using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Payouts.Providers;

namespace OptimizeAll.Api.Modules.Payouts;

public static class PayoutsModule
{
    /// <summary>Registers the Payouts module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddPayoutsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Payment provider integration layer. Add real providers here (services.AddSingleton<IPaymentProvider, WiseProvider>()).
        services.AddSingleton<IPaymentProvider, ManualPaymentProvider>();
        services.AddOptions<PaymentOptions>()
            .Configure<IConfiguration>((options, config) => config.GetSection(PaymentOptions.Section).Bind(options))
            .Validate<IEnumerable<IPaymentProvider>>(
                (options, providers) => providers.Any(p => string.Equals(p.Key, options.Provider, StringComparison.OrdinalIgnoreCase)),
                "Payments:Provider must name a registered payment provider (default \"manual\").")
            .ValidateOnStart();
        services.AddSingleton<IPaymentProviderRegistry, PaymentProviderRegistry>();
        services.AddScoped<PaymentDispatcher>();

        services.AddScoped<PayoutBatchService>();
        services.AddScoped<PayoutPaymentService>();
        services.AddScoped<ISeeder, PayoutsBaselineSeeder>();
        services.AddRecurringJob<PayoutPreparationJob>(TimeSpan.FromMinutes(15));
        return services;
    }
}
