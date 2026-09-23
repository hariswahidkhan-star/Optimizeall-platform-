using System.Globalization;
using System.Text;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Blog;

/// <summary>
/// Blog editorial workflow. <c>blog.write</c> drafts, edits drafts/in-review posts and submits them for review;
/// <c>blog.publish</c> publishes, schedules, unpublishes, returns posts to draft and edits live posts. Audited.
/// </summary>
public sealed class BlogService(CmsStore store, IAuditLogger audit, WebsiteRules rules, ICurrentUser user, TimeProvider clock)
{
    private AppDbContext Db => store.Db;

    private bool CanWrite => user.HasPermission(Permissions.BlogWrite) || user.HasPermission(Permissions.BlogPublish);
    private bool CanPublish => user.HasPermission(Permissions.BlogPublish);

    private void RequireWrite()
    {
        if (!CanWrite) throw DomainException.Forbidden("auth.forbidden", "You do not have permission to perform this action.");
    }

    private void RequirePublish()
    {
        if (!CanPublish)
            throw DomainException.Forbidden("blog.publish_required", "Only editors with publishing rights can do this. Submit the post for review instead.");
    }

    // ---------------------------------------------------------------- Categories

    public async Task<IReadOnlyList<BlogCategoryDto>> ListCategoriesAsync(CancellationToken ct)
    {
        RequireWrite();
        var categories = await Db.Set<BlogCategory>().AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var posts = await Db.Set<BlogPost>().AsNoTracking().Select(p => p.CategoryIds).ToListAsync(ct);
        return categories.Select(c => ToDto(c, posts.Count(ids => ids.Contains(c.Id)))).ToList();
    }

    public async Task<BlogCategoryDto> CreateCategoryAsync(BlogCategoryInput input, CancellationToken ct)
    {
        RequirePublish();
        var c = new BlogCategory();
        Apply(c, input);
        await store.EnsureSlugFreeAsync<BlogCategory>(c.Slug, null, ct);
        Db.Add(c);
        audit.Record("blog.category_created", nameof(BlogCategory), c.Id, after: ToDto(c, 0));
        await store.SaveAsync(ct);
        return ToDto(c, 0);
    }

    public async Task<BlogCategoryDto> UpdateCategoryAsync(Guid id, BlogCategoryInput input, CancellationToken ct)
    {
        RequirePublish();
        var c = await store.FindAsync<BlogCategory>(id, ct);
        CmsStore.CheckStamp(Db, c, input.ConcurrencyStamp);
        var before = ToDto(c, 0);
        Apply(c, input);
        await store.EnsureSlugFreeAsync<BlogCategory>(c.Slug, c.Id, ct);
        audit.Record("blog.category_updated", nameof(BlogCategory), c.Id, before, ToDto(c, 0));
        await store.SaveAsync(ct);
        return ToDto(c, 0);
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        RequirePublish();
        var posts = await Db.Set<BlogPost>().ToListAsync(ct);
        foreach (var p in posts.Where(p => p.CategoryIds.Contains(id))) p.CategoryIds = p.CategoryIds.Where(x => x != id).ToList();
        await store.DeleteAsync<BlogCategory>(id, "blog.category_deleted", c => ToDto(c, 0), ct);
    }

    private static void Apply(BlogCategory c, BlogCategoryInput r)
    {
        var e = new FieldErrors();
        c.Slug = WebsiteRules.Slug(r.Slug, "slug", e);
        e.ThrowIfAny();
        c.Name = r.Name.Trim();
        c.Description = WebsiteRules.Clean(r.Description);
        c.SortOrder = r.SortOrder;
    }

    private static BlogCategoryDto ToDto(BlogCategory c, int count) =>
        new(c.Id, c.Slug, c.Name, c.Description, c.SortOrder, count, c.UpdatedAt, c.ConcurrencyStamp);

    // ---------------------------------------------------------------- Posts

