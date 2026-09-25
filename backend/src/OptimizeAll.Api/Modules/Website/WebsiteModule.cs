using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Website.Blog;
using OptimizeAll.Api.Modules.Website.Careers;
using OptimizeAll.Api.Modules.Website.Catalog;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Api.Modules.Website.Pages;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Api.Modules.Website.Seed;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Api.Modules.Website.SiteSeo;
using OptimizeAll.Domain.Events;

namespace OptimizeAll.Api.Modules.Website;

public static class WebsiteModule
{
    /// <summary>Registers the Website module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddWebsiteModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<WebsiteRules>();
        services.AddScoped<CmsStore>();
        services.AddScoped<PageBlockValidator>();
        services.AddScoped<SiteSettingsService>();
        services.AddScoped<CatalogAdminService>();
        services.AddScoped<IServiceCatalog, ServiceCatalog>();
        services.AddScoped<PublicSiteService>();
        services.AddScoped<BlogService>();
        services.AddScoped<CareersService>();
        services.AddSingleton<FormGuard>();
        services.AddScoped<FormTokenLedger>();
        services.AddScoped<InquiryService>();
        services.AddScoped<BookingService>();
        services.AddScoped<NewsletterService>();
        services.AddScoped<OverviewService>();
        services.AddScoped<IEventHandler<WebsiteInquiryReceived>, InquiryNotificationHandler>();
        services.AddSiteSeo();

        services.AddRecurringJob<BlogSchedulerJob>(TimeSpan.FromMinutes(1));
        services.AddRecurringJob<UsedFormTokenCleanupJob>(TimeSpan.FromHours(1));

        services.AddScoped<ISeeder, WebsiteBaselineSeeder>();
        services.AddScoped<ISeeder, WebsiteDemoSeeder>();
        return services;
    }
}
