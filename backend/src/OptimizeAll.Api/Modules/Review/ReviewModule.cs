using OptimizeAll.Api.Common.Jobs;

namespace OptimizeAll.Api.Modules.Review;

public static class ReviewModule
{
    /// <summary>Registers the Review module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddReviewModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IReviewQueryService, ReviewQueryService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddRecurringJob<LiveCheckReminderJob>(TimeSpan.FromHours(1));
        return services;
    }
}
