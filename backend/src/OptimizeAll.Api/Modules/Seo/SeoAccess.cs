using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo;

public sealed record ClientOptionDto(Guid Id, string Name, string Slug);

/// <summary>Loads SEO records through <see cref="IClientScope"/> (other tenants' records answer 404).</summary>
public sealed partial class SeoAccess(AppDbContext db, IClientScope scope)
{
    public async Task<SeoSite> SiteAsync(Guid id, CancellationToken ct, bool tracked = false)
    {
        var q = db.Set<SeoSite>().Where(s => s.Id == id);
        if (!tracked) q = q.AsNoTracking();
        var site = await q.FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Site");
        await EnsureAsync(site.ClientAccountId, "Site", ct);
        return site;
    }

    public async Task<T> OwnedAsync<T>(Guid id, Func<T, Guid> clientOf, string what, CancellationToken ct, bool tracked = true) where T : Entity
    {
        var q = db.Set<T>().Where(e => e.Id == id);
        if (!tracked) q = q.AsNoTracking();
        var entity = await q.FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound(what);
        await EnsureAsync(clientOf(entity), what, ct);
        return entity;
    }

    /// <summary>404 (never 403) when the caller may not access the client.</summary>
    public async Task EnsureAsync(Guid clientAccountId, string what, CancellationToken ct)
    {
        try
        {
            await scope.EnsureAccessAsync(clientAccountId, ClientMemberRole.Viewer, ct);
        }
        catch (DomainException ex) when (ex.Kind == DomainErrorKind.NotFound)
        {
            throw DomainException.NotFound(what);
        }
    }

    public async Task<List<ClientOptionDto>> ClientOptionsAsync(CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<ClientAccount>().AsNoTracking(), c => c.Id, ct);
        return await q.Where(c => c.Status != ClientAccountStatus.Churned).OrderBy(c => c.Name)
            .Select(c => new ClientOptionDto(c.Id, c.Name, c.Slug)).ToListAsync(ct);
    }

    /// <summary>
    /// Normalizes a domain entry ("https://www.Example.com/about" → "www.example.com"; keeps an explicit port).
    /// Returns (host[:port], protocol-from-input-or-null) or null when invalid.
    /// </summary>
    public static (string Domain, string? Protocol)? NormalizeDomain(string input)
    {
        var value = input.Trim();
        string? protocol = null;
        if (value.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
                return null;
            protocol = uri.Scheme;
            value = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }
        value = value.TrimEnd('/').ToLowerInvariant();
        return DomainRegex().IsMatch(value) && value.Length <= 253 ? (value, protocol) : null;
    }

    [GeneratedRegex(@"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)*[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?::\d{1,5})?$")]
    private static partial Regex DomainRegex();
}
