using System.Data;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Rates;

public interface IRateAssignmentsService
{
    Task<PagedResult<RateAssignmentDto>> ListAsync(RateAssignmentQuery query, CancellationToken ct);
    Task<RateAssignmentDto> GetAsync(Guid id, CancellationToken ct);
    Task<RateAssignmentDto> CreateAsync(CreateRateAssignmentRequest request, CancellationToken ct);
    Task<RateAssignmentDto> UpdateAsync(Guid id, UpdateRateAssignmentRequest request, CancellationToken ct);
    Task<RateAssignmentDto> EndAsync(Guid id, EndRateAssignmentRequest request, CancellationToken ct);

    /// <summary>
    /// Stages a new assignment after every check (target, card, campaign, window, overlap, currency). Must be called
    /// inside a write transaction that holds the target's named lock (see <see cref="RateAssignmentsService.TargetLockName"/>).
    /// </summary>
    Task<RateAssignment> StageAsync(RateCard card, RateAssignmentTarget target, Guid? userId, Guid? groupId, Guid? campaignId,
        DateTime? validFrom, DateTime? validTo, string reason, CancellationToken ct);

    Task<IReadOnlyList<RateAssignmentDto>> ToDtosAsync(IReadOnlyList<RateAssignment> assignments, CancellationToken ct);
}

