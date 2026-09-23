using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Campaigns;

public interface ICampaignAdminService
{
    Task<PagedResult<AdminCampaignListItemDto>> ListAsync(AdminCampaignQuery query, CancellationToken ct);

    /// <summary>Lightweight id/title/status list for staff pickers and filters (newest first, at most 500).</summary>
    Task<IReadOnlyList<CampaignOptionDto>> OptionsAsync(string? search, CancellationToken ct);
    Task<AdminCampaignDto> GetAsync(Guid id, CancellationToken ct);
    Task<AdminCampaignDto> CreateAsync(CreateCampaignRequest request, CancellationToken ct);
    Task<AdminCampaignDto> UpdateAsync(Guid id, UpdateCampaignRequest request, CancellationToken ct);
    Task<AdminCampaignDto> PublishAsync(Guid id, CancellationToken ct);
    Task<AdminCampaignDto> PauseAsync(Guid id, string? reason, CancellationToken ct);
    Task<AdminCampaignDto> ResumeAsync(Guid id, CancellationToken ct);
    Task<AdminCampaignDto> EndAsync(Guid id, string? reason, CancellationToken ct);
    Task<AdminCampaignDto> ArchiveAsync(Guid id, CancellationToken ct);
    Task<AdminCampaignDto> DuplicateAsync(Guid id, CancellationToken ct);

    Task<CampaignAssetDto> AddAssetAsync(Guid campaignId, AssetInput input, CancellationToken ct);
    Task<CampaignAssetDto> UpdateAssetAsync(Guid campaignId, Guid assetId, AssetInput input, CancellationToken ct);
    Task DeleteAssetAsync(Guid campaignId, Guid assetId, CancellationToken ct);
    Task<IReadOnlyList<CampaignAssetDto>> ReorderAssetsAsync(Guid campaignId, ReorderAssetsRequest request, CancellationToken ct);

    Task<IReadOnlyList<DisclosureDto>> GetDisclosuresAsync(Guid campaignId, CancellationToken ct);
    Task<IReadOnlyList<DisclosureDto>> ReplaceDisclosuresAsync(Guid campaignId, ReplaceDisclosuresRequest request, CancellationToken ct);

    Task<IReadOnlyList<AdminCategoryDto>> ListCategoriesAsync(CancellationToken ct);
    Task<AdminCategoryDto> CreateCategoryAsync(CategoryInput input, CancellationToken ct);
    Task<AdminCategoryDto> UpdateCategoryAsync(Guid id, CategoryInput input, CancellationToken ct);
    Task<CategoryDeleteResultDto> DeleteCategoryAsync(Guid id, CancellationToken ct);
}

