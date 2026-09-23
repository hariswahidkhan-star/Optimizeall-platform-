namespace OptimizeAll.Domain.Events;

/// <summary>A public website form (contact, free audit, quote, consultation booking, careers excluded) was submitted.</summary>
public sealed record WebsiteInquiryReceived(
    Guid InquiryId, string InquiryType, string Name, string Email, string? Phone, string? Company, string? Website,
    string? Message, IReadOnlyList<string> ServiceSlugs, string? BudgetRange, string? UtmSource, string? UtmMedium,
    string? UtmCampaign, string? Referrer, DateTime OccurredAt) : IDomainEvent;

/// <summary>A newsletter subscription was confirmed (double opt-in completed).</summary>
public sealed record NewsletterSubscribed(Guid SubscriberId, string Email, DateTime OccurredAt) : IDomainEvent;

/// <summary>A landing-page / form-builder form was submitted (published by the LandingPages module).</summary>
public sealed record FormSubmitted(
    Guid SubmissionId, Guid FormId, Guid? ClientAccountId, string? Email, string? Name, string? Phone,
    IReadOnlyDictionary<string, string> Fields, string? UtmSource, string? UtmMedium, string? UtmCampaign,
    DateTime OccurredAt) : IDomainEvent;

/// <summary>A CRM deal was won and converted into a client account (published by the Crm module).</summary>
public sealed record ClientAccountCreated(Guid ClientAccountId, Guid? CrmCompanyId, Guid? ProposalId, DateTime OccurredAt) : IDomainEvent;

/// <summary>A client accepted a proposal (published by the Crm module).</summary>
public sealed record ProposalAccepted(Guid ProposalId, Guid? ClientAccountId, Guid? CrmDealId, DateTime OccurredAt) : IDomainEvent;

/// <summary>An invoice became fully paid (published by the Billing module).</summary>
public sealed record InvoicePaid(Guid InvoiceId, Guid ClientAccountId, decimal Amount, string Currency, DateTime OccurredAt) : IDomainEvent;

/// <summary>A client approved a deliverable (published by the Projects module).</summary>
public sealed record DeliverableApproved(Guid DeliverableId, Guid ProjectId, Guid ClientAccountId, DateTime OccurredAt) : IDomainEvent;
