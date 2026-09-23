using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Events;

namespace OptimizeAll.Api.Modules.Crm;

public static class CrmModule
{
    /// <summary>Registers the Crm module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddCrmModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<LeadScoringService>();
        services.AddScoped<CrmService>();
        services.AddScoped<ContactImportService>();
        services.AddScoped<InboundLeadService>();
        services.AddScoped<ProposalService>();
        services.AddScoped<ProposalAcceptanceService>();

        services.AddScoped<IEventHandler<WebsiteInquiryReceived>, WebsiteInquiryLeadHandler>();
        services.AddScoped<IEventHandler<FormSubmitted>, FormSubmittedLeadHandler>();
        services.AddScoped<IEventHandler<ContactEngagementRecorded>, ContactEngagementHandler>();

        services.AddRecurringJob<CrmTaskReminderJob>(TimeSpan.FromMinutes(5));
        services.AddScoped<ISeeder, CrmBaselineSeeder>();
        services.AddScoped<ISeeder, CrmBillingDemoSeeder>();
        return services;
    }
}
