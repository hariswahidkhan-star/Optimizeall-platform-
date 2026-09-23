namespace OptimizeAll.Api.Modules.Accounts;

public static class AccountsModule
{
    /// <summary>Registers the Accounts module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddAccountsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IPayoutDestinationReader, PayoutDestinationReader>();
        services.AddScoped<IParticipantStateService, ParticipantStateService>();
        services.AddScoped<IHomeService, HomeService>();
        return services;
    }
}
