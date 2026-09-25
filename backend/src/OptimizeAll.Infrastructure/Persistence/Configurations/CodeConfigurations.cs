using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

// Discount-code (affiliate) sales: programs, codes, assignments, sales and imports (docs/DISCOUNT_CODES.md).

internal sealed class CodeProgramConfiguration : IEntityTypeConfiguration<CodeProgram>
{
    public void Configure(EntityTypeBuilder<CodeProgram> b)
    {
        b.ToTable("code_programs", t =>
        {
            t.HasCheckConstraint("ck_code_programs_percent", "`Percent` IS NULL OR (`Percent` > 0 AND `Percent` <= 100)");
            t.HasCheckConstraint("ck_code_programs_flat", "`FlatAmount` IS NULL OR `FlatAmount` > 0");
        });
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.BrandName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Terms).HasMaxLength(4000);
        b.Property(x => x.StoreUrl).HasMaxLength(500);
        b.Property(x => x.DiscountLabel).HasMaxLength(120);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Percent).HasPrecision(9, 4);
        // Program list: by status, newest first.
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasIndex(x => x.CampaignId);
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Tiers).WithOne().HasForeignKey(t => t.ProgramId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CodeProgramTierConfiguration : IEntityTypeConfiguration<CodeProgramTier>
{
    public void Configure(EntityTypeBuilder<CodeProgramTier> b)
    {
        b.ToTable("code_program_tiers");
        b.HasIndex(x => new { x.ProgramId, x.ThresholdSales }).IsUnique();
        b.Property(x => x.Percent).HasPrecision(9, 4);
    }
}

internal sealed class CodePayoutOverrideConfiguration : IEntityTypeConfiguration<CodePayoutOverride>
{
    public void Configure(EntityTypeBuilder<CodePayoutOverride> b)
    {
        b.ToTable("code_payout_overrides", t =>
        {
            t.HasCheckConstraint("ck_code_payout_overrides_target",
                "(`Target` = 'Person' AND `UserId` IS NOT NULL AND `GroupId` IS NULL) OR (`Target` = 'Group' AND `GroupId` IS NOT NULL AND `UserId` IS NULL)");
        });
        b.Property(x => x.Percent).HasPrecision(9, 4);
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.EndReason).HasMaxLength(500);
        // Approval: a program's live overrides for one person and their groups.
        b.HasIndex(x => new { x.ProgramId, x.UserId });
        b.HasIndex(x => new { x.ProgramId, x.GroupId });
        b.HasOne<CodeProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RateGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DiscountCodeConfiguration : IEntityTypeConfiguration<DiscountCode>
{
    public void Configure(EntityTypeBuilder<DiscountCode> b)
    {
        b.ToTable("discount_codes");
        b.Property(x => x.Code).HasMaxLength(DiscountCode.MaxLength).IsRequired();
        b.Property(x => x.NormalizedCode).HasMaxLength(DiscountCode.MaxLength).IsRequired();
        b.Property(x => x.Note).HasMaxLength(300);
        // A code exists once per program (imports refuse duplicates; the index is the final guard).
        b.HasIndex(x => new { x.ProgramId, x.NormalizedCode }).IsUnique();
        // Code list filtered by status; auto-assign picks available codes.
        b.HasIndex(x => new { x.ProgramId, x.Status });
        b.HasOne<CodeProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CodeImportBatch>().WithMany().HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DiscountCodeAssignmentConfiguration : IEntityTypeConfiguration<DiscountCodeAssignment>
{
    public void Configure(EntityTypeBuilder<DiscountCodeAssignment> b)
    {
        b.ToTable("discount_code_assignments", t =>
        {
            t.HasCheckConstraint("ck_discount_code_assignments_target",
                "(`Target` = 'Person' AND `UserId` IS NOT NULL AND `GroupId` IS NULL) OR (`Target` = 'Group' AND `GroupId` IS NOT NULL AND `UserId` IS NULL)");
        });
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.EndReason).HasMaxLength(500);
        b.Ignore(x => x.EffectiveTo);
        // A code's assignment history; the live assignment of a code.
        b.HasIndex(x => new { x.CodeId, x.EndedAt });
        // "My codes" and attribution: a person's / a group's assignments.
        b.HasIndex(x => new { x.UserId, x.ProgramId });
        b.HasIndex(x => new { x.GroupId, x.ProgramId });
        b.HasIndex(x => x.ProgramId);
        b.HasOne<DiscountCode>().WithMany().HasForeignKey(x => x.CodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CodeProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RateGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CodeSaleConfiguration : IEntityTypeConfiguration<CodeSale>
{
    public void Configure(EntityTypeBuilder<CodeSale> b)
    {
        b.ToTable("code_sales", t =>
        {
            t.HasCheckConstraint("ck_code_sales_amounts", "`NetAmount` > 0 AND `DiscountAmount` >= 0 AND `ExchangeRate` > 0");
        });
        b.Property(x => x.OrderReference).HasMaxLength(CodeSale.MaxOrderReference).IsRequired();
        b.Property(x => x.NormalizedOrderReference).HasMaxLength(CodeSale.MaxOrderReference).IsRequired();
        b.Property(x => x.ActiveOrderKey).HasMaxLength(CodeSale.MaxOrderReference);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.ExchangeRate).HasPrecision(18, 8);
        b.Property(x => x.ProductNote).HasMaxLength(1000);
        b.Property(x => x.DecisionReason).HasMaxLength(1000);
        b.Property(x => x.PayoutSourceLabel).HasMaxLength(200);
        b.Property(x => x.AppliedCaps).HasMaxLength(200);
        b.Property(x => x.VerificationNote).HasMaxLength(500);
        b.Property(x => x.RefundReason).HasMaxLength(1000);
        // One live claim per order and program: two people can't both be paid for one order (concurrency-safe).
        b.HasIndex(x => new { x.ProgramId, x.ActiveOrderKey }).IsUnique();
        // Review queue (status, oldest first) and reconciliation (order reference lookups).
        b.HasIndex(x => new { x.Status, x.SubmittedAt });
        b.HasIndex(x => new { x.ProgramId, x.NormalizedOrderReference });
        b.HasIndex(x => new { x.ProgramId, x.Status });
        // "My sales", per-person caps and reports.
        b.HasIndex(x => new { x.UserId, x.ProgramId });
        b.HasIndex(x => x.CodeId);
        b.HasOne<CodeProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DiscountCode>().WithMany().HasForeignKey(x => x.CodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DiscountCodeAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RateGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.ProofFileId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CodeImportBatch>().WithMany().HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CodeSaleEventConfiguration : IEntityTypeConfiguration<CodeSaleEvent>
{
    public void Configure(EntityTypeBuilder<CodeSaleEvent> b)
    {
        b.ToTable("code_sale_events");
        b.Property(x => x.Action).HasMaxLength(40).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasIndex(x => new { x.SaleId, x.At });
        b.HasOne<CodeSale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CodeImportBatchConfiguration : IEntityTypeConfiguration<CodeImportBatch>
{
    public void Configure(EntityTypeBuilder<CodeImportBatch> b)
    {
        b.ToTable("code_import_batches");
        b.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.ProgramId, x.CreatedAt });
        b.HasOne<CodeProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
    }
}
