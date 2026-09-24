using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Modules.Notifications.Templates;

namespace OptimizeAll.Api.Modules.Notifications;

public static class NotificationsModule
{
    /// <summary>Registers the Notifications module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WhatsAppOptions>().Configure<IConfiguration>((o, config) => config.GetSection(WhatsAppOptions.Section).Bind(o));
        services.AddScoped<NotificationCenterService>();
        services.AddScoped<EmailTemplateService>();
        services.AddScoped<AccountEmails>();

        // Channel adapters. The dispatch job uses the last registered sender per channel.
        services.AddScoped<INotificationChannelSender, EmailChannelSender>();
        services.AddHttpClient<WhatsAppChannelSender>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddTransient<INotificationChannelSender>(sp => sp.GetRequiredService<WhatsAppChannelSender>());

        services.AddRecurringJob<NotificationDispatchJob>(TimeSpan.FromSeconds(30));
        return services;
    }
}
