using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Integrations;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class IntegrationConnectionConfiguration : IEntityTypeConfiguration<IntegrationConnection>
{
    public void Configure(EntityTypeBuilder<IntegrationConnection> b)
    {
        b.ToTable("integration_connections");
        b.Property(x => x.Provider).HasMaxLength(40).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
        b.Property(x => x.SettingsJson).HasMaxLength(8000).IsRequired();
        b.Property(x => x.EncryptedSecrets).HasMaxLength(16000).IsRequired();
        b.Property(x => x.StatusMessage).HasMaxLength(1000);
        b.HasIndex(x => new { x.Provider, x.ClientAccountId });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
