using OptimizeAll.Api.Common.Jobs;

namespace OptimizeAll.Api.Modules.Retention;

public static class RetentionModule
{
    /// <summary>Registers the Retention module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddRetentionModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecurringJob<RetentionJob>(TimeSpan.FromMinutes(60));
        return services;
    }
}
