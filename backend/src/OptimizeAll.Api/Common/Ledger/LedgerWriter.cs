using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Ledger;

public sealed record NewEarning(
    Guid UserId,
    EarningType Type,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    string Description,
    bool RequiresApproval,
    Guid? CampaignId = null,
    Guid? SubmissionId = null,
    Guid? ReferralId = null,
    Guid? RewardRuleSetId = null,
    int? RewardRuleSetVersion = null,
    Guid? RewardRuleId = null,
    string? Reason = null,
    Guid? CreatedByUserId = null,
    EarningRateSource? RateSource = null,
    Guid? CodeProgramId = null,
    Guid? CodeSaleId = null);

/// <summary>Where a post reward's rate came from (campaign rules, or a person-level card / group / custom rate).</summary>
public sealed record EarningRateSource(
    OptimizeAll.Domain.Rewards.RateSourceLevel Level, string Label, Guid? RateCardId = null, int? RateCardVersion = null,
    Guid? RateGroupId = null, Guid? RateAssignmentId = null);

public interface IPayoutScheduleProvider
{
    /// <summary>The schedule in force at <paramref name="atUtc"/>; a biweekly default if none has been configured.</summary>
    Task<PayoutSchedule> GetActiveAsync(DateTime atUtc, CancellationToken ct = default);
}

public sealed class PayoutScheduleProvider(AppDbContext db) : IPayoutScheduleProvider
{
    /// <summary>Default: biweekly, cutoff Sunday 23:59:59 UTC, anchored on Sunday 2026-01-04.</summary>
    public static PayoutSchedule Default() => new()
    {
        Frequency = PayoutFrequency.Biweekly,
        AnchorCutoffDate = new DateOnly(2026, 1, 4),
        CutoffLocalTime = new TimeOnly(23, 59, 59),
        TimeZone = "UTC",
        PaymentDelayDays = 5,
        MinimumPayoutAmount = 10m,
        SettlementCurrency = "USD",
        EarningHoldDays = 3,
        AutoPrepareBatches = true,
        EffectiveFrom = DateTime.MinValue,
    };

    public async Task<PayoutSchedule> GetActiveAsync(DateTime atUtc, CancellationToken ct = default) =>
        await db.Set<PayoutSchedule>().AsNoTracking()
            .Where(s => s.EffectiveFrom <= atUtc)
            .OrderByDescending(s => s.EffectiveFrom).ThenByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct)
        ?? Default();
}

public sealed record ResolvedRate(decimal Rate, Guid? ExchangeRateId);

public interface IExchangeRateProvider
{
    /// <summary>Latest rate effective at <paramref name="atUtc"/> converting 1 <paramref name="from"/> into <paramref name="to"/>.</summary>
    Task<ResolvedRate> GetRateAsync(string from, string to, DateTime atUtc, CancellationToken ct = default);
}

public sealed class ExchangeRateProvider(AppDbContext db) : IExchangeRateProvider
{
    public async Task<ResolvedRate> GetRateAsync(string from, string to, DateTime atUtc, CancellationToken ct = default)
    {
        from = Money.Normalize(from);
        to = Money.Normalize(to);
        if (from == to) return new ResolvedRate(1m, null);

        // Take the latest direct (from→to) AND the latest inverse (to→from) rate and use whichever took effect later:
        // finance may record a pair in either direction, and a newer inverse rate must win over a stale direct one.
        var direct = await db.Set<ExchangeRate>().AsNoTracking()
            .Where(r => r.BaseCurrency == from && r.QuoteCurrency == to && r.EffectiveAt <= atUtc)
            .OrderByDescending(r => r.EffectiveAt).ThenByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        var inverse = await db.Set<ExchangeRate>().AsNoTracking()
            .Where(r => r.BaseCurrency == to && r.QuoteCurrency == from && r.EffectiveAt <= atUtc)
            .OrderByDescending(r => r.EffectiveAt).ThenByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);

        // On an exact tie the direct quote wins (no rounding through 1/rate).
        if (direct is not null && (inverse is null || direct.EffectiveAt >= inverse.EffectiveAt))
            return new ResolvedRate(direct.Rate, direct.Id);
        if (inverse is not null) return new ResolvedRate(Math.Round(1m / inverse.Rate, 8, MidpointRounding.AwayFromZero), inverse.Id);

        throw new DomainException("fx.rate_missing",
            $"No exchange rate from {from} to {to} is configured. Add one under Finance → Exchange rates.", DomainErrorKind.Conflict);
    }
}

public interface ILedgerWriter
{
    /// <summary>
    /// Stages an immutable earning entry. Converts to the settlement currency using the rate in force now and
    /// stores both the original amount and the applied rate. Idempotent by <see cref="NewEarning.IdempotencyKey"/>:
    /// returns the existing entry if one was already recorded (the unique index is the final guard).
    /// </summary>
    Task<EarningEntry> RecordAsync(NewEarning earning, CancellationToken ct = default);

