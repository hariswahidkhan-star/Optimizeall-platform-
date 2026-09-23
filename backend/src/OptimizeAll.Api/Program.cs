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
var connectionString = config.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
services.AddSingleton(TimeProvider.System);
services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)), mysql =>
    {
        mysql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        mysql.CommandTimeout(60);
    }));

services.AddDataProtection()
    .SetApplicationName("OptimizeAll")
    .PersistKeysToDbContext<AppDbContext>();

// ---------- Security ----------
var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
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
                    .Select(u => new { u.Status, u.SecurityVersion }).FirstOrDefaultAsync(context.HttpContext.RequestAborted);
                if (state is null || state.Status != UserStatus.Active || state.SecurityVersion.ToString() != sv)
                    context.Fail("session revoked");
            },
        };
    });

services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
services.AddAuthorization(options =>
{
    options.FallbackPolicy = null;
});

services.AddHttpContextAccessor();
services.AddScoped<ICurrentUser, HttpCurrentUser>();
services.AddSingleton<ITokenService, TokenService>();
services.AddSingleton<IPrivacyHasher, PrivacyHasher>();
services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
services.AddAppRateLimiting(config);

var allowedOrigins = config.GetSection("Security:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
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

var emailMode = config.GetValue<string>("Email:Mode") ?? "File";
if (builder.Environment.IsProduction() && emailMode != "Smtp")
    throw new InvalidOperationException("Production requires Email:Mode=Smtp.");
if (emailMode == "Smtp") services.AddSingleton<IEmailSender, SmtpEmailSender>();
else services.AddSingleton<IEmailSender, FileEmailSender>();

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
services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
    o.ForwardLimit = config.GetValue("Hosting:ForwardLimit", 1);
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

if (config.GetValue("Swagger:Enabled", !app.Environment.IsProduction()))
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
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new()
{
    Predicate = c => c.Tags.Contains("ready"),
    ResultStatusCodes = { [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable },
});

if (config.GetValue("Database:InitializeOnStartup", true))
    await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
