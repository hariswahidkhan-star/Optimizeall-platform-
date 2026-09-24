using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Crm;

public enum CompanySize
{
    Unknown,
    /// <summary>1 person.</summary>
    Solo,
    /// <summary>2–10.</summary>
    Micro,
    /// <summary>11–50.</summary>
    Small,
    /// <summary>51–200.</summary>
    Medium,
    /// <summary>201–1000.</summary>
    Large,
    /// <summary>1000+.</summary>
    Enterprise,
}

/// <summary>Contact lifecycle, in funnel order (a contact only moves forward automatically).</summary>
public enum LifecycleStage
{
    Subscriber,
    Lead,
    MarketingQualifiedLead,
    SalesQualifiedLead,
    Opportunity,
    Customer,
    Evangelist,
}

/// <summary>Marketing consent. Only <see cref="Subscribed"/> contacts may receive marketing email.</summary>
public enum ConsentStatus
{
    Unknown,
    Subscribed,
    Unsubscribed,
    /// <summary>Transactional/sales contact only; no marketing consent given.</summary>
    NotGiven,
}

public enum DealSource
{
    WebsiteInquiry,
    Form,
    Referral,
    Outbound,
    Event,
    Other,
}

public enum StageKind
{
    Open,
    Won,
    Lost,
}

public enum DealStatus
{
    Open,
    Won,
    Lost,
}

public enum ActivityType
{
    Note,
    Call,
    Meeting,
    Email,
    Task,
}

/// <summary>UTM attribution captured at a touch point.</summary>
public class UtmTouch
{
    public string? Source { get; set; }
    public string? Medium { get; set; }
    public string? Campaign { get; set; }
    public DateTime? At { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Source) && string.IsNullOrWhiteSpace(Medium) && string.IsNullOrWhiteSpace(Campaign);
}

