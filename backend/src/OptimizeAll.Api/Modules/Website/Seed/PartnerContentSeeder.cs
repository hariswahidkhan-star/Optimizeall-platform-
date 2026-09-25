using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Seed;

/// <summary>Configuration of the partner-content seed (<c>Website:PartnerContent</c>).</summary>
public sealed class PartnerContentOptions
{
    public const string Section = "Website:PartnerContent";

    /// <summary>
    /// Seeds the PCI AI and Certuvo partner blog posts with the Baseline profile (so production gets them too). On by
    /// default; set <c>Website__PartnerContent__Enabled=false</c> to skip it.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Production-safe editorial content: the partner blog posts (PCI AI and Certuvo clusters) from
/// <c>Modules/Website/Content/partner-posts/*.md</c>, their two blog categories, the "Optimize All Editorial" byline and
/// one public cover image per cluster. Runs with the Baseline profile, gated by <see cref="PartnerContentOptions"/>.
/// <para>
/// Insert-only and idempotent by slug: a post that already exists (or that an editor deleted or renamed, remembered in
/// the <c>seed.website_partner_content</c> ledger) is never created again, and existing rows are never overwritten, so CMS
/// edits survive restarts. Posts are published with dates staggered back from the first seeding (never in the future).
/// </para>
/// </summary>
public sealed class PartnerContentSeeder(
    TimeProvider clock,
    IDatabaseDialect dialect,
    IFileStorage storage,
    IOptions<PartnerContentOptions> options,
    ILogger<PartnerContentSeeder> logger) : ISeeder
{
    public const string LedgerName = "website_partner_content";
    public const string AuthorSlug = "optimize-all-editorial";
    public const string AuthorName = "Optimize All Editorial";
    private const string LockName = "website-partner-content-seed";

    public string Profile => "Baseline";

    /// <summary>After <see cref="WebsiteBaselineSeeder"/> (60).</summary>
    public int Order => 65;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Partner content seed disabled ({Section}:Enabled=false)", PartnerContentOptions.Section);
            return;
        }
        var posts = PartnerPostLibrary.All;
        var writtenKeys = new List<string>();

