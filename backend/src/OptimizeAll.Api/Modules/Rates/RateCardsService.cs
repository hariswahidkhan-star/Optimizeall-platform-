using System.Data;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Rates;

public static class RateCardFactory
{
    public static RateCardVersion BuildVersion(RateCardRatesInput input, Guid cardId, int version, DateTime effectiveFrom, DateTime now,
        Guid createdBy, string reason)
    {
        var v = new RateCardVersion
        {
            RateCardId = cardId,
            Version = version,
            Currency = Money.Normalize(input.Currency),
            DailyCapPerParticipant = input.DailyCapPerParticipant,
            WeeklyCapPerParticipant = input.WeeklyCapPerParticipant,
            CampaignCapPerParticipant = input.CampaignCapPerParticipant,
            StackCampaignBonuses = input.StackCampaignBonuses,
            EffectiveFrom = effectiveFrom,
            CreatedAt = now,
            CreatedByUserId = createdBy,
            ChangeReason = reason.Trim(),
            Status = RateCardVersionStatus.Approved,
        };
        foreach (var l in input.Lines)
        {
            v.Lines.Add(new RateCardLine
            {
                VersionId = v.Id,
                Platform = l.Platform,
                Format = l.Format,
                CountryCode = string.IsNullOrWhiteSpace(l.CountryCode) ? null : l.CountryCode.Trim().ToUpperInvariant(),
                Amount = Money.IsSupported(v.Currency) ? Money.Round(l.Amount, v.Currency) : l.Amount,
                Label = string.IsNullOrWhiteSpace(l.Label) ? null : l.Label.Trim(),
            });
        }
        return v;
    }

    public static RateCardVersion Copy(RateCardVersion source, Guid cardId, int version, DateTime now, Guid createdBy, string reason)
    {
        var v = new RateCardVersion
        {
            RateCardId = cardId, Version = version, Currency = source.Currency,
            DailyCapPerParticipant = source.DailyCapPerParticipant, WeeklyCapPerParticipant = source.WeeklyCapPerParticipant,
            CampaignCapPerParticipant = source.CampaignCapPerParticipant, StackCampaignBonuses = source.StackCampaignBonuses,
            EffectiveFrom = now, CreatedAt = now, CreatedByUserId = createdBy, ChangeReason = reason, Status = RateCardVersionStatus.Approved,
        };
        foreach (var l in source.Lines)
            v.Lines.Add(new RateCardLine { VersionId = v.Id, Platform = l.Platform, Format = l.Format, CountryCode = l.CountryCode, Amount = l.Amount, Label = l.Label });
        return v;
    }

    public static object Snapshot(RateCardVersion v) => new
    {
        v.Version, v.Currency, v.DailyCapPerParticipant, v.WeeklyCapPerParticipant, v.CampaignCapPerParticipant,
        v.StackCampaignBonuses, v.EffectiveFrom, Status = v.Status.ToString(), v.MaxIncreasePercent,
        Lines = v.Lines.Select(l => new { Platform = l.Platform?.ToString(), Format = l.Format?.ToString(), l.CountryCode, l.Amount, l.Label }),
    };

    public static DateTime Utc(DateTime value) => RewardRuleSetFactory.Utc(value);
}

public interface IRateCardsService
{
    Task<PagedResult<RateCardListItemDto>> ListAsync(RateCardQuery query, CancellationToken ct);
    Task<RateCardDto> GetAsync(Guid id, CancellationToken ct);
    Task<RateCardDto> CreateAsync(CreateRateCardRequest request, CancellationToken ct);
    Task<RateCardDto> UpdateAsync(Guid id, UpdateRateCardRequest request, CancellationToken ct);
    Task<RateCardDto> CreateVersionAsync(Guid id, CreateRateCardVersionRequest request, CancellationToken ct);
    Task<RateCardDto> ApproveVersionAsync(Guid id, int version, ApproveRateCardVersionRequest request, CancellationToken ct);
    Task<RateCardDto> RejectVersionAsync(Guid id, int version, RejectRateCardVersionRequest request, CancellationToken ct);
    Task<RateCardDto> ActivateAsync(Guid id, ActivateRateCardRequest request, CancellationToken ct);
    Task<RateCardDto> ArchiveAsync(Guid id, ArchiveRateCardRequest request, CancellationToken ct);
    Task<RateCardDto> DuplicateAsync(Guid id, DuplicateRateCardRequest request, CancellationToken ct);