public class CrmCompany : AuditedEntity, IConcurrencyStamped
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Lower-case host without "www." (dedupe key for inbound leads).</summary>
    public string? Domain { get; set; }
    public string? Industry { get; set; }
    public CompanySize Size { get; set; } = CompanySize.Unknown;
    public string? CountryCode { get; set; }
    public Guid? OwnerUserId { get; set; }
    public List<string> Tags { get; set; } = new();

    /// <summary>Denormalized ",tag1,tag2," copy of <see cref="Tags"/> for portable tag filtering (maintained on save).</summary>
    public string TagIndex { get; set; } = string.Empty;

    /// <summary>Custom fields as a JSON object of string/number/boolean values (validated by <see cref="CustomFields"/>).</summary>
    public string CustomFieldsJson { get; set; } = "{}";

    /// <summary>Set when the company became a client.</summary>
    public Guid? ClientAccountId { get; set; }
    /// <summary>Set when the record was archived (hidden from lists and pickers; restorable, history kept).</summary>
    public DateTime? ArchivedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class CrmContact : AuditedEntity, IConcurrencyStamped
{
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string? Email { get; set; }

    /// <summary><see cref="Normalization.Email"/> of <see cref="Email"/>; unique when present (dedupe key).</summary>
    public string? NormalizedEmail { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
    public Guid? CompanyId { get; set; }
    public LifecycleStage LifecycleStage { get; set; } = LifecycleStage.Lead;
    public Guid? OwnerUserId { get; set; }
    public ConsentStatus ConsentStatus { get; set; } = ConsentStatus.Unknown;
    public DateTime? ConsentChangedAt { get; set; }
    public List<string> Tags { get; set; } = new();

    /// <summary>Denormalized ",tag1,tag2," copy of <see cref="Tags"/> for portable tag filtering (maintained on save).</summary>
    public string TagIndex { get; set; } = string.Empty;
    public string? Source { get; set; }

    /// <summary>Budget range reported by the lead (e.g. "5k-10k"), used by fit scoring.</summary>
    public string? BudgetRange { get; set; }

    public int Score { get; set; }
    public DateTime? ScoredAt { get; set; }

    public UtmTouch FirstTouch { get; set; } = new();
    public UtmTouch LastTouch { get; set; } = new();
    /// <summary>Set when the record was archived (hidden from lists and pickers; restorable, history kept).</summary>
    public DateTime? ArchivedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public string DisplayName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}

/// <summary>A configurable pipeline stage. Exactly one Won and one Lost stage exist; open stages are ordered by position.</summary>
public class PipelineStage : AuditedEntity, IConcurrencyStamped
{
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }

    /// <summary>Win probability in percent (0–100), used for the weighted forecast.</summary>
    public int WinProbability { get; set; }
    public StageKind Kind { get; set; } = StageKind.Open;
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class CrmDeal : AuditedEntity, IConcurrencyStamped
{
    public string Title { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public Guid? PrimaryContactId { get; set; }
    public Guid StageId { get; set; }
    public DealStatus Status { get; set; } = DealStatus.Open;
    public decimal Value { get; set; }
    public string Currency { get; set; } = "USD";
    public DateOnly? ExpectedCloseDate { get; set; }

    /// <summary>Service slugs of interest (website service catalog).</summary>
    public List<string> ServiceSlugs { get; set; } = new();
    public Guid? OwnerUserId { get; set; }
    public DealSource Source { get; set; } = DealSource.Other;

    /// <summary>e.g. the website inquiry type ("free-audit") or form name.</summary>
    public string? SourceDetail { get; set; }
    public string? BudgetRange { get; set; }
    public UtmTouch FirstTouch { get; set; } = new();
    public UtmTouch LastTouch { get; set; } = new();
    public string? LostReason { get; set; }
    public DateTime StageChangedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    /// <summary>Client account created from (or linked to) this deal.</summary>
    public Guid? ClientAccountId { get; set; }
    /// <summary>Set when the record was archived (hidden from lists and pickers; restorable, history kept).</summary>
    public DateTime? ArchivedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Additional contacts on a deal (the primary contact is on the deal itself).</summary>
public class CrmDealContact
{
    public Guid DealId { get; set; }
    public Guid ContactId { get; set; }
    public string? Role { get; set; }
    public DateTime AddedAt { get; set; }
}

/// <summary>A timeline entry: note, call, meeting, logged email or task (with due date, reminder and assignee).</summary>
public class CrmActivity : AuditedEntity, IConcurrencyStamped
{
    public ActivityType Type { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Body { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? DealId { get; set; }

    /// <summary>When a call/meeting/email happened or is scheduled.</summary>
    public DateTime? OccursAt { get; set; }
    public int? DurationMinutes { get; set; }

    /// <summary>Tasks: due time.</summary>
    public DateTime? DueAt { get; set; }

    /// <summary>When to remind the assignee (tasks and meetings).</summary>
    public DateTime? RemindAt { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    public DateTime? OverdueNotifiedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    /// <summary>True for entries generated by the system (inbound lead, stage change, proposal events).</summary>
    public bool IsSystem { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum ScoringCategory
{
    Fit,
    Engagement,
}

/// <summary>
/// A lead scoring rule. Fit rules match a contact/company attribute (<see cref="LeadScoring.FitFields"/>) against a
/// comma-separated list of values; engagement rules award points per recorded engagement event of a type, up to
/// <see cref="MaxOccurrences"/>. New event types need only a new row.
/// </summary>
public class LeadScoringRule : AuditedEntity, IConcurrencyStamped
{
    public string Name { get; set; } = string.Empty;
    public ScoringCategory Category { get; set; }

    /// <summary>Fit: industry, companySize, budgetRange, country, source, lifecycleStage. Engagement: the event type.</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Fit: comma-separated values (case-insensitive), "*" = any non-empty value. Engagement: unused.</summary>
    public string? MatchValue { get; set; }
    public int Points { get; set; }
    public int? MaxOccurrences { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>An engagement signal for a contact (form submitted, email clicked, …). Idempotent by <see cref="SourceKey"/>.</summary>
public class CrmEngagement : Entity
{
    public Guid ContactId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}

/// <summary>Inbound events already turned into CRM records (dedupe of retried/duplicated events).</summary>
public class CrmInboundEvent
{
    public string Key { get; set; } = string.Empty;
    public Guid? ContactId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public DateTime ProcessedAt { get; set; }
}

/// <summary>Round-robin cursor for lead assignment (one row per pool).</summary>
public class CrmAssignmentCursor : Entity
{
    public string Pool { get; set; } = string.Empty;
    public Guid? LastUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A saved list filter (contacts, companies or deals); private to its owner unless shared.</summary>
public class CrmSavedView : AuditedEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>"contacts", "companies" or "deals".</summary>
    public string Entity { get; set; } = string.Empty;

    /// <summary>JSON object of query-string filters (search, stage, owner, lifecycle, tag, …).</summary>
    public string FiltersJson { get; set; } = "{}";
    public Guid OwnerUserId { get; set; }
    public bool Shared { get; set; }
}

/// <summary>Validation of CRM custom fields: a flat JSON object with up to 50 string/number/boolean values.</summary>
public static class CustomFields
{
    public const int MaxFields = 50;
    public const int MaxKeyLength = 64;
    public const int MaxValueLength = 1000;

    /// <summary>Returns the canonical JSON or throws a validation <see cref="DomainException"/>.</summary>
    public static string Normalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "{}";
        System.Text.Json.JsonDocument doc;
        try
        {
            doc = System.Text.Json.JsonDocument.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            throw Invalid("Custom fields must be a JSON object.");
        }
        using (doc)
        {
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) throw Invalid("Custom fields must be a JSON object.");
            var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var key = property.Name.Trim();
                if (key.Length is 0 or > MaxKeyLength || !key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or ' '))
                    throw Invalid($"Custom field name '{Truncate(key)}' must be 1–{MaxKeyLength} letters, digits, spaces, '-' or '_'.");
                object? value = property.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => property.Value.GetString()!.Length <= MaxValueLength
                        ? property.Value.GetString()
                        : throw Invalid($"Custom field '{key}' is longer than {MaxValueLength} characters."),
                    System.Text.Json.JsonValueKind.Number => property.Value.GetDecimal(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Null => null,
                    _ => throw Invalid($"Custom field '{key}' must be text, a number, true/false or null."),
                };
                if (!result.TryAdd(key, value)) throw Invalid($"Custom field '{key}' appears twice.");
            }
            if (result.Count > MaxFields) throw Invalid($"At most {MaxFields} custom fields are allowed.");
            return System.Text.Json.JsonSerializer.Serialize(result);
        }
    }

    private static string Truncate(string s) => s.Length <= 30 ? s : s[..30] + "…";

    private static DomainException Invalid(string message) =>
        new("crm.invalid_custom_fields", message, errors: new Dictionary<string, string[]> { ["customFields"] = new[] { message } });
}

/// <summary>Normalization helpers for CRM dedupe keys.</summary>
public static class CrmNormalization
{
    private static readonly HashSet<string> FreeMailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "yahoo.com", "yahoo.co.uk", "hotmail.com", "hotmail.co.uk", "outlook.com", "live.com",
        "msn.com", "icloud.com", "me.com", "aol.com", "proton.me", "protonmail.com", "gmx.com", "mail.com", "yandex.com", "zoho.com",
    };

    /// <summary>Lower-case host of a website or domain ("https://www.Example.com/x" → "example.com"), or null.</summary>
    public static string? Domain(string? websiteOrDomain)
    {
        if (string.IsNullOrWhiteSpace(websiteOrDomain)) return null;
        var value = websiteOrDomain.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return null;
        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        return host.Contains('.') && host.Length <= 253 ? host : null;
    }

    /// <summary>The business domain of an email address (null for free-mail providers).</summary>
    public static string? EmailDomain(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.LastIndexOf('@');
        if (at < 0) return null;
        var domain = Domain(email[(at + 1)..]);
        return domain is null || FreeMailDomains.Contains(domain) ? null : domain;
    }

    public static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= 254 &&
        System.Net.Mail.MailAddress.TryCreate(email.Trim(), out var parsed) && parsed.Address == email.Trim() &&
        email.IndexOf('@') > 0 && email[(email.IndexOf('@') + 1)..].Contains('.');

    /// <summary>Splits "Ada Lovelace" into first/last name.</summary>
    public static (string First, string? Last) SplitName(string? name, string fallback)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) return (fallback, null);
        var space = trimmed.IndexOf(' ');
        return space < 0 ? (trimmed, null) : (trimmed[..space], trimmed[(space + 1)..].Trim());
    }

    public static List<string> Tags(IEnumerable<string>? tags) =>
        (tags ?? Array.Empty<string>()).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length is > 0 and <= 40)
        .Distinct(StringComparer.Ordinal).Take(20).ToList();
}
