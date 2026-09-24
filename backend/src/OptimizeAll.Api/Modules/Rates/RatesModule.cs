namespace OptimizeAll.Api.Modules.Rates;

public static class RatesModule
{
    /// <summary>Registers person-level pricing: rate cards, rate groups, assignments and rate resolution.</summary>
    public static IServiceCollection AddRatesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPersonalRateService, PersonalRateService>();
        services.AddScoped<IRateAssignmentsService, RateAssignmentsService>();
        services.AddScoped<IRateCardsService, RateCardsService>();
        services.AddScoped<IRateGroupsService, RateGroupsService>();
        services.AddScoped<IPersonRatesService, PersonRatesService>();
        return services;
    }
}
