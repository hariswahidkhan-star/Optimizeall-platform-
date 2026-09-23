using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Content;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Content;

/// <summary>Admin CMS for homepage banners, announcements, FAQ and onboarding steps. Every change is audited.</summary>
public sealed class ContentService(AppDbContext db, IAuditLogger audit)
{
    // ---------- Banners ----------

    public async Task<PagedResult<BannerDto>> ListBannersAsync(BannerQuery query, CancellationToken ct)
    {
        var q = db.Set<HomepageBanner>().AsNoTracking();
        if (query.IsActive is { } active) q = q.Where(b => b.IsActive == active);
        if (query.Audience is { } audience) q = q.Where(b => b.Audience == audience);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(b => EF.Functions.Like(b.Title, p) || (b.Body != null && EF.Functions.Like(b.Body, p)));
        }
        var page = await q.OrderBy(b => b.SortOrder).ThenByDescending(b => b.CreatedAt).ToPagedAsync(query, ct);
        return Map(page, ToDto);
    }

    public async Task<BannerDto> GetBannerAsync(Guid id, CancellationToken ct) => ToDto(await FindAsync<HomepageBanner>(id, ct, true));

    public async Task<BannerDto> CreateBannerAsync(BannerRequest request, CancellationToken ct)
    {
        var banner = new HomepageBanner();
        Apply(banner, request);
        db.Set<HomepageBanner>().Add(banner);
        audit.Record("content.banner_created", nameof(HomepageBanner), banner.Id, after: ToDto(banner));
        await db.SaveChangesAsync(ct);
        return ToDto(banner);
    }

    public async Task<BannerDto> UpdateBannerAsync(Guid id, UpdateBannerRequest request, CancellationToken ct)
    {
        var banner = await FindAsync<HomepageBanner>(id, ct);
        ConcurrencyGuard.Apply(db, banner, request.ConcurrencyStamp!.Value);
        var before = ToDto(banner);
        Apply(banner, request);
        audit.Record("content.banner_updated", nameof(HomepageBanner), banner.Id, before, ToDto(banner));
        await db.SaveChangesAsync(ct);
        return ToDto(banner);
    }

    public Task DeleteBannerAsync(Guid id, CancellationToken ct) => DeleteAsync<HomepageBanner>(id, "content.banner_deleted", b => ToDto(b), ct);

    public Task<ReorderResponse> ReorderBannersAsync(ReorderRequest request, CancellationToken ct) =>
        ReorderAsync<HomepageBanner>(request, "content.banners_reordered", (b, order) => b.SortOrder = order, ct);

    private static void Apply(HomepageBanner b, BannerRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        var imageUrl = Clean(r.ImageUrl);
        var ctaUrl = Clean(r.CtaUrl);
        var ctaLabel = Clean(r.CtaLabel);
        if (imageUrl is not null && !FieldRules.IsSafeContentUrl(imageUrl)) errors["imageUrl"] = new[] { UrlMessage };
        if (ctaUrl is not null && !FieldRules.IsSafeContentUrl(ctaUrl)) errors["ctaUrl"] = new[] { UrlMessage };
        if (ctaUrl is not null && ctaLabel is null) errors["ctaLabel"] = new[] { "Add a button label for the link." };
        if (ctaLabel is not null && ctaUrl is null) errors["ctaUrl"] = new[] { "Add the link the button opens." };
        var language = Clean(r.LanguageCode);
        if (language is not null && !FieldRules.IsLanguageCode(language)) errors["languageCode"] = new[] { "Use a language code such as en or ar." };
        if (!Enum.IsDefined(r.Audience!.Value)) errors["audience"] = new[] { "Unknown audience." };
        ValidateWindow(r.StartsAt, r.EndsAt, "endsAt", errors);
        ThrowIfAny(errors);

        b.Title = r.Title.Trim();
        b.Body = Clean(r.Body);
        b.ImageUrl = imageUrl;
        b.CtaLabel = ctaLabel;
        b.CtaUrl = ctaUrl;
        b.Audience = r.Audience!.Value;
        b.CountryCode = Clean(r.CountryCode)?.ToUpperInvariant();
        b.LanguageCode = language?.ToLowerInvariant();
        b.StartsAt = Utc(r.StartsAt);
        b.EndsAt = Utc(r.EndsAt);
        b.SortOrder = r.SortOrder;
        b.IsActive = r.IsActive;
    }

    public static BannerDto ToDto(HomepageBanner b) => new(b.Id, b.Title, b.Body, b.ImageUrl, b.CtaLabel, b.CtaUrl, b.Audience,
        b.CountryCode, b.LanguageCode, b.StartsAt, b.EndsAt, b.SortOrder, b.IsActive, b.CreatedAt, b.UpdatedAt, b.ConcurrencyStamp);

    // ---------- Announcements ----------

    public async Task<PagedResult<AnnouncementDto>> ListAnnouncementsAsync(AnnouncementQuery query, CancellationToken ct)
    {
        var q = db.Set<Announcement>().AsNoTracking();
        if (query.IsActive is { } active) q = q.Where(a => a.IsActive == active);
        if (query.Audience is { } audience) q = q.Where(a => a.Audience == audience);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(a => EF.Functions.Like(a.Title, p) || EF.Functions.Like(a.Body, p));
        }
        var page = await q.OrderByDescending(a => a.PublishAt).ToPagedAsync(query, ct);
        return Map(page, ToDto);
    }

    public async Task<AnnouncementDto> GetAnnouncementAsync(Guid id, CancellationToken ct) => ToDto(await FindAsync<Announcement>(id, ct, true));

    public async Task<AnnouncementDto> CreateAnnouncementAsync(AnnouncementRequest request, CancellationToken ct)
    {
        var a = new Announcement();
        Apply(a, request);
        db.Set<Announcement>().Add(a);
        audit.Record("content.announcement_created", nameof(Announcement), a.Id, after: ToDto(a));
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task<AnnouncementDto> UpdateAnnouncementAsync(Guid id, UpdateAnnouncementRequest request, CancellationToken ct)
    {
        var a = await FindAsync<Announcement>(id, ct);
        ConcurrencyGuard.Apply(db, a, request.ConcurrencyStamp!.Value);
        var before = ToDto(a);
        Apply(a, request);
        audit.Record("content.announcement_updated", nameof(Announcement), a.Id, before, ToDto(a));
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public Task DeleteAnnouncementAsync(Guid id, CancellationToken ct) =>
        DeleteAsync<Announcement>(id, "content.announcement_deleted", a => ToDto(a), ct);

    private static void Apply(Announcement a, AnnouncementRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(r.Audience!.Value)) errors["audience"] = new[] { "Unknown audience." };
        if (!Enum.IsDefined(r.Severity!.Value)) errors["severity"] = new[] { "Unknown severity." };
        ValidateWindow(r.PublishAt, r.ExpiresAt, "expiresAt", errors);
        ThrowIfAny(errors);

        a.Title = r.Title.Trim();
        a.Body = r.Body.Trim();
        a.Severity = r.Severity!.Value;
        a.Audience = r.Audience!.Value;
        a.PublishAt = Utc(r.PublishAt)!.Value;
        a.ExpiresAt = Utc(r.ExpiresAt);
        a.IsActive = r.IsActive;
    }

    public static AnnouncementDto ToDto(Announcement a) => new(a.Id, a.Title, a.Body, a.Severity, a.Audience, a.PublishAt,
        a.ExpiresAt, a.IsActive, a.CreatedAt, a.UpdatedAt, a.ConcurrencyStamp);

    // ---------- FAQ ----------

    public async Task<PagedResult<FaqDto>> ListFaqsAsync(FaqQuery query, CancellationToken ct)
    {
        var q = db.Set<FaqItem>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Category)) q = q.Where(f => f.Category == query.Category.Trim());
        if (query.IsPublished is { } published) q = q.Where(f => f.IsPublished == published);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(f => EF.Functions.Like(f.Question, p) || EF.Functions.Like(f.Answer, p) || EF.Functions.Like(f.Category, p));
        }
        var page = await q.OrderBy(f => f.Category).ThenBy(f => f.SortOrder).ThenBy(f => f.CreatedAt).ToPagedAsync(query, ct);
        return Map(page, ToDto);
    }

    public async Task<FaqDto> GetFaqAsync(Guid id, CancellationToken ct) => ToDto(await FindAsync<FaqItem>(id, ct, true));

    public async Task<FaqDto> CreateFaqAsync(FaqRequest request, CancellationToken ct)
    {
        var f = new FaqItem();
        Apply(f, request);
        db.Set<FaqItem>().Add(f);
        audit.Record("content.faq_created", nameof(FaqItem), f.Id, after: ToDto(f));
        await db.SaveChangesAsync(ct);
        return ToDto(f);
    }

    public async Task<FaqDto> UpdateFaqAsync(Guid id, UpdateFaqRequest request, CancellationToken ct)
    {
        var f = await FindAsync<FaqItem>(id, ct);
        ConcurrencyGuard.Apply(db, f, request.ConcurrencyStamp!.Value);
        var before = ToDto(f);
        Apply(f, request);
        audit.Record("content.faq_updated", nameof(FaqItem), f.Id, before, ToDto(f));
        await db.SaveChangesAsync(ct);
        return ToDto(f);
    }

    public Task DeleteFaqAsync(Guid id, CancellationToken ct) => DeleteAsync<FaqItem>(id, "content.faq_deleted", f => ToDto(f), ct);

    public Task<ReorderResponse> ReorderFaqsAsync(ReorderRequest request, CancellationToken ct) =>
        ReorderAsync<FaqItem>(request, "content.faqs_reordered", (f, order) => f.SortOrder = order, ct);

    private static void Apply(FaqItem f, FaqRequest r)
    {
        f.Question = r.Question.Trim();
        f.Answer = r.Answer.Trim();
        f.Category = r.Category.Trim();
        f.SortOrder = r.SortOrder;
        f.IsPublished = r.IsPublished;
    }

    public static FaqDto ToDto(FaqItem f) => new(f.Id, f.Question, f.Answer, f.Category, f.SortOrder, f.IsPublished,
        f.CreatedAt, f.UpdatedAt, f.ConcurrencyStamp);

    /// <summary>Published FAQ grouped by category; categories ordered by their first item's sort order.</summary>
    public async Task<PublicFaqDto> PublicFaqAsync(CancellationToken ct)
    {
        var items = await db.Set<FaqItem>().AsNoTracking().Where(f => f.IsPublished)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.CreatedAt).ToListAsync(ct);
        var categories = items.GroupBy(f => f.Category)
            .OrderBy(g => g.Min(f => f.SortOrder)).ThenBy(g => g.Key)
            .Select(g => new PublicFaqCategoryDto(g.Key, g.Select(f => new PublicFaqItemDto(f.Id, f.Question, f.Answer)).ToList()))
            .ToList();
        return new PublicFaqDto(categories);
    }

    // ---------- Onboarding steps ----------

    public async Task<PagedResult<OnboardingStepDto>> ListStepsAsync(OnboardingStepQuery query, CancellationToken ct)
    {
        var q = db.Set<OnboardingStep>().AsNoTracking();
        if (query.IsActive is { } active) q = q.Where(s => s.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(s => EF.Functions.Like(s.Key, p) || EF.Functions.Like(s.Title, p) || EF.Functions.Like(s.Description, p));
        }
        var page = await q.OrderBy(s => s.SortOrder).ThenBy(s => s.Key).ToPagedAsync(query, ct);
        return Map(page, ToDto);
    }

    public async Task<OnboardingStepDto> GetStepAsync(Guid id, CancellationToken ct) => ToDto(await FindAsync<OnboardingStep>(id, ct, true));

    public async Task<OnboardingStepDto> CreateStepAsync(OnboardingStepRequest request, CancellationToken ct)
    {
        var s = new OnboardingStep();
        Apply(s, request);
        await EnsureUniqueKeyAsync(s.Key, null, ct);
        db.Set<OnboardingStep>().Add(s);
        audit.Record("content.onboarding_step_created", nameof(OnboardingStep), s.Id, after: ToDto(s));
        await SaveUniqueKeyAsync(ct);
        return ToDto(s);
    }

    public async Task<OnboardingStepDto> UpdateStepAsync(Guid id, UpdateOnboardingStepRequest request, CancellationToken ct)
    {
        var s = await FindAsync<OnboardingStep>(id, ct);
        ConcurrencyGuard.Apply(db, s, request.ConcurrencyStamp!.Value);
        var before = ToDto(s);
        Apply(s, request);
        await EnsureUniqueKeyAsync(s.Key, s.Id, ct);
        audit.Record("content.onboarding_step_updated", nameof(OnboardingStep), s.Id, before, ToDto(s));
        await SaveUniqueKeyAsync(ct);
        return ToDto(s);
    }

    public Task DeleteStepAsync(Guid id, CancellationToken ct) =>
        DeleteAsync<OnboardingStep>(id, "content.onboarding_step_deleted", s => ToDto(s), ct);

    public Task<ReorderResponse> ReorderStepsAsync(ReorderRequest request, CancellationToken ct) =>
        ReorderAsync<OnboardingStep>(request, "content.onboarding_steps_reordered", (s, order) => s.SortOrder = order, ct);

    private static void Apply(OnboardingStep s, OnboardingStepRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        var key = r.Key.Trim();
        if (!FieldRules.IsSlug(key)) errors["key"] = new[] { "Use lower-case letters, digits and single dashes, e.g. add-social-account." };
        var actionUrl = Clean(r.ActionUrl);
        var actionLabel = Clean(r.ActionLabel);
        if (actionUrl is not null && !FieldRules.IsSafeContentUrl(actionUrl)) errors["actionUrl"] = new[] { UrlMessage };
        if (actionUrl is not null && actionLabel is null) errors["actionLabel"] = new[] { "Add a button label for the link." };
        if (!Enum.IsDefined(r.CompletionRule!.Value)) errors["completionRule"] = new[] { "Unknown completion rule." };
        ThrowIfAny(errors);

        s.Key = key;
        s.Title = r.Title.Trim();
        s.Description = r.Description.Trim();
        s.ActionLabel = actionLabel;
        s.ActionUrl = actionUrl;
        s.CompletionRule = r.CompletionRule!.Value;
        s.SortOrder = r.SortOrder;
        s.IsActive = r.IsActive;
    }

    public static OnboardingStepDto ToDto(OnboardingStep s) => new(s.Id, s.Key, s.Title, s.Description, s.ActionLabel, s.ActionUrl,
        s.CompletionRule, s.SortOrder, s.IsActive, s.CreatedAt, s.UpdatedAt, s.ConcurrencyStamp);

    private async Task EnsureUniqueKeyAsync(string key, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Set<OnboardingStep>().AnyAsync(s => s.Key == key && s.Id != exceptId, ct))
            throw DuplicateKey();
    }

    private async Task SaveUniqueKeyAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw DuplicateKey();
        }
    }

    private static DomainException DuplicateKey() =>
        DomainException.Conflict("content.duplicate_key", "Another onboarding step already uses this key.");

    // ---------- Shared ----------

    private const string UrlMessage = "Use an https:// link or an app path starting with '/'.";

    private async Task<T> FindAsync<T>(Guid id, CancellationToken ct, bool readOnly = false) where T : Entity
    {
        var q = readOnly ? db.Set<T>().AsNoTracking() : db.Set<T>();
        return await q.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound(typeof(T).Name);
    }

    private async Task DeleteAsync<T>(Guid id, string action, Func<T, object> snapshot, CancellationToken ct) where T : Entity
    {
        var entity = await FindAsync<T>(id, ct);
        audit.Record(action, typeof(T).Name, id, before: snapshot(entity));
        db.Set<T>().Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    private async Task<ReorderResponse> ReorderAsync<T>(ReorderRequest request, string action, Action<T, int> setOrder, CancellationToken ct)
        where T : Entity
    {
        var ids = request.Ids.Distinct().ToList();
        if (ids.Count != request.Ids.Count)
            throw FieldRules.FieldError("content.reorder_duplicates", "ids", "Each item can appear only once.");
        var entities = await db.Set<T>().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        if (entities.Count != ids.Count)
            throw FieldRules.FieldError("content.reorder_unknown", "ids", "Some items no longer exist. Reload and try again.");

        var byId = entities.ToDictionary(e => e.Id);
        for (var i = 0; i < ids.Count; i++) setOrder(byId[ids[i]], (i + 1) * 10);
        audit.Record(action, typeof(T).Name, "bulk", after: new { order = ids });
        await db.SaveChangesAsync(ct);
        return new ReorderResponse(ids.Count);
    }

    private static void ValidateWindow(DateTime? start, DateTime? end, string field, Dictionary<string, string[]> errors)
    {
        if (start is { } s && end is { } e && Utc(e) <= Utc(s))
            errors[field] = new[] { "The end must be after the start." };
    }

    private static void ThrowIfAny(Dictionary<string, string[]> errors)
    {
        if (errors.Count > 0)
            throw new DomainException("content.invalid", "Some fields are invalid.", DomainErrorKind.Validation, errors);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? Utc(DateTime? value) => value is null ? null
        : value.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value.Value.ToUniversalTime();

    private static PagedResult<TOut> Map<TIn, TOut>(PagedResult<TIn> page, Func<TIn, TOut> map) =>
        new(page.Items.Select(map).ToList(), page.Total, page.Page, page.PageSize);
}
