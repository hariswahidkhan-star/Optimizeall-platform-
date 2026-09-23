using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Seo.Audit;
using OptimizeAll.Api.Modules.Seo.Backlinks;
using OptimizeAll.Api.Modules.Seo.Crawling;
using OptimizeAll.Api.Modules.Seo.Demo;
using OptimizeAll.Api.Modules.Seo.Http;
using OptimizeAll.Api.Modules.Seo.Ranking;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo;

public static class SeoModule
{
    /// <summary>Registers the Seo module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddSeoModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SeoCrawlerOptions>().Bind(configuration.GetSection(SeoCrawlerOptions.Section));
        services.AddSingleton<IHostResolver, DnsHostResolver>();
        services.AddHttpClient<SafeHttpFetcher>(SafeHttpFetcher.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(SafeHttpFetcher.CreateHandler)
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient<IRankTrackingProvider, DataForSeoRankProvider>(c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient<ISearchConsoleClient, GoogleSearchConsoleClient>(c => c.Timeout = TimeSpan.FromSeconds(60));

        services.AddScoped<SeoAccess>();
        services.AddScoped<SiteCrawler>();
        services.AddScoped<SeoAuditRunner>();
        services.AddScoped<RankStore>();
        services.AddScoped<BacklinkChecker>();

        services.AddRecurringJob<SeoAuditJob>(TimeSpan.FromMinutes(1));
        services.AddRecurringJob<RankTrackingJob>(TimeSpan.FromHours(24));
        services.AddRecurringJob<BacklinkCheckJob>(TimeSpan.FromHours(24));

        services.AddScoped<ISeeder, SeoBaselineSeeder>();
        services.AddScoped<ISeeder, AgencyToolkitDemoSeeder>();
        return services;
    }
}

/// <summary>Baseline: SEO audit rule copy and the local-SEO citation directory list (idempotent upserts by key).</summary>
public sealed class SeoBaselineSeeder : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 300;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var rules = await db.Set<SeoAuditRule>().ToDictionaryAsync(r => r.Key, ct);
        foreach (var r in SeoAuditRules.All)
        {
            if (rules.ContainsKey(r.Key)) continue;
            db.Add(new SeoAuditRule { Key = r.Key, Title = r.Title, Category = r.Category, Severity = r.Severity, WhyItMatters = r.WhyItMatters, HowToFix = r.HowToFix });
        }

        var sources = await db.Set<SeoCitationSource>().ToDictionaryAsync(s => s.Key, ct);
        var order = 0;
        foreach (var d in LocalSeoCatalog.Directories)
        {
            order += 10;
            if (sources.TryGetValue(d.Key, out var existing))
            {
                existing.SortOrder = order;
                continue;
            }
            db.Add(new SeoCitationSource { Key = d.Key, Name = d.Name, Url = d.Url, Category = d.Category, Countries = d.Countries.ToList(), SortOrder = order });
        }
        await db.SaveChangesAsync(ct);
    }
}