public sealed class CampaignAdminService(
    AppDbContext db,
    ICurrentUser currentUser,
    IAuditLogger audit,
    IEventPublisher events,
    IRewardQuoteService quotes,
    IRewardRulesService rewardRules,
    ImageUrlPolicy images,
    TimeProvider clock) : ICampaignAdminService
{
    private static readonly TimeSpan DefaultDeadlineGrace = TimeSpan.FromDays(3);

    private static readonly JsonSerializerOptions DiffJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ queries

    public async Task<PagedResult<AdminCampaignListItemDto>> ListAsync(AdminCampaignQuery query, CancellationToken ct)
    {
        var q = db.Set<Campaign>().AsNoTracking();
        if (query.Status is { } status) q = q.Where(c => c.Status == status);
        if (query.CategoryId is { } categoryId) q = q.Where(c => c.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = PagingExtensions.LikePattern(query.Search);
            q = q.Where(c => EF.Functions.Like(c.Title, pattern, "\\") || EF.Functions.Like(c.Slug, pattern, "\\"));
        }
        q = (query.Sort ?? "newest").ToLowerInvariant() switch
        {
            "title" => query.Desc ? q.OrderByDescending(c => c.Title) : q.OrderBy(c => c.Title),
            "startsat" => query.Desc ? q.OrderByDescending(c => c.StartsAt) : q.OrderBy(c => c.StartsAt),
            "deadline" => query.Desc ? q.OrderByDescending(c => c.SubmissionDeadline) : q.OrderBy(c => c.SubmissionDeadline),
            _ => q.OrderByDescending(c => c.CreatedAt),
        };

        var total = await q.CountAsync(ct);
        var campaigns = await q.Include(c => c.Category).Include(c => c.Platforms).AsSplitQuery()
            .Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var ids = campaigns.Select(c => c.Id).ToList();
        var counts = await CountsAsync(ids, ct);
        var spent = await SpentAsync(ids, ct);

        var items = campaigns.Select(c =>
        {
            var s = spent.GetValueOrDefault(c.Id);
            return new AdminCampaignListItemDto(c.Id, c.Slug, c.Title, c.Status, c.Visibility, CategoryRef(c.Category),
                c.Platforms.Select(p => p.Platform).OrderBy(p => p).ToList(), c.StartsAt, c.EndsAt, c.SubmissionDeadline,
                counts.GetValueOrDefault(c.Id) ?? new SubmissionCountsDto(0, 0, 0, 0), c.BudgetCurrency, s, c.BudgetAmount,
                c.BudgetAmount - s, c.CreatedAt, c.UpdatedAt, c.PublishedAt);
        }).ToList();
        return new PagedResult<AdminCampaignListItemDto>(items, total, query.Page, query.PageSize);
    }

    public const int MaxOptions = 500;

    public async Task<IReadOnlyList<CampaignOptionDto>> OptionsAsync(string? search, CancellationToken ct)
    {
        var q = db.Set<Campaign>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = PagingExtensions.LikePattern(search);
            q = q.Where(c => EF.Functions.Like(c.Title, pattern, "\\") || EF.Functions.Like(c.Slug, pattern, "\\"));
        }
        return await q.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).Take(MaxOptions)
            .Select(c => new CampaignOptionDto(c.Id, c.Title, c.Status)).ToListAsync(ct);
    }

    public async Task<AdminCampaignDto> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Set<Campaign>().AsNoTracking().AsSplitQuery()
            .Include(x => x.Category).Include(x => x.Platforms).Include(x => x.Assets).Include(x => x.Disclosures)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Campaign");

        var currentSet = await quotes.GetCurrentRuleSetAsync(id, Now, ct)
                         ?? await db.Set<RewardRuleSet>().AsNoTracking().Include(r => r.Rules)
                             .Where(r => r.CampaignId == id).OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct);
        RewardRuleSetDto? setDto = null;
        if (currentSet is not null)
            setDto = (await rewardRules.ToDtosAsync(new[] { currentSet }, ct))[0];

        var counts = (await CountsAsync(new List<Guid> { id }, ct)).GetValueOrDefault(id) ?? new SubmissionCountsDto(0, 0, 0, 0);
        var spent = (await SpentAsync(new List<Guid> { id }, ct)).GetValueOrDefault(id);

        return new AdminCampaignDto(
            c.Id, c.Slug, c.Title, c.Summary, c.Description, CategoryRef(c.Category), c.Topics, c.Status, c.Visibility,
            c.StartsAt, c.EndsAt, c.SubmissionDeadline, c.TimeZone, c.PostingInstructions, c.DefaultDisclosureText,
            c.RequiredHashtags, c.RequiredMentions, c.BudgetAmount, c.BudgetCurrency, spent, c.BudgetAmount - spent,
            c.MaxSubmissionsPerParticipant, c.MinPostLiveHours, c.RequireScreenshot, EligibilityDto.From(c.Eligibility),
            c.Platforms.Select(p => p.Platform).OrderBy(p => p).ToList(), c.LandingHeadline, c.LandingBody, c.HeroImageUrl,
            c.TrackingDestinationUrl, c.UtmCampaign,
            c.Assets.OrderBy(a => a.SortOrder).ThenBy(a => a.CreatedAt).Select(CampaignAssetDto.From).ToList(),
            c.Disclosures.OrderBy(d => d.Platform).ThenBy(d => d.CountryCode).Select(d => new DisclosureDto(d.Id, d.Platform, d.CountryCode, d.Text)).ToList(),
            setDto, counts, c.CreatedByUserId, c.CreatedAt, c.UpdatedAt, c.PublishedAt, c.ConcurrencyStamp);
    }

    private async Task<Dictionary<Guid, SubmissionCountsDto>> CountsAsync(List<Guid> ids, CancellationToken ct)
    {
        var rows = await db.Set<Submission>().Where(s => ids.Contains(s.CampaignId))
            .GroupBy(s => new { s.CampaignId, s.Status })
            .Select(g => new { g.Key.CampaignId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        return rows.GroupBy(r => r.CampaignId).ToDictionary(g => g.Key, g => new SubmissionCountsDto(
            g.Sum(r => r.Count),
            g.Where(r => r.Status is SubmissionStatus.Pending or SubmissionStatus.UnderReview).Sum(r => r.Count),
            g.Where(r => r.Status == SubmissionStatus.Approved).Sum(r => r.Count),
            g.Where(r => r.Status == SubmissionStatus.Rejected).Sum(r => r.Count)));
    }

    private async Task<Dictionary<Guid, decimal>> SpentAsync(List<Guid> ids, CancellationToken ct)
    {
        var rows = await db.Set<EarningEntry>()
            .Where(e => e.CampaignId != null && ids.Contains(e.CampaignId.Value))
            .Where(RewardQuoteService.IsCounted) // Status IN (...): range scan on (CampaignId, Status)
            .GroupBy(e => e.CampaignId!.Value)
            .Select(g => new { CampaignId = g.Key, Sum = g.Sum(e => e.Amount) }).ToListAsync(ct);
        return rows.ToDictionary(r => r.CampaignId, r => r.Sum);
    }

    private static CategoryRefDto? CategoryRef(CampaignCategory? c) => c is null ? null : new CategoryRefDto(c.Id, c.Name, c.Slug);

    // ------------------------------------------------------------------ create / update

    public async Task<AdminCampaignDto> CreateAsync(CreateCampaignRequest request, CancellationToken ct)
    {
        currentUser.Require(Permissions.RewardsEdit);
        var now = Now;
        var campaign = new Campaign { CreatedByUserId = currentUser.Id, Status = CampaignStatus.Draft };
        var ruleSet = RewardRuleSetFactory.Build(request.RewardRules, campaign.Id, 1, now, currentUser.Id, "Initial reward rules");
        RewardEngine.EnsureValid(ruleSet);

        await ValidateFieldsAsync(request, ruleSet.Currency, ct);
        Apply(campaign, request, ruleSet.Currency);
        campaign.Slug = await UniqueSlugAsync(request.Slug ?? CampaignText.Slugify(request.Title), explicitSlug: request.Slug is not null, null, ct);

        db.Set<Campaign>().Add(campaign);
        db.Set<RewardRuleSet>().Add(ruleSet);
        audit.Record("campaign.created", nameof(Campaign), campaign.Id, after: Snapshot(campaign));
        audit.Record("campaign.reward_rules_changed", nameof(Campaign), campaign.Id,
            after: RewardRuleSetFactory.Snapshot(ruleSet), reason: ruleSet.ChangeReason);
        await SaveMappingSlugConflictAsync(ct);
        return await GetAsync(campaign.Id, ct);
    }

    public async Task<AdminCampaignDto> UpdateAsync(Guid id, UpdateCampaignRequest request, CancellationToken ct)
    {
        var campaign = await db.Set<Campaign>().Include(c => c.Platforms).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw DomainException.NotFound("Campaign");
        if (campaign.Status == CampaignStatus.Archived)
            throw DomainException.Conflict("campaign.archived", "Archived campaigns cannot be changed.");
        if (request.ConcurrencyStamp != campaign.ConcurrencyStamp)
            throw DomainException.Conflict("concurrency.conflict", "This campaign was changed by someone else. Reload and try again.");

        var currentSet = await db.Set<RewardRuleSet>().AsNoTracking().Where(r => r.CampaignId == id)
            .OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct);
        var currency = currentSet?.Currency ?? campaign.BudgetCurrency;
        await ValidateFieldsAsync(request, currency, ct);

        if (request.BudgetAmount != campaign.BudgetAmount)
        {
            currentUser.Require(Permissions.RewardsEdit);
            if (!request.Confirm || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
                throw FieldError("budgetAmount", "campaign.budget_change_unconfirmed",
                    "Changing the budget requires \"confirm\": true and a reason (at least 5 characters).");
        }

        var before = Snapshot(campaign);
        Apply(campaign, request, currency);
        if (request.Slug is not null && request.Slug != campaign.Slug)
            campaign.Slug = await UniqueSlugAsync(request.Slug, explicitSlug: true, campaign.Id, ct);

        var after = Snapshot(campaign);
        var (changedBefore, changedAfter) = Diff(before, after);
        db.Entry(campaign).Property(c => c.ConcurrencyStamp).OriginalValue = request.ConcurrencyStamp!.Value;
        if (changedAfter.Count > 0)
            audit.Record("campaign.updated", nameof(Campaign), campaign.Id, changedBefore, changedAfter,
                reason: string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim());
        await SaveMappingSlugConflictAsync(ct);
        return await GetAsync(campaign.Id, ct);
    }

    private async Task ValidateFieldsAsync(CampaignFieldsInput input, string ruleCurrency, CancellationToken ct)
    {
        var startsAt = RewardRuleSetFactory.Utc(input.StartsAt);
        var endsAt = RewardRuleSetFactory.Utc(input.EndsAt);
        if (startsAt == default || endsAt <= startsAt)
            throw FieldError("endsAt", "campaign.invalid_dates", "The campaign must end after it starts.");
        var deadline = RewardRuleSetFactory.Utc(input.SubmissionDeadline) ?? endsAt + DefaultDeadlineGrace;
        if (deadline < endsAt)
            throw FieldError("submissionDeadline", "campaign.invalid_deadline", "The submission deadline cannot be before the campaign ends.");
        if (!CampaignClock.IsValidZone(input.TimeZone))
            throw FieldError("timeZone", "campaign.invalid_time_zone", $"'{input.TimeZone}' is not a valid IANA time zone.");
        if (string.IsNullOrWhiteSpace(input.DefaultDisclosureText))
            throw FieldError("defaultDisclosureText", "campaign.disclosure_required", "A paid-content disclosure (e.g. #ad) is required.");
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Summary))
            throw FieldError(string.IsNullOrWhiteSpace(input.Title) ? "title" : "summary", "campaign.title_required",
                "A title and summary are required.");
        if (input.BudgetAmount.HasValue)
        {
            var budgetCurrency = Money.Normalize(input.BudgetCurrency ?? ruleCurrency);
            if (budgetCurrency != Money.Normalize(ruleCurrency))
                throw FieldError("budgetCurrency", "campaign.budget_currency_mismatch",
                    $"The budget must be in the reward currency ({ruleCurrency}).");
        }
        if (input.TrackingDestinationUrl is { Length: > 0 } tracking &&
            !(Uri.TryCreate(tracking, UriKind.Absolute, out var t) && t.Scheme == Uri.UriSchemeHttps))
            throw FieldError("trackingDestinationUrl", "campaign.invalid_tracking_url", "The tracking destination must be an absolute https URL.");
        if (input.HeroImageUrl is { Length: > 0 } hero && !images.IsAllowed(hero))
            throw FieldError("heroImageUrl", "campaign.invalid_image_url", "The hero image: " + ImageUrlPolicy.Message);
        if (input.Platforms.Count == 0)
            throw FieldError("platforms", "campaign.platform_required", "Choose at least one platform.");
        if (input.Eligibility.Countries.Any(c => c.Trim().Length != 2 || !c.Trim().All(char.IsAsciiLetter)))
            throw FieldError("eligibility.countries", "campaign.invalid_country", "Target countries must be two-letter ISO codes.");
        if (input.CategoryId is { } categoryId && !await db.Set<CampaignCategory>().AnyAsync(c => c.Id == categoryId, ct))
            throw FieldError("categoryId", "campaign.category_not_found", "The selected category does not exist.");
    }

    /// <summary>A business validation error that also names the offending field (camelCase key) for the editor.</summary>
    private static DomainException FieldError(string field, string code, string message,
        DomainErrorKind kind = DomainErrorKind.Validation) =>
        new(code, message, kind, new Dictionary<string, string[]> { [field] = [message] });

    public static bool IsAllowedMediaUrl(string url) =>
        url.StartsWith("/api/v1/files/", StringComparison.Ordinal) && Guid.TryParse(url["/api/v1/files/".Length..], out _) ||
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    private void Apply(Campaign c, CampaignFieldsInput input, string ruleCurrency)
    {
        c.Title = input.Title.Trim();
        c.Summary = input.Summary.Trim();
        c.Description = input.Description ?? string.Empty;
        c.CategoryId = input.CategoryId;
        c.Topics = CampaignText.Tags(input.Topics);
        c.Visibility = input.Visibility;
        c.StartsAt = RewardRuleSetFactory.Utc(input.StartsAt);
        c.EndsAt = RewardRuleSetFactory.Utc(input.EndsAt);
        c.SubmissionDeadline = RewardRuleSetFactory.Utc(input.SubmissionDeadline) ?? c.EndsAt + DefaultDeadlineGrace;
        c.TimeZone = input.TimeZone.Trim();
        c.PostingInstructions = input.PostingInstructions ?? string.Empty;
        c.DefaultDisclosureText = input.DefaultDisclosureText.Trim();
        c.RequiredHashtags = Blank(input.RequiredHashtags);
        c.RequiredMentions = Blank(input.RequiredMentions);
        c.BudgetAmount = input.BudgetAmount;
        c.BudgetCurrency = Money.Normalize(ruleCurrency);
        c.MaxSubmissionsPerParticipant = input.MaxSubmissionsPerParticipant;
        c.MinPostLiveHours = input.MinPostLiveHours;
        c.RequireScreenshot = input.RequireScreenshot;
        c.Eligibility ??= new CampaignEligibility();
        c.Eligibility.MinAccountAgeDays = input.Eligibility.MinAccountAgeDays;
        c.Eligibility.MinFollowers = input.Eligibility.MinFollowers;
        c.Eligibility.RequireVerifiedAccount = input.Eligibility.RequireVerifiedAccount;
        c.Eligibility.Countries = CampaignText.Upper(input.Eligibility.Countries);
        c.Eligibility.Languages = CampaignText.Lower(input.Eligibility.Languages);
        c.Eligibility.Interests = CampaignText.Tags(input.Eligibility.Interests);
        c.Eligibility.Tiers = input.Eligibility.Tiers.Distinct().ToList();

        var wanted = input.Platforms.Distinct().ToHashSet();
        c.Platforms.RemoveAll(p => !wanted.Contains(p.Platform));
        foreach (var p in wanted.Where(p => c.Platforms.All(x => x.Platform != p)))
            c.Platforms.Add(new CampaignPlatform { CampaignId = c.Id, Platform = p });

        c.LandingHeadline = Blank(input.LandingHeadline);
        c.LandingBody = Blank(input.LandingBody);
        c.HeroImageUrl = Blank(input.HeroImageUrl);
        c.TrackingDestinationUrl = Blank(input.TrackingDestinationUrl);
        c.UtmCampaign = Blank(input.UtmCampaign);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<string> UniqueSlugAsync(string desired, bool explicitSlug, Guid? exceptId, CancellationToken ct)
    {
        var baseSlug = desired.Trim().ToLowerInvariant();
        var taken = await db.Set<Campaign>().Where(c => c.Slug.StartsWith(baseSlug) && (exceptId == null || c.Id != exceptId))
            .Select(c => c.Slug).ToListAsync(ct);
        if (!taken.Contains(baseSlug)) return baseSlug;
        if (explicitSlug)
            throw FieldError("slug", "campaign.slug_taken", $"The slug '{baseSlug}' is already used by another campaign.",
                DomainErrorKind.Conflict);
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseSlug}-{i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private async Task SaveMappingSlugConflictAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex) && ex.InnerException!.Message.Contains("Slug"))
        {
            throw FieldError("slug", "campaign.slug_taken", "That slug is already used by another campaign.", DomainErrorKind.Conflict);
        }
    }

    private static Dictionary<string, object?> Snapshot(Campaign c) => new()
    {
        ["title"] = c.Title, ["slug"] = c.Slug, ["summary"] = c.Summary, ["description"] = c.Description,
        ["categoryId"] = c.CategoryId, ["topics"] = c.Topics.ToList(), ["visibility"] = c.Visibility.ToString(),
        ["startsAt"] = c.StartsAt, ["endsAt"] = c.EndsAt, ["submissionDeadline"] = c.SubmissionDeadline, ["timeZone"] = c.TimeZone,
        ["postingInstructions"] = c.PostingInstructions, ["defaultDisclosureText"] = c.DefaultDisclosureText,
        ["requiredHashtags"] = c.RequiredHashtags, ["requiredMentions"] = c.RequiredMentions,
        ["budgetAmount"] = c.BudgetAmount, ["budgetCurrency"] = c.BudgetCurrency,
        ["maxSubmissionsPerParticipant"] = c.MaxSubmissionsPerParticipant, ["minPostLiveHours"] = c.MinPostLiveHours,
        ["requireScreenshot"] = c.RequireScreenshot,
        ["eligibility"] = new
        {
            c.Eligibility.MinAccountAgeDays, c.Eligibility.MinFollowers, c.Eligibility.RequireVerifiedAccount,
            Countries = c.Eligibility.Countries.ToList(), Languages = c.Eligibility.Languages.ToList(),
            Interests = c.Eligibility.Interests.ToList(), Tiers = c.Eligibility.Tiers.Select(t => t.ToString()).ToList(),
        },
        ["platforms"] = c.Platforms.Select(p => p.Platform.ToString()).OrderBy(p => p).ToList(),
        ["landingHeadline"] = c.LandingHeadline, ["landingBody"] = c.LandingBody, ["heroImageUrl"] = c.HeroImageUrl,
        ["trackingDestinationUrl"] = c.TrackingDestinationUrl, ["utmCampaign"] = c.UtmCampaign,
    };

    private static (Dictionary<string, object?> Before, Dictionary<string, object?> After) Diff(
        Dictionary<string, object?> before, Dictionary<string, object?> after)
    {
        var b = new Dictionary<string, object?>();
        var a = new Dictionary<string, object?>();
        foreach (var key in after.Keys)
        {
            if (JsonSerializer.Serialize(before[key], DiffJson) == JsonSerializer.Serialize(after[key], DiffJson)) continue;
            b[key] = before[key];
            a[key] = after[key];
        }
        return (b, a);
    }

    // ------------------------------------------------------------------ status transitions

    public async Task<AdminCampaignDto> PublishAsync(Guid id, CancellationToken ct)
    {
        var now = Now;
        var campaign = await db.Set<Campaign>().AsNoTracking().Include(c => c.Platforms).Include(c => c.Assets)
            .AsSplitQuery().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Campaign");
        if (campaign.Status != CampaignStatus.Draft)
            throw DomainException.Conflict("campaign.invalid_transition", $"Only draft campaigns can be published (this one is {campaign.Status}).");

        var problems = new List<string>();
        var set = await quotes.GetCurrentRuleSetAsync(id, now, ct);
        if (set is null || set.Rules.All(r => r.Type != RewardRuleType.BaseRate)) problems.Add("Add a base reward rate.");
        if (campaign.Platforms.Count == 0) problems.Add("Choose at least one platform.");
        if (campaign.Assets.Count == 0 && string.IsNullOrWhiteSpace(campaign.PostingInstructions))
            problems.Add("Add at least one content asset or posting instructions.");
        if (campaign.EndsAt <= campaign.StartsAt) problems.Add("The campaign must end after it starts.");
        if (campaign.SubmissionDeadline < campaign.EndsAt) problems.Add("The submission deadline cannot be before the campaign ends.");
        if (campaign.SubmissionDeadline <= now) problems.Add("The submission deadline has already passed.");
        if (string.IsNullOrWhiteSpace(campaign.DefaultDisclosureText)) problems.Add("Add a paid-content disclosure.");
        if (problems.Count > 0)
            throw new DomainException("campaign.incomplete", "The campaign is not ready to publish: " + string.Join(" ", problems),
                errors: new Dictionary<string, string[]> { ["campaign"] = problems.ToArray() });

        var target = campaign.StartsAt > now ? CampaignStatus.Scheduled : CampaignStatus.Active;
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct))
        {
            var updated = await db.Set<Campaign>()
                .Where(c => c.Id == id && c.Status == CampaignStatus.Draft)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, target).SetProperty(c => c.PublishedAt, now).SetProperty(c => c.UpdatedAt, now), ct);
            if (updated == 0)
                throw DomainException.Conflict("campaign.invalid_transition", "The campaign was changed by someone else. Reload and try again.");
            audit.Record("campaign.published", nameof(Campaign), id,
                before: new { Status = CampaignStatus.Draft.ToString() }, after: new { Status = target.ToString(), PublishedAt = now });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        await events.PublishAsync(new CampaignPublished(id, now), ct);
        return await GetAsync(id, ct);
    }

    public Task<AdminCampaignDto> PauseAsync(Guid id, string? reason, CancellationToken ct) =>
        TransitionAsync(id, new[] { CampaignStatus.Active, CampaignStatus.Scheduled }, _ => CampaignStatus.Paused,
            "campaign.paused", RequireReason(reason), ct);

    public Task<AdminCampaignDto> ResumeAsync(Guid id, CancellationToken ct) =>
        TransitionAsync(id, new[] { CampaignStatus.Paused }, c => c.StartsAt > Now ? CampaignStatus.Scheduled : CampaignStatus.Active,
            "campaign.resumed", null, ct, c =>
            {
                if (c.SubmissionDeadline < Now)
                    throw DomainException.Conflict("campaign.deadline_passed", "The submission deadline has passed; end the campaign instead.");
            });

    public Task<AdminCampaignDto> EndAsync(Guid id, string? reason, CancellationToken ct) =>
        TransitionAsync(id, new[] { CampaignStatus.Active, CampaignStatus.Paused, CampaignStatus.Scheduled }, _ => CampaignStatus.Ended,
            "campaign.ended", RequireReason(reason), ct);

    public Task<AdminCampaignDto> ArchiveAsync(Guid id, CancellationToken ct) =>
        TransitionAsync(id, new[] { CampaignStatus.Draft, CampaignStatus.Ended }, _ => CampaignStatus.Archived,
            "campaign.archived", null, ct);

    private static string RequireReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5
            ? throw new DomainException("reason.required", "A reason (at least 5 characters) is required.")
            : reason.Trim();

    private async Task<AdminCampaignDto> TransitionAsync(Guid id, CampaignStatus[] from, Func<Campaign, CampaignStatus> to,
        string action, string? reason, CancellationToken ct, Action<Campaign>? check = null)
    {
        var campaign = await db.Set<Campaign>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw DomainException.NotFound("Campaign");
        if (!from.Contains(campaign.Status))
            throw DomainException.Conflict("campaign.invalid_transition",
                $"A {campaign.Status} campaign cannot be {action.Split('.')[1]}.");
        check?.Invoke(campaign);

        var target = to(campaign);
        var now = Now;
        await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct);
        var current = campaign.Status;
        var updated = await db.Set<Campaign>().Where(c => c.Id == id && c.Status == current)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, target).SetProperty(c => c.UpdatedAt, now), ct);
        if (updated == 0)
            throw DomainException.Conflict("campaign.invalid_transition", "The campaign was changed by someone else. Reload and try again.");
        audit.Record(action, nameof(Campaign), id, new { Status = current.ToString() }, new { Status = target.ToString() }, reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AdminCampaignDto> DuplicateAsync(Guid id, CancellationToken ct)
    {
        var source = await db.Set<Campaign>().AsNoTracking().AsSplitQuery()
            .Include(c => c.Platforms).Include(c => c.Assets).Include(c => c.Disclosures)
            .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Campaign");
        var sourceSet = await db.Set<RewardRuleSet>().AsNoTracking().Include(r => r.Rules)
            .Where(r => r.CampaignId == id).OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct)
            ?? throw DomainException.Conflict("campaign.no_reward_rules", "The source campaign has no reward rules to copy.");

        var now = Now;
        var copy = new Campaign
        {
            Title = source.Title.Length > 190 ? source.Title[..190] + " (copy)" : source.Title + " (copy)",
            Summary = source.Summary, Description = source.Description, CategoryId = source.CategoryId,
            Topics = source.Topics.ToList(), Status = CampaignStatus.Draft, Visibility = source.Visibility,
            StartsAt = source.StartsAt, EndsAt = source.EndsAt, SubmissionDeadline = source.SubmissionDeadline,
            TimeZone = source.TimeZone, PostingInstructions = source.PostingInstructions,
            DefaultDisclosureText = source.DefaultDisclosureText, RequiredHashtags = source.RequiredHashtags,
            RequiredMentions = source.RequiredMentions, BudgetAmount = source.BudgetAmount, BudgetCurrency = source.BudgetCurrency,
            MaxSubmissionsPerParticipant = source.MaxSubmissionsPerParticipant, MinPostLiveHours = source.MinPostLiveHours,
            RequireScreenshot = source.RequireScreenshot,
            Eligibility = new CampaignEligibility
            {
                MinAccountAgeDays = source.Eligibility.MinAccountAgeDays, MinFollowers = source.Eligibility.MinFollowers,
                RequireVerifiedAccount = source.Eligibility.RequireVerifiedAccount,
                Countries = source.Eligibility.Countries.ToList(), Languages = source.Eligibility.Languages.ToList(),
                Interests = source.Eligibility.Interests.ToList(), Tiers = source.Eligibility.Tiers.ToList(),
            },
            LandingHeadline = source.LandingHeadline, LandingBody = source.LandingBody, HeroImageUrl = source.HeroImageUrl,
            TrackingDestinationUrl = source.TrackingDestinationUrl, UtmCampaign = source.UtmCampaign,
            CreatedByUserId = currentUser.Id,
        };
        copy.Slug = await UniqueSlugAsync(TrimSlug(source.Slug) + "-copy", explicitSlug: false, null, ct);
        foreach (var p in source.Platforms) copy.Platforms.Add(new CampaignPlatform { CampaignId = copy.Id, Platform = p.Platform });
        foreach (var a in source.Assets)
            copy.Assets.Add(new CampaignAsset
            {
                CampaignId = copy.Id, Type = a.Type, Title = a.Title, Url = a.Url, FileId = a.FileId, Body = a.Body,
                Platform = a.Platform, TemplateId = a.TemplateId, SortOrder = a.SortOrder,
            });
        foreach (var d in source.Disclosures)
            copy.Disclosures.Add(new CampaignDisclosure { CampaignId = copy.Id, Platform = d.Platform, CountryCode = d.CountryCode, Text = d.Text });

        var set = RewardRuleSetFactory.Copy(sourceSet, copy.Id, 1, now, currentUser.Id,
            $"Copied from campaign {source.Slug} reward rules v{sourceSet.Version}");
        db.Set<Campaign>().Add(copy);
        db.Set<RewardRuleSet>().Add(set);
        audit.Record("campaign.duplicated", nameof(Campaign), copy.Id, after: new { SourceCampaignId = id, copy.Slug, RuleSetCopiedFromVersion = sourceSet.Version });
        await SaveMappingSlugConflictAsync(ct);
        return await GetAsync(copy.Id, ct);
    }

    private static string TrimSlug(string slug) => slug.Length > 90 ? slug[..90].Trim('-') : slug;

    // ------------------------------------------------------------------ assets

    public async Task<CampaignAssetDto> AddAssetAsync(Guid campaignId, AssetInput input, CancellationToken ct)
    {
        var campaign = await EditableCampaignAsync(campaignId, ct);
        var asset = new CampaignAsset { CampaignId = campaignId };
        await ApplyAssetAsync(asset, campaign, input, ct);
        asset.SortOrder = input.SortOrder
                          ?? (await db.Set<CampaignAsset>().Where(a => a.CampaignId == campaignId).MaxAsync(a => (int?)a.SortOrder, ct) ?? -1) + 1;
        db.Set<CampaignAsset>().Add(asset);
        audit.Record("campaign.asset_added", nameof(Campaign), campaignId, after: AssetSnapshot(asset));
        await db.SaveChangesAsync(ct);
        return CampaignAssetDto.From(asset);
    }

    public async Task<CampaignAssetDto> UpdateAssetAsync(Guid campaignId, Guid assetId, AssetInput input, CancellationToken ct)
    {
        var campaign = await EditableCampaignAsync(campaignId, ct);
        var asset = await db.Set<CampaignAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CampaignId == campaignId, ct)
            ?? throw DomainException.NotFound("Asset");
        var before = AssetSnapshot(asset);
        await ApplyAssetAsync(asset, campaign, input, ct);
        if (input.SortOrder is { } order) asset.SortOrder = order;
        audit.Record("campaign.asset_updated", nameof(Campaign), campaignId, before, AssetSnapshot(asset));
        await db.SaveChangesAsync(ct);
        return CampaignAssetDto.From(asset);
    }

    public async Task DeleteAssetAsync(Guid campaignId, Guid assetId, CancellationToken ct)
    {
        await EditableCampaignAsync(campaignId, ct);
        var asset = await db.Set<CampaignAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CampaignId == campaignId, ct)
            ?? throw DomainException.NotFound("Asset");
        db.Remove(asset);
        audit.Record("campaign.asset_removed", nameof(Campaign), campaignId, before: AssetSnapshot(asset));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CampaignAssetDto>> ReorderAssetsAsync(Guid campaignId, ReorderAssetsRequest request, CancellationToken ct)
    {
        await EditableCampaignAsync(campaignId, ct);
        var assets = await db.Set<CampaignAsset>().Where(a => a.CampaignId == campaignId).ToListAsync(ct);
        var ids = request.AssetIds.Distinct().ToList();
        if (ids.Count != assets.Count || assets.Any(a => !ids.Contains(a.Id)))
            throw new DomainException("campaign.reorder_mismatch", "List every asset of the campaign exactly once.");
        for (var i = 0; i < ids.Count; i++) assets.Single(a => a.Id == ids[i]).SortOrder = i;
        audit.Record("campaign.assets_reordered", nameof(Campaign), campaignId, after: new { Order = ids });
        await db.SaveChangesAsync(ct);
        return assets.OrderBy(a => a.SortOrder).Select(CampaignAssetDto.From).ToList();
    }

    private async Task<Campaign> EditableCampaignAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.Set<Campaign>().AsNoTracking().Include(c => c.Platforms).FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw DomainException.NotFound("Campaign");
        if (campaign.Status == CampaignStatus.Archived)
            throw DomainException.Conflict("campaign.archived", "Archived campaigns cannot be changed.");
        return campaign;
    }

    private async Task ApplyAssetAsync(CampaignAsset asset, Campaign campaign, AssetInput input, CancellationToken ct)
    {
        string? url = Blank(input.Url);
        if (input.FileId is { } fileId)
        {
            var file = await db.Set<StoredFile>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
            if (file is null || !file.IsPublic || file.Purpose == FilePurpose.SubmissionScreenshot)
                throw new DomainException("campaign.asset_file_invalid", "The file must be an uploaded public campaign image.");
            url = FileUrls.For(fileId);
        }
        else if (url is not null && input.Type == CampaignAssetType.Image && !images.IsAllowed(url))
        {
            // Images are rendered by the web app, so they must satisfy its CSP img-src (uploads + allowed hosts).
            throw FieldError("url", "campaign.asset_url_invalid", "Image assets: " + ImageUrlPolicy.Message);
        }
        else if (url is not null && !IsAllowedMediaUrl(url))
        {
            throw new DomainException("campaign.asset_url_invalid", "Asset URLs must be https URLs or uploaded files.");
        }

        if (input.Type == CampaignAssetType.Caption && string.IsNullOrWhiteSpace(input.Body))
            throw new DomainException("campaign.asset_body_required", "Caption assets need the approved text.");
        if (input.Type != CampaignAssetType.Caption && url is null)
            throw new DomainException("campaign.asset_url_required", "This asset type needs a URL or an uploaded file.");
        if (input.Platform is { } platform && campaign.Platforms.All(p => p.Platform != platform))
            throw new DomainException("campaign.asset_platform_invalid", $"{platform} is not one of this campaign's platforms.");
        if (input.TemplateId is { } templateId && !await db.Set<PostTemplate>().AnyAsync(t => t.Id == templateId, ct))
            throw new DomainException("campaign.template_not_found", "The selected post template does not exist.");

        asset.Type = input.Type;
        asset.Title = input.Title.Trim();
        asset.Url = url;
        asset.FileId = input.FileId;
        asset.Body = Blank(input.Body);
        asset.Platform = input.Platform;
        asset.TemplateId = input.TemplateId;
    }

    private static object AssetSnapshot(CampaignAsset a) => new { a.Id, a.Type, a.Title, a.Url, a.FileId, a.Platform, a.TemplateId, a.SortOrder };

    // ------------------------------------------------------------------ disclosures

    public async Task<IReadOnlyList<DisclosureDto>> GetDisclosuresAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.Set<Campaign>().AsNoTracking().Include(c => c.Disclosures).FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw DomainException.NotFound("Campaign");
        return campaign.Disclosures.OrderBy(d => d.Platform).ThenBy(d => d.CountryCode)
            .Select(d => new DisclosureDto(d.Id, d.Platform, d.CountryCode, d.Text)).ToList();
    }

    public async Task<IReadOnlyList<DisclosureDto>> ReplaceDisclosuresAsync(Guid campaignId, ReplaceDisclosuresRequest request, CancellationToken ct)
    {
        await EditableCampaignAsync(campaignId, ct);
        var incoming = request.Disclosures.Select(d => new CampaignDisclosure
        {
            CampaignId = campaignId, Platform = d.Platform,
            CountryCode = string.IsNullOrWhiteSpace(d.CountryCode) ? null : d.CountryCode.Trim().ToUpperInvariant(),
            Text = d.Text.Trim(),
        }).ToList();
        if (incoming.Any(d => d.Text.Length == 0))
            throw new DomainException("campaign.disclosure_required", "Disclosure text cannot be empty.");
        if (incoming.GroupBy(d => (d.Platform, d.CountryCode)).Any(g => g.Count() > 1))
            throw new DomainException("campaign.duplicate_disclosure", "Each platform/country combination can only have one disclosure.");

        await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct);
        var existing = await db.Set<CampaignDisclosure>().Where(d => d.CampaignId == campaignId).ToListAsync(ct);
        var before = existing.Select(d => new { d.Platform, d.CountryCode, d.Text }).ToList();
        db.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        db.AddRange(incoming);
        audit.Record("campaign.disclosures_replaced", nameof(Campaign), campaignId, before,
            incoming.Select(d => new { d.Platform, d.CountryCode, d.Text }).ToList());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetDisclosuresAsync(campaignId, ct);
    }

    // ------------------------------------------------------------------ categories

    public async Task<IReadOnlyList<AdminCategoryDto>> ListCategoriesAsync(CancellationToken ct)
    {
        var categories = await db.Set<CampaignCategory>().AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var counts = await db.Set<Campaign>().Where(c => c.CategoryId != null).GroupBy(c => c.CategoryId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return categories.Select(c => ToAdmin(c, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<AdminCategoryDto> CreateCategoryAsync(CategoryInput input, CancellationToken ct)
    {
        var slug = input.Slug ?? CampaignText.Slugify(input.Name);
        if (await db.Set<CampaignCategory>().AnyAsync(c => c.Slug == slug, ct))
            throw DomainException.Conflict("category.slug_taken", $"A category with slug '{slug}' already exists.");
        var category = new CampaignCategory
        {
            Name = input.Name.Trim(), Slug = slug, Description = Blank(input.Description), Icon = Blank(input.Icon),
            SortOrder = input.SortOrder, IsActive = input.IsActive,
        };
        db.Add(category);
        audit.Record("campaign_category.created", nameof(CampaignCategory), category.Id, after: CategoryDto.From(category));
        await SaveCategoryAsync(ct);
        return ToAdmin(category, 0);
    }

    public async Task<AdminCategoryDto> UpdateCategoryAsync(Guid id, CategoryInput input, CancellationToken ct)
    {
        var category = await db.Set<CampaignCategory>().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Category");
        var before = CategoryDto.From(category);
        var slug = input.Slug ?? category.Slug;
        if (slug != category.Slug && await db.Set<CampaignCategory>().AnyAsync(c => c.Slug == slug && c.Id != id, ct))
            throw DomainException.Conflict("category.slug_taken", $"A category with slug '{slug}' already exists.");
        category.Name = input.Name.Trim();
        category.Slug = slug;
        category.Description = Blank(input.Description);
        category.Icon = Blank(input.Icon);
        category.SortOrder = input.SortOrder;
        category.IsActive = input.IsActive;
        audit.Record("campaign_category.updated", nameof(CampaignCategory), id, before, CategoryDto.From(category));
        await SaveCategoryAsync(ct);
        var count = await db.Set<Campaign>().CountAsync(c => c.CategoryId == id, ct);
        return ToAdmin(category, count);
    }

    public async Task<CategoryDeleteResultDto> DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        var category = await db.Set<CampaignCategory>().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Category");
        var count = await db.Set<Campaign>().CountAsync(c => c.CategoryId == id, ct);
        if (count > 0)
        {
            category.IsActive = false;
            audit.Record("campaign_category.deactivated", nameof(CampaignCategory), id, after: new { IsActive = false, CampaignCount = count });
            await db.SaveChangesAsync(ct);
            return new CategoryDeleteResultDto(false, true, count);
        }
        db.Remove(category);
        audit.Record("campaign_category.deleted", nameof(CampaignCategory), id, before: CategoryDto.From(category));
        await db.SaveChangesAsync(ct);
        return new CategoryDeleteResultDto(true, false, 0);
    }

    private async Task SaveCategoryAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("category.slug_taken", "A category with that slug already exists.");
        }
    }

    private static AdminCategoryDto ToAdmin(CampaignCategory c, int count) =>
        new(c.Id, c.Name, c.Slug, c.Description, c.Icon, c.SortOrder, c.IsActive, count);
}
