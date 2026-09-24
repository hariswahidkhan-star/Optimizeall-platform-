using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Search;

// ---------- DTOs ----------

public sealed class GlobalSearchQuery
{
    /// <summary>Search text: at least 2 characters after trimming.</summary>
    [Required, MaxLength(100)]
    public string Q { get; set; } = string.Empty;

    /// <summary>Results per type (1–10, default 5).</summary>
    [Range(1, SearchService.MaxPerType)]
    public int Limit { get; set; } = SearchService.DefaultPerType;
}

public sealed record SearchHitDto(Guid Id, string Title, string? Subtitle, string Url);

public sealed record SearchGroupDto(string Type, string Label, IReadOnlyList<SearchHitDto> Items);

public sealed record SearchResultDto(string Query, IReadOnlyList<SearchGroupDto> Groups);

// ---------- Service ----------

/// <summary>
/// Staff global search (command palette). Each record type is searched only when the caller holds the permission that
/// opens it, client-owned records go through <see cref="IClientScope"/>, and every type is capped. Uses LIKE with
/// escaped input (portable across MySQL and SQLite).
/// </summary>
public sealed class SearchService(AppDbContext db, ICurrentUser currentUser, IClientScope scope)
{
    public const int MinLength = 2;
    public const int DefaultPerType = 5;
    public const int MaxPerType = 10;

    /// <summary>Permissions that make at least one record type searchable.</summary>
    public static readonly string[] SearchPermissions =
    {
        Permissions.ClientsView, Permissions.CrmView, Permissions.ProjectsView, Permissions.BillingView,
        Permissions.CampaignsView, Permissions.UsersView,
    };

