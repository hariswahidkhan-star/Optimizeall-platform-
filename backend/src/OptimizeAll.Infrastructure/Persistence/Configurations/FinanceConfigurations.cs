using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class EarningEntryConfiguration : IEntityTypeConfiguration<EarningEntry>
{
    public void Configure(EntityTypeBuilder<EarningEntry> b)
    {
        b.ToTable("earning_entries", t =>
        {
            t.HasCheckConstraint("ck_earning_rate_positive", "`ExchangeRate` > 0");
            t.HasCheckConstraint("ck_earning_sign_consistent",
                "(`Amount` >= 0 AND `SettlementAmount` >= 0) OR (`Amount` <= 0 AND `SettlementAmount` <= 0)");
            t.HasCheckConstraint("ck_earning_reversal_negative",
                "`Type` <> 'Reversal' OR (`Amount` < 0 AND `ReversesEntryId` IS NOT NULL)");
            t.HasCheckConstraint("ck_earning_reason_required",
                "`Type` NOT IN ('Adjustment','Reversal') OR (`Reason` IS NOT NULL AND CHAR_LENGTH(`Reason`) > 0)");
        });
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.SettlementCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.ExchangeRate).HasPrecision(18, 8);
        b.Property(x => x.IdempotencyKey).HasMaxLength(150).IsRequired();

        // Idempotency: the same logical earning (e.g. a submission's post reward) can only be recorded once,
        // even if two reviewers approve concurrently or a job retries.
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.Property(x => x.Description).HasMaxLength(300).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(1000);

        b.HasIndex(x => new { x.UserId, x.Status });
        b.HasIndex(x => new { x.Status, x.AvailableAt });
        b.HasIndex(x => new { x.UserId, x.CampaignId, x.CreatedAt });
        // Campaign budget / spent aggregates filter on (CampaignId, Status IN ...).
        b.HasIndex(x => new { x.CampaignId, x.Status });
        b.HasIndex(x => x.SubmissionId);
        b.HasIndex(x => x.PayoutItemId);
        b.HasIndex(x => x.ReversesEntryId).IsUnique();

        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PayoutItem>().WithMany().HasForeignKey(x => x.PayoutItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EarningEntry>().WithMany().HasForeignKey(x => x.ReversesEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ExchangeRate>().WithMany().HasForeignKey(x => x.ExchangeRateId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> b)
    {
        b.ToTable("exchange_rates", t => t.HasCheckConstraint("ck_exchange_rate_positive", "`Rate` > 0"));
        b.Property(x => x.BaseCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.QuoteCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Rate).HasPrecision(18, 8);
        b.Property(x => x.Source).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.BaseCurrency, x.QuoteCurrency, x.EffectiveAt }).IsUnique();
    }
}

internal sealed class PayoutScheduleConfiguration : IEntityTypeConfiguration<PayoutSchedule>
{
    public void Configure(EntityTypeBuilder<PayoutSchedule> b)
    {
        b.ToTable("payout_schedules", t =>
        {
            t.HasCheckConstraint("ck_payout_schedule_min_nonnegative", "`MinimumPayoutAmount` >= 0");
            t.HasCheckConstraint("ck_payout_schedule_hold_nonnegative", "`EarningHoldDays` >= 0 AND `PaymentDelayDays` >= 0");
        });
        b.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        b.Property(x => x.SettlementCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.ChangeReason).HasMaxLength(500);
        b.HasIndex(x => x.EffectiveFrom);
    }
}

internal sealed class PayoutBatchConfiguration : IEntityTypeConfiguration<PayoutBatch>
{
    public void Configure(EntityTypeBuilder<PayoutBatch> b)
    {
        b.ToTable("payout_batches", t => t.HasCheckConstraint("ck_payout_batch_total_nonnegative", "`TotalAmount` >= 0"));
        b.Property(x => x.Reference).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Reference).IsUnique();
        b.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.CancelReason).HasMaxLength(1000);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.PeriodKey).HasMaxLength(10).IsRequired();
        b.Property(x => x.ExclusionsJson).HasColumnType("json");
        b.HasIndex(x => new { x.Status, x.CutoffAt });
        b.HasIndex(x => new { x.PeriodKey, x.Currency });
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayoutItemConfiguration : IEntityTypeConfiguration<PayoutItem>
{
    public void Configure(EntityTypeBuilder<PayoutItem> b)
    {
        b.ToTable("payout_items", t => t.HasCheckConstraint("ck_payout_item_amount_positive", "`Amount` > 0"));
        // A participant appears at most once per batch.
        b.HasIndex(x => new { x.BatchId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Status });
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.PaymentProvider).HasMaxLength(40).IsRequired();
        b.Property(x => x.PaymentReference).HasMaxLength(120);
        b.Property(x => x.ProviderTransactionId).HasMaxLength(120);
        b.Property(x => x.FailureReason).HasMaxLength(1000);
        b.Property(x => x.HoldReason).HasMaxLength(1000);
        b.Property(x => x.DestinationHint).HasMaxLength(64);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> b)
    {
        b.ToTable("payment_attempts");
        b.Property(x => x.Provider).HasMaxLength(40).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(150).IsRequired();
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.Property(x => x.ProviderReference).HasMaxLength(120);
        b.Property(x => x.Message).HasMaxLength(1000);
        b.HasIndex(x => x.PayoutItemId);
        b.HasOne<PayoutItem>().WithMany().HasForeignKey(x => x.PayoutItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayoutItemEarningConfiguration : IEntityTypeConfiguration<PayoutItemEarning>
{
    public void Configure(EntityTypeBuilder<PayoutItemEarning> b)
    {
        b.ToTable("payout_item_earnings");
        b.HasIndex(x => new { x.PayoutItemId, x.EarningEntryId }).IsUnique();
        // Deliberately NOT unique: reconciliation must be able to see (and report) an earning paid twice.
        b.HasIndex(x => x.EarningEntryId);
        b.HasIndex(x => x.UserId);
        b.HasOne<PayoutItem>().WithMany().HasForeignKey(x => x.PayoutItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayoutHoldConfiguration : IEntityTypeConfiguration<PayoutHold>
{
    public void Configure(EntityTypeBuilder<PayoutHold> b)
    {
        b.ToTable("payout_holds");
        b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ReleaseNote).HasMaxLength(1000);
        b.HasIndex(x => new { x.UserId, x.ReleasedAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.IsActive);
    }
}
