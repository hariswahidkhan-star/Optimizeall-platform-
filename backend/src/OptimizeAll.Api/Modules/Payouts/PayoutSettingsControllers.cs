using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Ledger;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Payouts;

/// <summary>Versioned payout schedule (frequency, cutoff, minimum, settlement currency, hold period).</summary>
[ApiController]
[Route("api/v1/finance/payout-schedule")]
public sealed class PayoutScheduleController(
    AppDbContext db, IPayoutScheduleProvider schedules, IAuditLogger audit, ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    /// <summary>Tolerance for an <c>effectiveFrom</c> of "now" sent by a client whose clock is slightly behind.</summary>
    private static readonly TimeSpan EffectiveFromSkew = TimeSpan.FromMinutes(5);

    [HttpGet]
    [HasPermission(Permissions.PayoutsView)]
    public async Task<PayoutScheduleResponse> Get(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var current = await schedules.GetActiveAsync(now, ct);
        var history = await db.Set<PayoutSchedule>().AsNoTracking()
            .OrderByDescending(s => s.EffectiveFrom).ThenByDescending(s => s.CreatedAt).ToListAsync(ct);
        var isDefault = !history.Any(h => h.Id == current.Id);
        return new PayoutScheduleResponse(
            ToDto(current, isDefault),
            PayoutPeriodDto.From(PayoutPeriodCalculator.PeriodContaining(current, now)),
            PayoutPeriodDto.From(PayoutPeriodCalculator.LastCompletedPeriod(current, now)),
            PayoutPeriodCalculator.Upcoming(current, now, 6).Select(PayoutPeriodDto.From).ToList(),
            history.Where(h => h.EffectiveFrom > now).Select(h => ToDto(h, false)).ToList(),
            history.Select(h => ToDto(h, false)).ToList());
    }

    /// <summary>Inserts a new schedule version effective from <c>effectiveFrom</c>.</summary>
    [HttpPut]
    [HasPermission(Permissions.PayoutSettingsEdit)]
    public async Task<PayoutScheduleResponse> Update(UpdatePayoutScheduleRequest request, CancellationToken ct)
    {
        FinanceGuards.RequireConfirm(request.Confirm);
        var now = clock.GetUtcNow().UtcDateTime;
        var currency = FinanceGuards.RequireCurrency(request.SettlementCurrency, "settlementCurrency");
        PayoutPeriodCalculator.ResolveTimeZone(request.TimeZone.Trim());
        var cutoffTime = TimeOnly.ParseExact(request.CutoffLocalTime, request.CutoffLocalTime.Length == 5 ? "HH:mm" : "HH:mm:ss",
            CultureInfo.InvariantCulture);
        var effectiveFrom = request.EffectiveFrom!.Value.ToUniversalTime();
        if (effectiveFrom < now - EffectiveFromSkew)
            throw new DomainException("payout.effective_in_past", "A schedule change cannot take effect in the past.");
        if (effectiveFrom < now) effectiveFrom = now;
        var anchor = request.AnchorCutoffDate!.Value;
        if (anchor.Year is < 2000 or > 2100)
            throw new DomainException("payout.invalid_anchor", "The anchor cutoff date must be between 2000 and 2100.");

        var active = await schedules.GetActiveAsync(now, ct);
        if (!string.Equals(active.SettlementCurrency, currency, StringComparison.Ordinal))
        {
            var old = active.SettlementCurrency;
            var unpaid = await db.Set<EarningEntry>().AnyAsync(e => e.SettlementCurrency == old &&
                (e.Status == EarningStatus.Approved || e.Status == EarningStatus.Scheduled || e.Status == EarningStatus.PendingApproval), ct);
            if (unpaid)
                throw DomainException.Conflict("payout.settlement_currency_in_use",
                    $"Unpaid earnings are still settled in {old}. Pay out, reverse or decline them before changing the settlement currency.");
        }

        var schedule = new PayoutSchedule
        {
            Frequency = request.Frequency!.Value,
            AnchorCutoffDate = anchor,
            CutoffLocalTime = cutoffTime,
            TimeZone = request.TimeZone.Trim(),
            PaymentDelayDays = request.PaymentDelayDays,
            MinimumPayoutAmount = Money.Round(request.MinimumPayoutAmount, currency),
            SettlementCurrency = currency,
            EarningHoldDays = request.EarningHoldDays,
            AutoPrepareBatches = request.AutoPrepareBatches,
            EffectiveFrom = effectiveFrom,
            CreatedAt = now,
            CreatedByUserId = currentUser.Id,
            ChangeReason = request.Reason.Trim(),
        };
        db.Set<PayoutSchedule>().Add(schedule);
        audit.Record("payout.schedule_changed", nameof(PayoutSchedule), schedule.Id,
            before: ToDto(active, false), after: ToDto(schedule, false), reason: schedule.ChangeReason);
        await db.SaveChangesAsync(ct);
        return await Get(ct);
    }

    public static PayoutScheduleDto ToDto(PayoutSchedule s, bool isDefault) => new(
        isDefault ? null : s.Id, s.Frequency, s.AnchorCutoffDate, s.CutoffLocalTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        s.TimeZone, s.PaymentDelayDays, s.MinimumPayoutAmount, s.SettlementCurrency, s.EarningHoldDays, s.AutoPrepareBatches,
        isDefault ? null : s.EffectiveFrom, isDefault ? null : s.CreatedAt, s.CreatedByUserId, s.ChangeReason, isDefault);
}

