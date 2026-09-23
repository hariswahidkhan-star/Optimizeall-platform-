namespace OptimizeAll.Api.Modules.Submissions;

public static class SubmissionsModule
{
    /// <summary>Registers the Submissions module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddSubmissionsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISubmissionService, SubmissionService>();
        return services;
    }
}