    public async Task<PagedResult<BlogPostSummaryDto>> ListPostsAsync(BlogPostQuery query, CancellationToken ct)
    {
        RequireWrite();
        var q = Db.Set<BlogPost>().AsNoTracking();
        if (query.Status is { } status) q = q.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = PagingExtensions.LikePattern(query.Search);
            q = q.Where(p => EF.Functions.Like(p.Title, pattern) || EF.Functions.Like(p.Slug, pattern) || EF.Functions.Like(p.Excerpt, pattern));
        }
        var rows = await q.OrderByDescending(p => p.UpdatedAt).ToListAsync(ct);
        if (query.CategoryId is { } cat) rows = rows.Where(p => p.CategoryIds.Contains(cat)).ToList();
        var total = rows.Count;
        rows = rows.Skip(query.Skip).Take(query.PageSize).ToList();
        var authors = await Db.Set<TeamMember>().AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var categories = await Db.Set<BlogCategory>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var items = rows.Select(p => new BlogPostSummaryDto(p.Id, p.Slug, p.Title, p.Status,
            p.AuthorId is { } a ? authors.GetValueOrDefault(a) : null,
            p.CategoryIds.Where(categories.ContainsKey).Select(id => categories[id]).ToList(), p.Tags, p.ReadingMinutes, p.PublishAt,
            p.PublishedAt, p.UpdatedAt)).ToList();
        return new PagedResult<BlogPostSummaryDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<BlogPostDto> GetPostAsync(Guid id, CancellationToken ct)
    {
        RequireWrite();
        return ToDto(await store.FindAsync<BlogPost>(id, ct, true));
    }

    public async Task<BlogPostDto> CreatePostAsync(BlogPostInput input, CancellationToken ct)
    {
        RequireWrite();
        var p = new BlogPost { Status = BlogPostStatus.Draft, CreatedByUserId = user.Id };
        await ApplyAsync(p, input, ct);
        await store.EnsureSlugFreeAsync<BlogPost>(p.Slug, null, ct);
        Db.Add(p);
        audit.Record("blog.post_created", nameof(BlogPost), p.Id, after: Snapshot(p));
        await store.SaveAsync(ct);
        return ToDto(p);
    }

    public async Task<BlogPostDto> UpdatePostAsync(Guid id, BlogPostInput input, CancellationToken ct)
    {
        RequireWrite();
        var p = await store.FindAsync<BlogPost>(id, ct);
        CmsStore.CheckStamp(Db, p, input.ConcurrencyStamp);
        // Editing what readers see (or will see at a scheduled time) is publishing.
        if (p.Status is BlogPostStatus.Published or BlogPostStatus.Scheduled or BlogPostStatus.Archived) RequirePublish();
        var before = Snapshot(p);
        await ApplyAsync(p, input, ct);
        await store.EnsureSlugFreeAsync<BlogPost>(p.Slug, p.Id, ct);
        audit.Record("blog.post_updated", nameof(BlogPost), p.Id, before, Snapshot(p));
        await store.SaveAsync(ct);
        return ToDto(p);
    }

    public async Task DeletePostAsync(Guid id, CancellationToken ct)
    {
        RequireWrite();
        var p = await store.FindAsync<BlogPost>(id, ct, true);
        // Writers may delete their own unpublished drafts; anything else needs publishing rights.
        if (!(p.Status == BlogPostStatus.Draft && p.CreatedByUserId == user.Id)) RequirePublish();
        await store.DeleteAsync<BlogPost>(id, "blog.post_deleted", Snapshot, ct);
    }

    public Task<BlogPostDto> SubmitAsync(Guid id, BlogActionInput input, CancellationToken ct) =>
        TransitionAsync(id, input, "blog.post_submitted", ct, p =>
        {
            RequireWrite();
            if (p.Status != BlogPostStatus.Draft) throw InvalidTransition("Only drafts can be submitted for review.");
            p.Status = BlogPostStatus.InReview;
            p.SubmittedByUserId = user.Id;
        });

    public Task<BlogPostDto> PublishAsync(Guid id, BlogActionInput input, CancellationToken ct) =>
        TransitionAsync(id, input, "blog.post_published", ct, p =>
        {
            RequirePublish();
            if (p.Status == BlogPostStatus.Published) throw InvalidTransition("This post is already published.");
            EnsureComplete(p);
            var now = clock.GetUtcNow().UtcDateTime;
            p.Status = BlogPostStatus.Published;
            p.PublishAt = null;
            p.PublishedAt ??= now;
            p.PublishedByUserId = user.Id;
        });

