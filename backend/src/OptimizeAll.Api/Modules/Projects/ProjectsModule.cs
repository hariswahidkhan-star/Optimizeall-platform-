using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

public static class ProjectsModule
{
    /// <summary>Registers the Projects module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddProjectsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<DeliveryLookup>();
        services.AddScoped<DeliveryFileService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<DeliveryTemplateService>();
        services.AddScoped<TaskService>();
        services.AddScoped<DeliverableService>();
        services.AddScoped<TimeService>();
        services.AddScoped<ReportService>();
        services.AddScoped<CommunicationService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ClientPortalService>();

        // Report section providers (other modules add theirs the same way).
        services.AddScoped<IClientReportSection, DeliveryReportSection>();
        services.AddScoped<IClientReportSection, FeedbackReportSection>();

        services.AddRecurringJob<RecurringTaskJob>(TimeSpan.FromHours(1));
        services.AddRecurringJob<MonthlyReportDraftJob>(TimeSpan.FromHours(6));
        services.AddRecurringJob<DeliverableSlaJob>(TimeSpan.FromMinutes(15));

        services.AddScoped<ISeeder, DeliveryBaselineSeeder>();
        services.AddScoped<DeliveryDemoSeeder>();
        services.AddScoped<ISeeder>(sp => sp.GetRequiredService<DeliveryDemoSeeder>());
        return services;
    }
}