        await using var _ = await dialect.AcquireNamedLockAsync(db, LockName, TimeSpan.FromSeconds(60), ct);
        try
        {
            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            var ledger = await SeedLedger.LoadAsync(db, LedgerName, ct);
            var now = clock.GetUtcNow().UtcDateTime;

            // ---- Categories
            var categories = await db.Set<BlogCategory>().ToDictionaryAsync(c => c.Slug, ct);
            var maxSort = categories.Count == 0 ? 0 : categories.Values.Max(c => c.SortOrder);
            foreach (var (slug, name, description) in PartnerPostLibrary.Categories)
            {
                var seeded = ledger.WasSeeded("category:" + slug);
                ledger.Record("category:" + slug);
                if (seeded || categories.ContainsKey(slug)) continue;
                maxSort += 10;
                var category = new BlogCategory { Slug = slug, Name = name, Description = description, SortOrder = maxSort };
                db.Add(category);
                categories[slug] = category;
            }

            // ---- Byline
            var author = await db.Set<TeamMember>().FirstOrDefaultAsync(m => m.Slug == AuthorSlug, ct);
            var authorSeeded = ledger.WasSeeded("author:" + AuthorSlug);
            ledger.Record("author:" + AuthorSlug);
            if (author is null && !authorSeeded)
            {
                author = new TeamMember
                {
                    Slug = AuthorSlug, Name = AuthorName, Role = "Editorial team",
                    Bio = "Guides from the Optimize All editorial team on project controls, project finance and professional certification. " +
                          "Optimize All is the official marketing partner of PCI AI and Certuvo.",
                    Expertise = new List<string> { "Project controls", "Project finance", "Certification exam preparation" },
                    IsPublished = true, SortOrder = 1000,
                };
                db.Add(author);
            }

            // ---- Posts
            var existingSlugs = (await db.Set<BlogPost>().Select(p => p.Slug).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
            var covers = new Dictionary<string, string>(StringComparer.Ordinal);
            var created = new List<(BlogPost Post, PartnerPost Source)>();
            foreach (var source in posts)
            {
                var seeded = ledger.WasSeeded("post:" + source.Slug);
                ledger.Record("post:" + source.Slug);
                if (seeded || existingSlugs.Contains(source.Slug)) continue;

                if (!covers.TryGetValue(source.Cover, out var coverUrl))
                {
                    coverUrl = await CoverUrlAsync(db, source.Cover, now, writtenKeys, ct);
                    covers[source.Cover] = coverUrl;
                }
                var body = MarkdownSanitizer.Sanitize(source.Body);
                var publishedAt = now.AddDays(-Math.Max(1, source.PublishedDaysAgo));
                var post = new BlogPost
                {
                    Slug = source.Slug,
                    Title = source.Title,
                    Excerpt = source.Description,
                    BodyMarkdown = body,
                    ReadingMinutes = MarkdownSanitizer.ReadingMinutes(body),
                    CoverImageUrl = coverUrl,
                    CoverImageAlt = source.CoverAlt,
                    AuthorId = author?.Id,
                    CategoryIds = source.Categories.Where(categories.ContainsKey).Select(c => categories[c].Id).ToList(),
                    Tags = source.Tags.Select(t => t.ToLowerInvariant()).ToList(),
                    Status = BlogPostStatus.Published,
                    PublishedAt = publishedAt,
                    CreatedAt = publishedAt,
                    Seo = new SeoMeta { Title = source.Title, Description = source.Description },
                };
                db.Add(post);
                existingSlugs.Add(source.Slug);
                created.Add((post, source));
            }

            // Related posts resolve by slug among every post that exists now.
            if (created.Count > 0)
            {
                var ids = await db.Set<BlogPost>().Select(p => new { p.Slug, p.Id }).ToDictionaryAsync(p => p.Slug, p => p.Id, ct);
                foreach (var (post, source) in created)
                {
                    foreach (var p in created) ids.TryAdd(p.Post.Slug, p.Post.Id);
                    post.RelatedPostIds = source.Related.Where(ids.ContainsKey).Select(s => ids[s]).Distinct().Take(6).ToList();
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            if (created.Count > 0)
                logger.LogInformation("Partner content seed published {Count} blog post(s)", created.Count);
        }
        catch
        {
            foreach (var key in writtenKeys)
            {
                try { storage.Delete(key); } catch (IOException) { }
            }
            throw;
        }
    }

    /// <summary>
    /// The public URL of a cluster's cover image: the file created on an earlier run (matched by content hash and name),
    /// or a new public content image written through <see cref="IFileStorage"/> like an admin upload.
    /// </summary>
    private async Task<string> CoverUrlAsync(AppDbContext db, string name, DateTime now, List<string> writtenKeys, CancellationToken ct)
    {
        var bytes = PartnerPostLibrary.CoverBytes(name);
        var sha = Normalization.Sha256Hex(bytes);
        var fileName = "partner-cover-" + name + ".png";
        var existing = await db.Set<StoredFile>()
            .Where(f => f.Sha256 == sha && f.OriginalFileName == fileName && f.IsPublic && f.Purpose == FilePurpose.ContentImage)
            .Select(f => new { f.Id, f.StorageKey }).FirstOrDefaultAsync(ct);
        if (existing is not null && storage.OpenRead(existing.StorageKey) is { } open)
        {
            await open.DisposeAsync();
            return FileUrls.For(existing.Id);
        }

        var info = ImageInspector.Inspect(bytes) ?? throw new InvalidOperationException($"Partner cover '{name}' is not a valid image.");
        var key = storage.NewKey(now, info.Extension);
        await storage.WriteAsync(key, bytes, ct);
        writtenKeys.Add(key);
        var file = new StoredFile
        {
            OwnerUserId = Guid.Empty, // system content
            Purpose = FilePurpose.ContentImage,
            StorageKey = key,
            ContentType = info.ContentType,
            SizeBytes = bytes.Length,
            Sha256 = sha,
            OriginalFileName = fileName,
            Width = info.Width,
            Height = info.Height,
            CreatedAt = now,
            IsPublic = true,
        };
        db.Add(file);
        return FileUrls.For(file.Id);
    }
}
