using OptimizeAll.Api.Common.Persistence;

namespace OptimizeAll.Api.Modules.Content;

public static class ContentModule
{
    /// <summary>Registers the Content module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddContentModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ContentService>();
        services.AddScoped<ISeeder, ContentBaselineSeeder>();
        return services;
    }
}