    public Task<BlogPostDto> ScheduleAsync(Guid id, BlogActionInput input, CancellationToken ct) =>
        TransitionAsync(id, input, "blog.post_scheduled", ct, p =>
        {
            RequirePublish();
            var at = WebsiteRules.Utc(input.PublishAt);
            if (at is null || at <= clock.GetUtcNow().UtcDateTime.AddMinutes(1))
                throw FieldRulesError("publishAt", "Pick a publish time in the future.");
            if (p.Status == BlogPostStatus.Published) throw InvalidTransition("Unpublish the post before scheduling it.");
            EnsureComplete(p);
            p.Status = BlogPostStatus.Scheduled;
            p.PublishAt = at;
            p.PublishedByUserId = user.Id;
        });

    public Task<BlogPostDto> UnpublishAsync(Guid id, BlogActionInput input, CancellationToken ct) =>
        TransitionAsync(id, input, "blog.post_unpublished", ct, p =>
        {
            RequirePublish();
            if (p.Status is not (BlogPostStatus.Published or BlogPostStatus.Scheduled)) throw InvalidTransition("Only live or scheduled posts can be unpublished.");
            p.Status = BlogPostStatus.Archived;
            p.PublishAt = null;
        });

    public Task<BlogPostDto> ReturnToDraftAsync(Guid id, BlogActionInput input, CancellationToken ct) =>
        TransitionAsync(id, input, "blog.post_returned_to_draft", ct, p =>
        {
            RequirePublish();
            if (p.Status == BlogPostStatus.Draft) throw InvalidTransition("This post is already a draft.");
            p.Status = BlogPostStatus.Draft;
            p.PublishAt = null;
        });

