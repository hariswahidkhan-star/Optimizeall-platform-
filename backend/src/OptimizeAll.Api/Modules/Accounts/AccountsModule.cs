namespace OptimizeAll.Api.Modules.Accounts;

public static class AccountsModule
{
    /// <summary>Registers the Accounts module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddAccountsModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
