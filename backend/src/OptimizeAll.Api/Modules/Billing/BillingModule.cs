namespace OptimizeAll.Api.Modules.Billing;

public static class BillingModule
{
    /// <summary>Registers the Billing module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
