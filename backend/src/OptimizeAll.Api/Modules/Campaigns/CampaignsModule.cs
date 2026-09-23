using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;

namespace OptimizeAll.Api.Modules.Campaigns;

public static class CampaignsModule
{
    /// <summary>Registers the Campaigns module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddCampaignsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IParticipantEligibility, ParticipantEligibility>();
        services.AddScoped<ICampaignCatalogService, CampaignCatalogService>();
        services.AddScoped<ICampaignAdminService, CampaignAdminService>();
        services.AddScoped<ISeeder, CampaignCategorySeeder>();
        services.AddRecurringJob<CampaignScheduleJob>(TimeSpan.FromMinutes(1));
        return services;
    }
}
