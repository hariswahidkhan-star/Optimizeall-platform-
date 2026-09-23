using OptimizeAll.Api.Common.Persistence;

namespace OptimizeAll.Api.Modules.Seed;

public static class SeedModule
{
    /// <summary>
    /// Registers the Seed module's services. The "Demo" profile (<see cref="DemoSeeder"/>) creates a complete,
    /// internally consistent staging dataset; it only runs when <c>Database:Seed</c> lists "Demo".
    /// </summary>
    public static IServiceCollection AddSeedModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<DemoSeeder>();
        services.AddScoped<ISeeder>(sp => sp.GetRequiredService<DemoSeeder>());
        return services;
    }
}
