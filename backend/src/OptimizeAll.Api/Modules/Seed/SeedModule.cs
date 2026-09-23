namespace OptimizeAll.Api.Modules.Seed;

public static class SeedModule
{
    /// <summary>Registers the Seed module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddSeedModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
