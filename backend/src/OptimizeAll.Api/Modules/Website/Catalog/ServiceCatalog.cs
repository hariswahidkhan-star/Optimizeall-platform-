using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website;

/// <summary>A package as other modules (CRM proposals, billing) see it. Amounts always travel with their currency.</summary>
public sealed record PackageInfo(
    Guid Id, Guid ServiceId, string ServiceSlug, string ServiceName, string Name, string? Description, decimal? Price, string Currency,
    PackageBillingPeriod BillingPeriod, decimal? SetupFee, bool IsCustomQuote, bool IsActive, IReadOnlyList<string> Features);

public sealed record ServiceInfo(
    Guid Id, string Slug, string Name, string Tagline, string CategorySlug, string CategoryName, bool IsPublished, IReadOnlyList<PackageInfo> Packages);

/// <summary>
/// Read-only access to the agency's service catalog for other modules (CRM, billing, projects). Packages are referenced by
/// id; inactive packages are still returned (with <see cref="PackageInfo.IsActive"/> false) so historical proposals and
/// invoices keep resolving.
/// </summary>
public interface IServiceCatalog
{
    Task<ServiceInfo?> GetServiceAsync(Guid serviceId, CancellationToken ct = default);
    Task<ServiceInfo?> GetServiceBySlugAsync(string slug, CancellationToken ct = default);
    Task<PackageInfo?> GetPackageAsync(Guid packageId, CancellationToken ct = default);
    Task<IReadOnlyList<PackageInfo>> GetPackagesAsync(IEnumerable<Guid> packageIds, CancellationToken ct = default);

    /// <summary>All services with their packages, ordered like the website. Unpublished services only when asked.</summary>
    Task<IReadOnlyList<ServiceInfo>> ListServicesAsync(bool includeUnpublished = false, CancellationToken ct = default);
}

public sealed class ServiceCatalog(AppDbContext db) : IServiceCatalog
{
    public async Task<ServiceInfo?> GetServiceAsync(Guid serviceId, CancellationToken ct = default) =>
        (await LoadAsync(s => s.Id == serviceId, ct)).FirstOrDefault();

    public async Task<ServiceInfo?> GetServiceBySlugAsync(string slug, CancellationToken ct = default) =>
        (await LoadAsync(s => s.Slug == slug, ct)).FirstOrDefault();

    public async Task<PackageInfo?> GetPackageAsync(Guid packageId, CancellationToken ct = default) =>
        (await GetPackagesAsync(new[] { packageId }, ct)).FirstOrDefault();

    public async Task<IReadOnlyList<PackageInfo>> GetPackagesAsync(IEnumerable<Guid> packageIds, CancellationToken ct = default)
    {
        var ids = packageIds.Distinct().ToList();
        if (ids.Count == 0) return Array.Empty<PackageInfo>();
        var rows = await (from p in db.Set<ServicePackage>().AsNoTracking()
                          join s in db.Set<AgencyService>() on p.ServiceId equals s.Id
                          where ids.Contains(p.Id)
                          select new { p, s.Slug, s.Name }).ToListAsync(ct);
        return rows.Select(x => Map(x.p, x.Slug, x.Name)).ToList();
    }

    public async Task<IReadOnlyList<ServiceInfo>> ListServicesAsync(bool includeUnpublished = false, CancellationToken ct = default) =>
        await LoadAsync(s => includeUnpublished || s.IsPublished, ct);

    private async Task<IReadOnlyList<ServiceInfo>> LoadAsync(System.Linq.Expressions.Expression<Func<AgencyService, bool>> filter, CancellationToken ct)
    {
        var services = await db.Set<AgencyService>().AsNoTracking().Where(filter).Include(s => s.Packages).ToListAsync(ct);
        var categoryIds = services.Select(s => s.CategoryId).Distinct().ToList();
        var categories = await db.Set<ServiceCategory>().AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        return services
            .OrderBy(s => categories.TryGetValue(s.CategoryId, out var c) ? c.SortOrder : int.MaxValue).ThenBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s =>
            {
                var c = categories[s.CategoryId];
                return new ServiceInfo(s.Id, s.Slug, s.Name, s.Tagline, c.Slug, c.Name, s.IsPublished && c.IsPublished,
                    s.Packages.OrderBy(p => p.SortOrder).Select(p => Map(p, s.Slug, s.Name)).ToList());
            }).ToList();
    }

    private static PackageInfo Map(ServicePackage p, string serviceSlug, string serviceName) => new(
        p.Id, p.ServiceId, serviceSlug, serviceName, p.Name, p.Description, p.Price, p.Currency, p.BillingPeriod, p.SetupFee, p.IsCustomQuote,
        p.IsActive, p.Features);
}
