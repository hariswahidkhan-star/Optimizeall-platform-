using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Crm;
using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Clients;

public sealed class OnboardingTemplateItem
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "Other";
    public OnboardingOwner Owner { get; set; } = OnboardingOwner.Agency;
}

public sealed class OnboardingTemplateItemRequest
{
    /// <summary>Stable key; blank for a new item (derived from the title).</summary>
    [MaxLength(64)]
    public string? Key { get; set; }

    [Required, StringLength(200, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(64)]
    public string? Category { get; set; }

    public OnboardingOwner Owner { get; set; } = OnboardingOwner.Agency;
}

public sealed class OnboardingTemplateRequest
{
    [Required, MaxLength(60)]
    public List<OnboardingTemplateItemRequest> Items { get; set; } = new();

    [Required, StringLength(64)]
    public string? Version { get; set; }
}

public sealed record OnboardingTemplateDto(IReadOnlyList<OnboardingTemplateItem> Items, string Version);

public sealed class EditOnboardingItemRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(64)]
    public string? Category { get; set; }

    public OnboardingOwner Owner { get; set; } = OnboardingOwner.Agency;

    [Range(0, 10000)]
    public int? SortOrder { get; set; }
}

/// <summary>
/// The onboarding checklist every new client starts with (setting <c>clients.onboardingTemplate</c>; defaults to
/// <see cref="OnboardingChecklistTemplate"/>). Editing it never changes the checklists of existing clients.
/// </summary>
public sealed class OnboardingTemplateService(AppDbContext db, ISettingsService settings, IAuditLogger audit, ICurrentUser currentUser)
{
    public const string Key = "clients.onboardingTemplate";

    public static List<OnboardingTemplateItem> Defaults() => OnboardingChecklistTemplate.Items.Select(i => new OnboardingTemplateItem
    {
        Key = i.Key, Title = i.Title, Description = i.Description, Category = i.Category, Owner = i.Owner,
    }).ToList();

    public Task<List<OnboardingTemplateItem>> ItemsAsync(CancellationToken ct) => settings.GetAsync(Key, Defaults(), ct);

    public async Task<OnboardingTemplateDto> GetAsync(CancellationToken ct)
    {
        var items = await ItemsAsync(ct);
        return new OnboardingTemplateDto(items, OptionLists.Version(items));
    }

    public async Task<OnboardingTemplateDto> SaveAsync(OnboardingTemplateRequest r, CancellationToken ct)
    {
        var current = await ItemsAsync(ct);
        OptionLists.EnsureVersion(OptionLists.Version(current), r.Version);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<OnboardingTemplateItem>();
        for (var i = 0; i < r.Items.Count; i++)
        {
            var item = r.Items[i];
            if (!Enum.IsDefined(item.Owner)) throw DeliveryRules.Invalid("onboarding.invalid_owner", $"items[{i}]", "Choose who completes this step.");
            var key = ClientService.NormalizeSlug(string.IsNullOrWhiteSpace(item.Key) ? item.Title : item.Key);
            if (key.Length > 56) key = key[..56];
            if (key.Length < 2) throw DeliveryRules.Invalid("onboarding.invalid_key", $"items[{i}]", "Use a title with letters or digits.");
            var unique = key;
            for (var n = 2; !keys.Add(unique); n++) unique = $"{key}-{n}";
            items.Add(new OnboardingTemplateItem
            {
                Key = unique, Title = item.Title.Trim(), Description = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim(),
                Category = string.IsNullOrWhiteSpace(item.Category) ? "Other" : item.Category.Trim(), Owner = item.Owner,
            });
        }
        await settings.SetAsync(Key, items, currentUser.IdOrNull, "Onboarding checklist created for every new client.", ct);
        audit.Record("clients.onboarding_template_updated", "SystemSetting", Key, new { Items = current.Count }, new { Items = items.Count });
        await db.SaveChangesAsync(ct);
        return new OnboardingTemplateDto(items, OptionLists.Version(items));
    }
}

/// <summary>Agency-wide onboarding checklist template.</summary>
[ApiController]
[HasPermission(Permissions.ClientsView)]
[Route("api/v1/agency/settings/onboarding-template")]
public sealed class OnboardingTemplateController(OnboardingTemplateService template) : ControllerBase
{
    [HttpGet]
    public Task<OnboardingTemplateDto> Get(CancellationToken ct) => template.GetAsync(ct);

    [HttpPut]
    [HasPermission(Permissions.ClientsManage)]
    public Task<OnboardingTemplateDto> Save(OnboardingTemplateRequest r, CancellationToken ct) => template.SaveAsync(r, ct);
}
