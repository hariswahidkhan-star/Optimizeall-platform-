using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Website.Catalog;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Blog;

public sealed record BlogCategoryDto(Guid Id, string Slug, string Name, string? Description, int SortOrder, int PostCount, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class BlogCategoryInput : StampedInput
{
    [Required, MaxLength(100)] public string Slug { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    [Range(-100000, 100000)] public int SortOrder { get; set; }
}

public sealed record BlogPostSummaryDto(
    Guid Id, string Slug, string Title, BlogPostStatus Status, string? AuthorName, IReadOnlyList<string> Categories, IReadOnlyList<string> Tags,
    int ReadingMinutes, DateTime? PublishAt, DateTime? PublishedAt, DateTime UpdatedAt);

public sealed record BlogPostDto(
    Guid Id, string Slug, string Title, string Excerpt, string BodyMarkdown, string? CoverImageUrl, string? CoverImageAlt, Guid? AuthorId,
    IReadOnlyList<Guid> CategoryIds, IReadOnlyList<string> Tags, int ReadingMinutes, BlogPostStatus Status, DateTime? PublishAt,
    DateTime? PublishedAt, IReadOnlyList<Guid> RelatedPostIds, SeoDto Seo, Guid? CreatedByUserId, Guid? SubmittedByUserId,
    Guid? PublishedByUserId, DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp,
    /// <summary>What the caller may do next (drives the editor's buttons).</summary>
    BlogPermissionsDto Can);

public sealed record BlogPermissionsDto(bool Edit, bool Submit, bool Publish, bool Schedule, bool Unpublish, bool ReturnToDraft, bool Delete);

public sealed class BlogPostInput : StampedInput
{
    [Required, MaxLength(120)] public string Slug { get; set; } = string.Empty;
    [Required, MaxLength(180)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string Excerpt { get; set; } = string.Empty;
    [Required, MaxLength(100000)] public string BodyMarkdown { get; set; } = string.Empty;
    [MaxLength(500)] public string? CoverImageUrl { get; set; }
    [MaxLength(200)] public string? CoverImageAlt { get; set; }
    public Guid? AuthorId { get; set; }
    public List<Guid>? CategoryIds { get; set; }
    public List<string?>? Tags { get; set; }
    public List<Guid>? RelatedPostIds { get; set; }
    public SeoInput? Seo { get; set; }
}

/// <summary>Workflow actions: submit, publish, schedule (with <see cref="PublishAt"/>), unpublish, return to draft.</summary>
public sealed class BlogActionInput : StampedInput
{
    /// <summary>For schedule: when the post goes live (UTC, in the future).</summary>
    public DateTime? PublishAt { get; set; }

    /// <summary>Optional note (e.g. why a post was returned to draft); recorded in the audit log.</summary>
    [MaxLength(1000)] public string? Note { get; set; }
}

public sealed class BlogPostQuery : PageQuery
{
    public BlogPostStatus? Status { get; set; }
    public Guid? CategoryId { get; set; }
}

public sealed class PublicBlogQuery
{
    [Range(1, 1000)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 9;
    [MaxLength(100)] public string? Category { get; set; }
    [MaxLength(60)] public string? Tag { get; set; }
    [MaxLength(100)] public string? Search { get; set; }
}
