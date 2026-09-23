namespace OptimizeAll.Api.Modules.Support;

public static class SupportModule
{
    /// <summary>Registers the Support module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddSupportModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SupportService>();
        return services;
    }
}
