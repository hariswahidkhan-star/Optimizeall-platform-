namespace OptimizeAll.Api.Modules.Admin;

public static class AdminModule
{
    /// <summary>Registers the Admin module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddAdminModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditLogService>();
        services.AddScoped<AdminUsersService>();
        services.AddScoped<AdminSettingsService>();
        services.AddScoped<AdminJobsService>();
        return services;
    }
}
