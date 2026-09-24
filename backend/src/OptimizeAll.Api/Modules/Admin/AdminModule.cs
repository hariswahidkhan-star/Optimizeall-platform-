namespace OptimizeAll.Api.Modules.Admin;

public static class AdminModule
{
    /// <summary>Registers the Admin module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddAdminModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditLogService>();
        services.AddScoped<AdminUsersService>();
        services.AddScoped<TestUsersService>();
        services.AddScoped<AdminSettingsService>();
        services.AddScoped<AdminJobsService>();
        services.AddScoped<Roles.AdminRolesService>();
        return services;
    }
}
