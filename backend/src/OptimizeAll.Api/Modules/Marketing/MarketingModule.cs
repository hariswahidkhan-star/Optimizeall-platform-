using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Marketing.Achievements;
using OptimizeAll.Api.Modules.Marketing.Experiments;
using OptimizeAll.Api.Modules.Marketing.Referrals;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Api.Modules.Marketing.Tracking;
using OptimizeAll.Domain.Events;

namespace OptimizeAll.Api.Modules.Marketing;

public static class MarketingModule
{
    /// <summary>Registers the Marketing module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddMarketingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<MarketingUrls>();
        services.AddScoped<ReferralService>();
        services.AddScoped<AchievementEvaluator>();
        services.AddScoped<ExperimentAssignmentService>();
        services.AddScoped<TrackingService>();
        services.AddScoped<ISeeder, AchievementSeeder>();

        // Referrals & invitations
        services.AddScoped<IEventHandler<UserRegistered>, InvitationUseHandler>();
        services.AddScoped<IEventHandler<UserRegistered>, ReferralRegistrationHandler>();
        services.AddScoped<IEventHandler<EmailVerified>, ReferralEmailVerifiedHandler>();
        services.AddScoped<IEventHandler<SubmissionApproved>, ReferralSubmissionApprovedHandler>();
        services.AddScoped<IEventHandler<PayoutItemPaid>, ReferralPayoutPaidHandler>();
        services.AddScoped<IEventHandler<SubmissionReversed>, ReferralSubmissionReversedHandler>();
        services.AddRecurringJob<ReferralExpiryJob>(TimeSpan.FromHours(1));

        // Achievements
        services.AddScoped<IEventHandler<SubmissionApproved>, AchievementSubmissionApprovedHandler>();
        services.AddScoped<IEventHandler<PayoutItemPaid>, AchievementPayoutPaidHandler>();

        return services;
    }
}