    public async Task<SearchResultDto> SearchAsync(GlobalSearchQuery query, CancellationToken ct)
    {
        if (!SearchPermissions.Any(currentUser.HasPermission))
            throw DomainException.Forbidden("search.forbidden", "Search is available to agency and platform staff.");
        var text = (query.Q ?? string.Empty).Trim();
        if (text.Length < MinLength)
            throw new DomainException("search.query_too_short", $"Type at least {MinLength} characters.", DomainErrorKind.Validation,
                new Dictionary<string, string[]> { ["q"] = new[] { $"Type at least {MinLength} characters." } });
        var take = Math.Clamp(query.Limit, 1, MaxPerType);
        var p = PagingExtensions.LikePattern(text);
        var groups = new List<SearchGroupDto>();

        if (currentUser.HasPermission(Permissions.ClientsView))
        {
            var q = await scope.ApplyAsync(db.Set<ClientAccount>().AsNoTracking(), c => c.Id, ct);
            var rows = await q.Where(c => EF.Functions.Like(c.Name, p, "\\") || (c.Industry != null && EF.Functions.Like(c.Industry, p, "\\")))
                .OrderBy(c => c.Name).Take(take)
                .Select(c => new { c.Id, c.Name, c.Industry, c.Status }).ToListAsync(ct);
            Add("clients", "Clients", rows.Select(c => new SearchHitDto(c.Id, c.Name,
                string.Join(" · ", new[] { c.Industry, c.Status.ToString() }.Where(s => !string.IsNullOrEmpty(s))), $"/agency/clients/{c.Id}")));
        }

        if (currentUser.HasPermission(Permissions.CrmView))
        {
            var contacts = await db.Set<CrmContact>().AsNoTracking()
                .Where(c => c.ArchivedAt == null && (EF.Functions.Like(c.FirstName, p, "\\") ||
                                                     (c.LastName != null && EF.Functions.Like(c.LastName, p, "\\")) ||
                                                     (c.Email != null && EF.Functions.Like(c.Email, p, "\\"))))
                .OrderBy(c => c.FirstName).ThenBy(c => c.LastName).Take(take)
                .Select(c => new { c.Id, c.FirstName, c.LastName, c.Email, c.JobTitle }).ToListAsync(ct);
            Add("contacts", "Contacts", contacts.Select(c => new SearchHitDto(c.Id,
                string.IsNullOrWhiteSpace(c.LastName) ? c.FirstName : $"{c.FirstName} {c.LastName}",
                c.Email ?? c.JobTitle, $"/agency/crm/contacts/{c.Id}")));

            var deals = await db.Set<CrmDeal>().AsNoTracking()
                .Where(d => d.ArchivedAt == null && EF.Functions.Like(d.Title, p, "\\"))
                .OrderByDescending(d => d.UpdatedAt).Take(take)
                .Select(d => new { d.Id, d.Title, d.Status, d.Value, d.Currency }).ToListAsync(ct);
            Add("deals", "Deals", deals.Select(d => new SearchHitDto(d.Id, d.Title,
                $"{d.Status} · {d.Value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} {d.Currency}", $"/agency/crm/deals/{d.Id}")));
        }

        if (currentUser.HasPermission(Permissions.ProjectsView))
        {
            var q = await scope.ApplyAsync(db.Set<Project>().AsNoTracking(), x => x.ClientAccountId, ct);
            var rows = await (from x in q
                              join c in db.Set<ClientAccount>().AsNoTracking() on x.ClientAccountId equals c.Id
                              where EF.Functions.Like(x.Name, p, "\\")
                              orderby x.Name
                              select new { x.Id, x.Name, x.Status, Client = c.Name }).Take(take).ToListAsync(ct);
            Add("projects", "Projects", rows.Select(x => new SearchHitDto(x.Id, x.Name, $"{x.Client} · {x.Status}", $"/agency/projects/{x.Id}")));
        }

        if (currentUser.HasPermission(Permissions.BillingView))
        {
            var q = await scope.ApplyAsync(db.Set<Invoice>().AsNoTracking(), x => x.ClientAccountId, ct);
            var rows = await (from i in q
                              join c in db.Set<ClientAccount>().AsNoTracking() on i.ClientAccountId equals c.Id
                              where (i.Number != null && EF.Functions.Like(i.Number, p, "\\")) || EF.Functions.Like(c.Name, p, "\\")
                              orderby i.CreatedAt descending
                              select new { i.Id, i.Number, i.Status, i.Total, i.Currency, Client = c.Name }).Take(take).ToListAsync(ct);
            Add("invoices", "Invoices", rows.Select(i => new SearchHitDto(i.Id, i.Number ?? "Draft invoice",
                $"{i.Client} · {i.Status} · {i.Total.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} {i.Currency}",
                $"/agency/billing/invoices/{i.Id}")));
        }

        if (currentUser.HasPermission(Permissions.CampaignsView))
        {
            var rows = await db.Set<Campaign>().AsNoTracking()
                .Where(c => EF.Functions.Like(c.Title, p, "\\") || EF.Functions.Like(c.Slug, p, "\\"))
                .OrderByDescending(c => c.UpdatedAt).Take(take)
                .Select(c => new { c.Id, c.Title, c.Status }).ToListAsync(ct);
            Add("campaigns", "Campaigns", rows.Select(c => new SearchHitDto(c.Id, c.Title, c.Status.ToString(), $"/manage/campaigns/{c.Id}")));
        }

        if (currentUser.HasPermission(Permissions.UsersView))
        {
            var rows = await db.Set<User>().AsNoTracking()
                .Where(u => EF.Functions.Like(u.DisplayName, p, "\\") || EF.Functions.Like(u.Email, p, "\\"))
                .OrderBy(u => u.DisplayName).Take(take)
                .Select(u => new { u.Id, u.DisplayName, u.Email, u.Status }).ToListAsync(ct);
            Add("users", "Users", rows.Select(u => new SearchHitDto(u.Id, u.DisplayName,
                u.Status == UserStatus.Active ? u.Email : $"{u.Email} · {u.Status}", $"/admin/users/{u.Id}")));
        }

        return new SearchResultDto(text, groups);

        void Add(string type, string label, IEnumerable<SearchHitDto> items)
        {
            var list = items.ToList();
            if (list.Count > 0) groups.Add(new SearchGroupDto(type, label, list));
        }
    }
}

// ---------- API ----------

/// <summary>Global search for staff (the Ctrl/Cmd+K command palette).</summary>
[ApiController]
[Authorize]
[Route("api/v1/search")]
public sealed class SearchController(SearchService search) : ControllerBase
{
    /// <summary>
    /// Searches clients, contacts, deals, projects, invoices, campaigns and users — only the types the caller may open,
    /// tenancy-scoped, at most <c>limit</c> (default 5, max 10) per type. 400 <c>search.query_too_short</c> under 2
    /// characters; 403 <c>search.forbidden</c> without any staff search permission; rate limited (60/min per user).
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.Search)]
    public Task<SearchResultDto> Search([FromQuery] GlobalSearchQuery query, CancellationToken ct) => search.SearchAsync(query, ct);
}

public static class SearchModule
{
    public static IServiceCollection AddSearchModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SearchService>();
        return services;
    }
}
