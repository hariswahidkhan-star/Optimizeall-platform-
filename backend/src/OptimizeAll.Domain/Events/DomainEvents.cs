namespace OptimizeAll.Domain.Events;

/// <summary>
/// In-process integration events. Published by a module after its transaction commits; handlers in other
/// modules react (referral qualification, achievements, notifications). Handlers must be idempotent.
/// </summary>
public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}

public sealed record UserRegistered(
    Guid UserId, string? ReferralCode, string? InviteCode, string? IpHash, string? DeviceHash, DateTime OccurredAt) : IDomainEvent;

public sealed record EmailVerified(Guid UserId, DateTime OccurredAt) : IDomainEvent;

public sealed record SubmissionCreated(Guid SubmissionId, Guid UserId, Guid CampaignId, DateTime OccurredAt) : IDomainEvent;

public sealed record SubmissionApproved(
    Guid SubmissionId, Guid UserId, Guid CampaignId, bool IsFirstApprovedSubmissionForUser, DateTime OccurredAt) : IDomainEvent;

public sealed record SubmissionReversed(Guid SubmissionId, Guid UserId, Guid CampaignId, string Reason, DateTime OccurredAt) : IDomainEvent;

public sealed record CampaignPublished(Guid CampaignId, DateTime OccurredAt) : IDomainEvent;

public sealed record PayoutItemPaid(Guid PayoutItemId, Guid UserId, decimal Amount, string Currency, DateTime OccurredAt) : IDomainEvent;
