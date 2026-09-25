using Microsoft.Extensions.DependencyInjection.Extensions;
using OptimizeAll.Api.Common.Jobs;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>Technical SEO for the public website: server rendering, sitemaps, robots.txt, llms.txt, IndexNow, SEO overview.</summary>
public static class SiteSeoModule
{
    public static IServiceCollection AddSiteSeo(this IServiceCollection services)
    {
        services.AddScoped<SeoSettingsService>();
        services.AddScoped<SeoPageResolver>();
        services.AddScoped<LlmsTxtService>();
        services.AddScoped<SeoOverviewService>();
        // The Website module's slug-redirect manager replaces this with its own lookup (registered before or after).
        services.TryAddScoped<ISeoRedirectLookup, NoSeoRedirects>();
        services.AddHttpClient(IndexNowJob.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddRecurringJob<IndexNowJob>(TimeSpan.FromMinutes(10));
        return services;
    }
}