    private async Task<BlogPostDto> TransitionAsync(Guid id, BlogActionInput input, string action, CancellationToken ct, Action<BlogPost> change)
    {
        var p = await store.FindAsync<BlogPost>(id, ct);
        CmsStore.CheckStamp(Db, p, input.ConcurrencyStamp);
        var before = new { p.Status, p.PublishAt, p.PublishedAt };
        change(p);
        audit.Record(action, nameof(BlogPost), p.Id, before, new { p.Status, p.PublishAt, p.PublishedAt }, WebsiteRules.Clean(input.Note));
        await Db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    private static void EnsureComplete(BlogPost p)
    {
        var e = new FieldErrors();
        if (string.IsNullOrWhiteSpace(p.BodyMarkdown) || MarkdownSanitizer.ToPlainText(p.BodyMarkdown).Length < 100)
            e.Add("bodyMarkdown", "Write at least a short article (100+ characters) before publishing.");
        if (string.IsNullOrWhiteSpace(p.Excerpt)) e.Add("excerpt", "Add an excerpt before publishing.");
        if (p.CoverImageUrl is not null && string.IsNullOrWhiteSpace(p.CoverImageAlt))
            e.Add("coverImageAlt", "Describe the cover image for screen-reader users before publishing.");
        e.ThrowIfAny("blog.incomplete", "This post isn't ready to publish yet.");
    }

    private static DomainException InvalidTransition(string message) => DomainException.Conflict("blog.invalid_transition", message);

    private static DomainException FieldRulesError(string field, string message) => Accounts.FieldRules.FieldError("blog.invalid", field, message);

    private async Task ApplyAsync(BlogPost p, BlogPostInput r, CancellationToken ct)
    {
        var e = new FieldErrors();
        var slug = WebsiteRules.Slug(r.Slug, "slug", e);
        if (slug.Length > 120) e.Add("slug", "Keep the slug under 120 characters.");
        var body = WebsiteRules.Markdown(r.BodyMarkdown, "bodyMarkdown", e, 100000) ?? string.Empty;
        var cover = rules.Image(r.CoverImageUrl, "coverImageUrl", e);
        if (r.AuthorId is { } author && !await Db.Set<TeamMember>().AnyAsync(m => m.Id == author, ct)) e.Add("authorId", "Pick a team member as the author.");
        var categories = (r.CategoryIds ?? new List<Guid>()).Distinct().ToList();
        if (categories.Count > 5) e.Add("categoryIds", "Pick at most 5 categories.");
        if (categories.Count > 0 && await Db.Set<BlogCategory>().CountAsync(c => categories.Contains(c.Id), ct) != categories.Count)
            e.Add("categoryIds", "Some categories no longer exist.");
        var tags = WebsiteRules.Lines(r.Tags?.Select(t => t?.Trim().ToLowerInvariant()), "tags", e, 12, 40);
        if (tags.Any(t => !Accounts.FieldRules.IsSlug(t.Replace(' ', '-')))) e.Add("tags", "Tags use letters, digits, spaces and dashes.");
        var related = (r.RelatedPostIds ?? new List<Guid>()).Distinct().Where(x => x != p.Id).ToList();
        if (related.Count > 6) e.Add("relatedPostIds", "Pick at most 6 related posts.");
        if (related.Count > 0 && await Db.Set<BlogPost>().CountAsync(x => related.Contains(x.Id), ct) != related.Count)
            e.Add("relatedPostIds", "Some related posts no longer exist.");
        var seo = rules.Seo(r.Seo, e);
        e.ThrowIfAny();

        p.Slug = slug;
        p.Title = r.Title.Trim();
        p.Excerpt = r.Excerpt.Trim();
        p.BodyMarkdown = body;
        p.ReadingMinutes = MarkdownSanitizer.ReadingMinutes(body);
        p.CoverImageUrl = cover;
        p.CoverImageAlt = WebsiteRules.Clean(r.CoverImageAlt);
        p.AuthorId = r.AuthorId;
        p.CategoryIds = categories;
        p.Tags = tags;
        p.RelatedPostIds = related;
        p.Seo = seo;
    }

    private static object Snapshot(BlogPost p) => new { p.Slug, p.Title, p.Status, p.PublishAt, p.PublishedAt, p.AuthorId, p.Tags, BodyLength = p.BodyMarkdown.Length };

    private BlogPostDto ToDto(BlogPost p)
    {
        var publish = CanPublish;
        var isOwnDraft = p.Status == BlogPostStatus.Draft && p.CreatedByUserId == user.IdOrNull;
        var can = new BlogPermissionsDto(
            Edit: p.Status is BlogPostStatus.Draft or BlogPostStatus.InReview || publish,
            Submit: p.Status == BlogPostStatus.Draft,
            Publish: publish && p.Status != BlogPostStatus.Published,
            Schedule: publish && p.Status != BlogPostStatus.Published,
            Unpublish: publish && p.Status is BlogPostStatus.Published or BlogPostStatus.Scheduled,
            ReturnToDraft: publish && p.Status != BlogPostStatus.Draft,
            Delete: publish || isOwnDraft);
        return new BlogPostDto(p.Id, p.Slug, p.Title, p.Excerpt, p.BodyMarkdown, p.CoverImageUrl, p.CoverImageAlt, p.AuthorId, p.CategoryIds,
            p.Tags, p.ReadingMinutes, p.Status, p.PublishAt, p.PublishedAt, p.RelatedPostIds, SeoDto.From(p.Seo), p.CreatedByUserId,
            p.SubmittedByUserId, p.PublishedByUserId, p.CreatedAt, p.UpdatedAt, p.ConcurrencyStamp, can);
    }
}

/// <summary>Publishes scheduled posts whose time has come. Conditional per-row update: safe to retry and to run on several instances.</summary>
public sealed class BlogSchedulerJob(AppDbContext db, IAuditLogger audit, TimeProvider clock) : IJob
{
    public string Name => nameof(BlogSchedulerJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.Set<BlogPost>().AsNoTracking()
            .Where(p => p.Status == BlogPostStatus.Scheduled && p.PublishAt != null && p.PublishAt <= now)
            .Select(p => new { p.Id, p.PublishAt }).Take(200).ToListAsync(ct);
        var published = 0;
        foreach (var post in due)
        {
            var at = post.PublishAt!.Value;
            var changed = await db.Set<BlogPost>()
                .Where(p => p.Id == post.Id && p.Status == BlogPostStatus.Scheduled && p.PublishAt == post.PublishAt)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, BlogPostStatus.Published)
                    .SetProperty(p => p.PublishedAt, p => p.PublishedAt ?? at)
                    .SetProperty(p => p.PublishAt, (DateTime?)null)
                    .SetProperty(p => p.UpdatedAt, now)
                    .SetProperty(p => p.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (changed == 1)
            {
                audit.RecordSystem("blog.post_published", nameof(BlogPost), post.Id, new { Status = BlogPostStatus.Published, ScheduledFor = at });
                published++;
            }
        }
        if (published > 0) await db.SaveChangesAsync(ct);
        return $"Published {published} scheduled post(s).";
    }
}

/// <summary>Public (anonymous) blog reads and the RSS feed. Only published posts whose publish time has passed.</summary>
public static class PublicBlogQueries
{
    private static IQueryable<BlogPost> Live(AppDbContext db, DateTime now) =>
        db.Set<BlogPost>().AsNoTracking().Where(p => p.Status == BlogPostStatus.Published && p.PublishedAt != null && p.PublishedAt <= now);

