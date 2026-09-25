using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Website.Public;

namespace OptimizeAll.Api.Modules.Website.Partners;

/// <summary>Partners and sponsored placements (docs/WEBSITE.md "Partners and sponsored placements").</summary>
public static class PartnersModule
{
    public static IServiceCollection AddWebsitePartners(this IServiceCollection services)
    {
        services.AddScoped<PartnerAdminService>();
        services.AddScoped<PartnerPublicService>();
        services.AddScoped<PartnerTrackingService>();
        services.AddScoped<ISitemapContributor, PartnerSitemapContributor>();
        services.AddScoped<ISeeder, PartnerBaselineSeeder>();
        return services;
    }
}
