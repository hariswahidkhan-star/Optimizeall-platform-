namespace OptimizeAll.Api.Modules.Auth.Google;

public static class GoogleSignInModule
{
    public static IServiceCollection AddGoogleSignIn(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<GoogleAuthOptions>(config.GetSection(GoogleAuthOptions.Section));
        services.AddHttpClient(GoogleEndpoints.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<GoogleJwksProvider>();
        services.AddSingleton<GoogleIdTokenValidator>();
        services.AddSingleton<GoogleFlowProtector>();
        services.AddScoped<GoogleOidcClient>();
        services.AddScoped<GoogleSignInService>();
        return services;
    }
}
