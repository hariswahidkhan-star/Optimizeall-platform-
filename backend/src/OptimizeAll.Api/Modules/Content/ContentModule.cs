using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Content.Copy;

namespace OptimizeAll.Api.Modules.Content;

public static class ContentModule
{
    /// <summary>Registers the Content module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddContentModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ContentService>();
        services.AddScoped<SiteCopyService>();
        services.AddScoped<ISeeder, ContentBaselineSeeder>();
        return services;
    }
}
