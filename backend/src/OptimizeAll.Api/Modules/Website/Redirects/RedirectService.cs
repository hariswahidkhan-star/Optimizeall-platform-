using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.LandingPages;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Redirects;

/// <summary>
/// The address of one item of content before and after a save. <see cref="From"/> is its live address before the save
/// (null when it was not live), <see cref="To"/> its live address after the save (null when it is not live now).
/// </summary>
public sealed record AddressChange(string ContentType, Guid ContentId, string? From, string? To);

public sealed record RedirectDto(
    Guid Id, string FromPath, string ToPath, SiteRedirectSource Source, string? ContentType, Guid? ContentId, DateTime CreatedAt, DateTime UpdatedAt);

public sealed class RedirectInput
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(RedirectPaths.MaxLength)]
    public string FromPath { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(RedirectPaths.MaxLength)]
    public string ToPath { get; set; } = string.Empty;
}

public sealed class RedirectQuery : PageQuery
{
    public SiteRedirectSource? Source { get; set; }
}

/// <summary>
/// Permanent (301) redirects of old public addresses (docs/WEBSITE.md "Redirects"). When the live address of a page, post,
/// service, service line, case study, industry or landing page changes, the old address is recorded automatically
/// (<see cref="StageAsync"/>, called by the CMS inside <see cref="WriteAsync{T}"/>); staff can add and remove manual
/// redirects (site.manage, audited). Every row points at a final address: recording A → B also re-points X → A to
/// X → B, and a row whose address is claimed by live content again is removed, so chains and loops cannot form.
/// Public lookups (<see cref="ResolveAsync"/>) never redirect an address that serves live content.
/// </summary>
public sealed class RedirectService(
    AppDbContext db, IDatabaseDialect dialect, IAuditLogger audit, ICurrentUser user, LandingPageService landing, TimeProvider clock)
{
    private const string LockName = "website-redirects";

    /// <summary>
    /// Runs <paramref name="work"/> (which stages redirect changes and saves) serialized with every other redirect writer:
    /// the named lock is taken before the write transaction, and the transaction commits only when the work succeeds.
    /// </summary>
    public async Task<T> WriteAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await using var gate = await LockAsync(ct);
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task WriteAsync(Func<Task> work, CancellationToken ct) =>
        await WriteAsync(async () => { await work(); return true; }, ct);

    /// <summary>The redirect writers' named lock (take it before any write transaction).</summary>
    public async Task<IAsyncDisposable> LockAsync(CancellationToken ct)
    {
        try
        {
            return await dialect.AcquireNamedLockAsync(db, LockName, TimeSpan.FromSeconds(30), ct);
        }
        catch (TimeoutException)
        {
            throw DomainException.Conflict("website.redirects_busy", "Someone else is changing website addresses right now. Please try again in a moment.");
        }
    }

    /// <summary>
    /// True when saving <paramref name="change"/> has redirect bookkeeping to do: a live address moved, or an address went
    /// live. Content that stays live at the same address has nothing to do (its address was claimed when it went live).
    /// </summary>
    public static bool Affects(AddressChange change) => change.To is not null && change.From != change.To;

    /// <summary>
    /// Stages the redirect bookkeeping for one content save (the caller saves, inside <see cref="WriteAsync{T}"/>):
    /// the new live address is claimed (a redirect from it is removed), and when the live address moved, the old address
    /// redirects to the new one and every redirect to the old address is re-pointed at the new one.
    /// </summary>
    public async Task StageAsync(AddressChange change, CancellationToken ct)
    {
        if (!Affects(change)) return;
        var to = change.To!;
        var from =change.From is { } f && f != to && !RedirectPaths.IsProtected(f) ? f : null;
        var rows = await db.Set<SiteRedirect>()
            .Where(r => r.FromPath == to || (from != null && (r.FromPath == from || r.ToPath == from)))
            .ToListAsync(ct);

        foreach (var claimed in rows.Where(r => r.FromPath == to))
        {
            db.Remove(claimed);
            audit.Record("website.redirect_removed", nameof(SiteRedirect), claimed.Id, Snapshot(claimed), reason: "The address is live content again.");
        }
        if (from is null) return;

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var chained in rows.Where(r => r.ToPath == from && r.FromPath != to && r.FromPath != from))
        {
            var before = Snapshot(chained);
            chained.ToPath = to;
            audit.Record("website.redirect_updated", nameof(SiteRedirect), chained.Id, before, Snapshot(chained), "Its target moved.");
        }
        var own = rows.FirstOrDefault(r => r.FromPath == from);
        if (own is null)
        {
            own = new SiteRedirect { FromPath = from, CreatedAt = now };
            db.Add(own);
        }
        own.ToPath = to;
        own.Source = SiteRedirectSource.Automatic;
        own.ContentType = change.ContentType;
        own.ContentId = change.ContentId;
        own.CreatedByUserId = user.IdOrNull;
        audit.Record("website.redirect_created", nameof(SiteRedirect), own.Id, after: Snapshot(own), reason: "The address of live content changed.");
    }

    /// <summary>Saves a content change together with its redirect bookkeeping (lock + transaction only when needed).</summary>
    public async Task SaveAsync(AddressChange change, Func<Task> save, CancellationToken ct)
    {
        if (!Affects(change))
        {
            await save();
            return;
        }
        await WriteAsync(async () =>
        {
            await StageAsync(change, ct);
            await save();
        }, ct);
    }

    // ---------------------------------------------------------------- public lookups

    /// <summary>
    /// The Location a request target should be redirected to (with its other query parameters carried over), or null.
    /// Built-in and portal addresses and addresses serving live content are never redirected.
    /// </summary>
    public async Task<string?> ResolveAsync(string? requestTarget, CancellationToken ct)
    {
        if (RedirectPaths.Parse(requestTarget) is not { } key || RedirectPaths.IsProtected(key.Key)) return null;
        var to = await db.Set<SiteRedirect>().AsNoTracking().Where(r => r.FromPath == key.Key).Select(r => r.ToPath).FirstOrDefaultAsync(ct);
        if (to is null || await IsLiveAsync(key.Key, ct)) return null;
        return RedirectPaths.Location(to, key.Query);
    }

    /// <summary>Whether a (normalized) address currently serves published content.</summary>
    public async Task<bool> IsLiveAsync(string key, CancellationToken ct)
    {
        if (RedirectPaths.IsServiceLine(key, out var line))
            return await db.Set<ServiceCategory>().AnyAsync(c => c.Slug == line && c.IsPublished, ct);
        var segments = key.Trim('/').Split('/');
        var now = clock.GetUtcNow().UtcDateTime;
        switch (segments.Length)
        {
            case 1:
                return await db.Set<SitePage>().AnyAsync(p => p.Slug == segments[0] && p.IsPublished && (p.PublishAt == null || p.PublishAt <= now), ct);
            case 2:
                var slug = segments[1];
                return segments[0] switch
                {
                    "blog" => await db.Set<BlogPost>().AnyAsync(p => p.Slug == slug && p.Status == BlogPostStatus.Published && p.PublishedAt <= now, ct),
                    "services" => await db.Set<AgencyService>().AnyAsync(s => s.Slug == slug && s.IsPublished &&
                        db.Set<ServiceCategory>().Any(c => c.Id == s.CategoryId && c.IsPublished), ct),
                    "case-studies" => await db.Set<CaseStudy>().AnyAsync(c => c.Slug == slug && c.IsPublished, ct),
                    "industries" => await db.Set<Industry>().AnyAsync(i => i.Slug == slug && i.IsPublished, ct),
                    "careers" => await db.Set<JobOpening>().AnyAsync(j => j.Slug == slug && j.Status == JobOpeningStatus.Open &&
                        (j.ClosesAt == null || j.ClosesAt > now), ct),
                    _ => false,
                };
            case 3 when segments[0] == "lp":
                var clientSlug = segments[1];
                var clientId = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Slug == clientSlug).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
                return clientId is { } id && await landing.FindLiveAsync(id, segments[2], ct) is not null;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- staff (site.manage)

    public async Task<PagedResult<RedirectDto>> ListAsync(RedirectQuery query, CancellationToken ct)
    {
        var q = db.Set<SiteRedirect>().AsNoTracking();
        if (query.Source is { } source) q = q.Where(r => r.Source == source);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search.Trim().ToLowerInvariant());
            q = q.Where(r => EF.Functions.Like(r.FromPath, like, "\\") || EF.Functions.Like(r.ToPath, like, "\\"));
        }
        var page = await q.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.FromPath).ToPagedAsync(query, ct);
        return CmsStore.Map(page, ToDto);
    }

    /// <summary>
    /// Adds a manual redirect. The source must be a free address (not built in, not live content, no redirect yet); a
    /// target that is itself redirected is followed to its final address, and a target that leads back to the source is
    /// refused (loop). Redirects to the source are re-pointed at the target.
    /// </summary>
    public Task<RedirectDto> CreateAsync(RedirectInput input, CancellationToken ct) => WriteAsync(async () =>
    {
        var e = new FieldErrors();
        var fromKey = RedirectPaths.Parse(input.FromPath);
        var target = RedirectPaths.NormalizeTarget(input.ToPath);
        if (fromKey is not { } from || from.Query.Length > 0)
            e.Add("fromPath", "Enter an address on this site, starting with '/', e.g. /old-page (no query string).");
        else if (RedirectPaths.IsProtected(from.Key))
            e.Add("fromPath", "This address belongs to a built-in page or the app and cannot be redirected.");
        if (target is null) e.Add("toPath", "Enter an address on this site, starting with '/', e.g. /new-page.");
        e.ThrowIfAny("website.invalid_redirect", "Check the addresses.");
        var fromPath = fromKey!.Value.Key;

        if (await db.Set<SiteRedirect>().AnyAsync(r => r.FromPath == fromPath, ct))
            throw Conflict("website.redirect_exists", "fromPath", "This address already redirects. Delete that redirect first.");
        if (await IsLiveAsync(fromPath, ct))
            throw Conflict("website.redirect_source_live", "fromPath", "This address shows published content. Unpublish it or change its slug first.");

        // Collapse: a target that is redirected itself is replaced by its final address.
        var toKey = RedirectPaths.Parse(target)!.Value;
        var final = toKey.Query.Length == 0
            ? await db.Set<SiteRedirect>().AsNoTracking().Where(r => r.FromPath == toKey.Key).Select(r => r.ToPath).FirstOrDefaultAsync(ct)
            : null;
        var to = final ?? target!;
        var finalKey = RedirectPaths.Parse(to)!.Value.Key;
        if (finalKey == fromPath)
            throw Conflict("website.redirect_loop", "toPath", "This would send visitors in a circle back to the address they came from.");

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var chained in await db.Set<SiteRedirect>().Where(r => r.ToPath == fromPath).ToListAsync(ct))
        {
            var before = Snapshot(chained);
            chained.ToPath = to;
            audit.Record("website.redirect_updated", nameof(SiteRedirect), chained.Id, before, Snapshot(chained), "Its target now redirects.");
        }
        var row = new SiteRedirect
        {
            FromPath = fromPath, ToPath = to, Source = SiteRedirectSource.Manual, CreatedByUserId = user.Id, CreatedAt = now,
        };
        db.Add(row);
        audit.Record("website.redirect_created", nameof(SiteRedirect), row.Id, after: Snapshot(row));
        await SaveAsync(ct);
        return ToDto(row);
    }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct) => WriteAsync(async () =>
    {
        var row = await db.Set<SiteRedirect>().FirstOrDefaultAsync(r => r.Id == id, ct)
                  ?? throw new DomainException("website.not_found", "Redirect was not found.", DomainErrorKind.NotFound);
        db.Remove(row);
        audit.Record("website.redirect_deleted", nameof(SiteRedirect), row.Id, before: Snapshot(row));
        await SaveAsync(ct);
    }, ct);

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw Conflict("website.redirect_exists", "fromPath", "This address already redirects. Delete that redirect first.");
        }
    }

    private static DomainException Conflict(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Conflict, new Dictionary<string, string[]> { [field] = new[] { message } });

    private static object Snapshot(SiteRedirect r) => new { r.FromPath, r.ToPath, r.Source, r.ContentType, r.ContentId };

    public static RedirectDto ToDto(SiteRedirect r) =>
        new(r.Id, r.FromPath, r.ToPath, r.Source, r.ContentType, r.ContentId, r.CreatedAt, r.UpdatedAt);
}