    private sealed record Lookups(IReadOnlyDictionary<Guid, BlogCategory> Categories, IReadOnlyDictionary<Guid, TeamMember> Authors);

    private static async Task<Lookups> LookupsAsync(AppDbContext db, CancellationToken ct) => new(
        await db.Set<BlogCategory>().AsNoTracking().ToDictionaryAsync(c => c.Id, ct),
        await db.Set<TeamMember>().AsNoTracking().ToDictionaryAsync(m => m.Id, ct));

    private static PostCardDto Card(BlogPost p, Lookups l) => new(
        p.Slug, p.Title, p.Excerpt, p.CoverImageUrl, p.CoverImageAlt,
        p.AuthorId is { } a && l.Authors.TryGetValue(a, out var author) && author.IsPublished ? author.Name : null,
        p.CategoryIds.Where(l.Categories.ContainsKey).Select(id => new BlogCategoryRefDto(l.Categories[id].Slug, l.Categories[id].Name)).ToList(),
        p.Tags, p.ReadingMinutes, p.PublishedAt);

    public static async Task<IReadOnlyList<PostCardDto>> LatestAsync(AppDbContext db, DateTime now, int count, CancellationToken ct)
    {
        var l = await LookupsAsync(db, ct);
        return (await Live(db, now).OrderByDescending(p => p.PublishedAt).Take(count).ToListAsync(ct)).Select(p => Card(p, l)).ToList();
    }

    public static async Task<BlogIndexDto> IndexAsync(AppDbContext db, DateTime now, PublicBlogQuery query, CancellationToken ct)
    {
        var l = await LookupsAsync(db, ct);
        var q = Live(db, now);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = PagingExtensions.LikePattern(query.Search);
            q = q.Where(p => EF.Functions.Like(p.Title, pattern) || EF.Functions.Like(p.Excerpt, pattern) || EF.Functions.Like(p.BodyMarkdown, pattern));
        }
        var all = await q.OrderByDescending(p => p.PublishedAt).ToListAsync(ct);
        var everything = string.IsNullOrWhiteSpace(query.Search) ? all : await Live(db, now).ToListAsync(ct);

