using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Website;

public enum SiteRedirectSource
{
    /// <summary>Recorded by the CMS when the address of live content changed (slug rename of a published item).</summary>
    Automatic,

    /// <summary>Added by staff in Website → Redirects.</summary>
    Manual,
}

/// <summary>
/// A permanent (301) redirect of an old public address to its current one. <see cref="FromPath"/> and
/// <see cref="ToPath"/> are normalized same-site paths ("/about-us", "/blog/old-post", "/lp/client/offer"); a service line
/// is addressed as "/services?category={slug}". Chains are collapsed when recorded (every row points at a final
/// address), and a row is removed as soon as live content claims its <see cref="FromPath"/> again.
/// </summary>
public class SiteRedirect : AuditedEntity
{
    public string FromPath { get; set; } = string.Empty;
    public string ToPath { get; set; } = string.Empty;
    public SiteRedirectSource Source { get; set; }

    /// <summary>For automatic redirects: what moved ("page", "post", "service", "service-line", "case-study", "industry", "landing-page").</summary>
    public string? ContentType { get; set; }

    public Guid? ContentId { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
