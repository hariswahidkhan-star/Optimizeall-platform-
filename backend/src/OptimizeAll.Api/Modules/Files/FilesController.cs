using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Files;

[ApiController]
public sealed class FilesController(IFileService files, ICurrentUser currentUser, AppDbContext db, IAuditLogger audit) : ControllerBase
{
    /// <summary>
    /// Streams a file. Public campaign/content images are anonymous; submission screenshots only for the owner and
    /// reviewers/campaign managers. Anything the caller may not read is a 404 (existence is not revealed).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("api/v1/files/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var content = await files.OpenForReadAsync(id, ct);
        if (content is null)
            throw new DomainException("file.not_found", "File was not found.", DomainErrorKind.NotFound);

        var headers = Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        headers.CacheControl = content.File.IsPublic ? "public, max-age=86400" : "private, max-age=300";
        headers.ContentDisposition = $"inline; filename=\"{content.File.OriginalFileName.Replace("\"", string.Empty)}\"";
        if (content.File.IsPublic) headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        return File(content.Content, content.File.ContentType, enableRangeProcessing: false);
    }

    /// <summary>Uploads a public campaign/content image (PNG, JPEG or WebP, max 10 MB, 200–10000 px per side).</summary>
    [Authorize]
    [HttpPost("api/v1/admin/files")]
    [RequireAnyPermission(Permissions.CampaignsManage, Permissions.ContentManage)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<StoredFileDto>> Upload([FromForm] UploadFileForm form, CancellationToken ct)
    {
        var canCampaigns = currentUser.HasPermission(Permissions.CampaignsManage);
        var canContent = currentUser.HasPermission(Permissions.ContentManage);
        if (!canCampaigns && !canContent)
            throw DomainException.Forbidden("auth.forbidden", "You do not have permission to perform this action.");
        if (form.File is null)
            throw new DomainException("file.required", "Choose an image to upload.");

        var purpose = form.Purpose ?? (canCampaigns ? FilePurpose.CampaignAsset : FilePurpose.ContentImage);
        if (purpose == FilePurpose.SubmissionScreenshot)
            throw new DomainException("file.invalid_purpose", "Screenshots are uploaded with a submission.");

        var file = await files.SaveImageAsync(form.File, purpose, currentUser.Id, isPublic: true, ct);
        audit.Record("file.uploaded", nameof(StoredFile), file.Id,
            after: new { file.Purpose, file.ContentType, file.SizeBytes, file.Width, file.Height, file.Sha256 });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            files.Discard(file);
            throw;
        }
        return CreatedAtAction(nameof(Get), new { id = file.Id }, StoredFileDto.From(file));
    }
}

public sealed class UploadFileForm
{
    public IFormFile? File { get; set; }

    /// <summary>CampaignAsset (default for campaign managers) or ContentImage.</summary>
    public FilePurpose? Purpose { get; set; }
}
