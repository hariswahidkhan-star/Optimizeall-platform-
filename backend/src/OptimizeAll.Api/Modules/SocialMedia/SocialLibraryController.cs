using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

public sealed record MediaDto(
    Guid Id, Guid ClientAccountId, MediaKind Kind, string Title, string PreviewUrl, string? PublicUrl, bool IsUpload, int? Width, int? Height,
    int? DurationSeconds, long? SizeBytes, string? ContentType, string? AltText, IReadOnlyList<string> Tags, bool IsPublic, DateTime CreatedAt);

public sealed class MediaUploadForm
{
    public IFormFile? File { get; set; }
    [MaxLength(200)] public string? Title { get; set; }
    [MaxLength(1000)] public string? AltText { get; set; }

    /// <summary>Serve the image without a session (needed for Instagram/Facebook API publishing).</summary>
    public bool IsPublic { get; set; }
    [MaxLength(500)] public string? Tags { get; set; }
}

public sealed class MediaUrlInput
{
    [Required] public MediaKind? Kind { get; set; }
    [Required, MaxLength(2000)] public string Url { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Range(1, 20000)] public int? Width { get; set; }
    [Range(1, 20000)] public int? Height { get; set; }
    [Range(1, 86400)] public int? DurationSeconds { get; set; }
    [Range(1, 10L * 1024 * 1024 * 1024)] public long? SizeBytes { get; set; }
    [MaxLength(1000)] public string? AltText { get; set; }
    [MaxLength(20)] public List<string> Tags { get; set; } = new();
}

public sealed class MediaUpdateInput
{
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(1000)] public string? AltText { get; set; }
    [MaxLength(20)] public List<string> Tags { get; set; } = new();
    [Range(1, 86400)] public int? DurationSeconds { get; set; }
}

public sealed record HashtagSetDto(Guid Id, Guid ClientAccountId, string Name, IReadOnlyList<string> Hashtags, Guid ConcurrencyStamp);

public sealed class HashtagSetInput
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MinLength(1), MaxLength(60)] public List<string> Hashtags { get; set; } = new();
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record SnippetDto(Guid Id, Guid ClientAccountId, string Name, string Body, Guid ConcurrencyStamp);

public sealed class SnippetInput
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(4000)] public string Body { get; set; } = string.Empty;
    public Guid? ConcurrencyStamp { get; set; }
}