        IEnumerable<BlogPost> filtered = all;
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var cat = l.Categories.Values.FirstOrDefault(c => c.Slug == query.Category.Trim());
            filtered = cat is null ? Enumerable.Empty<BlogPost>() : filtered.Where(p => p.CategoryIds.Contains(cat.Id));
        }
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim().ToLowerInvariant();
            filtered = filtered.Where(p => p.Tags.Contains(tag));
        }
        var list = filtered.ToList();
        var items = list.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(p => Card(p, l)).ToList();
        var categories = l.Categories.Values.OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new BlogCategoryCountDto(c.Slug, c.Name, c.Description, everything.Count(p => p.CategoryIds.Contains(c.Id))))
            .Where(c => c.PostCount > 0).ToList();
        var tags = everything.SelectMany(p => p.Tags).GroupBy(t => t).Select(g => new TagCountDto(g.Key, g.Count()))
            .OrderByDescending(t => t.PostCount).ThenBy(t => t.Tag).Take(30).ToList();
        return new BlogIndexDto(items, list.Count, query.Page, query.PageSize, categories, tags);
    }

    public static async Task<PublicPostDto> PostAsync(AppDbContext db, PublicSiteService site, DateTime now, string slug, CancellationToken ct)
    {
        var p = await Live(db, now).FirstOrDefaultAsync(x => x.Slug == slug, ct) ?? throw CmsStore.NotFound<BlogPost>();
        var l = await LookupsAsync(db, ct);
        var author = p.AuthorId is { } a && l.Authors.TryGetValue(a, out var m) && m.IsPublished
            ? new AuthorDto(m.Slug, m.Name, m.Role, m.Bio, m.PhotoUrl, m.SocialLinks) : null;

        var related = new List<BlogPost>();
        if (p.RelatedPostIds.Count > 0)
        {
            var ids = p.RelatedPostIds;
            related.AddRange(await Live(db, now).Where(x => ids.Contains(x.Id)).ToListAsync(ct));
        }
        if (related.Count < 3)
        {
            var candidates = await Live(db, now).Where(x => x.Id != p.Id).OrderByDescending(x => x.PublishedAt).Take(50).ToListAsync(ct);
            related.AddRange(candidates.Where(c => related.All(r => r.Id != c.Id))
                .OrderByDescending(c => c.CategoryIds.Intersect(p.CategoryIds).Count() * 2 + c.Tags.Intersect(p.Tags).Count())
                .ThenByDescending(c => c.PublishedAt).Take(3 - related.Count));
        }

        var ld = new JsonLd(await site.BaseUrlAsync(ct), await site.SettingsAsync(ct));
        var path = $"/blog/{p.Slug}";
        var settings = await site.SettingsAsync(ct);
        var seo = new PublicSeoDto(p.Seo.Title ?? p.Title, p.Seo.Description ?? p.Excerpt, p.Seo.OgImageUrl ?? p.CoverImageUrl ?? settings.Seo.DefaultOgImageUrl,
            ld.Url(p.Seo.CanonicalUrl ?? path), p.Seo.NoIndex);
        return new PublicPostDto(p.Slug, p.Title, p.Excerpt, p.BodyMarkdown, p.CoverImageUrl, p.CoverImageAlt, author,
            p.CategoryIds.Where(l.Categories.ContainsKey).Select(id => new BlogCategoryRefDto(l.Categories[id].Slug, l.Categories[id].Name)).ToList(),
            p.Tags, p.ReadingMinutes, p.PublishedAt, p.UpdatedAt, related.Select(r => Card(r, l)).ToList(), seo,
            new[]
            {
                ld.Article("BlogPosting", p.Title, p.Excerpt, path, p.CoverImageUrl, p.PublishedAt, p.UpdatedAt, author?.Name),
                ld.Breadcrumbs(("Home", "/"), ("Blog", "/blog"), (p.Title, path)),
            });
    }

    /// <summary>RSS 2.0 feed of the 30 latest posts.</summary>
    public static async Task<string> RssAsync(AppDbContext db, PublicSiteService site, DateTime now, CancellationToken ct)
    {
        var baseUrl = await site.BaseUrlAsync(ct);
        var settings = await site.SettingsAsync(ct);
        var l = await LookupsAsync(db, ct);
        var posts = await Live(db, now).OrderByDescending(p => p.PublishedAt).Take(30).ToListAsync(ct);
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
        {
            w.WriteStartDocument();
            w.WriteStartElement("rss");
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("xmlns", "atom", null, "http://www.w3.org/2005/Atom");
            w.WriteStartElement("channel");
            w.WriteElementString("title", $"{settings.SiteName} blog");
            w.WriteElementString("link", baseUrl + "/blog");
            w.WriteElementString("description", settings.Seo.DefaultDescription ?? settings.Tagline);
            w.WriteElementString("language", "en");
            w.WriteStartElement("atom", "link", "http://www.w3.org/2005/Atom");
            w.WriteAttributeString("href", baseUrl + "/api/v1/public/blog/rss.xml");
            w.WriteAttributeString("rel", "self");
            w.WriteAttributeString("type", "application/rss+xml");
            w.WriteEndElement();
            if (posts.Count > 0) w.WriteElementString("lastBuildDate", posts[0].PublishedAt!.Value.ToString("r", CultureInfo.InvariantCulture));
            foreach (var p in posts)
            {
                var link = $"{baseUrl}/blog/{p.Slug}";
                w.WriteStartElement("item");
                w.WriteElementString("title", p.Title);
                w.WriteElementString("link", link);
                w.WriteStartElement("guid");
                w.WriteAttributeString("isPermaLink", "true");
                w.WriteString(link);
                w.WriteEndElement();
                w.WriteElementString("pubDate", p.PublishedAt!.Value.ToString("r", CultureInfo.InvariantCulture));
                w.WriteElementString("description", p.Excerpt);
                foreach (var id in p.CategoryIds.Where(l.Categories.ContainsKey)) w.WriteElementString("category", l.Categories[id].Name);
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
    }
}