    /// <summary>Creates a hidden custom card with its first version (used by negotiated custom rates).</summary>
    RateCard CreateCustomCard(Guid ownerId, string ownerName, RateCardRatesInput rates, string? name, string reason);
}

public sealed class RateCardsService(
    AppDbContext db,
    IAuditLogger audit,
    ICurrentUser currentUser,
    ISettingsService settings,
    IPersonalRateService rates,
    IRateAssignmentsService assignments,
    TimeProvider clock) : IRateCardsService
{
    public const string CardsLock = "rates:cards";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<RateCardListItemDto>> ListAsync(RateCardQuery query, CancellationToken ct)
    {
        var q = db.Set<RateCard>().AsNoTracking().Where(c => c.Kind == RateCardKind.Standard);
        if (query.Status is { } status) q = q.Where(c => c.Status == status);
        else q = q.Where(c => c.Status != RateCardStatus.Archived);
        if (!string.IsNullOrWhiteSpace(query.Currency))
        {
            var currency = Money.Normalize(query.Currency);
            q = q.Where(c => c.Currency == currency);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(c => EF.Functions.Like(c.Name, like, "\\"));
        }
        var total = await q.CountAsync(ct);
        var page = await q.OrderBy(c => c.Name).ThenBy(c => c.Id).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var ids = page.Select(c => c.Id).ToList();
        var versions = await db.Set<RateCardVersion>().AsNoTracking().Include(v => v.Lines)
            .Where(v => ids.Contains(v.RateCardId)).ToListAsync(ct);
        var now = Now;
        var active = await db.Set<RateAssignment>().AsNoTracking()
            .Where(a => ids.Contains(a.RateCardId) && a.EndedAt == null && (a.ValidTo == null || a.ValidTo > now))
            .GroupBy(a => a.RateCardId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var items = page.Select(c =>
        {
            var mine = versions.Where(v => v.RateCardId == c.Id).ToList();
            var current = mine.Where(v => v.Status == RateCardVersionStatus.Approved).OrderByDescending(v => v.Version).FirstOrDefault();
            var amounts = current?.Lines.Select(l => l.Amount).ToList() ?? new List<decimal>();
            return new RateCardListItemDto(c.Id, c.Name, c.Description, c.Kind, c.Status, c.Currency, c.CurrentVersion,
                amounts.Count, amounts.Count == 0 ? null : amounts.Min(), amounts.Count == 0 ? null : amounts.Max(),
                active.GetValueOrDefault(c.Id), mine.Any(v => v.Status == RateCardVersionStatus.PendingApproval), c.UpdatedAt);
        }).ToList();
        return new PagedResult<RateCardListItemDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<RateCardDto> GetAsync(Guid id, CancellationToken ct)
    {
        var card = await db.Set<RateCard>().AsNoTracking().AsSplitQuery().Include(c => c.Versions).ThenInclude(v => v.Lines)
            .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();
        var names = await Names(card.Versions.SelectMany(v => new[] { (Guid?)v.CreatedByUserId, v.DecidedByUserId })
            .Append(card.OwnerUserId), ct);
        var now = Now;
        var current = RateCardRules.VersionInForce(card.Versions, now);
        var used = await db.Set<SubmissionRate>().CountAsync(r => r.RateCardId == id, ct);
        var threshold = await settings.GetAsync(SettingKeys.RatesFourEyesIncreasePercent, 0, ct);
        var assignmentDtos = await assignments.ToDtosAsync(
            await db.Set<RateAssignment>().AsNoTracking().Where(a => a.RateCardId == id)
                .OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct), ct);
        return new RateCardDto(card.Id, card.Name, card.Description, card.Kind, card.Status, card.Currency, card.CurrentVersion,
            card.OwnerUserId is { } o ? Ref(names, o) : null, card.CreatedAt, card.UpdatedAt, card.ArchivedAt, card.ArchiveReason,
            card.ConcurrencyStamp,
            card.Versions.OrderByDescending(v => v.Version).Select(v => new RateCardVersionDto(
                v.Id, v.Version, v.Currency, v.DailyCapPerParticipant, v.WeeklyCapPerParticipant, v.CampaignCapPerParticipant,
                v.StackCampaignBonuses, v.EffectiveFrom, v.CreatedAt, Ref(names, v.CreatedByUserId), v.ChangeReason, v.Status,
                v.DecidedByUserId is { } d ? Ref(names, d) : null, v.DecidedAt, v.DecisionNote, v.MaxIncreasePercent,
                current?.Id == v.Id,
                v.Lines.OrderBy(l => l.Platform.HasValue ? 1 : 0).ThenBy(l => l.Platform).ThenBy(l => l.Format).ThenBy(l => l.CountryCode)
                    .Select(RateCardLineDto.From).ToList())).ToList(),
            assignmentDtos, used, threshold > 0, threshold);
    }

    public async Task<RateCardDto> CreateAsync(CreateRateCardRequest request, CancellationToken ct)
    {
        var now = Now;
        var card = new RateCard
        {
            Name = request.Name.Trim(), Description = Blank(request.Description), Kind = RateCardKind.Standard,
            Status = request.Activate ? RateCardStatus.Active : RateCardStatus.Draft, CreatedByUserId = currentUser.Id,
        };
        var version = RateCardFactory.BuildVersion(request, card.Id, 1, now, now, currentUser.Id, request.Reason);
        RateCardRules.EnsureValid(version);
        card.Currency = version.Currency;
        card.CurrentVersion = 1;
        card.Versions.Add(version);

        await using (await db.Dialect().AcquireNamedLockAsync(db, CardsLock, LockTimeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await EnsureNameFreeAsync(card.Name, null, ct);
            db.Set<RateCard>().Add(card);
            audit.Record("rate_card.created", nameof(RateCard), card.Id,
                after: new { card.Name, card.Description, Status = card.Status.ToString(), Version = RateCardFactory.Snapshot(version) },
                reason: request.Reason.Trim());
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(card.Id, ct);
    }

    public RateCard CreateCustomCard(Guid ownerId, string ownerName, RateCardRatesInput rates, string? name, string reason)
    {
        var now = Now;
        var cardName = string.IsNullOrWhiteSpace(name) ? $"Custom rate — {ownerName}" : name.Trim();
        var card = new RateCard
        {
            Name = cardName.Length > 120 ? cardName[..120] : cardName, Kind = RateCardKind.Custom, Status = RateCardStatus.Active,
            OwnerUserId = ownerId, CreatedByUserId = currentUser.Id, CurrentVersion = 1,
        };
        var version = RateCardFactory.BuildVersion(rates, card.Id, 1, now, now, currentUser.Id, reason);
        RateCardRules.EnsureValid(version);
        card.Currency = version.Currency;
        card.Versions.Add(version);
        db.Set<RateCard>().Add(card);
        audit.Record("rate_card.created", nameof(RateCard), card.Id,
            after: new { card.Name, Kind = card.Kind.ToString(), OwnerUserId = ownerId, Version = RateCardFactory.Snapshot(version) },
            reason: reason.Trim());
        return card;
    }

    public async Task<RateCardDto> UpdateAsync(Guid id, UpdateRateCardRequest request, CancellationToken ct)
    {
        await using (await db.Dialect().AcquireNamedLockAsync(db, CardsLock, LockTimeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, "rate_cards", id, ct);
            var card = await db.Set<RateCard>().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();
            ConcurrencyGuard.Apply(db, card, request.ConcurrencyStamp!.Value);
            if (card.Status == RateCardStatus.Archived) throw Archived();
            var before = new { card.Name, card.Description };
            var name = request.Name.Trim();
            if (card.Kind == RateCardKind.Standard && !string.Equals(name, card.Name, StringComparison.OrdinalIgnoreCase))
                await EnsureNameFreeAsync(name, card.Id, ct);
            card.Name = name;
            card.Description = Blank(request.Description);
            audit.Record("rate_card.updated", nameof(RateCard), card.Id, before, new { card.Name, card.Description });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<RateCardDto> CreateVersionAsync(Guid id, CreateRateCardVersionRequest request, CancellationToken ct)
    {
        if (!request.Confirm)
            throw new DomainException("confirmation.required", "Confirm the rate change by sending \"confirm\": true.");
        var now = Now;
        var effectiveFrom = request.EffectiveFrom is { } requested ? RateCardFactory.Utc(requested) : now;
        if (effectiveFrom < now.AddMinutes(-5))
            throw new DomainException("rate_card.effective_in_past",
                "A new version can't take effect in the past: submissions already priced keep their rate. Use now or a future time.",
                errors: new Dictionary<string, string[]> { ["effectiveFrom"] = new[] { "Use now or a future time." } });
        if (effectiveFrom < now) effectiveFrom = now;
        var threshold = await settings.GetAsync(SettingKeys.RatesFourEyesIncreasePercent, 0, ct);

        RateCardVersion version;
        try
        {
            await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted);
            await db.Dialect().LockRowAsync(db, "rate_cards", id, ct);
            var card = await db.Set<RateCard>().Include(c => c.Versions).ThenInclude(v => v.Lines)
                .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();
            if (card.Status == RateCardStatus.Archived) throw Archived();
            var latest = card.Versions.Count == 0 ? 0 : card.Versions.Max(v => v.Version);
            if (request.BaseVersion is { } baseVersion && baseVersion != latest)
                throw DomainException.Conflict("rate_card.version_conflict",
                    $"Someone else saved version {latest} of this rate card after you opened version {baseVersion}. Reload to see it, then make your change again.");
            if (card.Versions.Any(v => v.Status == RateCardVersionStatus.PendingApproval))
                throw DomainException.Conflict("rate_card.pending_approval",
                    "A rate increase on this card is waiting for approval. Approve or reject it before saving another version.");

            version = RateCardFactory.BuildVersion(request, card.Id, latest + 1, effectiveFrom, now, currentUser.Id, request.Reason);
            RateCardRules.EnsureValid(version);
            var previous = card.Versions.Where(v => v.Status == RateCardVersionStatus.Approved).OrderByDescending(v => v.Version).FirstOrDefault();
            version.MaxIncreasePercent = RateCardRules.MaxIncreasePercent(previous, version) is { } pct && pct != decimal.MaxValue ? pct : null;
            var increase = RateCardRules.MaxIncreasePercent(previous, version);
            // Four-eyes: a raise above the configured threshold (or a currency change while it is on) waits for a second person.
            if (threshold > 0 && increase is { } inc && inc > threshold)
                version.Status = RateCardVersionStatus.PendingApproval;

            if (version.Status == RateCardVersionStatus.Approved && !string.Equals(version.Currency, previous?.Currency, StringComparison.OrdinalIgnoreCase))
                await EnsureAssignmentsConvertibleAsync(card.Id, version.Currency, ct);

            db.Set<RateCardVersion>().Add(version);
            if (version.Status == RateCardVersionStatus.Approved)
            {
                card.CurrentVersion = version.Version;
                card.Currency = version.Currency;
            }
            ConcurrencyGuard.Touch(db, card);
            audit.Record(version.Status == RateCardVersionStatus.Approved ? "rate_card.version_created" : "rate_card.version_pending_approval",
                nameof(RateCard), card.Id,
                before: previous is null ? null : RateCardFactory.Snapshot(previous),
                after: RateCardFactory.Snapshot(version), reason: request.Reason.Trim());
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("rate_card.version_conflict", "Someone else saved a new version at the same time. Reload and try again.");
        }
        return await GetAsync(id, ct);
    }

    public Task<RateCardDto> ApproveVersionAsync(Guid id, int version, ApproveRateCardVersionRequest request, CancellationToken ct) =>
        DecideAsync(id, version, approve: true, Blank(request.Note), ct);

    public Task<RateCardDto> RejectVersionAsync(Guid id, int version, RejectRateCardVersionRequest request, CancellationToken ct) =>
        DecideAsync(id, version, approve: false, request.Reason.Trim(), ct);

    private async Task<RateCardDto> DecideAsync(Guid id, int number, bool approve, string? note, CancellationToken ct)
    {
        var now = Now;
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, "rate_cards", id, ct);
            var card = await db.Set<RateCard>().Include(c => c.Versions).ThenInclude(v => v.Lines)
                .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();
            var version = card.Versions.FirstOrDefault(v => v.Version == number)
                ?? throw new DomainException("rate_card.version_not_found", "That version does not exist.", DomainErrorKind.NotFound);
            if (version.Status != RateCardVersionStatus.PendingApproval)
                throw DomainException.Conflict("rate_card.not_pending", $"Version {number} is not waiting for approval (it is {version.Status}).");
            if (approve)
            {
                // Four-eyes: the person who raised the rate can't approve it, and nobody approves their own deal.
                if (version.CreatedByUserId == currentUser.Id)
                    throw DomainException.Forbidden("rates.self_approval", "A different person must approve a rate increase you made.");
                if (card.OwnerUserId == currentUser.Id)
                    throw DomainException.Forbidden("rates.self_approval", "You cannot approve a change to your own rate.");
                if (card.Status == RateCardStatus.Archived) throw Archived();
                var previous = card.Versions.Where(v => v.Status == RateCardVersionStatus.Approved).OrderByDescending(v => v.Version).FirstOrDefault();
                if (!string.Equals(version.Currency, previous?.Currency, StringComparison.OrdinalIgnoreCase))
                    await EnsureAssignmentsConvertibleAsync(card.Id, version.Currency, ct);
                version.Status = RateCardVersionStatus.Approved;
                // Takes effect when approved at the earliest: nothing is priced with an unapproved rate.
                if (version.EffectiveFrom < now) version.EffectiveFrom = now;
                card.CurrentVersion = Math.Max(card.CurrentVersion, version.Version);
                card.Currency = version.Currency;
            }
            else version.Status = RateCardVersionStatus.Rejected;
            version.DecidedAt = now;
            version.DecidedByUserId = currentUser.Id;
            version.DecisionNote = note;
            ConcurrencyGuard.Touch(db, card);
            audit.Record(approve ? "rate_card.version_approved" : "rate_card.version_rejected", nameof(RateCard), card.Id,
                after: new { version.Version, Status = version.Status.ToString(), version.MaxIncreasePercent, version.EffectiveFrom }, reason: note);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<RateCardDto> ActivateAsync(Guid id, ActivateRateCardRequest request, CancellationToken ct)
    {
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, "rate_cards", id, ct);
            var card = await db.Set<RateCard>().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();
            ConcurrencyGuard.Apply(db, card, request.ConcurrencyStamp!.Value);
            if (card.Status != RateCardStatus.Draft)
                throw DomainException.Conflict("rate_card.not_draft", $"Only draft cards can be activated (this one is {card.Status}).");
            if (!await db.Set<RateCardVersion>().AnyAsync(v => v.RateCardId == id && v.Status == RateCardVersionStatus.Approved, ct))
                throw DomainException.Conflict("rate_card.no_version", "The card has no approved version yet.");
            card.Status = RateCardStatus.Active;
            audit.Record("rate_card.activated", nameof(RateCard), card.Id, new { Status = "Draft" }, new { Status = "Active" });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<RateCardDto> ArchiveAsync(Guid id, ArchiveRateCardRequest request, CancellationToken ct)
    {
        var now = Now;
        var reason = request.Reason.Trim();
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, "rate_cards", id, ct);
            var card = await db.Set<RateCard>().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();
            ConcurrencyGuard.Apply(db, card, request.ConcurrencyStamp!.Value);
            if (card.Status == RateCardStatus.Archived) throw Archived();
            var live = await db.Set<RateAssignment>()
                .Where(a => a.RateCardId == id && a.EndedAt == null && (a.ValidTo == null || a.ValidTo > now)).ToListAsync(ct);
            if (live.Count > 0 && !request.EndAssignments)
                throw DomainException.Conflict("rate_card.in_use",
                    $"This card is assigned {live.Count} time{(live.Count == 1 ? "" : "s")}. End those assignments first, or archive with \"endAssignments\": true.");
            foreach (var a in live)
            {
                a.EndedAt = now;
                a.EndedByUserId = currentUser.Id;
                a.EndReason = ReasonText.Fit($"Rate card archived: {reason}", 500);
            }
            card.Status = RateCardStatus.Archived;
            card.ArchivedAt = now;
            card.ArchivedByUserId = currentUser.Id;
            card.ArchiveReason = reason;
            audit.Record("rate_card.archived", nameof(RateCard), card.Id, new { Status = "Active" },
                new { Status = "Archived", EndedAssignments = live.Count, AssignmentIds = live.Select(a => a.Id).Take(500) }, reason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<RateCardDto> DuplicateAsync(Guid id, DuplicateRateCardRequest request, CancellationToken ct)
    {
        var now = Now;
        var source = await db.Set<RateCard>().AsNoTracking().Include(c => c.Versions).ThenInclude(v => v.Lines)
            .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();
        var from = source.Versions.Where(v => v.Status == RateCardVersionStatus.Approved).OrderByDescending(v => v.Version).FirstOrDefault()
                   ?? throw DomainException.Conflict("rate_card.no_version", "The card has no approved version to copy.");
        var copy = new RateCard
        {
            Name = request.Name.Trim(), Description = source.Description, Kind = RateCardKind.Standard, Status = RateCardStatus.Draft,
            CreatedByUserId = currentUser.Id, Currency = from.Currency, CurrentVersion = 1,
        };
        copy.Versions.Add(RateCardFactory.Copy(from, copy.Id, 1, now, currentUser.Id, $"Copied from '{source.Name}' v{from.Version}"));
        await using (await db.Dialect().AcquireNamedLockAsync(db, CardsLock, LockTimeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await EnsureNameFreeAsync(copy.Name, null, ct);
            db.Set<RateCard>().Add(copy);
            audit.Record("rate_card.duplicated", nameof(RateCard), copy.Id, after: new { SourceCardId = id, SourceVersion = from.Version, copy.Name });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(copy.Id, ct);
    }

    /// <summary>Every campaign currency the card's live assignments can price in must convert from <paramref name="currency"/>.</summary>
    private async Task EnsureAssignmentsConvertibleAsync(Guid cardId, string currency, CancellationToken ct)
    {
        var now = Now;
        var live = await db.Set<RateAssignment>().AsNoTracking()
            .Where(a => a.RateCardId == cardId && a.EndedAt == null && (a.ValidTo == null || a.ValidTo > now))
            .Select(a => a.CampaignId).ToListAsync(ct);
        if (live.Count == 0) return;
        var scoped = live.Where(c => c.HasValue).Select(c => c!.Value).Distinct().ToList();
        var currencies = new List<string>();
        if (scoped.Count > 0)
            currencies.AddRange(await db.Set<Campaign>().Where(c => scoped.Contains(c.Id)).Select(c => c.BudgetCurrency).ToListAsync(ct));
        if (live.Any(c => c is null)) currencies.AddRange(await rates.LiveCampaignCurrenciesAsync(ct));
        await rates.EnsureConvertibleAsync(currency, currencies, now, ct);
    }

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.Trim().ToLower();
        if (await db.Set<RateCard>().AnyAsync(c => c.Kind == RateCardKind.Standard && c.Status != RateCardStatus.Archived &&
                                                   c.Name.ToLower() == lower && (exceptId == null || c.Id != exceptId), ct))
            throw new DomainException("rate_card.name_taken", $"A rate card named '{name.Trim()}' already exists.", DomainErrorKind.Conflict,
                new Dictionary<string, string[]> { ["name"] = new[] { "This name is already used by another rate card." } });
    }

    private async Task<Dictionary<Guid, string>> Names(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        return await db.Set<User>().AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    private static UserRefDto Ref(Dictionary<Guid, string> names, Guid id) => new(id, names.GetValueOrDefault(id, "Unknown user"));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DomainException NotFound() => DomainException.NotFound("RateCard");

    private static DomainException Archived() => DomainException.Conflict("rate_card.archived", "This rate card is archived and can't be changed.");
}