/// <summary>The client's media library, hashtag library and caption snippets.</summary>
[ApiController]
[Route("api/v1/agency/social")]
public sealed class SocialLibraryController(
    AppDbContext db, SocialAccess access, IFileService files, IFileStorage storage, SocialPublishingService publishing,
    ICurrentUser currentUser, IAuditLogger audit) : ControllerBase
{
    [HttpGet("clients/{clientId:guid}/media")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<PagedResult<MediaDto>> Media(Guid clientId, [FromQuery] PageQuery query, [FromQuery] MediaKind? kind, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var q = db.Set<SocialMediaAsset>().AsNoTracking().Where(m => m.ClientAccountId == clientId);
        if (kind is { } k) q = q.Where(m => m.Kind == k);
        if (!string.IsNullOrWhiteSpace(query.Search)) q = q.Where(m => EF.Functions.Like(m.Title, PagingExtensions.LikePattern(query.Search), "\\"));
        var page = await q.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).ToPagedAsync(query, ct);
        return new PagedResult<MediaDto>(page.Items.Select(m => ToDto(m, staff: true)).ToList(), page.Total, page.Page, page.PageSize);
    }

    /// <summary>Uploads an image (PNG/JPEG/WebP, validated and metadata-stripped by the Files module).</summary>
    [HttpPost("clients/{clientId:guid}/media/upload")]
    [HasPermission(Permissions.SocialManage)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<MediaDto>> Upload(Guid clientId, [FromForm] MediaUploadForm form, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        if (form.File is null) throw new DomainException("file.required", "Choose an image to upload.");
        var file = await files.SaveImageAsync(form.File, FilePurpose.ContentImage, currentUser.Id, form.IsPublic, ct);
        var asset = new SocialMediaAsset
        {
            ClientAccountId = clientId,
            Kind = MediaKind.Image,
            Title = string.IsNullOrWhiteSpace(form.Title) ? file.OriginalFileName : form.Title.Trim(),
            FileId = file.Id,
            ContentType = file.ContentType,
            SizeBytes = file.SizeBytes,
            Width = file.Width,
            Height = file.Height,
            AltText = string.IsNullOrWhiteSpace(form.AltText) ? null : form.AltText.Trim(),
            Tags = SplitTags(form.Tags),
            IsPublic = form.IsPublic,
            CreatedByUserId = currentUser.Id,
        };
        db.Set<SocialMediaAsset>().Add(asset);
        audit.Record("social.media.uploaded", nameof(SocialMediaAsset), asset.Id, after: new { file.Id, file.ContentType, file.SizeBytes, form.IsPublic });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            files.Discard(file);
            throw;
        }
        return Created($"/api/v1/agency/social/media/{asset.Id}", ToDto(asset, staff: true));
    }

    /// <summary>Adds an image or video hosted elsewhere (https only). Needed for video, which is not uploaded here.</summary>
    [HttpPost("clients/{clientId:guid}/media/url")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<ActionResult<MediaDto>> AddUrl(Guid clientId, MediaUrlInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        if (!Uri.TryCreate(input.Url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new DomainException("social.invalid_url", "Use an https URL.", errors: new Dictionary<string, string[]> { ["url"] = new[] { "Use an https URL." } });
        var asset = new SocialMediaAsset
        {
            ClientAccountId = clientId,
            Kind = input.Kind!.Value,
            Title = input.Title.Trim(),
            ExternalUrl = uri.ToString(),
            Width = input.Width,
            Height = input.Height,
            DurationSeconds = input.DurationSeconds,
            SizeBytes = input.SizeBytes,
            AltText = string.IsNullOrWhiteSpace(input.AltText) ? null : input.AltText.Trim(),
            Tags = input.Tags.Select(t => t.Trim()).Where(t => t.Length is > 0 and <= 40).Distinct().ToList(),
            IsPublic = true,
            CreatedByUserId = currentUser.Id,
        };
        db.Set<SocialMediaAsset>().Add(asset);
        audit.Record("social.media.linked", nameof(SocialMediaAsset), asset.Id, after: new { asset.Kind, asset.ExternalUrl });
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/agency/social/media/{asset.Id}", ToDto(asset, staff: true));
    }

    [HttpPut("media/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<MediaDto> UpdateMedia(Guid id, MediaUpdateInput input, CancellationToken ct)
    {
        var asset = await access.OwnedAsync<SocialMediaAsset>(id, m => m.ClientAccountId, "Media", ct);
        asset.Title = input.Title.Trim();
        asset.AltText = string.IsNullOrWhiteSpace(input.AltText) ? null : input.AltText.Trim();
        asset.Tags = input.Tags.Select(t => t.Trim()).Where(t => t.Length is > 0 and <= 40).Distinct().ToList();
        if (asset.Kind == MediaKind.Video) asset.DurationSeconds = input.DurationSeconds;
        await db.SaveChangesAsync(ct);
        return ToDto(asset, staff: true);
    }

    [HttpDelete("media/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IActionResult> DeleteMedia(Guid id, CancellationToken ct)
    {
        var asset = await access.OwnedAsync<SocialMediaAsset>(id, m => m.ClientAccountId, "Media", ct);
        var used = await db.Set<SocialPostVariant>().AsNoTracking().Where(v => v.ClientAccountId == asset.ClientAccountId)
            .Select(v => v.MediaIds).ToListAsync(ct);
        if (used.Any(ids => ids.Contains(asset.Id)))
            throw DomainException.Conflict("social.media_in_use", "This media is used by a post; remove it from the post first.");
        db.Remove(asset);
        audit.Record("social.media.deleted", nameof(SocialMediaAsset), asset.Id);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Streams an uploaded image to staff of the client (private preview; never cached publicly).</summary>
    [HttpGet("media/{id:guid}/content")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IActionResult> Content(Guid id, CancellationToken ct)
    {
        var asset = await access.OwnedAsync<SocialMediaAsset>(id, m => m.ClientAccountId, "Media", ct);
        return await StreamAsync(this, db, storage, asset, ct);
    }

    internal static async Task<IActionResult> StreamAsync(ControllerBase controller, AppDbContext db, IFileStorage storage, SocialMediaAsset asset, CancellationToken ct)
    {
        if (asset.FileId is null)
        {
            if (asset.ExternalUrl is null) throw DomainException.NotFound("Media");
            return controller.Redirect(asset.ExternalUrl);
        }
        var file = await db.Set<StoredFile>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == asset.FileId, ct) ?? throw DomainException.NotFound("Media");
        var stream = storage.OpenRead(file.StorageKey) ?? throw DomainException.NotFound("Media");
        var headers = controller.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        headers.CacheControl = "private, max-age=300";
        return controller.File(stream, file.ContentType, enableRangeProcessing: false);
    }

    [HttpGet("clients/{clientId:guid}/hashtag-sets")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IReadOnlyList<HashtagSetDto>> HashtagSets(Guid clientId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        return await db.Set<SocialHashtagSet>().AsNoTracking().Where(h => h.ClientAccountId == clientId).OrderBy(h => h.Name)
            .Select(h => new HashtagSetDto(h.Id, h.ClientAccountId, h.Name, h.Hashtags, h.ConcurrencyStamp)).ToListAsync(ct);
    }

    [HttpPost("clients/{clientId:guid}/hashtag-sets")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<HashtagSetDto> CreateHashtagSet(Guid clientId, HashtagSetInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var set = new SocialHashtagSet { ClientAccountId = clientId, Name = input.Name.Trim(), Hashtags = Tags(input.Hashtags) };
        db.Set<SocialHashtagSet>().Add(set);
        await db.SaveChangesAsync(ct);
        return new HashtagSetDto(set.Id, clientId, set.Name, set.Hashtags, set.ConcurrencyStamp);
    }

    [HttpPut("hashtag-sets/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<HashtagSetDto> UpdateHashtagSet(Guid id, HashtagSetInput input, CancellationToken ct)
    {
        var set = await access.OwnedAsync<SocialHashtagSet>(id, h => h.ClientAccountId, "Hashtag set", ct);
        if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != set.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "Changed meanwhile; reload.");
            db.Entry(set).Property(x => x.ConcurrencyStamp).OriginalValue = stamp;
        }
        set.Name = input.Name.Trim();
        set.Hashtags = Tags(input.Hashtags);
        await db.SaveChangesAsync(ct);
        return new HashtagSetDto(set.Id, set.ClientAccountId, set.Name, set.Hashtags, set.ConcurrencyStamp);
    }

    [HttpDelete("hashtag-sets/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IActionResult> DeleteHashtagSet(Guid id, CancellationToken ct)
    {
        var set = await access.OwnedAsync<SocialHashtagSet>(id, h => h.ClientAccountId, "Hashtag set", ct);
        db.Remove(set);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("clients/{clientId:guid}/snippets")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IReadOnlyList<SnippetDto>> Snippets(Guid clientId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        return await db.Set<SocialCaptionSnippet>().AsNoTracking().Where(s => s.ClientAccountId == clientId).OrderBy(s => s.Name)
            .Select(s => new SnippetDto(s.Id, s.ClientAccountId, s.Name, s.Body, s.ConcurrencyStamp)).ToListAsync(ct);
    }

    [HttpPost("clients/{clientId:guid}/snippets")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<SnippetDto> CreateSnippet(Guid clientId, SnippetInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var s = new SocialCaptionSnippet { ClientAccountId = clientId, Name = input.Name.Trim(), Body = input.Body };
        db.Set<SocialCaptionSnippet>().Add(s);
        await db.SaveChangesAsync(ct);
        return new SnippetDto(s.Id, clientId, s.Name, s.Body, s.ConcurrencyStamp);
    }

    [HttpPut("snippets/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<SnippetDto> UpdateSnippet(Guid id, SnippetInput input, CancellationToken ct)
    {
        var s = await access.OwnedAsync<SocialCaptionSnippet>(id, x => x.ClientAccountId, "Snippet", ct);
        if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != s.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "Changed meanwhile; reload.");
            db.Entry(s).Property(x => x.ConcurrencyStamp).OriginalValue = stamp;
        }
        s.Name = input.Name.Trim();
        s.Body = input.Body;
        await db.SaveChangesAsync(ct);
        return new SnippetDto(s.Id, s.ClientAccountId, s.Name, s.Body, s.ConcurrencyStamp);
    }

    [HttpDelete("snippets/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IActionResult> DeleteSnippet(Guid id, CancellationToken ct)
    {
        var s = await access.OwnedAsync<SocialCaptionSnippet>(id, x => x.ClientAccountId, "Snippet", ct);
        db.Remove(s);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private MediaDto ToDto(SocialMediaAsset m, bool staff) => Map(m, publishing.PublicUrl(m), staff);

    internal static MediaDto Map(SocialMediaAsset m, string? publicUrl, bool staff) => new(
        m.Id, m.ClientAccountId, m.Kind, m.Title,
        m.FileId is null ? m.ExternalUrl ?? string.Empty
            : m.IsPublic ? FileUrls.For(m.FileId.Value)
            : staff ? $"/api/v1/agency/social/media/{m.Id}/content" : $"/api/v1/client/social/media/{m.Id}/content",
        publicUrl, m.FileId is not null, m.Width, m.Height, m.DurationSeconds, m.SizeBytes, m.ContentType, m.AltText, m.Tags, m.IsPublic, m.CreatedAt);

    private static List<string> Tags(IEnumerable<string> raw) =>
        raw.Select(PostValidator.NormalizeHashtag).Where(h => h.Length > 1 && h.Length <= 100).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> SplitTags(string? raw) =>
        (raw ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(t => t.Length <= 40).Distinct().Take(20).ToList();
}
