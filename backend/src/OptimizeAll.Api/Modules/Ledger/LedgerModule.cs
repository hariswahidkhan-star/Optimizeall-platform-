namespace OptimizeAll.Api.Modules.Ledger;

public static class LedgerModule
{
    /// <summary>Registers the Ledger module's services, jobs and event handlers.</summary>
    /// <remarks>
    /// The ledger core (ILedgerWriter, IPayoutScheduleProvider, IExchangeRateProvider) lives in Api/Common/Ledger and is
    /// registered in Program.cs because other modules depend on it.
    /// </remarks>
    public static IServiceCollection AddLedgerModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IEarningsSummaryService, EarningsSummaryService>();
        services.AddScoped<LedgerAdminService>();
        return services;
    }
}
