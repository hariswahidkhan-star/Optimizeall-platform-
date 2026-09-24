using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Hosting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Website;
using OptimizeAll.Api.Modules.Crm;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Api.Modules.EmailMarketing;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Api.Modules.Ads;
using OptimizeAll.Api.Modules.Seo;
using OptimizeAll.Api.Modules.LandingPages;
using OptimizeAll.Api.Modules.Integrations;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Admin;
using OptimizeAll.Api.Modules.Analytics;
using OptimizeAll.Api.Modules.Auth;
using OptimizeAll.Api.Modules.Campaigns;
using OptimizeAll.Api.Modules.Content;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Ledger;
using OptimizeAll.Api.Modules.Marketing;
using OptimizeAll.Api.Modules.Notifications;
using OptimizeAll.Api.Modules.Payouts;
using OptimizeAll.Api.Modules.Retention;
using OptimizeAll.Api.Modules.Review;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Api.Modules.Seed;
using OptimizeAll.Api.Modules.Social;
using OptimizeAll.Api.Modules.Submissions;
using OptimizeAll.Api.Modules.Support;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var services = builder.Services;

// ---------- Options ----------
services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
services.Configure<SecurityOptions>(config.GetSection(SecurityOptions.Section));
services.Configure<EmailOptions>(config.GetSection(EmailOptions.Section));
services.Configure<DatabaseOptions>(config.GetSection(DatabaseOptions.Section));
services.Configure<BootstrapOptions>(config.GetSection(BootstrapOptions.Section));
services.Configure<JobOptions>(config.GetSection(JobOptions.Section));
services.Configure<DevToolsOptions>(config.GetSection(DevToolsOptions.Section));

// ---------- Persistence ----------
services.AddSingleton(TimeProvider.System);
// Configuration is read lazily (per service resolution) so hosts/tests can override it before the app starts.
// Database:Provider = MySql (default) | Sqlite; see DatabaseConnection.
services.AddDbContext<AppDbContext>((sp, options) =>
    DatabaseConnection.Configure(options, sp.GetRequiredService<IConfiguration>()));
services.AddSingleton<IDatabaseDialect>(sp =>
    DatabaseDialects.For(DatabaseConnection.Provider(sp.GetRequiredService<IConfiguration>())));

services.AddDataProtection()
    .SetApplicationName("OptimizeAll")
    .PersistKeysToDbContext<AppDbContext>();
// SQLite: the key ring lives in files next to the database file instead. SQLite has one writer at a time, and the key
// ring is written through its own DbContext, so creating a key while a request holds a write transaction (e.g.
// encrypting a payout destination) would wait for that same request.
services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
    .Configure<IConfiguration, ILoggerFactory>((o, cfg, loggers) =>
    {
        if (DatabaseConnection.Provider(cfg) == DatabaseProvider.Sqlite)
            o.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(
                new DirectoryInfo(DatabaseConnection.SqliteKeyDirectory(cfg)), loggers);
    });

// ---------- Security ----------
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<Microsoft.Extensions.Options.IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.GetKey(),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = AppClaims.UserId,
            RoleClaimType = AppClaims.Role,
        };
        // Reject tokens of suspended users or after a password change/forced sign-out, without waiting for expiry.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var sub = context.Principal?.FindFirst(AppClaims.UserId)?.Value;
                var sv = context.Principal?.FindFirst(AppClaims.SecurityVersion)?.Value;
                if (!Guid.TryParse(sub, out var userId)) { context.Fail("invalid subject"); return; }
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var state = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId)
                    .Select(u => new { u.Status, u.SecurityVersion, u.PermissionVersion }).FirstOrDefaultAsync(context.HttpContext.RequestAborted);
                if (state is null || state.Status != UserStatus.Active || state.SecurityVersion.ToString() != sv)
                {
                    context.Fail("session revoked");
                    return;
                }
                // Effective permissions (built-in + custom roles) for this request, keyed by the current permission version.
                context.HttpContext.Items[PermissionResolver.PermissionVersionItem] = (userId, state.PermissionVersion);
                await context.HttpContext.RequestServices.GetRequiredService<IPermissionResolver>()
                    .ResolveAsync(context.Principal!, context.HttpContext.RequestAborted);
            },
        };
    });

// Token lifetime is evaluated against the injectable clock (consistent with every other time rule and testable).
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<TimeProvider>((o, clock) =>
    o.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, parameters) =>
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return (notBefore is null || notBefore.Value <= now.Add(parameters.ClockSkew)) &&
               (expires is null || expires.Value >= now.Subtract(parameters.ClockSkew));
    });

services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
services.AddScoped<IAuthorizationHandler, PermissionHandler>();
services.AddSingleton<CustomRolePermissionCache>();
services.AddScoped<IPermissionResolver, PermissionResolver>();
services.AddScoped<IPermissionDirectory, PermissionDirectory>();
// Default deny: every endpoint needs a signed-in user unless it opts out with [AllowAnonymous] (public pages, auth
// flows, tracking redirects, health checks) or declares a stricter [HasPermission].
services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