/// <summary>Payout holds stop a participant's earnings from being paid until released.</summary>
[ApiController]
[Route("api/v1/finance/holds")]
[HasPermission(Permissions.PayoutsHold)]
public sealed class PayoutHoldsController(
    AppDbContext db, IAuditLogger audit, INotificationService notifications, ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<PayoutHoldDto>> List([FromQuery] HoldQuery query, CancellationToken ct)
    {
        var holds = from h in db.Set<PayoutHold>().AsNoTracking()
                    join u in db.Set<User>() on h.UserId equals u.Id
                    select new { Hold = h, u.Email, u.DisplayName };
        if (query.Active == true) holds = holds.Where(x => x.Hold.ReleasedAt == null);
        if (query.Active == false) holds = holds.Where(x => x.Hold.ReleasedAt != null);
        if (query.UserId is { } userId) holds = holds.Where(x => x.Hold.UserId == userId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            holds = holds.Where(x => EF.Functions.Like(x.Email, like) || EF.Functions.Like(x.DisplayName, like));
        }
        var page = await holds.OrderByDescending(x => x.Hold.CreatedAt).ToPagedAsync(query, ct);
        return new PagedResult<PayoutHoldDto>(page.Items.Select(x => ToDto(x.Hold, x.Email, x.DisplayName)).ToList(),
            page.Total, page.Page, page.PageSize);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateHoldRequest request, CancellationToken ct)
    {
        var userId = request.UserId!.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await PayoutStore.BeginAsync(db, ct);
        // Serialize with batch preparation/regeneration: take the same MySQL named lock on this transaction's
        // connection BEFORE reading anything. A prepare that is running finishes (and commits its draft items) first,
        // so the draft items below are visible and get held; a prepare that starts after us waits and sees the hold.
        // Released after commit (disposed before the transaction).
        await using var prepareLock = await PayoutStore.AcquirePrepareLockAsync(db, ct);
        // Row-lock the participant so two concurrent requests cannot both create an active hold.
        var locked = await db.Database
            .SqlQuery<string>($"SELECT `Email` AS `Value` FROM users WHERE `Id` = {userId.ToString()} FOR UPDATE").ToListAsync(ct);
        if (locked.Count == 0) throw DomainException.NotFound("User");
        if (await db.Set<PayoutHold>().AnyAsync(h => h.UserId == userId && h.ReleasedAt == null, ct))
            throw DomainException.Conflict("payout.hold_exists", "This participant already has an active payout hold.");

        var hold = new PayoutHold { UserId = userId, Reason = request.Reason.Trim(), CreatedAt = now, CreatedByUserId = currentUser.Id };
        db.Set<PayoutHold>().Add(hold);

        // Pending items of draft batches are held immediately; finalized items need a finance decision.
        var draftItems = await (from i in db.Set<PayoutItem>()
                                join b in db.Set<PayoutBatch>() on i.BatchId equals b.Id
                                where i.UserId == userId && i.Status == PayoutItemStatus.Pending && b.Status == PayoutBatchStatus.Draft
                                select new { i.Id, i.BatchId }).ToListAsync(ct);
        foreach (var item in draftItems)
        {
            await db.Set<PayoutItem>().Where(i => i.Id == item.Id && i.Status == PayoutItemStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, PayoutItemStatus.Held)
                    .SetProperty(i => i.HoldReason, "Payout hold: " + hold.Reason)
                    .SetProperty(i => i.UpdatedAt, now).SetProperty(i => i.ConcurrencyStamp, Guid.NewGuid()), ct);
            await db.Set<PayoutBatch>().Where(b => b.Id == item.BatchId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.ConcurrencyStamp, Guid.NewGuid()), ct);
            await PayoutStore.RecomputeTotalsAsync(db, item.BatchId, now, ct);
        }
        var awaiting = await db.Set<PayoutItem>().AsNoTracking()
            .Where(i => i.UserId == userId && i.Status == PayoutItemStatus.AwaitingPayment).Select(i => i.Id).ToListAsync(ct);

        audit.Record("payout.hold_created", nameof(PayoutHold), hold.Id,
            after: new { hold.UserId, HeldDraftItems = draftItems.Count, AwaitingPaymentItems = awaiting.Count }, reason: hold.Reason);
        await notifications.StageAsync(new NotificationRequest(userId, NotificationTypes.PayoutHold, "Your payouts are paused",
            EarningsSummaryService.NeutralHoldMessage, "/earnings"), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var user = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Email, u.DisplayName }).FirstAsync(ct);
        return StatusCode(StatusCodes.Status201Created,
            new CreateHoldResponse(ToDto(hold, user.Email, user.DisplayName), draftItems.Select(i => i.Id).ToList(), awaiting));
    }

    [HttpPost("{id:guid}/release")]
    public async Task<PayoutHoldDto> Release(Guid id, ReleaseHoldRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hold = await db.Set<PayoutHold>().AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct)
                   ?? throw DomainException.NotFound("PayoutHold");
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var released = await db.Set<PayoutHold>().Where(h => h.Id == id && h.ReleasedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.ReleasedAt, now).SetProperty(h => h.ReleasedByUserId, currentUser.Id)
                .SetProperty(h => h.ReleaseNote, note), ct);
        if (released == 0) throw DomainException.Conflict("payout.hold_not_active", "This hold was already released.");

        audit.Record("payout.hold_released", nameof(PayoutHold), id, after: new { hold.UserId }, reason: note);
        await notifications.StageAsync(new NotificationRequest(hold.UserId, NotificationTypes.PayoutHold, "Your payouts are active again",
            "Your payouts have resumed. Eligible earnings will be included in the next payout.", "/earnings"), ct);
        await db.SaveChangesAsync(ct);

        var row = await (from h in db.Set<PayoutHold>().AsNoTracking()
                         join u in db.Set<User>() on h.UserId equals u.Id
                         where h.Id == id
                         select new { Hold = h, u.Email, u.DisplayName }).FirstAsync(ct);
        return ToDto(row.Hold, row.Email, row.DisplayName);
    }

    private static PayoutHoldDto ToDto(PayoutHold h, string email, string name) => new(
        h.Id, new UserRefDto(h.UserId, name, email), h.Reason, h.CreatedAt, h.CreatedByUserId, h.ReleasedAt is null,
        h.ReleasedAt, h.ReleasedByUserId, h.ReleaseNote);
}