    /// <summary>Moves a PendingApproval entry to Approved and starts its payout hold period.</summary>
    Task ApproveAsync(EarningEntry entry, Guid? approvedBy, CancellationToken ct = default);

    /// <summary>Declines a PendingApproval entry (never payable). A reason is mandatory.</summary>
    void Decline(EarningEntry entry, string reason, Guid? declinedBy);

    /// <summary>
    /// Reverses an entry. Unpaid entries are cancelled (status Reversed) with a zero-sum negative leg for the audit
    /// trail; paid entries get a negative Approved entry that is netted against the participant's next payout.
    /// Entries in a finalized/draft payout batch cannot be reversed until removed from the batch or paid.
    /// </summary>
    Task<EarningEntry> ReverseAsync(EarningEntry entry, string reason, Guid? actor, CancellationToken ct = default);
}

public sealed class LedgerWriter(
    AppDbContext db,
    IPayoutScheduleProvider schedules,
    IExchangeRateProvider rates,
    IAuditLogger audit,
    TimeProvider clock) : ILedgerWriter
{
    public async Task<EarningEntry> RecordAsync(NewEarning earning, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(earning.IdempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(earning));
        if (!Money.IsSupported(earning.Currency))
            throw new DomainException("ledger.currency_unsupported", $"Currency {earning.Currency} is not supported.");
        if (earning.Type is EarningType.Adjustment or EarningType.Reversal && string.IsNullOrWhiteSpace(earning.Reason))
            throw new DomainException("ledger.reason_required", "A reason is required for adjustments and reversals.");
        if (earning.Type != EarningType.Adjustment && earning.Type != EarningType.Reversal && earning.Amount < 0)
            throw new DomainException("ledger.negative_amount", "Earnings must be positive; use an adjustment for debits.");
        if (earning.Amount == 0)
            throw new DomainException("ledger.zero_amount", "Earning amount must not be zero.");

        var existing = db.Set<EarningEntry>().Local.FirstOrDefault(e => e.IdempotencyKey == earning.IdempotencyKey)
                       ?? await db.Set<EarningEntry>().FirstOrDefaultAsync(e => e.IdempotencyKey == earning.IdempotencyKey, ct);
        if (existing is not null) return existing;

        var now = clock.GetUtcNow().UtcDateTime;
        var schedule = await schedules.GetActiveAsync(now, ct);
        var currency = Money.Normalize(earning.Currency);
        var amount = Money.Round(earning.Amount, currency);
        var rate = await rates.GetRateAsync(currency, schedule.SettlementCurrency, now, ct);
        var settlement = Money.Convert(amount, rate.Rate, schedule.SettlementCurrency);

        var entry = new EarningEntry
        {
            UserId = earning.UserId,
            CampaignId = earning.CampaignId,
            SubmissionId = earning.SubmissionId,
            ReferralId = earning.ReferralId,
            CodeProgramId = earning.CodeProgramId,
            CodeSaleId = earning.CodeSaleId,
            Type = earning.Type,
            Status = earning.RequiresApproval ? EarningStatus.PendingApproval : EarningStatus.Approved,
            Amount = amount,
            Currency = currency,
            ExchangeRate = rate.Rate,
            ExchangeRateId = rate.ExchangeRateId,
            SettlementAmount = settlement,
            SettlementCurrency = schedule.SettlementCurrency,
            RewardRuleSetId = earning.RewardRuleSetId,
            RewardRuleSetVersion = earning.RewardRuleSetVersion,
            RewardRuleId = earning.RewardRuleId,
            RateSource = earning.RateSource?.Level,
            RateSourceLabel = earning.RateSource?.Label is { } label && label.Length > 200 ? label[..200] : earning.RateSource?.Label,
            RateCardId = earning.RateSource?.RateCardId,
            RateCardVersion = earning.RateSource?.RateCardVersion,
            RateGroupId = earning.RateSource?.RateGroupId,
            RateAssignmentId = earning.RateSource?.RateAssignmentId,
            IdempotencyKey = earning.IdempotencyKey,
            Description = earning.Description.Length > 300 ? earning.Description[..300] : earning.Description,
            Reason = earning.Reason,
            CreatedAt = now,
            CreatedByUserId = earning.CreatedByUserId,
        };
        if (!earning.RequiresApproval)
        {
            entry.ApprovedAt = now;
            entry.ApprovedByUserId = earning.CreatedByUserId;
            // Debits (negative adjustments) net against the very next payout; credits wait for the hold period.
            entry.AvailableAt = amount < 0 ? now : now.AddDays(schedule.EarningHoldDays);
        }

        db.Set<EarningEntry>().Add(entry);
        return entry;
    }

    public async Task ApproveAsync(EarningEntry entry, Guid? approvedBy, CancellationToken ct = default)
    {
        if (entry.Status != EarningStatus.PendingApproval)
            throw DomainException.Conflict("ledger.not_pending", "Only pending earnings can be approved.");
        var now = clock.GetUtcNow().UtcDateTime;
        var schedule = await schedules.GetActiveAsync(now, ct);
        entry.Status = EarningStatus.Approved;
        entry.ApprovedAt = now;
        entry.ApprovedByUserId = approvedBy;
        entry.AvailableAt = now.AddDays(schedule.EarningHoldDays);
        audit.Record("ledger.earning_approved", nameof(EarningEntry), entry.Id,
            after: new { entry.Type, entry.Amount, entry.Currency });
    }

    public void Decline(EarningEntry entry, string reason, Guid? declinedBy)
    {
        if (entry.Status != EarningStatus.PendingApproval)
            throw DomainException.Conflict("ledger.not_pending", "Only pending earnings can be declined.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("ledger.reason_required", "A reason is required.");
        entry.Status = EarningStatus.Declined;
        entry.Reason = reason;
        audit.Record("ledger.earning_declined", nameof(EarningEntry), entry.Id,
            after: new { entry.Type, entry.Amount, entry.Currency, declinedBy }, reason: reason);
    }

    public Task<EarningEntry> ReverseAsync(EarningEntry entry, string reason, Guid? actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("ledger.reason_required", "A reason is required for reversals.");
        if (entry.Type == EarningType.Reversal)
            throw DomainException.Conflict("ledger.cannot_reverse_reversal", "A reversal cannot itself be reversed.");
        if (entry.ReversedByEntryId is not null || entry.Status is EarningStatus.Reversed)
            throw DomainException.Conflict("ledger.already_reversed", "This earning has already been reversed.");
        if (entry.Amount < 0)
            throw DomainException.Conflict("ledger.cannot_reverse_debit",
                "A debit cannot be reversed; record a positive adjustment instead.");
        if (entry.Status == EarningStatus.Declined)
            throw DomainException.Conflict("ledger.declined", "Declined earnings were never payable and need no reversal.");
        if (entry.Status == EarningStatus.Scheduled)
            throw DomainException.Conflict("ledger.in_payout_batch",
                "This earning is in a payout batch. Hold or remove the participant's payout item before reversing.");

        var now = clock.GetUtcNow().UtcDateTime;
        var wasPaid = entry.Status == EarningStatus.Paid;
        var reversal = new EarningEntry
        {
            UserId = entry.UserId,
            CampaignId = entry.CampaignId,
            SubmissionId = entry.SubmissionId,
            ReferralId = entry.ReferralId,
            CodeProgramId = entry.CodeProgramId,
            CodeSaleId = entry.CodeSaleId,
            Type = EarningType.Reversal,
            // A paid earning becomes a clawback owed by the participant; an unpaid one is simply cancelled.
            Status = wasPaid ? EarningStatus.Approved : EarningStatus.Reversed,
            Amount = -entry.Amount,
            Currency = entry.Currency,
            ExchangeRate = entry.ExchangeRate,
            ExchangeRateId = entry.ExchangeRateId,
            SettlementAmount = -entry.SettlementAmount,
            SettlementCurrency = entry.SettlementCurrency,
            RewardRuleSetId = entry.RewardRuleSetId,
            RewardRuleSetVersion = entry.RewardRuleSetVersion,
            RewardRuleId = entry.RewardRuleId,
            RateSource = entry.RateSource,
            RateSourceLabel = entry.RateSourceLabel,
            RateCardId = entry.RateCardId,
            RateCardVersion = entry.RateCardVersion,
            RateGroupId = entry.RateGroupId,
            RateAssignmentId = entry.RateAssignmentId,
            IdempotencyKey = $"reversal:{entry.Id}",
            Description = $"Reversal of {entry.Type}",
            Reason = reason,
            ReversesEntryId = entry.Id,
            CreatedAt = now,
            CreatedByUserId = actor,
            ApprovedAt = now,
            ApprovedByUserId = actor,
            AvailableAt = now,
            ReversedAt = wasPaid ? null : now,
        };
        db.Set<EarningEntry>().Add(reversal);

        entry.ReversedByEntryId = reversal.Id;
        entry.ReversedAt = now;
        if (!wasPaid) entry.Status = EarningStatus.Reversed;

        audit.Record("ledger.earning_reversed", nameof(EarningEntry), entry.Id,
            before: new { entry.Type, entry.Amount, entry.Currency, Status = wasPaid ? "Paid" : "Unpaid" },
            after: new { ReversalEntryId = reversal.Id, reversal.Amount, reversal.Status }, reason: reason);

        return Task.FromResult(reversal);
    }
}
