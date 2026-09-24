using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Shared;

/// <summary>Collects field errors and throws one 400 <c>website.invalid</c> problem with all of them.</summary>
public sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = new List<string>();
        if (!list.Contains(message)) list.Add(message);
    }

    public void ThrowIfAny(string code = "website.invalid", string message = "Some fields are invalid.")
    {
        if (_errors.Count > 0)
            throw new DomainException(code, message, DomainErrorKind.Validation,
                _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
    }
}

/// <summary>Shared input rules for the website CMS (URLs, images, slugs, lists, Markdown).</summary>
public sealed class WebsiteRules(ImageUrlPolicy images)
{
    public const string UrlMessage = "Use an https:// link or an app path starting with '/'.";
    public const string SlugMessage = "Use lower-case letters, digits and single dashes, e.g. local-seo.";

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public string? Image(string? value, string field, FieldErrors errors)
    {
        var v = Clean(value);
        if (v is not null && !images.IsAllowed(v)) errors.Add(field, ImageUrlPolicy.Message);
        return v;
    }

    public List<string> Images(IEnumerable<string?>? values, string field, FieldErrors errors, int max = 20)
    {
        var list = (values ?? Array.Empty<string?>()).Select(Clean).OfType<string>().Distinct().ToList();
        if (list.Count > max) errors.Add(field, $"Add at most {max} images.");
        if (list.Any(u => !images.IsAllowed(u))) errors.Add(field, ImageUrlPolicy.Message);
        return list;
    }

    public static string? Link(string? value, string field, FieldErrors errors)
    {
        var v = Clean(value);
        if (v is not null && !FieldRules.IsSafeContentUrl(v)) errors.Add(field, UrlMessage);
        return v;
    }

    public static string Slug(string? value, string field, FieldErrors errors)
    {
        var v = (value ?? string.Empty).Trim();
        if (!FieldRules.IsSlug(v) || v.Length > 100) errors.Add(field, SlugMessage);
        return v;
    }

    /// <summary>Trims, drops empties and duplicates, and enforces count and length limits.</summary>
    public static List<string> Lines(IEnumerable<string?>? values, string field, FieldErrors errors, int maxItems = 30, int maxLength = 300)
    {
        var list = (values ?? Array.Empty<string?>()).Select(Clean).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (list.Count > maxItems) errors.Add(field, $"Add at most {maxItems} items.");
        if (list.Any(v => v.Length > maxLength)) errors.Add(field, $"Each item can be at most {maxLength} characters.");
        return list;
    }

    public static string? Markdown(string? value, string field, FieldErrors errors, int maxLength = 60000)
    {
        var v = Clean(value);
        if (v is null) return null;
        if (v.Length > maxLength) errors.Add(field, $"Keep this under {maxLength:N0} characters.");
        return MarkdownSanitizer.Sanitize(v);
    }

    public static List<FaqEntry> Faqs(IEnumerable<FaqEntryInput>? values, string field, FieldErrors errors, int max = 30)
    {
        var list = new List<FaqEntry>();
        foreach (var f in values ?? Array.Empty<FaqEntryInput>())
        {
            var q = Clean(f.Question);
            var a = Clean(f.Answer);
            if (q is null && a is null) continue;
            if (q is null || a is null) { errors.Add(field, "Each FAQ needs a question and an answer."); continue; }
            if (q.Length > 300 || a.Length > 3000) { errors.Add(field, "Questions are limited to 300 and answers to 3,000 characters."); continue; }
            list.Add(new FaqEntry(q, MarkdownSanitizer.Sanitize(a)));
        }
        if (list.Count > max) errors.Add(field, $"Add at most {max} questions.");
        return list;
    }

    public static List<ProcessStep> Steps(IEnumerable<ProcessStepInput>? values, string field, FieldErrors errors, int max = 12)
    {
        var list = new List<ProcessStep>();
        foreach (var s in values ?? Array.Empty<ProcessStepInput>())
        {
            var t = Clean(s.Title);
            var d = Clean(s.Description);
            if (t is null && d is null) continue;
            if (t is null || d is null) { errors.Add(field, "Each step needs a title and a description."); continue; }
            if (t.Length > 100 || d.Length > 600) { errors.Add(field, "Step titles are limited to 100 and descriptions to 600 characters."); continue; }
            list.Add(new ProcessStep(t, d));
        }
        if (list.Count > max) errors.Add(field, $"Add at most {max} steps.");
        return list;
    }

    public SeoMeta Seo(SeoInput? input, FieldErrors errors)
    {
        input ??= new SeoInput();
        var seo = new SeoMeta
        {
            Title = Clean(input.Title),
            Description = Clean(input.Description),
            OgImageUrl = Image(input.OgImageUrl, "seo.ogImageUrl", errors),
            CanonicalUrl = Link(input.CanonicalUrl, "seo.canonicalUrl", errors),
            NoIndex = input.NoIndex,
        };
        if (seo.Title?.Length > 70) errors.Add("seo.title", "Keep the SEO title under 70 characters.");
        if (seo.Description?.Length > 200) errors.Add("seo.description", "Keep the meta description under 200 characters.");
        return seo;
    }

