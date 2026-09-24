using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

public sealed record DeliveryFileKind(string ContentType, string Extension);

/// <summary>
/// Allowlist for client delivery files (deliverables, attachments, brand assets, logos), identified by magic bytes — the
/// client's file name and Content-Type are never trusted: PNG, JPEG, WebP (metadata stripped), PDF and MP4. Max 50 MB.
/// Videos hosted elsewhere and documents (Google Docs, Figma…) are added as links instead.
/// </summary>
public static class DeliveryFileValidator
{
    public const long MaxBytes = 50L * 1024 * 1024;

    private static readonly string[] Mp4Brands = { "isom", "iso2", "iso3", "iso4", "iso5", "iso6", "mp41", "mp42", "avc1", "M4V ", "dash", "MSNV" };

    public static DeliveryFileKind? Identify(ReadOnlySpan<byte> data)
    {
        var image = ImageInspector.Inspect(data);
        if (image is not null) return new DeliveryFileKind(image.ContentType, image.Extension);
        if (data.Length >= 8 && Ascii(data, 0, "%PDF-")) return new DeliveryFileKind("application/pdf", ".pdf");
        if (data.Length >= 12 && Ascii(data, 4, "ftyp"))
        {
            var brand = Encoding.ASCII.GetString(data.Slice(8, 4));
            if (Mp4Brands.Contains(brand)) return new DeliveryFileKind("video/mp4", ".mp4");
        }
        return null;
    }

    private static bool Ascii(ReadOnlySpan<byte> d, int offset, string text)
    {
        if (offset + text.Length > d.Length) return false;
        for (var i = 0; i < text.Length; i++)
            if (d[offset + i] != (byte)text[i]) return false;
        return true;
    }
}

public sealed record DeliveryFileDto(Guid Id, string FileName, string ContentType, long SizeBytes, DateTime CreatedAt, string StaffUrl, string ClientUrl)
{
    public static DeliveryFileDto From(DeliveryFile f) => new(f.Id, f.OriginalFileName, f.ContentType, f.SizeBytes, f.CreatedAt,
        $"/api/v1/agency/files/{f.Id}", $"/api/v1/client/orgs/{f.ClientAccountId}/files/{f.Id}");
}