public sealed class RateAssignmentsService(
    AppDbContext db,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IPersonalRateService rates,
    TimeProvider clock) : IRateAssignmentsService
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Serializes assignment changes of one person or group so overlapping deals can't be created in parallel.</summary>
    public static string TargetLockName(Guid targetId) => $"rates:target:{targetId:N}";

    public async Task<PagedResult<RateAssignmentDto>> ListAsync(RateAssignmentQuery query, CancellationToken ct)
    {
        var q = db.Set<RateAssignment>().AsNoTracking();
        if (query.UserId is { } userId) q = q.Where(a => a.UserId == userId);
        if (query.GroupId is { } groupId) q = q.Where(a => a.GroupId == groupId);
        if (query.CampaignId is { } campaignId) q = q.Where(a => a.CampaignId == campaignId);
        if (query.RateCardId is { } cardId) q = q.Where(a => a.RateCardId == cardId);
        if (query.ActiveOnly)
        {
            var now = Now;
            q = q.Where(a => a.EndedAt == null && (a.ValidTo == null || a.ValidTo > now));
        }
        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<RateAssignmentDto>(await ToDtosAsync(page, ct), total, query.Page, query.PageSize);
    }

    public async Task<RateAssignmentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Set<RateAssignment>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        return (await ToDtosAsync(new[] { a }, ct))[0];
    }

    public async Task<RateAssignmentDto> CreateAsync(CreateRateAssignmentRequest request, CancellationToken ct)
    {
        var target = request.Target!.Value;
        var targetId = (target == RateAssignmentTarget.Person ? request.UserId : request.GroupId)
            ?? throw new DomainException("rates.target_required",
                target == RateAssignmentTarget.Person ? "Choose the person (userId)." : "Choose the group (groupId).",
                errors: new Dictionary<string, string[]> { [target == RateAssignmentTarget.Person ? "userId" : "groupId"] = new[] { "Required." } });
        if (target == RateAssignmentTarget.Person && request.GroupId is not null || target == RateAssignmentTarget.Group && request.UserId is not null)
            throw new DomainException("rates.target_ambiguous", "Send either a userId (Person) or a groupId (Group), not both.");

        RateAssignment assignment;
        await using (await db.Dialect().AcquireNamedLockAsync(db, TargetLockName(targetId), LockTimeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, "rate_cards", request.RateCardId!.Value, ct, RowLockMode.Share);
            var card = await db.Set<RateCard>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.RateCardId, ct)
                ?? throw DomainException.NotFound("RateCard");
            if (card.Kind == RateCardKind.Custom)
                throw new DomainException("rates.custom_card_not_assignable",
                    "A custom rate belongs to one person; edit it from their Rates tab instead of assigning it.");
            assignment = await StageAsync(card, target, request.UserId, request.GroupId, request.CampaignId, request.ValidFrom, request.ValidTo,
                request.Reason, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(assignment.Id, ct);
    }

    public async Task<RateAssignment> StageAsync(RateCard card, RateAssignmentTarget target, Guid? userId, Guid? groupId, Guid? campaignId,
        DateTime? validFrom, DateTime? validTo, string reason, CancellationToken ct)
    {
        var now = Now;
        if (card.Status != RateCardStatus.Active)
            throw DomainException.Conflict("rate_card.not_active",
                card.Status == RateCardStatus.Draft ? "Activate the rate card before assigning it." : "This rate card is archived and can't be assigned.");

        RateGroup? group = null;
        if (target == RateAssignmentTarget.Person)
            await EnsureAssignablePersonAsync(userId!.Value, ct);
        else
        {
            group = await db.Set<RateGroup>().AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct) ?? throw DomainException.NotFound("RateGroup");
            if (group.ArchivedAt is not null) throw DomainException.Conflict("rate_group.archived", "This group is archived.");
        }

        Campaign? campaign = null;
        if (campaignId is { } cid)
        {
            campaign = await db.Set<Campaign>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct) ?? throw DomainException.NotFound("Campaign");
            if (campaign.Status is CampaignStatus.Archived or CampaignStatus.Ended)
                throw DomainException.Conflict("campaign.closed", "Rates can't be assigned for an ended or archived campaign.");
        }

        var (from, to) = ValidateWindow(validFrom, validTo, now);
        await EnsureNoOverlapAsync(null, target, userId, groupId, campaignId, card.Kind == RateCardKind.Custom, from, to, ct);

        // Currency: the card must convert into every campaign currency it can price (never priced at 0 later).
        var currencies = campaign is not null ? new[] { campaign.BudgetCurrency } : await rates.LiveCampaignCurrenciesAsync(ct);
        await rates.EnsureConvertibleAsync(card.Currency, currencies, now, ct);

        var assignment = new RateAssignment
        {
            RateCardId = card.Id, Target = target, UserId = userId, GroupId = groupId, CampaignId = campaignId,
            IsCustom = card.Kind == RateCardKind.Custom, ValidFrom = from, ValidTo = to, Note = reason.Trim(),
            CreatedByUserId = currentUser.Id,
        };
        db.Set<RateAssignment>().Add(assignment);
        audit.Record("rate_assignment.created", nameof(RateAssignment), assignment.Id, after: new
        {
            RateCardId = card.Id, CardName = card.Name, Target = target.ToString(), userId, groupId, GroupName = group?.Name,
            campaignId, ValidFrom = from, ValidTo = to,
            Level = RateAssignment.LevelFor(target, assignment.IsCustom, campaignId is not null, group?.MembershipMode).ToString(),
        }, reason: reason.Trim());
        return assignment;
    }

    public async Task<RateAssignmentDto> UpdateAsync(Guid id, UpdateRateAssignmentRequest request, CancellationToken ct)
    {
        var existing = await db.Set<RateAssignment>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw NotFound();
        var targetId = existing.UserId ?? existing.GroupId!.Value;
        await using (await db.Dialect().AcquireNamedLockAsync(db, TargetLockName(targetId), LockTimeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, "rate_assignments", id, ct);
            var a = await db.Set<RateAssignment>().FirstAsync(x => x.Id == id, ct);
            ConcurrencyGuard.Apply(db, a, request.ConcurrencyStamp!.Value);
            if (a.EndedAt is not null) throw Ended();
            var now = Now;
            // A start that already passed stays as it was (history): only future starts and the end may move.
            var from = a.ValidFrom is { } oldFrom && oldFrom <= now ? oldFrom : request.ValidFrom is { } f ? RateCardFactory.Utc(f) : (DateTime?)null;
            if (a.ValidFrom is { } started && started <= now && request.ValidFrom is { } requested && RateCardFactory.Utc(requested) != started)
                throw new DomainException("rates.window_started", "This assignment has already started; its start date can no longer change.",
                    errors: new Dictionary<string, string[]> { ["validFrom"] = new[] { "Already started." } });
            var (validFrom, validTo) = ValidateWindow(from, request.ValidTo, now, allowPastStart: true);
            await EnsureNoOverlapAsync(a.Id, a.Target, a.UserId, a.GroupId, a.CampaignId, a.IsCustom, validFrom, validTo, ct);
            var before = new { a.ValidFrom, a.ValidTo };
            a.ValidFrom = validFrom;
            a.ValidTo = validTo;
            audit.Record("rate_assignment.updated", nameof(RateAssignment), a.Id, before, new { a.ValidFrom, a.ValidTo }, request.Reason.Trim());
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<RateAssignmentDto> EndAsync(Guid id, EndRateAssignmentRequest request, CancellationToken ct)
    {
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, "rate_assignments", id, ct);
            var a = await db.Set<RateAssignment>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
            ConcurrencyGuard.Apply(db, a, request.ConcurrencyStamp!.Value);
            if (a.EndedAt is not null) throw Ended();
            a.EndedAt = Now;
            a.EndedByUserId = currentUser.Id;
            a.EndReason = request.Reason.Trim();
            audit.Record("rate_assignment.ended", nameof(RateAssignment), a.Id, new { EndedAt = (DateTime?)null }, new { a.EndedAt }, a.EndReason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    private async Task EnsureAssignablePersonAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw DomainException.NotFound("User");
        if (!user.HasRole(Role.Participant))
            throw new DomainException("rates.not_participant", "Rates can only be assigned to participants.");
        if (user.Status == UserStatus.Deactivated)
            throw DomainException.Conflict("rates.user_deactivated", "This account is deactivated.");
    }

    private static (DateTime? From, DateTime? To) ValidateWindow(DateTime? validFrom, DateTime? validTo, DateTime now, bool allowPastStart = false)
    {
        var from = validFrom is { } f ? RateCardFactory.Utc(f) : (DateTime?)null;
        var to = validTo is { } t ? RateCardFactory.Utc(t) : (DateTime?)null;
        if (from is { } s && to is { } e && e <= s)
            throw new DomainException("rates.invalid_window", "The end must be after the start.",
                errors: new Dictionary<string, string[]> { ["validTo"] = new[] { "Must be after the start." } });
        if (to is { } end && end <= now)
            throw new DomainException("rates.invalid_window", "The end must be in the future.",
                errors: new Dictionary<string, string[]> { ["validTo"] = new[] { "Must be in the future." } });
        if (!allowPastStart && from is { } start && start < now.AddDays(-1))
            throw new DomainException("rates.invalid_window",
                "The start can be at most one day in the past (posts already submitted keep their price).",
                errors: new Dictionary<string, string[]> { ["validFrom"] = new[] { "At most one day in the past." } });
        return (from, to);
    }

    /// <summary>
    /// One card per person/group, scope (campaign or global) and kind at a time: overlapping windows are refused (409
    /// rates.duplicate_assignment) so a level never has two competing rates for the same target.
    /// </summary>
    private async Task EnsureNoOverlapAsync(Guid? exceptId, RateAssignmentTarget target, Guid? userId, Guid? groupId, Guid? campaignId,
        bool isCustom, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var now = Now;
        var q = db.Set<RateAssignment>().AsNoTracking()
            .Where(a => a.Target == target && a.CampaignId == campaignId && a.IsCustom == isCustom && a.EndedAt == null &&
                        (a.ValidTo == null || a.ValidTo > now) && (exceptId == null || a.Id != exceptId));
        q = target == RateAssignmentTarget.Person ? q.Where(a => a.UserId == userId) : q.Where(a => a.GroupId == groupId);
        foreach (var other in await q.ToListAsync(ct))
        {
            var overlaps = (other.ValidTo is null || from is null || from < other.ValidTo) && (to is null || other.ValidFrom is null || other.ValidFrom < to);
            if (!overlaps) continue;
            var card = await db.Set<RateCard>().AsNoTracking().Where(c => c.Id == other.RateCardId).Select(c => c.Name).FirstAsync(ct);
            throw new DomainException("rates.duplicate_assignment",
                $"{(isCustom ? "A custom rate" : $"The card '{card}'")} already applies to this {(target == RateAssignmentTarget.Person ? "person" : "group")} " +
                $"{(campaignId is null ? "in every campaign" : "in this campaign")} during that period. End or shorten it first.",
                DomainErrorKind.Conflict, new Dictionary<string, string[]> { ["assignmentId"] = new[] { other.Id.ToString() } });
        }
    }

    public async Task<IReadOnlyList<RateAssignmentDto>> ToDtosAsync(IReadOnlyList<RateAssignment> assignments, CancellationToken ct)
    {
        if (assignments.Count == 0) return Array.Empty<RateAssignmentDto>();
        var cardIds = assignments.Select(a => a.RateCardId).Distinct().ToList();
        var cards = await db.Set<RateCard>().AsNoTracking().Where(c => cardIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var groupIds = assignments.Where(a => a.GroupId.HasValue).Select(a => a.GroupId!.Value).Distinct().ToList();
        var groups = await db.Set<RateGroup>().AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        var campaignIds = assignments.Where(a => a.CampaignId.HasValue).Select(a => a.CampaignId!.Value).Distinct().ToList();
        var campaigns = await db.Set<Campaign>().AsNoTracking().Where(c => campaignIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var userIds = assignments.SelectMany(a => new[] { a.UserId, (Guid?)a.CreatedByUserId }).Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var now = Now;
        return assignments.Select(a =>
        {
            var card = cards[a.RateCardId];
            var group = a.GroupId is { } gid ? groups.GetValueOrDefault(gid) : null;
            var level = RateAssignment.LevelFor(a.Target, a.IsCustom, a.CampaignId is not null, group?.MembershipMode);
            var active = a.EndedAt is null && (a.ValidTo is null || a.ValidTo > now) && card.Status == RateCardStatus.Active &&
                         (group is null || group.ArchivedAt is null);
            return new RateAssignmentDto(a.Id, level, RateSources.Describe(level), a.Target,
                new RateCardRefDto(card.Id, card.Name, card.Kind, card.Status, card.Currency, card.CurrentVersion),
                a.UserId is { } uid ? new UserRefDto(uid, users.GetValueOrDefault(uid, "Unknown user")) : null,
                group is null ? null : new RateGroupRefDto(group.Id, group.Name, group.MembershipMode, group.Priority),
                a.CampaignId is { } cid ? new RateCampaignRefDto(cid, campaigns.GetValueOrDefault(cid, string.Empty)) : null,
                a.IsCustom, a.ValidFrom, a.ValidTo, a.EndedAt, a.EndReason, a.Note, active, a.CreatedAt,
                new UserRefDto(a.CreatedByUserId, users.GetValueOrDefault(a.CreatedByUserId, "Unknown user")), a.ConcurrencyStamp);
        }).ToList();
    }

    private static DomainException NotFound() => DomainException.NotFound("RateAssignment");

    private static DomainException Ended() => DomainException.Conflict("rates.assignment_ended", "This assignment has already ended.");
}