    public static bool IsCurrency(string? value) => value is { Length: 3 } && value.All(char.IsAsciiLetterUpper);

    public static DateTime? Utc(DateTime? value) => value is null ? null
        : value.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value.Value.ToUniversalTime();
}

/// <summary>Persistence helpers for CMS rows: lookups, slug uniqueness, audited delete, reorder.</summary>
public sealed class CmsStore(AppDbContext db, IAuditLogger audit, IDatabaseDialect dialect)
{
    public AppDbContext Db => db;

    public async Task<T> FindAsync<T>(Guid id, CancellationToken ct, bool readOnly = false) where T : Entity
    {
        var q = readOnly ? db.Set<T>().AsNoTracking() : db.Set<T>();
        return await q.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound<T>();
    }

    public static DomainException NotFound<T>() =>
        new("website.not_found", $"{Label<T>()} was not found.", DomainErrorKind.NotFound);

    /// <summary>409 website.slug_taken when another row of the same kind already uses the slug.</summary>
    public async Task EnsureSlugFreeAsync<T>(string slug, Guid? exceptId, CancellationToken ct) where T : Entity, ISlugged
    {
        if (await db.Set<T>().AnyAsync(x => x.Slug == slug && x.Id != exceptId, ct))
            throw SlugTaken($"Another {Label<T>().ToLowerInvariant()} already uses the slug '{slug}'.");
    }

    public static void CheckStamp<T>(AppDbContext db, T entity, Guid? stamp) where T : class, IConcurrencyStamped =>
        ConcurrencyGuard.Apply(db, entity, stamp ?? Guid.Empty);

    /// <summary>Saves, turning a unique-index violation (slug raced by another editor) into 409 website.slug_taken.</summary>
    public async Task SaveAsync(CancellationToken ct, string duplicateMessage = "Another item already uses this slug.")
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw SlugTaken(duplicateMessage);
        }
    }

    public static DomainException SlugTaken(string message = "Another item already uses this slug.") =>
        new("website.slug_taken", message, DomainErrorKind.Conflict,
            new Dictionary<string, string[]> { ["slug"] = new[] { message } });

    public async Task DeleteAsync<T>(Guid id, string action, Func<T, object> snapshot, CancellationToken ct) where T : Entity
    {
        var entity = await FindAsync<T>(id, ct);
        audit.Record(action, typeof(T).Name, id, before: snapshot(entity));
        db.Set<T>().Remove(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw DomainException.Conflict("website.in_use", $"This {Label<T>().ToLowerInvariant()} is still in use. Unpublish it or remove what refers to it first.");
        }
    }

    public async Task<int> ReorderAsync<T>(IReadOnlyList<Guid> ids, string action, Action<T, int> setOrder, CancellationToken ct) where T : Entity
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count != ids.Count)
            throw FieldRules.FieldError("website.reorder_duplicates", "ids", "Each item can appear only once.");
        var entities = await db.Set<T>().Where(e => distinct.Contains(e.Id)).ToListAsync(ct);
        if (entities.Count != distinct.Count)
            throw FieldRules.FieldError("website.reorder_unknown", "ids", "Some items no longer exist. Reload and try again.");
        var byId = entities.ToDictionary(e => e.Id);
        for (var i = 0; i < distinct.Count; i++) setOrder(byId[distinct[i]], (i + 1) * 10);
        audit.Record(action, typeof(T).Name, "bulk", after: new { order = distinct });
        await db.SaveChangesAsync(ct);
        return distinct.Count;
    }

    public static string Label<T>() => typeof(T).Name switch
    {
        nameof(AgencyService) => "Service",
        nameof(ServiceCategory) => "Service category",
        nameof(ServicePackage) => "Package",
        nameof(CaseStudy) => "Case study",
        nameof(TeamMember) => "Team member",
        nameof(SitePage) => "Page",
        nameof(BlogPost) => "Post",
        nameof(BlogCategory) => "Blog category",
        nameof(JobOpening) => "Job",
        nameof(JobApplication) => "Application",
        nameof(WebsiteInquiry) => "Inquiry",
        nameof(ConsultationBooking) => "Booking",
        nameof(ConsultationBlackout) => "Blackout date",
        _ => typeof(T).Name,
    };

    public static PagedResult<TOut> Map<TIn, TOut>(PagedResult<TIn> page, Func<TIn, TOut> map) =>
        new(page.Items.Select(map).ToList(), page.Total, page.Page, page.PageSize);
}

/// <summary>Permission checks that need "any of" semantics (attributes combine with AND).</summary>
public static class WebsiteAccess
{
    public static void RequireAny(this ICurrentUser user, params string[] permissions)
    {
        if (!permissions.Any(user.HasPermission))
            throw DomainException.Forbidden("auth.forbidden", "You do not have permission to perform this action.");
    }
}
