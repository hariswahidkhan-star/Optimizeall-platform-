using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Eligibility;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Social;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Social;

public interface ISocialAccountService
{
    Task<SocialAccountListDto> ListMineAsync(Guid userId, CancellationToken ct);
    Task<SocialAccountDto> CreateAsync(Guid userId, CreateSocialAccountRequest request, CancellationToken ct);
    Task<SocialAccountChangeResponse> UpdateAsync(Guid userId, Guid id, UpdateSocialAccountRequest request, CancellationToken ct);
    Task<SocialAccountDto> DeactivateAsync(Guid userId, Guid id, CancellationToken ct);
    Task<SocialAccountDto> ReactivateAsync(Guid userId, Guid id, CancellationToken ct);
    Task<SocialAccountDto> RequestVerificationAsync(Guid userId, Guid id, CancellationToken ct);

    Task<PagedResult<ReviewSocialAccountDto>> ReviewListAsync(ReviewSocialAccountQuery query, CancellationToken ct);
    Task<ReviewSocialAccountDetailDto> ReviewGetAsync(Guid id, CancellationToken ct);
    Task<ReviewSocialAccountDetailDto> DecideAsync(Guid reviewerId, Guid id, SocialAccountDecisionRequest request, CancellationToken ct);
}

public sealed class SocialAccountService(
    AppDbContext db,
    ISettingsService settings,
    IAuditLogger audit,
    INotificationService notifications,
    TimeProvider clock) : ISocialAccountService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---------- Participant ----------

    public async Task<SocialAccountListDto> ListMineAsync(Guid userId, CancellationToken ct)
    {
        var (criteria, participant) = await CriteriaAsync(userId, ct);
        var accounts = await db.Set<SocialAccount>().AsNoTracking().Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsActive).ThenBy(a => a.Platform).ThenBy(a => a.Handle).ToListAsync(ct);
        return new SocialAccountListDto(criteria.MinAccountAgeDays, criteria.MinFollowers, SocialProfileRules.MaxActiveAccountsPerUser,
            accounts.Select(a => ToDto(a, criteria, participant)).ToList());
    }

    public async Task<SocialAccountDto> CreateAsync(Guid userId, CreateSocialAccountRequest request, CancellationToken ct)
    {
        var platform = request.Platform!.Value;
        if (!Enum.IsDefined(platform))
            throw FieldRules.FieldError("social.invalid_platform", "platform", "Choose a supported platform.");
        var createdAt = ValidateDeclaredFacts(platform, request.Handle, request.ProfileUrl, request.AccountCreatedAt!.Value,
            request.PrimaryLanguage, request.AudienceCountryCode);

        await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct);
        // Lock the owner first so concurrent creates/reactivations are serialized against the active-profile limit.
        await LockUserAsync(userId, ct);
        await EnsureBelowActiveLimitAsync(userId, ct);

        var normalized = Normalization.Handle(request.Handle);
        await EnsureHandleAvailableAsync(userId, platform, normalized, null, ct);

        var account = new SocialAccount
        {
            UserId = userId,
            Platform = platform,
            Handle = request.Handle.Trim().TrimStart('@'),
            NormalizedHandle = normalized,
            ProfileUrl = request.ProfileUrl.Trim(),
            AccountCreatedAt = createdAt,
            FollowerCount = request.FollowerCount,
            PrimaryLanguage = NormalizeLanguage(request.PrimaryLanguage),
            AudienceCountryCode = NormalizeCountry(request.AudienceCountryCode),
        };
        db.Set<SocialAccount>().Add(account);
        audit.Record("social.account_added", nameof(SocialAccount), account.Id, after: AuditView(account));
        await SaveHandleAsync(ct);
        await tx.CommitAsync(ct);

        var (criteria, participant) = await CriteriaAsync(userId, ct);
        return ToDto(account, criteria, participant);
    }

    public async Task<SocialAccountChangeResponse> UpdateAsync(Guid userId, Guid id, UpdateSocialAccountRequest request, CancellationToken ct)
    {
        var account = await OwnedAsync(userId, id, ct);
        if (!account.IsActive)
            throw DomainException.Conflict("social.inactive", "Reactivate this profile before editing it.");
        ConcurrencyGuard.Apply(db, account, request.ConcurrencyStamp!.Value);

        var createdAt = ValidateDeclaredFacts(account.Platform, request.Handle, request.ProfileUrl, request.AccountCreatedAt!.Value,
            request.PrimaryLanguage, request.AudienceCountryCode);
        var normalized = Normalization.Handle(request.Handle);
        if (normalized != account.NormalizedHandle)
            await EnsureHandleAvailableAsync(userId, account.Platform, normalized, account.Id, ct);

        var before = AuditView(account);
        var factsChanged = normalized != account.NormalizedHandle ||
                           Normalization.PostUrl(request.ProfileUrl) != Normalization.PostUrl(account.ProfileUrl) ||
                           createdAt != account.AccountCreatedAt ||
                           request.FollowerCount != account.FollowerCount;

        account.Handle = request.Handle.Trim().TrimStart('@');
        account.NormalizedHandle = normalized;
        account.ProfileUrl = request.ProfileUrl.Trim();
        account.AccountCreatedAt = createdAt;
        account.FollowerCount = request.FollowerCount;
        account.PrimaryLanguage = NormalizeLanguage(request.PrimaryLanguage);
        account.AudienceCountryCode = NormalizeCountry(request.AudienceCountryCode);

        // Verified facts must stay verified: changing what a reviewer checked sends the profile back to Unverified.
        var reset = factsChanged && account.VerificationStatus is SocialAccountVerificationStatus.Verified
            or SocialAccountVerificationStatus.PendingReview;
        if (reset)
        {
            account.VerificationStatus = SocialAccountVerificationStatus.Unverified;
            account.VerifiedAt = null;
            account.VerifiedByUserId = null;
            account.VerificationNote = null;
        }

        audit.Record(reset ? "social.account_updated_verification_reset" : "social.account_updated",
            nameof(SocialAccount), account.Id, before, AuditView(account));
        await SaveHandleAsync(ct);

        var (criteria, participant) = await CriteriaAsync(userId, ct);
        return new SocialAccountChangeResponse(ToDto(account, criteria, participant), reset,
            reset
                ? "Profile updated. Because you changed the handle, profile link, creation date or follower count, it needs to be verified again."
                : "Profile updated.");
    }

    public async Task<SocialAccountDto> DeactivateAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var account = await OwnedAsync(userId, id, ct);
        if (account.IsActive)
        {
            account.IsActive = false;
            audit.Record("social.account_deactivated", nameof(SocialAccount), account.Id);
            await db.SaveChangesAsync(ct);
        }
        var (criteria, participant) = await CriteriaAsync(userId, ct);
        return ToDto(account, criteria, participant);
    }

    public async Task<SocialAccountDto> ReactivateAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct);
        // Lock the owner first so concurrent creates/reactivations are serialized against the active-profile limit.
        await LockUserAsync(userId, ct);
        var account = await OwnedAsync(userId, id, ct);
        if (!account.IsActive)
        {
            await EnsureBelowActiveLimitAsync(userId, ct);
            account.IsActive = true;
            audit.Record("social.account_reactivated", nameof(SocialAccount), account.Id);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        var (criteria, participant) = await CriteriaAsync(userId, ct);
        return ToDto(account, criteria, participant);
    }

    public async Task<SocialAccountDto> RequestVerificationAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var account = await OwnedAsync(userId, id, ct);
        if (!account.IsActive)
            throw DomainException.Conflict("social.inactive", "Reactivate this profile before requesting verification.");
        if (account.VerificationStatus is not (SocialAccountVerificationStatus.Unverified or SocialAccountVerificationStatus.Rejected))
            throw DomainException.Conflict("social.verification_not_allowed",
                account.VerificationStatus == SocialAccountVerificationStatus.Verified
                    ? "This profile is already verified."
                    : "Verification has already been requested for this profile.");

        var before = account.VerificationStatus;
        account.VerificationStatus = SocialAccountVerificationStatus.PendingReview;
        audit.Record("social.verification_requested", nameof(SocialAccount), account.Id,
            new { verificationStatus = before }, new { verificationStatus = account.VerificationStatus });
        await db.SaveChangesAsync(ct);
        var (criteria, participant) = await CriteriaAsync(userId, ct);
        return ToDto(account, criteria, participant);
    }

    // ---------- Staff review ----------

    public async Task<PagedResult<ReviewSocialAccountDto>> ReviewListAsync(ReviewSocialAccountQuery query, CancellationToken ct)
    {
        var now = Now;
        var q = from a in db.Set<SocialAccount>().AsNoTracking()
                join u in db.Set<User>().AsNoTracking() on a.UserId equals u.Id
                select new { a, u };
        if (query.Status is { } status) q = q.Where(x => x.a.VerificationStatus == status);
        if (query.Platform is { } platform) q = q.Where(x => x.a.Platform == platform);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = PagingExtensions.LikePattern(query.Search);
            var handle = PagingExtensions.LikePattern(Normalization.Handle(query.Search));
            q = q.Where(x => EF.Functions.Like(x.a.NormalizedHandle, handle, "\\") || EF.Functions.Like(x.u.Email, pattern, "\\") ||
                             EF.Functions.Like(x.u.DisplayName, pattern, "\\"));
        }
        // Review queue: oldest waiting first unless the caller asks for newest.
        q = query.Desc ? q.OrderByDescending(x => x.a.UpdatedAt).ThenByDescending(x => x.a.Id) : q.OrderBy(x => x.a.UpdatedAt).ThenBy(x => x.a.Id);

        var page = await q.ToPagedAsync(query, ct);
        return new PagedResult<ReviewSocialAccountDto>(page.Items.Select(x => ToReviewDto(x.a, x.u, now)).ToList(),
            page.Total, page.Page, page.PageSize);
    }

    public async Task<ReviewSocialAccountDetailDto> ReviewGetAsync(Guid id, CancellationToken ct)
    {
        var account = await db.Set<SocialAccount>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
                      ?? throw DomainException.NotFound("SocialAccount");
        return await DetailAsync(account, ct);
    }

    public async Task<ReviewSocialAccountDetailDto> DecideAsync(Guid reviewerId, Guid id, SocialAccountDecisionRequest request, CancellationToken ct)
    {
        var decision = request.Decision!.Value;
        if (!Enum.IsDefined(decision))
            throw FieldRules.FieldError("social.invalid_decision", "decision", "Decision must be Verified or Rejected.");
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (decision == SocialVerificationDecision.Rejected && note is null)
            throw FieldRules.FieldError("social.note_required", "note", "Explain why the profile was rejected; the participant will see this note.");

        var account = await db.Set<SocialAccount>().FirstOrDefaultAsync(a => a.Id == id, ct)
                      ?? throw DomainException.NotFound("SocialAccount");
        if (account.UserId == reviewerId)
            throw DomainException.Forbidden("social.self_verification", "You can't review your own social profile.");
        if (account.VerificationStatus != SocialAccountVerificationStatus.PendingReview)
            throw DomainException.Conflict("social.not_pending", "This profile is not waiting for review.");
        ConcurrencyGuard.Apply(db, account, request.ConcurrencyStamp!.Value);

        if (request.VerifiedAccountCreatedAt is { } corrected)
        {
            var error = SocialProfileRules.ValidateAccountCreatedAt(corrected, Now);
            if (error is not null) throw FieldRules.FieldError("social.invalid_created_at", "verifiedAccountCreatedAt", error);
        }

        var before = AuditView(account);
        account.VerificationStatus = decision == SocialVerificationDecision.Verified
            ? SocialAccountVerificationStatus.Verified
            : SocialAccountVerificationStatus.Rejected;
        account.VerifiedAt = Now;
        account.VerifiedByUserId = reviewerId;
        account.VerificationNote = note;
        if (request.VerifiedAccountCreatedAt is { } createdAt) account.AccountCreatedAt = SocialProfileRules.ToUtc(createdAt);
        if (request.VerifiedFollowerCount is { } followers) account.FollowerCount = followers;

        audit.Record(decision == SocialVerificationDecision.Verified ? "social.account_verified" : "social.account_rejected",
            nameof(SocialAccount), account.Id, before, AuditView(account), note);

        var verified = decision == SocialVerificationDecision.Verified;
        await notifications.StageAsync(new NotificationRequest(
            account.UserId,
            NotificationTypes.SocialAccountVerified,
            verified
                ? $"Your {account.Platform} profile @{account.Handle} is verified"
                : $"Your {account.Platform} profile @{account.Handle} could not be verified",
            verified
                ? "A reviewer checked your profile and confirmed its details." + (note is null ? string.Empty : $" Note: {note}")
                : $"A reviewer could not verify this profile. Reason: {note} You can update the details and request verification again.",
            AppLinks.SocialAccounts,
            new[] { NotificationChannel.InApp, NotificationChannel.Email }), ct);

        await db.SaveChangesAsync(ct);
        return await DetailAsync(account, ct);
    }

    // ---------- Helpers ----------

    private async Task<ReviewSocialAccountDetailDto> DetailAsync(SocialAccount account, CancellationToken ct)
    {
        var owner = await db.Set<User>().AsNoTracking().FirstAsync(u => u.Id == account.UserId, ct);
        var (criteria, participant) = await CriteriaAsync(owner, ct);
        var eligibility = EligibilityEvaluator.EvaluateAccount(criteria, participant, account, Now);

        SocialAccountActorDto? verifiedBy = null;
        if (account.VerifiedByUserId is { } reviewer)
            verifiedBy = await db.Set<User>().AsNoTracking().Where(u => u.Id == reviewer)
                .Select(u => new SocialAccountActorDto(u.Id, u.DisplayName)).FirstOrDefaultAsync(ct);

        var entityId = account.Id.ToString();
        var history = await (from l in db.Set<AuditLog>().AsNoTracking()
                             where l.EntityType == nameof(SocialAccount) && l.EntityId == entityId
                             join u in db.Set<User>().AsNoTracking() on l.ActorUserId equals u.Id into actors
                             from u in actors.DefaultIfEmpty()
                             orderby l.Id descending
                             select new SocialAccountHistoryDto(l.CreatedAt, l.Action, l.ActorUserId, u == null ? null : u.DisplayName, l.Reason))
            .Take(50).ToListAsync(ct);

        var activeCount = await db.Set<SocialAccount>().CountAsync(a => a.UserId == account.UserId && a.IsActive, ct);
        return new ReviewSocialAccountDetailDto(ToReviewDto(account, owner, Now), eligibility.IsEligible, eligibility.EligibleFrom,
            eligibility.Reasons, verifiedBy, activeCount, history);
    }

    private DateTime ValidateDeclaredFacts(SocialPlatform platform, string handle, string profileUrl, DateTime createdAt,
        string? language, string? audienceCountry)
    {
        var errors = new Dictionary<string, string[]>();
        if (SocialProfileRules.ValidateHandle(handle) is { } handleError) errors["handle"] = new[] { handleError };
        if (SocialProfileRules.ValidateProfileUrl(platform, profileUrl) is { } urlError) errors["profileUrl"] = new[] { urlError };
        if (SocialProfileRules.ValidateAccountCreatedAt(createdAt, Now) is { } dateError) errors["accountCreatedAt"] = new[] { dateError };
        if (!string.IsNullOrWhiteSpace(language) && !FieldRules.IsLanguageCode(language.Trim()))
            errors["primaryLanguage"] = new[] { "Use a language code such as en or ar." };
        if (!string.IsNullOrWhiteSpace(audienceCountry) && !FieldRules.IsCountryCode(audienceCountry.Trim()))
            errors["audienceCountryCode"] = new[] { "Use a two-letter country code." };
        if (errors.Count > 0)
            throw new DomainException("social.invalid", "Some profile details are invalid.", DomainErrorKind.Validation, errors);
        if (!SocialProfileRules.ProfileUrlMatchesHandle(platform, profileUrl, handle))
            throw FieldRules.FieldError("social.url_handle_mismatch", "profileUrl",
                $"The profile link must point to @{Normalization.Handle(handle)} on {platform}.");
        return SocialProfileRules.ToUtc(createdAt);
    }

    /// <summary>Row-locks the owner (inside the caller's transaction) to serialize changes to their active-profile count.</summary>
    private async Task LockUserAsync(Guid userId, CancellationToken ct)
    {
        if (!await db.Dialect().LockRowAsync(db, "users", userId, ct)) throw DomainException.NotFound("User");
    }

    private async Task EnsureBelowActiveLimitAsync(Guid userId, CancellationToken ct)
    {
        var active = await db.Set<SocialAccount>().CountAsync(a => a.UserId == userId && a.IsActive, ct);
        if (active >= SocialProfileRules.MaxActiveAccountsPerUser)
            throw DomainException.Conflict("social.limit_reached",
                $"You can have at most {SocialProfileRules.MaxActiveAccountsPerUser} active social profiles. Remove one first.");
    }

    private async Task EnsureHandleAvailableAsync(Guid userId, SocialPlatform platform, string normalized, Guid? exceptId, CancellationToken ct)
    {
        var owner = await db.Set<SocialAccount>().AsNoTracking()
            .Where(a => a.Platform == platform && a.NormalizedHandle == normalized && a.Id != exceptId)
            .Select(a => new { a.UserId, a.IsActive }).FirstOrDefaultAsync(ct);
        if (owner is null) return;
        if (owner.UserId == userId)
            throw DomainException.Conflict("social.already_added",
                owner.IsActive
                    ? "You have already added this profile."
                    : "You added this profile before and removed it. Reactivate it instead of adding it again.");
        throw AlreadyRegistered();
    }

    private async Task SaveHandleAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            // Lost a race with another registration of the same (platform, handle).
            throw AlreadyRegistered();
        }
    }

    private static DomainException AlreadyRegistered() => DomainException.Conflict("social.already_registered",
        "This profile is already registered on Optimize All. If it belongs to you, contact support.");

    private async Task<SocialAccount> OwnedAsync(Guid userId, Guid id, CancellationToken ct) =>
        await db.Set<SocialAccount>().FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct)
        ?? throw DomainException.NotFound("SocialAccount");

    private async Task<(EligibilityCriteria, ParticipantProfile)> CriteriaAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Set<User>().AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        return await CriteriaAsync(user, ct);
    }

    private async Task<(EligibilityCriteria, ParticipantProfile)> CriteriaAsync(User user, CancellationToken ct) =>
        (EligibilityCriteria.Global(await settings.MinAccountAgeDaysAsync(ct), await settings.MinFollowersAsync(ct)),
            ParticipantProfile.From(user));

    private SocialAccountDto ToDto(SocialAccount a, EligibilityCriteria criteria, ParticipantProfile participant)
    {
        var e = EligibilityEvaluator.EvaluateAccount(criteria, participant, a, Now);
        return new SocialAccountDto(a.Id, a.Platform, a.Handle, a.ProfileUrl, a.AccountCreatedAt, e.AccountAgeDays, a.FollowerCount,
            a.PrimaryLanguage, a.AudienceCountryCode, a.VerificationStatus, a.VerificationNote, a.IsActive, e.IsEligible,
            e.EligibleFrom, e.Reasons, a.CreatedAt, a.UpdatedAt, a.ConcurrencyStamp);
    }

    private static ReviewSocialAccountDto ToReviewDto(SocialAccount a, User u, DateTime now) => new(
        a.Id, a.Platform, a.Handle, a.ProfileUrl, a.AccountCreatedAt, Math.Max(a.AccountAgeDays(now), 0), a.FollowerCount,
        a.PrimaryLanguage, a.AudienceCountryCode, a.VerificationStatus, a.VerificationNote, a.VerifiedAt, a.IsActive,
        a.CreatedAt, a.UpdatedAt, new SocialAccountOwnerDto(u.Id, u.DisplayName, u.Email, u.CountryCode), a.ConcurrencyStamp);

    private static object AuditView(SocialAccount a) => new
    {
        a.Platform, a.Handle, a.ProfileUrl, a.AccountCreatedAt, a.FollowerCount, a.VerificationStatus, a.IsActive,
    };

    private static string? NormalizeLanguage(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string? NormalizeCountry(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
