using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Social;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        b.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength().IsRequired();
        b.Property(x => x.LanguageCode).HasMaxLength(10).IsRequired();
        b.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        b.Property(x => x.Interests).HasJsonList();
        b.Property(x => x.StatusReason).HasMaxLength(500);
        b.Property(x => x.ReferralCode).HasMaxLength(16).IsRequired();
        b.HasIndex(x => x.ReferralCode).IsUnique();
        b.Property(x => x.WhatsAppNumber).HasMaxLength(20);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => x.IsTestAccount);
        b.HasMany(x => x.Roles).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.IsEmailVerified);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("user_roles");
        b.HasKey(x => new { x.UserId, x.Role });
        b.HasIndex(x => x.Role);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.FamilyId });
        b.Property(x => x.RevokedReason).HasMaxLength(100);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(300);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ImpersonationSessionConfiguration : IEntityTypeConfiguration<ImpersonationSession>
{
    public void Configure(EntityTypeBuilder<ImpersonationSession> b)
    {
        b.ToTable("impersonation_sessions");
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.ImpersonatorUserId, x.EndedAt });
        b.HasIndex(x => x.TargetUserId);
        b.Property(x => x.EndedReason).HasMaxLength(40);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ImpersonatorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> b)
    {
        b.ToTable("user_tokens");
        b.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Purpose });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PayoutProfileConfiguration : IEntityTypeConfiguration<PayoutProfile>
{
    public void Configure(EntityTypeBuilder<PayoutProfile> b)
    {
        b.ToTable("payout_profiles");
        b.HasIndex(x => x.UserId).IsUnique();
        b.Property(x => x.AccountHolderName).HasMaxLength(150).IsRequired();
        b.Property(x => x.MaskedDestination).HasMaxLength(64).IsRequired();
        b.Property(x => x.EncryptedDestination).HasMaxLength(2048).IsRequired();
        b.Property(x => x.PreferredCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SocialAccountConfiguration : IEntityTypeConfiguration<SocialAccount>
{
    public void Configure(EntityTypeBuilder<SocialAccount> b)
    {
        b.ToTable("social_accounts");
        b.Property(x => x.Handle).HasMaxLength(100).IsRequired();
        b.Property(x => x.NormalizedHandle).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.Platform, x.NormalizedHandle }).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasIndex(x => x.VerificationStatus);
        b.Property(x => x.ProfileUrl).HasMaxLength(500).IsRequired();
        b.Property(x => x.PrimaryLanguage).HasMaxLength(10);
        b.Property(x => x.AudienceCountryCode).HasMaxLength(2).IsFixedLength();
        b.Property(x => x.VerificationNote).HasMaxLength(500);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
