using OptimizeAll.Api.Common.Jobs;

namespace OptimizeAll.Api.Modules.Integrations;

public static class IntegrationsModule
{
    /// <summary>Registers the Integrations module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(IntegrationVerifier.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<IntegrationVerifier>();
        services.AddRecurringJob<IntegrationExpiryJob>(TimeSpan.FromHours(24));
        return services;
    }
}
