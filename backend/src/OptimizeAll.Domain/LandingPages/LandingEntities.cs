using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.LandingPages;

public enum LandingPageStatus
{
    Draft,
    Published,
    Archived,
}

/// <summary>
/// A client landing page built from blocks, served publicly at <c>/lp/{clientSlug}/{slug}</c> once published. The
/// editable draft lives here (<see cref="VariantsJson"/>); visitors only ever see an immutable
/// <see cref="LandingPageVersion"/> snapshot.
/// </summary>
public class LandingPage : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>URL slug, unique per client.</summary>
    public string Slug { get; set; } = string.Empty;
    public LandingPageStatus Status { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? OgImageUrl { get; set; }
    public bool NoIndex { get; set; }
    public string? TemplateKey { get; set; }

    /// <summary>Draft variants: JSON array of {key, name, weight, blocks}. Variant "A" is the control.</summary>
    public string VariantsJson { get; set; } = "[]";

    /// <summary>A/B test on: visitors are split between the published variants by weight.</summary>
    public bool ExperimentEnabled { get; set; }

    /// <summary>Salt for sticky assignment; a new id starts a fresh experiment (new assignments and results).</summary>
    public Guid ExperimentId { get; set; } = IdGenerator.NewId();
    public DateTime? ExperimentStartedAt { get; set; }

    public Guid? PublishedVersionId { get; set; }
    public DateTime? PublishedAt { get; set; }

    /// <summary>True when the draft differs from the published version.</summary>
    public bool HasUnpublishedChanges { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = IdGenerator.NewId();
}

/// <summary>
/// An immutable publish snapshot (meta + variants + experiment settings as JSON). Properties are init-only; no endpoint
/// updates or deletes versions, and republishing always inserts a new version.
/// </summary>
public class LandingPageVersion
{
    public Guid Id { get; init; } = IdGenerator.NewId();
    public Guid PageId { get; init; }
    public Guid ClientAccountId { get; init; }
    public int Version { get; init; }
    public string SnapshotJson { get; init; } = "{}";

    /// <summary>SHA-256 of <see cref="SnapshotJson"/> (tamper evidence; checked when the version is served).</summary>
    public string ContentHash { get; init; } = string.Empty;
    public DateTime PublishedAt { get; init; }
    public Guid? PublishedByUserId { get; init; }
}

/// <summary>Sticky A/B assignment of a visitor to a landing-page variant (unique per experiment and visitor).</summary>
public class LandingPageAssignment : Entity
{
    public Guid PageId { get; set; }
    public Guid ExperimentId { get; set; }
    public string SubjectKey { get; set; } = string.Empty;
    public string VariantKey { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
}

/// <summary>A counted (non-bot, non-preview) view of a published landing page.</summary>
public class LandingPageView : Entity
{
    public Guid PageId { get; set; }
    public Guid ClientAccountId { get; set; }
    public Guid VersionId { get; set; }
    public string VariantKey { get; set; } = "A";
    public Guid? ExperimentId { get; set; }
    public string VisitorHash { get; set; } = string.Empty;
    public DateTime ViewedAt { get; set; }

    /// <summary>First view of this page by the visitor that UTC day.</summary>
    public bool IsUnique { get; set; }
    public string? ReferrerHost { get; set; }
    public string? UtmSource { get; set; }
}

public class LandingPageTemplate
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MetaTitle { get; set; } = string.Empty;
    public string MetaDescription { get; set; } = string.Empty;

    /// <summary>Block array JSON; "{{form}}" placeholders are replaced by the form chosen when creating a page.</summary>
    public string BlocksJson { get; set; } = "[]";

    /// <summary>Form template to create alongside a page made from this template.</summary>
    public string? FormTemplateKey { get; set; }
    public int SortOrder { get; set; }
}

public enum FormStatus
{
    Draft,
    Active,
    Archived,
}

public enum CaptchaProvider
{
    None,
    HCaptcha,
    Turnstile,
}

/// <summary>A form definition (fields, steps, conditional logic, spam and notification settings).</summary>
public class Form : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public FormStatus Status { get; set; } = FormStatus.Active;

    /// <summary><see cref="FormSchema"/> JSON (steps → fields).</summary>
    public string SchemaJson { get; set; } = "{\"steps\":[]}";
    public string SubmitLabel { get; set; } = "Submit";
    public string SuccessMessage { get; set; } = "Thank you! We will be in touch shortly.";
    public string? RedirectUrl { get; set; }

    /// <summary>Staff users notified (in-app + email) of each submission.</summary>
    public List<Guid> NotifyUserIds { get; set; } = new();
    public bool AutoresponderEnabled { get; set; }
    public string? AutoresponderSubject { get; set; }
    public string? AutoresponderBody { get; set; }

    /// <summary>Origins (https://example.com) allowed to embed the form in an iframe.</summary>
    public List<string> AllowedOrigins { get; set; } = new();
    public string? ConsentText { get; set; }

    /// <summary>Current consent text version (0 = no consent text).</summary>
    public int ConsentVersion { get; set; }
    public CaptchaProvider Captcha { get; set; }
    public int MinFillSeconds { get; set; } = 3;
    public string? TemplateKey { get; set; }
    public Guid ConcurrencyStamp { get; set; } = IdGenerator.NewId();
}

/// <summary>Every consent wording a form has used; submissions reference the version shown.</summary>
public class FormConsentVersion : Entity
{
    public Guid FormId { get; set; }
    public int Version { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class FormTemplate
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SchemaJson { get; set; } = "{\"steps\":[]}";
    public string SubmitLabel { get; set; } = "Submit";
    public string SuccessMessage { get; set; } = string.Empty;
    public string? ConsentText { get; set; }
    public string? AutoresponderSubject { get; set; }
    public string? AutoresponderBody { get; set; }
    public int SortOrder { get; set; }
}

public class FormSubmission : Entity
{
    public Guid FormId { get; set; }
    public Guid ClientAccountId { get; set; }
    public Guid? LandingPageId { get; set; }
    public string? VariantKey { get; set; }
    public Guid? ExperimentId { get; set; }

    /// <summary>Validated field values (JSON object of key → string; multi-values joined by ", ").</summary>
    public string DataJson { get; set; } = "{}";
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmTerm { get; set; }
    public string? UtmContent { get; set; }
    public string? Referrer { get; set; }
    public string? EmbedOrigin { get; set; }

    /// <summary>Keyed HMAC of the submitter's IP (never the raw IP).</summary>
    public string? IpHash { get; set; }
    public int? ConsentVersion { get; set; }
    public bool ConsentGiven { get; set; }
    public DateTime SubmittedAt { get; set; }

    /// <summary>Set once FormSubmitted was published (conditional update, so it is published at most once).</summary>
    public DateTime? EventPublishedAt { get; set; }
}

public class FormSubmissionFile : Entity
{
    public Guid SubmissionId { get; set; }
    public Guid FormId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
}

public enum OutboxEmailStatus
{
    Pending,
    Sent,
    Failed,
    Skipped,
}

/// <summary>Outbox for emails to addresses that are not platform users (form autoresponders).</summary>
public class FormEmailOutbox : Entity
{
    /// <summary>Idempotency key, e.g. "autoresponder:{submissionId}".</summary>
    public string Key { get; set; } = string.Empty;
    public Guid SubmissionId { get; set; }
    public string ToAddress { get; set; } = string.Empty;
    public string? ToName { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public OutboxEmailStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}
