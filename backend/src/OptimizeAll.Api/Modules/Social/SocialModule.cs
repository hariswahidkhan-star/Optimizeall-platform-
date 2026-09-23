namespace OptimizeAll.Api.Modules.Social;

public static class SocialModule
{
    /// <summary>Registers the Social module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddSocialModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISocialAccountService, SocialAccountService>();
        return services;
    }
}