services.AddHttpContextAccessor();
services.AddScoped<ICurrentUser, HttpCurrentUser>();
services.AddScoped<IClientScope, ClientScope>();
services.AddScoped<ICredentialVault, CredentialVault>();
services.AddSingleton<ITokenService, TokenService>();
services.AddSingleton<IPrivacyHasher, PrivacyHasher>();
services.AddSingleton<ImageUrlPolicy>();
services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
services.AddAppRateLimiting();

services.AddCors();
services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
    .Configure<Microsoft.Extensions.Options.IOptions<SecurityOptions>>((options, security) =>
        options.AddDefaultPolicy(policy =>
        {
            if (security.Value.AllowedOrigins.Length > 0)
                policy.WithOrigins(security.Value.AllowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }));

// ---------- Cross-cutting services ----------
services.AddScoped<IAuditLogger, AuditLogger>();
services.AddSingleton<IEventPublisher, EventPublisher>();
services.AddScoped<ISettingsService, SettingsService>();
services.AddScoped<INotificationService, NotificationService>();
services.AddScoped<IPayoutScheduleProvider, PayoutScheduleProvider>();
services.AddScoped<IExchangeRateProvider, ExchangeRateProvider>();
services.AddScoped<ILedgerWriter, LedgerWriter>();
services.AddSingleton<JobRunner>();
services.AddScoped<IAuthService, AuthService>();

services.AddSingleton<SmtpEmailSender>();
services.AddSingleton<FileEmailSender>();
services.AddSingleton<IEmailSender>(sp =>
{
    var mode = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>().Value.Mode;
    if (sp.GetRequiredService<IHostEnvironment>().IsProduction() && mode != "Smtp")
        throw new InvalidOperationException("Production requires Email:Mode=Smtp.");
    return mode == "Smtp" ? sp.GetRequiredService<SmtpEmailSender>() : sp.GetRequiredService<FileEmailSender>();
});

// ---------- Modules ----------
services
    .AddAccountsModule(config)
    .AddSocialModule(config)
    .AddContentModule(config)
    .AddNotificationsModule(config)
    .AddSupportModule(config)
    .AddAdminModule(config)
    .AddCampaignsModule(config)
    .AddRewardsModule(config)
    .AddSubmissionsModule(config)
    .AddReviewModule(config)
    .AddFilesModule(config)
    .AddLedgerModule(config)
    .AddPayoutsModule(config)
    .AddMarketingModule(config)
    .AddAnalyticsModule(config)
    .AddRetentionModule(config)
    .AddWebsiteModule(config)
    .AddCrmModule(config)
    .AddBillingModule(config)
    .AddClientsModule(config)
    .AddProjectsModule(config)
    .AddEmailMarketingModule(config)
    .AddSocialMediaModule(config)
    .AddAdsModule(config)
    .AddSeoModule(config)
    .AddLandingPagesModule(config)
    .AddIntegrationsModule(config)
    .AddSeedModule(config);

// ---------- HTTP ----------
services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });
services.AddProblemDetails();
services.AddExceptionHandler<ProblemExceptionHandler>();
services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: new[] { "ready" });
// X-Forwarded-* is only honoured from trusted reverse proxies (Hosting:TrustedProxies / Hosting:TrustedNetworks,
// e.g. the nginx container's network); otherwise clients could spoof their IP to evade rate limits and audit.
services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((o, cfg) =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    foreach (var proxy in cfg.GetSection("Hosting:TrustedProxies").Get<string[]>() ?? Array.Empty<string>())
        o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    foreach (var network in cfg.GetSection("Hosting:TrustedNetworks").Get<string[]>() ?? Array.Empty<string>())
    {
        var parts = network.Split('/');
        o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse(parts[0]), int.Parse(parts[1])));
    }
});
services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 12 * 1024 * 1024);

services.AddEndpointsApiExplorer();
services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Optimize All API",
        Version = "v1",
        Description = "Paid social sharing campaigns: participants, campaigns, submissions, review, ledger and payouts.",
    });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Description = "Access token from POST /api/v1/auth/login",
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>(),
    });
    o.CustomSchemaIds(t => t.FullName!.Replace("OptimizeAll.Api.Modules.", string.Empty).Replace('+', '.'));
    var xml = Path.Combine(AppContext.BaseDirectory, "OptimizeAll.Api.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSecurityHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();

if (app.Configuration.GetValue("Swagger:Enabled", !app.Environment.IsProduction()))
{
    app.UseSwagger(o => o.RouteTemplate = "api/docs/{documentName}/openapi.json");
    app.UseSwaggerUI(o =>
    {
        o.RoutePrefix = "api/docs";
        o.SwaggerEndpoint("/api/docs/v1/openapi.json", "Optimize All API v1");
    });
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new()
{
    Predicate = c => c.Tags.Contains("ready"),
    ResultStatusCodes = { [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable },
}).AllowAnonymous();

if (app.Configuration.GetValue("Database:InitializeOnStartup", true))
    await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
