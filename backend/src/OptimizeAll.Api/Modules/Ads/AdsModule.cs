using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Domain.Ads;

namespace OptimizeAll.Api.Modules.Ads;

public static class AdsModule
{
    /// <summary>Registers the Ads module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddAdsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdsOptions>().Configure<IConfiguration>((o, config) => config.GetSection(AdsOptions.Section).Bind(o));
        services.AddScoped<AdsKpiService>();
        services.AddScoped<AdMetricWriter>();
        services.AddScoped<PacingService>();
        services.AddScoped<NamingService>();
        services.AddScoped<AdsSyncService>();

        // Reporting adapters (the registry uses the last registration per platform).
        services.AddHttpClient(GoogleAdsReportingProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient(MetaAdsReportingProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddScoped<IAdsReportingProvider, GoogleAdsReportingProvider>();
        services.AddScoped<IAdsReportingProvider, MetaAdsReportingProvider>();
        foreach (var platform in new[] { AdPlatform.TikTokAds, AdPlatform.LinkedInAds, AdPlatform.MicrosoftAds, AdPlatform.SnapchatAds })
            services.AddScoped<IAdsReportingProvider>(_ => new NotConfiguredAdsProvider(platform));
        services.AddScoped<AdsProviderRegistry>();

        services.AddRecurringJob<AdsSyncJob>(TimeSpan.FromHours(6));
        services.AddRecurringJob<AdsAlertJob>(TimeSpan.FromHours(24));
        return services;
    }
}