public sealed class DeliveryFileService(
    AppDbContext db, IFileStorage storage, IClientScope scope, ICurrentUser currentUser, IDatabaseDialect dialect, TimeProvider clock)
{
    /// <summary>
    /// Validates and stores a private file of a client, staging its row (saved by the caller). Call <see cref="Discard"/>
    /// if the caller's save fails.
    /// </summary>
    public async Task<DeliveryFile> SaveAsync(Guid clientId, IFormFile? upload, CancellationToken ct)
    {
        if (upload is null || upload.Length == 0) throw DeliveryRules.Invalid("file.empty", "file", "Choose a file to upload.");
        if (upload.Length > DeliveryFileValidator.MaxBytes)
            throw DeliveryRules.Invalid("file.too_large", "file", "Files must be 50 MB or smaller.");

        using var buffer = new MemoryStream((int)upload.Length);
        await using (var input = upload.OpenReadStream()) await input.CopyToAsync(buffer, ct);
        if (buffer.Length > DeliveryFileValidator.MaxBytes)
            throw DeliveryRules.Invalid("file.too_large", "file", "Files must be 50 MB or smaller.");
        var kind = DeliveryFileValidator.Identify(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))
                   ?? throw DeliveryRules.Invalid("file.unsupported_type", "file",
                       "Upload a PNG, JPEG, WebP, PDF or MP4 file. Share other formats (documents, hosted videos) as a link.");

        var bytes = kind.ContentType.StartsWith("image/", StringComparison.Ordinal)
            ? ImageMetadataStripper.Strip(buffer.ToArray())
            : buffer.ToArray();
        var now = clock.GetUtcNow().UtcDateTime;
        var key = storage.NewKey(now, kind.Extension);
        await storage.WriteAsync(key, bytes, ct);
        var file = new DeliveryFile
        {
            ClientAccountId = clientId,
            StorageKey = key,
            ContentType = kind.ContentType,
            SizeBytes = bytes.Length,
            Sha256 = Normalization.Sha256Hex(bytes),
            OriginalFileName = FileService.SanitizeFileName(upload.FileName, kind.Extension),
            UploadedByUserId = currentUser.Id,
            CreatedAt = now,
        };
        db.Set<DeliveryFile>().Add(file);
        return file;
    }

    public void Discard(DeliveryFile file)
    {
        try { storage.Delete(file.StorageKey); } catch (IOException) { }
    }

    /// <summary>Saves the staged file (and anything else staged), deleting the bytes again if the save fails.</summary>
    public async Task CommitAsync(DeliveryFile file, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            Discard(file);
            throw;
        }
    }

    /// <summary>Opens a file the caller may read (staff, or a member of the owning client); null otherwise.</summary>
    public async Task<(DeliveryFile File, Stream Content)?> OpenAsync(Guid fileId, Guid? expectedClientId, CancellationToken ct)
    {
        var file = await db.Set<DeliveryFile>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return null;
        if (expectedClientId is { } cid && file.ClientAccountId != cid) return null;
        if (!scope.IsStaff)
        {
            var mine = await scope.MemberClientIdsAsync(ct);
            if (!mine.Contains(file.ClientAccountId)) return null;
            // Membership alone is not enough: files of internal work (unsent versions, task attachments) stay staff-only.
            if (await IsInternalOnlyAsync(file, ct)) return null;
        }
        var stream = storage.OpenRead(file.StorageKey);
        return stream is null ? null : (file, stream);
    }

    /// <summary>
    /// Ids from <paramref name="ids"/> that are files of the client (attachments must belong to the same tenant). For a
    /// client user no file may be internal-only, so an internal file id can't be re-shared through a message.
    /// </summary>
    public async Task EnsureFilesOfClientAsync(Guid clientId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var distinct = ids.Distinct().ToList();
        var rows = await db.Set<DeliveryFile>().AsNoTracking().Where(f => distinct.Contains(f.Id) && f.ClientAccountId == clientId).ToListAsync(ct);
        var ok = rows.Count == distinct.Count;
        if (ok && !scope.IsStaff)
            foreach (var row in rows)
                if (await IsInternalOnlyAsync(row, ct)) { ok = false; break; }
        if (!ok)
            throw DeliveryRules.Invalid("file.invalid_attachment", "attachmentFileIds", "An attachment was not found. Upload it again.");
    }

    /// <summary>
    /// True when a client's file belongs to internal work only and must not reach the client's users: it is the file of a
    /// deliverable version that was never sent to the client, a task attachment (tasks show the client only their title
    /// and status) or an attachment in an internal (staff-only) thread, and it was not also shared with the client (a sent
    /// version, an attachment in a client-visible thread, a brand asset, the logo, or an upload by a member of the
    /// organization).
    /// </summary>
    public async Task<bool> IsInternalOnlyAsync(DeliveryFile file, CancellationToken ct)
    {
        var id = file.Id;
        var clientId = file.ClientAccountId;
        var versions = await (from v in db.Set<DeliverableVersion>().AsNoTracking()
                              join d in db.Set<Deliverable>().AsNoTracking() on v.DeliverableId equals d.Id
                              where v.FileId == id
                              select new { Sent = v.Number <= d.LastSentVersion }).ToListAsync(ct);
        if (versions.Any(v => v.Sent)) return false;
        var messages = await MessageAttachmentsAsync(clientId, ct);
        if (messages.Any(m => !m.IsInternal && m.FileIds.Contains(id))) return false;
        var internalUse = versions.Count > 0 || messages.Any(m => m.IsInternal && m.FileIds.Contains(id))
                          || await db.Set<TaskAttachment>().AnyAsync(a => a.FileId == id, ct);
        if (!internalUse) return false;
        if (await db.Set<ClientMember>().AnyAsync(m => m.ClientAccountId == clientId && m.UserId == file.UploadedByUserId, ct)) return false;
        if (await db.Set<ClientAccount>().AnyAsync(c => c.Id == clientId && c.LogoFileId == id, ct)) return false;
        return !await db.Set<BrandAsset>().AnyAsync(a => a.ClientAccountId == clientId && a.FileId == id, ct);
    }

    private sealed record MessageAttachments(bool IsInternal, List<Guid> FileIds);

    /// <summary>Attachment lists of the client's messages with their thread's visibility (a JSON list column, checked in memory).</summary>
    private async Task<List<MessageAttachments>> MessageAttachmentsAsync(Guid clientId, CancellationToken ct)
    {
        var rows = await (from m in db.Set<ThreadMessage>().AsNoTracking()
                          join t in db.Set<MessageThread>().AsNoTracking() on m.ThreadId equals t.Id
                          where m.ClientAccountId == clientId
                          select new { t.IsInternal, m.AttachmentFileIds }).ToListAsync(ct);
        return rows.Where(r => r.AttachmentFileIds.Count > 0).Select(r => new MessageAttachments(r.IsInternal, r.AttachmentFileIds)).ToList();
    }

    /// <summary>
    /// Serializes removals of a client's file references (brand assets, task attachments) with each other and with new
    /// task attachments, so a file is never purged while it is being attached elsewhere. Acquire before
    /// <c>BeginWriteTransactionAsync</c>.
    /// </summary>
    public Task<IAsyncDisposable> LockClientFilesAsync(Guid clientId, CancellationToken ct) =>
        dialect.AcquireNamedLockAsync(db, $"delivery-files:{clientId:N}", TimeSpan.FromSeconds(30), ct);

    /// <summary>
    /// After a reference to a file was removed (and saved) inside the caller's write transaction: when nothing else refers
    /// to the file any more (brand asset, task attachment, deliverable version, logo, message attachment), stages the
    /// deletion of its row and returns its storage key, so the caller deletes the bytes with <see cref="DeleteStoredBytes"/>
    /// once the transaction committed. Returns null when the file is still in use (it is kept).
    /// </summary>
    public async Task<string?> StageRemovalIfUnreferencedAsync(Guid fileId, CancellationToken ct)
    {
        var file = await db.Set<DeliveryFile>().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return null;
        if (await db.Set<BrandAsset>().AnyAsync(a => a.FileId == fileId, ct)) return null;
        if (await db.Set<TaskAttachment>().AnyAsync(a => a.FileId == fileId, ct)) return null;
        if (await db.Set<DeliverableVersion>().AnyAsync(v => v.FileId == fileId, ct)) return null;
        if (await db.Set<ClientAccount>().AnyAsync(c => c.LogoFileId == fileId, ct)) return null;
        if ((await MessageAttachmentsAsync(file.ClientAccountId, ct)).Any(m => m.FileIds.Contains(fileId))) return null;
        db.Remove(file);
        return file.StorageKey;
    }

    /// <summary>Deletes stored bytes after the removal of their row committed (best effort: a leftover blob is unreachable).</summary>
    public void DeleteStoredBytes(string? storageKey)
    {
        if (storageKey is null) return;
        try { storage.Delete(storageKey); } catch (IOException) { }
    }
}

