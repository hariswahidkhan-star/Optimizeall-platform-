using OptimizeAll.Api.Common.Events;
using OptimizeAll.Domain.Events;

namespace OptimizeAll.Api.Modules.Clients;

public static class ClientsModule
{
    /// <summary>Registers the Clients module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddClientsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ClientService>();
        services.AddScoped<OnboardingTemplateService>();
        services.AddScoped<ClientRelationshipService>();
        services.AddScoped<ClientHealthService>();
        services.AddScoped<IEventHandler<InvoicePaid>, InvoicePaidHealthHandler>();
        return services;
    }
}
