using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Website;

/// <summary>
/// Editorial workflow: writers (<c>blog.write</c>) draft and submit for review; publishers (<c>blog.publish</c>) publish
/// now or schedule (the scheduler job flips due posts to Published), archive, or send back to draft.
/// </summary>
public enum BlogPostStatus
{
    Draft,
    InReview,
    Scheduled,
    Published,
    Archived,
}

public class BlogCategory : AuditedEntity, IConcurrencyStamped, ISlugged
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class BlogPost : AuditedEntity, IConcurrencyStamped, ISlugged
{
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// The slug the post was last live under (set whenever it is live). When an unpublished post is renamed and published
    /// again, its old public address redirects to the new one from this, although the post was not live at the rename.
    /// </summary>
    public string? LastLiveSlug { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;

    /// <summary>Sanitized Markdown (raw HTML and unsafe links removed on save).</summary>
    public string BodyMarkdown { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string? CoverImageAlt { get; set; }

    /// <summary>Byline: a team member.</summary>
    public Guid? AuthorId { get; set; }
    public List<Guid> CategoryIds { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    /// <summary>Computed from the body on save.</summary>
    public int ReadingMinutes { get; set; }
    public BlogPostStatus Status { get; set; } = BlogPostStatus.Draft;

    /// <summary>When a Scheduled post goes live.</summary>
    public DateTime? PublishAt { get; set; }

    /// <summary>When the post first went live (kept on re-publish so URLs and feeds stay stable).</summary>
    public DateTime? PublishedAt { get; set; }
    public List<Guid> RelatedPostIds { get; set; } = new();
    public SeoMeta Seo { get; set; } = new();
    public Guid? CreatedByUserId { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
