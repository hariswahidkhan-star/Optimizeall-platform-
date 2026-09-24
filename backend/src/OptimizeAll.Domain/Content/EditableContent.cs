using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Content;

/// <summary>
/// An editor's override of one page-copy key (see the site-copy catalog). Only overridden keys are stored: a key without
/// a row shows the catalog default, and "reset to default" deletes the row.
/// </summary>
public class ContentCopyEntry : AuditedEntity, IConcurrencyStamped
{
    public string Key { get; set; } = string.Empty;

    /// <summary>Plain text (tags are stripped on save). Unbounded.</summary>
    public string Value { get; set; } = string.Empty;

    public Guid? UpdatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>
/// An editor's override of a transactional email template (notification layout, a notification type, website emails).
/// Templates are plain text with <c>{{variable}}</c> placeholders; the HTML part is generated and HTML-encoded.
/// </summary>
public class EmailTemplateOverride : AuditedEntity, IConcurrencyStamped
{
    public string Key { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    /// <summary>Unbounded plain text.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Label of the call-to-action button, for templates that have one.</summary>
    public string? ActionLabel { get; set; }

    public Guid? UpdatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
