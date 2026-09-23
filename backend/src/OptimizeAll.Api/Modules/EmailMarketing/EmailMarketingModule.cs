using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Automations;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Reporting;
using OptimizeAll.Api.Modules.EmailMarketing.Seed;
using OptimizeAll.Api.Modules.EmailMarketing.Segments;
using OptimizeAll.Api.Modules.EmailMarketing.Settings;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Api.Modules.EmailMarketing.Templates;
using OptimizeAll.Api.Modules.EmailMarketing.Tracking;
using OptimizeAll.Domain.Events;

namespace OptimizeAll.Api.Modules.EmailMarketing;

public static class EmailMarketingModule
{
    /// <summary>Registers the EmailMarketing module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddEmailMarketingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Shared
        services.AddSingleton<EmailMarketingUrls>();
        services.AddSingleton<TrackingTokens>();
        services.AddScoped<EmailAccess>();
        services.AddScoped<EmailSettingsStore>();
        services.AddScoped<MessageComposer>();

        // Provider adapters. The resolver picks the last registration per key, so a deployment or test can substitute one.
        services.AddScoped<IEmailMarketingProvider, SmtpEmailMarketingProvider>();
        services.AddHttpClient<SendGridEmailProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IEmailMarketingProvider>(sp => sp.GetRequiredService<SendGridEmailProvider>());
        services.AddHttpClient<MailgunEmailProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IEmailMarketingProvider>(sp => sp.GetRequiredService<MailgunEmailProvider>());
        services.AddScoped<IEmailMarketingProvider>(_ => new UnavailableEmailProvider("ses", "The Amazon SES adapter is not available in this release; use SMTP (SES SMTP credentials work), SendGrid or Mailgun."));
        services.AddScoped<IEmailMarketingProvider>(_ => new UnavailableEmailProvider("postmark", "The Postmark adapter is not available in this release; use SMTP (Postmark SMTP works), SendGrid or Mailgun."));
        services.AddScoped<EmailProviderResolver>();
        services.AddHttpClient<TwilioSmsProvider>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<ISmsProvider>(sp => sp.GetRequiredService<TwilioSmsProvider>());
        services.AddHttpClient<WhatsAppCloudTemplateProvider>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<IWhatsAppTemplateProvider>(sp => sp.GetRequiredService<WhatsAppCloudTemplateProvider>());
        services.AddScoped<ProviderStatus>();

        // Features
        services.AddScoped<AutomationTriggers>();
        services.AddScoped<AudienceService>();
        services.AddScoped<ImportService>();
        services.AddScoped<SegmentQueryBuilder>();
        services.AddScoped<SegmentService>();
        services.AddScoped<TemplateService>();
        services.AddScoped<CampaignAudience>();
        services.AddScoped<CampaignService>();
        services.AddScoped<EngagementService>();
        services.AddScoped<SignalsService>();
        services.AddScoped<WebhookService>();
        services.AddScoped<ReportService>();
        services.AddScoped<AutomationService>();
        services.AddScoped<EmailSettingsService>();

        // Jobs
        services.AddRecurringJob<CampaignSendJob>(TimeSpan.FromSeconds(30));
        services.AddRecurringJob<AutomationJob>(TimeSpan.FromMinutes(1));
        services.AddRecurringJob<SubscriberImportJob>(TimeSpan.FromSeconds(20));

        // Events from other modules
        services.AddScoped<IEventHandler<FormSubmitted>, FormSubmittedEmailHandler>();
        services.AddScoped<IEventHandler<NewsletterSubscribed>, NewsletterSubscribedEmailHandler>();

        // Seeds
        services.AddScoped<ISeeder, EmailBaselineSeeder>();
        services.AddScoped<ISeeder, EmailDemoSeeder>();
        return services;
    }
}