internal static class DeliveryFileResults
{
    public static IActionResult Stream(ControllerBase controller, DeliveryFile file, Stream content)
    {
        var headers = controller.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        headers.CacheControl = "private, no-store";
        var inline = file.ContentType.StartsWith("image/", StringComparison.Ordinal) || file.ContentType is "application/pdf" or "video/mp4";
        headers.ContentDisposition = $"{(inline ? "inline" : "attachment")}; filename=\"{file.OriginalFileName.Replace("\"", string.Empty)}\"";
        return controller.File(content, file.ContentType, enableRangeProcessing: file.ContentType == "video/mp4");
    }
}

/// <summary>Staff access to client files (upload for a client, download). Another tenant's or unknown file: 404.</summary>
[ApiController]
[HasPermission(Permissions.ClientsView)]
public sealed class AgencyFilesController(DeliveryFileService files, IClientScope scope, IAuditLogger audit) : ControllerBase
{
    [HttpGet("api/v1/agency/files/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var opened = await files.OpenAsync(id, null, ct) ?? throw DomainException.NotFound("File");
        return DeliveryFileResults.Stream(this, opened.File, opened.Content);
    }

    /// <summary>Uploads a private file for a client (attachments, brand assets). PNG/JPEG/WebP/PDF/MP4, max 50 MB.</summary>
    [HttpPost("api/v1/agency/clients/{clientId:guid}/files")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    [RequestSizeLimit(DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<DeliveryFileDto>> Upload(Guid clientId, [FromForm] DeliveryUploadForm form, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var file = await files.SaveAsync(clientId, form.File, ct);
        audit.Record("delivery.file_uploaded", nameof(DeliveryFile), file.Id, after: new { file.ClientAccountId, file.ContentType, file.SizeBytes, file.Sha256 });
        await files.CommitAsync(file, ct);
        return StatusCode(StatusCodes.Status201Created, DeliveryFileDto.From(file));
    }
}

/// <summary>Client-portal access to the organization's files. Another tenant's file: 404.</summary>
[ApiController]
[HasPermission(Permissions.ClientPortal)]
public sealed class ClientFilesController(DeliveryFileService files, IClientScope scope, IAuditLogger audit) : ControllerBase
{
    [HttpGet("api/v1/client/orgs/{clientId:guid}/files/{id:guid}")]
    public async Task<IActionResult> Get(Guid clientId, Guid id, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var opened = await files.OpenAsync(id, clientId, ct) ?? throw DomainException.NotFound("File");
        return DeliveryFileResults.Stream(this, opened.File, opened.Content);
    }

    /// <summary>Uploads a file for a message or brief (any member of the organization).</summary>
    [HttpPost("api/v1/client/orgs/{clientId:guid}/files")]
    [RequestSizeLimit(DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<DeliveryFileDto>> Upload(Guid clientId, [FromForm] DeliveryUploadForm form, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var file = await files.SaveAsync(clientId, form.File, ct);
        audit.Record("delivery.file_uploaded_by_client", nameof(DeliveryFile), file.Id, after: new { file.ClientAccountId, file.ContentType, file.SizeBytes });
        await files.CommitAsync(file, ct);
        return StatusCode(StatusCodes.Status201Created, DeliveryFileDto.From(file));
    }
}

public sealed class DeliveryUploadForm
{
    public IFormFile? File { get; set; }
}
