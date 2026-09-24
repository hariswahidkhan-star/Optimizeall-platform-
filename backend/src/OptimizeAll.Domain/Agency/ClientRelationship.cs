using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Agency;

/// <summary>The service role an agency staff member plays on a client's account team.</summary>
public enum ClientServiceRole
{
    AccountManager,
    Strategist,
    Seo,
    Ads,
    Social,
    Content,
    Design,
}

/// <summary>A staff member assigned to a client's account team with a service role (one row per user and role).</summary>
public class ClientTeamAssignment : Entity
{
    public Guid ClientAccountId { get; set; }
    public Guid UserId { get; set; }
    public ClientServiceRole ServiceRole { get; set; }

    /// <summary>The primary contact for that service line (shown first in the client portal).</summary>
    public bool IsPrimary { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid? AssignedByUserId { get; set; }
}

public enum OnboardingItemStatus
{
    Pending,
    Done,
    NotApplicable,
}

/// <summary>Who has to act on an onboarding item.</summary>
public enum OnboardingOwner
{
    Agency,
    Client,
}

/// <summary>
/// One step of a client's onboarding checklist (GA4 access, ad accounts, brand assets, kickoff call, contract…). Created
/// from <c>OnboardingChecklistTemplate</c> when the client is created; progress is visible in the client portal.
/// </summary>
public class ClientOnboardingItem : Entity
{
    public Guid ClientAccountId { get; set; }

    /// <summary>Stable template key (unique per client), e.g. "ga4-access".</summary>
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "Access";
    public OnboardingOwner Owner { get; set; }
    public int SortOrder { get; set; }
    public OnboardingItemStatus Status { get; set; } = OnboardingItemStatus.Pending;
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedByUserId { get; set; }

    /// <summary>
    /// True when a client-owned step was marked done by agency staff on the client's behalf (shown to the client and
    /// kept in the audit log). False when the client completed it or for agency-owned steps.
    /// </summary>
    public bool CompletedOnBehalfOfClient { get; set; }

    public string? Note { get; set; }
}

public sealed record BrandColor(string Name, string Hex);

public sealed record BrandPersona(string Name, string Description);

/// <summary>
/// A client's brand kit: shared with every service agent (content, design, social, ads) and visible in the client
/// portal. Asset files are <see cref="BrandAsset"/> rows.
/// </summary>
public class BrandKit : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public List<BrandColor> Colors { get; set; } = new();
    public List<string> Fonts { get; set; } = new();
    public string? ToneOfVoice { get; set; }
    public List<BrandPersona> Personas { get; set; } = new();
    public List<string> Competitors { get; set; } = new();
    public List<string> Dos { get; set; } = new();
    public List<string> Donts { get; set; } = new();
    public List<string> KeyMessages { get; set; } = new();
    public Guid? UpdatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum BrandAssetKind
{
    Logo,
    Image,
    Guideline,
    Other,
}

/// <summary>A file in a client's brand kit (logo variants, photography, brand guidelines PDF).</summary>
public class BrandAsset : Entity
{
    public Guid ClientAccountId { get; set; }
    public Guid FileId { get; set; }
    public BrandAssetKind Kind { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid UploadedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ClientFeedbackKind
{
    /// <summary>1–5 satisfaction score after a deliverable is approved.</summary>
    Csat,
    /// <summary>0–10 quarterly Net Promoter Score survey in the client portal.</summary>
    Nps,
}

/// <summary>A CSAT or NPS response from a client user. One per user per deliverable (CSAT) or per quarter (NPS).</summary>
public class ClientFeedback : Entity
{
    public Guid ClientAccountId { get; set; }
    public Guid UserId { get; set; }
    public ClientFeedbackKind Kind { get; set; }
    public int Score { get; set; }
    public string? Comment { get; set; }
    public Guid? DeliverableId { get; set; }

    /// <summary>NPS quarter, e.g. "2026-Q3".</summary>
    public string? Period { get; set; }

    /// <summary>Unique: "csat:{deliverableId}:{userId}" or "nps:{period}:{clientId}:{userId}".</summary>
    public string DedupeKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
