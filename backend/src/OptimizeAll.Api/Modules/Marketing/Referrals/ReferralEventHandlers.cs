using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Referrals;

/// <summary>Creates a referral when a new participant registered with a referral code.</summary>
public sealed class ReferralRegistrationHandler(ReferralService referrals) : IEventHandler<UserRegistered>
{
    public Task HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken) =>
        referrals.CreateFromRegistrationAsync(domainEvent, cancellationToken);
}

/// <summary>Counts a registration against an invitation link (atomic conditional increment under MaxUses).</summary>
public sealed class InvitationUseHandler(AppDbContext db, TimeProvider clock) : IEventHandler<UserRegistered>
{
    public async Task HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domainEvent.InviteCode)) return;
        var code = domainEvent.InviteCode.Trim();
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Set<InvitationLink>()
            .Where(i => i.Code == code && i.IsActive &&
                        (i.ExpiresAt == null || i.ExpiresAt > now) &&
                        (i.MaxUses == null || i.UseCount < i.MaxUses))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UseCount, i => i.UseCount + 1), cancellationToken);
    }
}

public sealed class ReferralEmailVerifiedHandler(ReferralService referrals) : IEventHandler<EmailVerified>
{
    public Task HandleAsync(EmailVerified domainEvent, CancellationToken cancellationToken) =>
        referrals.QualifyAsync(domainEvent.UserId, ReferralQualifyingAction.EmailVerified, cancellationToken);
}

public sealed class ReferralSubmissionApprovedHandler(ReferralService referrals) : IEventHandler<SubmissionApproved>
{
    public Task HandleAsync(SubmissionApproved domainEvent, CancellationToken cancellationToken) =>
        referrals.OnSubmissionApprovedAsync(domainEvent, cancellationToken);
}

public sealed class ReferralPayoutPaidHandler(ReferralService referrals) : IEventHandler<PayoutItemPaid>
{
    public Task HandleAsync(PayoutItemPaid domainEvent, CancellationToken cancellationToken) =>
        referrals.QualifyAsync(domainEvent.UserId, ReferralQualifyingAction.FirstPaidPayout, cancellationToken);
}

public sealed class ReferralSubmissionReversedHandler(ReferralService referrals) : IEventHandler<SubmissionReversed>
{
    public Task HandleAsync(SubmissionReversed domainEvent, CancellationToken cancellationToken) =>
        referrals.OnSubmissionReversedAsync(domainEvent, cancellationToken);
}

/// <summary>Hourly: marks Registered referrals whose qualifying window has passed as Expired.</summary>
public sealed class ReferralExpiryJob(ReferralService referrals) : IJob
{
    public string Name => nameof(ReferralExpiryJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var expired = await referrals.ExpireAsync(ct);
        return $"expired={expired}";
    }
}
