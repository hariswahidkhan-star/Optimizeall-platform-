using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.LandingPages.Templates;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.LandingPages;

public static class LandingPagesModule
{
    /// <summary>Registers the LandingPages module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddLandingPagesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<LandingPageService>();
        services.AddScoped<FormService>();
        services.AddScoped<FormSubmissionService>();
        services.AddSingleton<FormFileStore>();
        services.AddSingleton<FormRenderTokens>();
        services.AddHttpClient<CaptchaVerifier>(c => c.Timeout = TimeSpan.FromSeconds(10));

        services.AddRecurringJob<FormEmailDispatchJob>(TimeSpan.FromMinutes(1));
        services.AddRecurringJob<FormEventRetryJob>(TimeSpan.FromMinutes(5));

        services.AddScoped<ISeeder, LandingPagesBaselineSeeder>();
        return services;
    }
}

/// <summary>Baseline: landing-page and form templates (inserted when missing, refreshed by key otherwise).</summary>
public sealed class LandingPagesBaselineSeeder : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 310;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var forms = await db.Set<FormTemplate>().ToDictionaryAsync(t => t.Key, ct);
        foreach (var t in TemplateCatalog.Forms)
        {
            if (forms.TryGetValue(t.Key, out var existing))
            {
                existing.Name = t.Name;
                existing.Description = t.Description;
                existing.SchemaJson = t.SchemaJson;
                existing.SubmitLabel = t.SubmitLabel;
                existing.SuccessMessage = t.SuccessMessage;
                existing.ConsentText = t.ConsentText;
                existing.AutoresponderSubject = t.AutoresponderSubject;
                existing.AutoresponderBody = t.AutoresponderBody;
                existing.SortOrder = t.SortOrder;
            }
            else db.Add(Copy(t));
        }

        var pages = await db.Set<LandingPageTemplate>().ToDictionaryAsync(t => t.Key, ct);
        foreach (var t in TemplateCatalog.Pages)
        {
            if (pages.TryGetValue(t.Key, out var existing))
            {
                existing.Name = t.Name;
                existing.Category = t.Category;
                existing.Description = t.Description;
                existing.MetaTitle = t.MetaTitle;
                existing.MetaDescription = t.MetaDescription;
                existing.BlocksJson = t.BlocksJson;
                existing.FormTemplateKey = t.FormTemplateKey;
                existing.SortOrder = t.SortOrder;
            }
            else db.Add(new LandingPageTemplate
            {
                Key = t.Key, Name = t.Name, Category = t.Category, Description = t.Description, MetaTitle = t.MetaTitle,
                MetaDescription = t.MetaDescription, BlocksJson = t.BlocksJson, FormTemplateKey = t.FormTemplateKey, SortOrder = t.SortOrder,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static FormTemplate Copy(FormTemplate t) => new()
    {
        Key = t.Key, Name = t.Name, Description = t.Description, SchemaJson = t.SchemaJson, SubmitLabel = t.SubmitLabel,
        SuccessMessage = t.SuccessMessage, ConsentText = t.ConsentText, AutoresponderSubject = t.AutoresponderSubject,
        AutoresponderBody = t.AutoresponderBody, SortOrder = t.SortOrder,
    };
}
