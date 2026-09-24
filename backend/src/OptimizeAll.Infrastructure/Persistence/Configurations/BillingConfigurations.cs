using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal static class PricedLineMapping
{
    public static void MapPricedLine<T>(this EntityTypeBuilder<T> b) where T : PricedLine
    {
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.ServiceSlug).HasMaxLength(100);
        b.Property(x => x.TaxName).HasMaxLength(80);
        b.HasOne<TaxRate>().WithMany().HasForeignKey(x => x.TaxRateId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> b)
    {
        b.ToTable("tax_rates");
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
        b.Property(x => x.Notes).HasMaxLength(1000);
    }
}

internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        b.ToTable("billing_number_sequences");
        b.Property(x => x.Key).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices");
        b.Property(x => x.Number).HasMaxLength(40);
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Notes);
        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.IdempotencyKey).HasMaxLength(150);
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.Property(x => x.PublicTokenHash).HasMaxLength(64);
        b.HasIndex(x => x.PublicTokenHash).IsUnique();
        b.Property(x => x.PublicTokenProtected).HasMaxLength(1000);
        b.Property(x => x.VoidReason).HasMaxLength(1000);
        b.Property(x => x.WriteOffReason).HasMaxLength(1000);
        b.HasIndex(x => new { x.ClientAccountId, x.Status });
        b.HasIndex(x => new { x.Status, x.DueDate });
        b.HasIndex(x => x.IssueDate);
        b.HasIndex(x => x.ContractId);
        b.HasIndex(x => x.ProposalId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Contract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Proposal>().WithMany().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> b)
    {
        b.ToTable("invoice_lines");
        b.MapPricedLine();
        b.HasIndex(x => new { x.InvoiceId, x.Position });
        b.HasIndex(x => x.ServiceSlug);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("invoice_payments");
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Reference).HasMaxLength(120).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.ActiveReference).HasMaxLength(120);
        b.Property(x => x.ReversalReason).HasMaxLength(1000);
        b.Ignore(x => x.IsReversal);
        // A retried request returns the original payment; the same bank reference can't be recorded twice on an invoice
        // while it counts (reversed payments and reversal rows have no active reference, so a correction can reuse it).
        b.HasIndex(x => x.RequestId).IsUnique();
        b.HasIndex(x => new { x.InvoiceId, x.ActiveReference }).IsUnique();
        b.HasIndex(x => new { x.InvoiceId, x.Reference });
        // At most one reversal per payment.
        b.HasIndex(x => x.ReversalOfPaymentId).IsUnique();
        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.ReversalOfPaymentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ReversedByUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.ClientAccountId, x.PaidOn });
        b.HasIndex(x => x.PaidOn);
        b.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> b)
    {
        b.ToTable("credit_notes");
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        b.HasIndex(x => x.RequestId).IsUnique();
        b.HasIndex(x => new { x.ClientAccountId, x.Status });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CreditNoteApplicationConfiguration : IEntityTypeConfiguration<CreditNoteApplication>
{
    public void Configure(EntityTypeBuilder<CreditNoteApplication> b)
    {
        b.ToTable("credit_note_applications");
        b.HasIndex(x => x.CreditNoteId);
        b.HasIndex(x => x.InvoiceId);
        b.HasOne<CreditNote>().WithMany().HasForeignKey(x => x.CreditNoteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvoiceReminderConfiguration : IEntityTypeConfiguration<InvoiceReminder>
{
    public void Configure(EntityTypeBuilder<InvoiceReminder> b)
    {
        b.ToTable("invoice_reminders");
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        b.Ignore(x => x.IsManual);
        b.HasIndex(x => new { x.InvoiceId, x.Kind }).IsUnique();
        b.HasIndex(x => x.RequestId).IsUnique();
        b.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.SentByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ClientReminderPolicyConfiguration : IEntityTypeConfiguration<ClientReminderPolicy>
{
    public void Configure(EntityTypeBuilder<ClientReminderPolicy> b)
    {
        b.ToTable("client_reminder_policies");
        b.Property(x => x.OffsetsDays).HasJsonList();
        b.HasIndex(x => x.ClientAccountId).IsUnique();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PaymentClaimConfiguration : IEntityTypeConfiguration<PaymentClaim>
{
    public void Configure(EntityTypeBuilder<PaymentClaim> b)
    {
        b.ToTable("payment_claims");
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Reference).HasMaxLength(120).IsRequired();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.ReviewNote).HasMaxLength(1000);
        b.HasIndex(x => x.RequestId).IsUnique();
        b.HasIndex(x => new { x.InvoiceId, x.Status });
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentProofConfiguration : IEntityTypeConfiguration<PaymentProof>
{
    public void Configure(EntityTypeBuilder<PaymentProof> b)
    {
        b.ToTable("payment_proofs");
        b.Property(x => x.StorageKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.PaymentId);
        b.HasIndex(x => x.PaymentClaimId);
        b.HasIndex(x => x.InvoiceId);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentClaim>().WithMany().HasForeignKey(x => x.PaymentClaimId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> b)
    {
        b.ToTable("contracts");
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Notes);
        b.Property(x => x.CancelReason).HasMaxLength(1000);
        b.HasIndex(x => new { x.ClientAccountId, x.Status });
        b.HasIndex(x => x.Status);
        // Accepting a proposal creates at most one contract per billing frequency.
        b.HasIndex(x => new { x.ProposalId, x.BillingFrequency }).IsUnique();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ContractId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Proposal>().WithMany().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ContractLineConfiguration : IEntityTypeConfiguration<ContractLine>
{
    public void Configure(EntityTypeBuilder<ContractLine> b)
    {
        b.ToTable("contract_lines");
        b.MapPricedLine();
        b.HasIndex(x => new { x.ContractId, x.Position });
    }
}

internal sealed class ServiceCatalogItemConfiguration : IEntityTypeConfiguration<ServiceCatalogItem>
{
    public void Configure(EntityTypeBuilder<ServiceCatalogItem> b)
    {
        b.ToTable("service_catalog_items");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.ServiceSlug).HasMaxLength(100);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.HasIndex(x => new { x.IsActive, x.SortOrder });
        b.HasOne<TaxRate>().WithMany().HasForeignKey(x => x.TaxRateId).OnDelete(DeleteBehavior.SetNull);
    }
}
