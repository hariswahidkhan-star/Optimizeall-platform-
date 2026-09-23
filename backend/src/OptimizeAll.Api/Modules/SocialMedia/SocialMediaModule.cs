using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

public static class SocialMediaModule
{
    /// <summary>Registers the SocialMedia module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddSocialMediaModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SocialMediaOptions>().Configure<IConfiguration>((o, config) => config.GetSection(SocialMediaOptions.Section).Bind(o));

        services.AddScoped<SocialAccess>();
        services.AddScoped<NetworkPresetProvider>();
        services.AddScoped<SocialAppCredentials>();
        services.AddScoped<ProfileTokenStore>();
        services.AddScoped<OAuthStateKey>();
        services.AddScoped<SocialPostService>();
        services.AddScoped<SocialPublishingService>();
        services.AddScoped<SocialAnalyticsService>();
        services.AddScoped<SocialMetricImporter>();

        // Provider adapters. Named HTTP clients so tests/deployments can swap the primary handler.
        services.AddHttpClient(MetaGraphClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(XPublisher.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<MetaGraphClient>();
        services.AddScoped<ISocialPublisher, FacebookPagePublisher>();
        services.AddScoped<ISocialPublisher, InstagramPublisher>();
        services.AddScoped<ISocialPublisher, XPublisher>();
        foreach (var network in new[] { SocialNetwork.LinkedIn, SocialNetwork.TikTok, SocialNetwork.YouTube, SocialNetwork.Pinterest, SocialNetwork.GoogleBusiness })
            services.AddScoped<ISocialPublisher>(_ => new NotConfiguredPublisher(network));
        services.AddScoped<SocialPublisherRegistry>();

        services.AddScoped<ISocialOAuthClient, MetaOAuthClient>();
        services.AddScoped<ISocialOAuthClient, XOAuthClient>();
        services.AddScoped<ISocialOAuthClient, AuthorizeOnlyOAuthClient>();
        services.AddScoped<SocialOAuthRegistry>();

        services.AddScoped<ISocialListeningProvider, NotConfiguredListeningProvider>();
        services.AddScoped<ISocialInboxProvider, NotConfiguredInboxProvider>();

        services.AddRecurringJob<SocialPublishingJob>(TimeSpan.FromMinutes(1));
        services.AddRecurringJob<SocialEvergreenJob>(TimeSpan.FromHours(1));
        services.AddRecurringJob<SocialMetricsSyncJob>(TimeSpan.FromHours(24));

        services.AddScoped<ISeeder, SocialMediaBaselineSeeder>();
        services.AddScoped<ISeeder, SocialAdsDemoSeeder>();
        return services;
    }
}

/// <summary>
/// Baseline: best-practice network presets (limits, media specs, recommended posting times) and a small list of
/// holidays/awareness days with their sources. Idempotent; existing rows (possibly edited by staff) are kept.
/// </summary>
public sealed class SocialMediaBaselineSeeder(TimeProvider clock) : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 60;

    internal static readonly (int Month, int Day, int? Year, string Name, string[] Countries, string Source)[] AwarenessDays =
    {
        (2, 4, null, "World Cancer Day", Array.Empty<string>(), "https://www.worldcancerday.org/"),
        (3, 8, null, "International Women's Day", Array.Empty<string>(), "https://www.un.org/en/observances/womens-day"),
        (3, 20, null, "International Day of Happiness", Array.Empty<string>(), "https://www.un.org/en/observances/happiness-day"),
        (4, 7, null, "World Health Day", Array.Empty<string>(), "https://www.who.int/campaigns/world-health-day"),
        (4, 22, null, "Earth Day", Array.Empty<string>(), "https://www.earthday.org/"),
        (6, 5, null, "World Environment Day", Array.Empty<string>(), "https://www.un.org/en/observances/environment-day"),
        (6, 21, null, "International Day of Yoga", Array.Empty<string>(), "https://www.un.org/en/observances/yoga-day"),
        (9, 27, null, "World Tourism Day", Array.Empty<string>(), "https://www.untourism.int/world-tourism-day"),
        (10, 1, null, "International Coffee Day", Array.Empty<string>(), "https://www.ico.org/"),
        (10, 10, null, "World Mental Health Day", Array.Empty<string>(), "https://www.who.int/campaigns/world-mental-health-day"),
        (10, 16, null, "World Food Day", Array.Empty<string>(), "https://www.fao.org/world-food-day/en"),
        (11, 14, null, "World Diabetes Day", Array.Empty<string>(), "https://worlddiabetesday.org/"),
        (7, 4, null, "Independence Day (US)", new[] { "US" }, "https://www.opm.gov/policy-data-oversight/pay-leave/federal-holidays/"),
        (11, 26, 2026, "Thanksgiving Day (US)", new[] { "US" }, "https://www.opm.gov/policy-data-oversight/pay-leave/federal-holidays/"),
        (8, 31, 2026, "Summer bank holiday (England and Wales)", new[] { "GB" }, "https://www.gov.uk/bank-holidays"),
        (12, 26, null, "Boxing Day (UK)", new[] { "GB" }, "https://www.gov.uk/bank-holidays"),
        (12, 2, null, "UAE National Day (Eid Al Etihad)", new[] { "AE" }, "https://u.ae/en/about-the-uae/public-holidays"),
        (3, 23, null, "Pakistan Day", new[] { "PK" }, "https://cabinet.gov.pk/"),
        (8, 14, null, "Independence Day (Pakistan)", new[] { "PK" }, "https://cabinet.gov.pk/"),
    };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.Set<SocialNetworkPreset>().Select(p => p.Network).ToListAsync(ct);
        foreach (var preset in NetworkPresets.Defaults.Where(d => !existing.Contains(d.Network)))
            db.Set<SocialNetworkPreset>().Add(preset.ToEntity(now));

        var days = await db.Set<SocialAwarenessDay>().Select(d => new { d.Month, d.Day, d.Name }).ToListAsync(ct);
        foreach (var d in AwarenessDays.Where(d => !days.Any(x => x.Month == d.Month && x.Day == d.Day && x.Name == d.Name)))
            db.Set<SocialAwarenessDay>().Add(new SocialAwarenessDay
            {
                Month = d.Month, Day = d.Day, Year = d.Year, Name = d.Name, Countries = d.Countries.ToList(), SourceUrl = d.Source,
            });
        await db.SaveChangesAsync(ct);
    }
}
