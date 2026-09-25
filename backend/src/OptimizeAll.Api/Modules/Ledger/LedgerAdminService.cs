using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Domain.Support;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Ledger;

/// <summary>Finance write operations on the ledger: adjustments, reversals, bonus approvals and exchange rates.</summary>
public sealed class LedgerAdminService(
    AppDbContext db,
    ILedgerWriter ledger,
    IAuditLogger audit,
    INotificationService notifications,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const decimal MaxExchangeRate = 1_000_000m;

    public async Task<AdjustmentResultDto> CreateAdjustmentAsync(CreateAdjustmentRequest request, CancellationToken ct)
    {
        FinanceGuards.RequireConfirm(request.Confirm);
        var currency = FinanceGuards.RequireCurrency(request.Currency);
        var amount = Money.Round(request.Amount!.Value, currency);
        if (amount == 0)
            throw new DomainException("ledger.zero_amount", "The adjustment amount must not be zero (after rounding to the currency's minor unit).");
        var userId = request.UserId!.Value;
        // Segregation of duties: nobody may credit or debit their own account.
        if (userId == currentUser.Id)
            throw DomainException.Forbidden("ledger.self_adjustment", "You cannot create an adjustment on your own account.");
        var key = $"adjustment:{request.RequestId!.Value:N}";

        // Idempotent replay: a retried/double-clicked request returns the adjustment it already created.
        var existing = await db.Set<EarningEntry>().AsNoTracking().FirstOrDefaultAsync(e => e.IdempotencyKey == key, ct);
        if (existing is not null) return await ReplayAsync(existing, userId, amount, currency, ct);

        if (!await db.Set<User>().AnyAsync(u => u.Id == userId, ct)) throw DomainException.NotFound("User");

        Guid? campaignId = null;
        if (request.SubmissionId is { } submissionId)
        {
            var submission = await db.Set<Submission>().AsNoTracking()
                .Where(s => s.Id == submissionId).Select(s => new { s.UserId, s.CampaignId }).FirstOrDefaultAsync(ct)
                ?? throw DomainException.NotFound("Submission");
            if (submission.UserId != userId)
                throw new DomainException("ledger.submission_mismatch", "The submission does not belong to this participant.");
            campaignId = submission.CampaignId;
        }

        string? ticketReference = null;
        if (request.SupportTicketId is { } ticketId)
        {
            var ticket = await db.Set<SupportTicket>().AsNoTracking()
                .Where(t => t.Id == ticketId).Select(t => new { t.UserId, t.Reference }).FirstOrDefaultAsync(ct)
                ?? throw DomainException.NotFound("SupportTicket");
            if (ticket.UserId != userId)
                throw new DomainException("ledger.ticket_mismatch", "The support ticket does not belong to this participant.");
            ticketReference = ticket.Reference;
        }

        var reason = request.Reason.Trim();
        var description = (amount > 0 ? "Manual credit" : "Manual debit") +
                          (ticketReference is null ? string.Empty : $" (support ticket {ticketReference})");
        // Credits create money: they wait in the pending-earnings queue until a DIFFERENT user approves them (the
        // approval enforces approver ≠ creator ≠ beneficiary). Debits only reduce what is owed and apply immediately.
        var entry = await ledger.RecordAsync(new NewEarning(
            userId, EarningType.Adjustment, amount, currency, key, description, RequiresApproval: amount > 0,
            CampaignId: campaignId, SubmissionId: request.SubmissionId, Reason: reason,
            CreatedByUserId: currentUser.Id), ct);

        audit.Record("ledger.adjustment_created", nameof(EarningEntry), entry.Id,
            after: new
            {
                entry.UserId, entry.Amount, entry.Currency, entry.SettlementAmount, entry.SettlementCurrency,
                entry.SubmissionId, SupportTicketId = request.SupportTicketId, RequestId = request.RequestId, entry.Status,
            },
            reason: reason);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            // A concurrent request with the same requestId won the race: return its result.
            db.ChangeTracker.Clear();
            var winner = await db.Set<EarningEntry>().AsNoTracking().FirstAsync(e => e.IdempotencyKey == key, ct);
            return await ReplayAsync(winner, userId, amount, currency, ct);
        }

        var row = await LedgerQueries.SingleAsync(db, entry.Id, ct);
        return new AdjustmentResultDto(true, row!.ToLedgerDto());
    }

    private async Task<AdjustmentResultDto> ReplayAsync(EarningEntry existing, Guid userId, decimal amount, string currency, CancellationToken ct)
    {
        if (existing.Type != EarningType.Adjustment || existing.UserId != userId || existing.Amount != amount || existing.Currency != currency)
            throw DomainException.Conflict("ledger.request_id_reused",
                "This requestId was already used for a different adjustment. Generate a new requestId.");
        var row = await LedgerQueries.SingleAsync(db, existing.Id, ct);
        return new AdjustmentResultDto(false, row!.ToLedgerDto());
    }

    public async Task<ReversalResultDto> ReverseAsync(Guid earningId, ReverseEarningRequest request, CancellationToken ct)
    {
        FinanceGuards.RequireConfirm(request.Confirm);
        var entry = await db.Set<EarningEntry>().FirstOrDefaultAsync(e => e.Id == earningId, ct)
                    ?? throw DomainException.NotFound("Earning");
        var reversal = await ledger.ReverseAsync(entry, request.Reason.Trim(), currentUser.Id, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("ledger.already_reversed", "This earning has already been reversed.");
        }

        var original = await LedgerQueries.SingleAsync(db, entry.Id, ct);
        var leg = await LedgerQueries.SingleAsync(db, reversal.Id, ct);
        return new ReversalResultDto(original!.ToLedgerDto(), leg!.ToLedgerDto());
    }

    public async Task<LedgerRowDto> ApprovePendingAsync(Guid earningId, Guid concurrencyStamp, CancellationToken ct)
    {
        var entry = await LoadPendingAsync(earningId, concurrencyStamp, ct);
        await EnsureNotAwaitingLiveCheckAsync(entry, ct);
        await ledger.ApproveAsync(entry, currentUser.Id, ct);

        await notifications.StageAsync(new NotificationRequest(
            entry.UserId, NotificationTypes.EarningApproved, "Earning approved",
            $"Your {Describe(entry.Type)} of {Money.Format(entry.Amount, entry.Currency)} was approved and will be included in an upcoming payout.",
            AppLinks.Earnings), ct);
        await db.SaveChangesAsync(ct);
        return (await LedgerQueries.SingleAsync(db, entry.Id, ct))!.ToLedgerDto();
    }

    public async Task<LedgerRowDto> DeclinePendingAsync(Guid earningId, DeclinePendingEarningRequest request, CancellationToken ct)
    {
        var entry = await LoadPendingAsync(earningId, request.ConcurrencyStamp!.Value, ct);
        ledger.Decline(entry, request.Reason.Trim(), currentUser.Id);
        await db.SaveChangesAsync(ct);
        return (await LedgerQueries.SingleAsync(db, entry.Id, ct))!.ToLedgerDto();
    }

    private async Task<EarningEntry> LoadPendingAsync(Guid earningId, Guid concurrencyStamp, CancellationToken ct)
    {
        var entry = await db.Set<EarningEntry>().FirstOrDefaultAsync(e => e.Id == earningId, ct)
                    ?? throw DomainException.NotFound("Earning");
        if (entry.Status != EarningStatus.PendingApproval)
            throw DomainException.Conflict("ledger.not_pending", "This earning is no longer pending approval.");
        if (entry.ConcurrencyStamp != concurrencyStamp)
            throw DomainException.Conflict("concurrency.conflict", "This earning was changed by someone else. Reload and try again.");
        // Four-eyes: whoever recorded a manual bonus/credit cannot approve it, and nobody decides their own earnings.
        if (entry.CreatedByUserId is { } creator && creator == currentUser.Id)
            throw DomainException.Forbidden("ledger.self_approval", "You cannot approve or decline an earning you created.");
        if (entry.UserId == currentUser.Id)
            throw DomainException.Forbidden("ledger.self_approval", "You cannot approve or decline an earning credited to your own account.");
        return entry;
    }

    private async Task EnsureNotAwaitingLiveCheckAsync(EarningEntry entry, CancellationToken ct)
    {
        if (entry.SubmissionId is not { } submissionId) return;
        var status = await db.Set<Submission>().AsNoTracking()
            .Where(s => s.Id == submissionId).Select(s => (LiveCheckStatus?)s.LiveCheckStatus).FirstOrDefaultAsync(ct);
        if (status == LiveCheckStatus.Pending)
            throw DomainException.Conflict("ledger.awaiting_live_check",
                "This reward is waiting for the post's live-duration check and is approved by the review team, not here.");
    }

    private static string Describe(EarningType type) => type switch
    {
        EarningType.PostReward => "post reward",
        EarningType.FirstPostBonus => "first post bonus",
        EarningType.TimeLimitedBonus => "bonus",
        EarningType.QualityBonus => "quality bonus",
        EarningType.ReferralReward => "referral reward",
        EarningType.SaleCommission => "sale commission",
        EarningType.SaleTierBonus => "sales tier bonus",
        _ => "earning",
    };

    public async Task<ExchangeRateDto> CreateExchangeRateAsync(CreateExchangeRateRequest request, CancellationToken ct)
    {
        FinanceGuards.RequireConfirm(request.Confirm);
        var baseCurrency = FinanceGuards.RequireCurrency(request.BaseCurrency, "baseCurrency");
        var quoteCurrency = FinanceGuards.RequireCurrency(request.QuoteCurrency, "quoteCurrency");
        if (baseCurrency == quoteCurrency)
            throw new DomainException("fx.same_currency", "Base and quote currency must differ.");
        var rate = Math.Round(request.Rate!.Value, 8, MidpointRounding.AwayFromZero);
        if (rate <= 0 || rate > MaxExchangeRate)
            throw new DomainException("fx.invalid_rate", $"The rate must be greater than 0 and at most {MaxExchangeRate:N0}.");
        var now = clock.GetUtcNow().UtcDateTime;
        var effectiveAt = request.EffectiveAt!.Value.ToUniversalTime();
        if (effectiveAt < now.AddDays(-1))
            throw new DomainException("fx.effective_too_early",
                "A rate cannot take effect more than one day in the past; existing earnings keep the rate they were created with.");

        var row = new ExchangeRate
        {
            BaseCurrency = baseCurrency,
            QuoteCurrency = quoteCurrency,
            Rate = rate,
            EffectiveAt = effectiveAt,
            Source = request.Source.Trim(),
            CreatedAt = now,
            CreatedByUserId = currentUser.Id,
        };
        db.Set<ExchangeRate>().Add(row);
        audit.Record("fx.rate_created", nameof(ExchangeRate), row.Id,
            after: new { row.BaseCurrency, row.QuoteCurrency, row.Rate, row.EffectiveAt, row.Source }, reason: request.Reason.Trim());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("fx.duplicate", "A rate for this currency pair with the same effective time already exists.");
        }
        return ToDto(row);
    }

    public static ExchangeRateDto ToDto(ExchangeRate r) =>
        new(r.Id, r.BaseCurrency, r.QuoteCurrency, r.Rate, r.EffectiveAt, r.Source, r.CreatedAt, r.CreatedByUserId);
}
