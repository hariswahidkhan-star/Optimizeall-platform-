namespace OptimizeAll.Api.Modules.Payouts;

public static class PayoutsModule
{
    /// <summary>Registers the Payouts module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddPayoutsModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
