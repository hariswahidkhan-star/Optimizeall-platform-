namespace OptimizeAll.Api.Modules.Ledger;

public static class LedgerModule
{
    /// <summary>Registers the Ledger module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddLedgerModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
