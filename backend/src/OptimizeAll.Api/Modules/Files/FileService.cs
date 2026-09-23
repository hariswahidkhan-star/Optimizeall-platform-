using System.Text;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Files;

public sealed record StoredFileDto(Guid Id, string Url, string ContentType, long SizeBytes, int? Width, int? Height,
    string Sha256, string OriginalFileName, string Purpose, bool IsPublic, DateTime CreatedAt)
{
    public static StoredFileDto From(StoredFile f) => new(f.Id, FileUrls.For(f.Id), f.ContentType, f.SizeBytes, f.Width, f.Height,
        f.Sha256, f.OriginalFileName, f.Purpose.ToString(), f.IsPublic, f.CreatedAt);
}

public static class FileUrls
{
    public static string For(Guid fileId) => $"/api/v1/files/{fileId}";
}

public sealed record FileContent(StoredFile File, Stream Content);

public interface IFileService
{
    /// <summary>
    /// Validates an uploaded image (magic bytes, dimensions, size), writes it to private storage and stages its
    /// <see cref="StoredFile"/> row in the DbContext (saved by the caller). Call <see cref="Discard"/> if the caller's
    /// transaction fails so the bytes are not orphaned.
    /// </summary>
    Task<StoredFile> SaveImageAsync(IFormFile upload, FilePurpose purpose, Guid ownerUserId, bool isPublic, CancellationToken ct);

    void Discard(StoredFile file);

    /// <summary>Opens a file the caller may read, or null (not found / not allowed — indistinguishable on purpose).</summary>
    Task<FileContent?> OpenForReadAsync(Guid fileId, CancellationToken ct);
}

public sealed class FileService(AppDbContext db, IFileStorage storage, ICurrentUser currentUser, TimeProvider clock) : IFileService
{
    public const long MaxBytes = 10 * 1024 * 1024;
    public const int MinDimension = 200;
    public const int MaxDimension = 10000;

    public async Task<StoredFile> SaveImageAsync(IFormFile upload, FilePurpose purpose, Guid ownerUserId, bool isPublic, CancellationToken ct)
    {
        if (upload.Length == 0)
            throw new DomainException("file.empty", "The uploaded file is empty.");
        if (upload.Length > MaxBytes)
            throw new DomainException("file.too_large", "Images must be 10 MB or smaller.");

        using var buffer = new MemoryStream((int)upload.Length);
        await using (var input = upload.OpenReadStream())
        {
            await input.CopyToAsync(buffer, ct);
        }
        if (buffer.Length > MaxBytes)
            throw new DomainException("file.too_large", "Images must be 10 MB or smaller.");
        var bytes = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);

        var info = ImageInspector.Inspect(bytes.Span)
            ?? throw new DomainException("file.unsupported_type", "Upload a PNG, JPEG or WebP image.");
        if (info.Width < MinDimension || info.Height < MinDimension)
            throw new DomainException("file.too_small", $"Images must be at least {MinDimension}×{MinDimension} pixels.");
        if (info.Width > MaxDimension || info.Height > MaxDimension)
            throw new DomainException("file.too_large_dimensions", $"Images must be at most {MaxDimension}×{MaxDimension} pixels.");

        var now = clock.GetUtcNow().UtcDateTime;
        var key = storage.NewKey(now, info.Extension);
        await storage.WriteAsync(key, bytes, ct);

        var file = new StoredFile
        {
            OwnerUserId = ownerUserId,
            Purpose = purpose,
            StorageKey = key,
            ContentType = info.ContentType,
            SizeBytes = bytes.Length,
            Sha256 = Normalization.Sha256Hex(bytes.Span),
            OriginalFileName = SanitizeFileName(upload.FileName, info.Extension),
            Width = info.Width,
            Height = info.Height,
            CreatedAt = now,
            IsPublic = isPublic,
        };
        db.Set<StoredFile>().Add(file);
        return file;
    }

    public void Discard(StoredFile file)
    {
        try { storage.Delete(file.StorageKey); } catch (IOException) { }
    }

    public async Task<FileContent?> OpenForReadAsync(Guid fileId, CancellationToken ct)
    {
        var file = await db.Set<StoredFile>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null || !CanRead(file)) return null;
        var stream = storage.OpenRead(file.StorageKey);
        return stream is null ? null : new FileContent(file, stream);
    }

    private bool CanRead(StoredFile file)
    {
        if (file.IsPublic && file.Purpose is FilePurpose.CampaignAsset or FilePurpose.ContentImage) return true;
        if (!currentUser.IsAuthenticated) return false;
        if (currentUser.Id == file.OwnerUserId) return true;
        return file.Purpose switch
        {
            FilePurpose.SubmissionScreenshot =>
                currentUser.HasPermission(Permissions.SubmissionsReview) || currentUser.HasPermission(Permissions.CampaignsManage),
            _ => currentUser.HasPermission(Permissions.CampaignsManage) || currentUser.HasPermission(Permissions.ContentManage),
        };
    }

    /// <summary>Keeps a display-safe file name: no path, no control or reserved characters, bounded length, real extension.</summary>
    public static string SanitizeFileName(string? name, string extension)
    {
        var baseName = Path.GetFileNameWithoutExtension(Path.GetFileName((name ?? string.Empty).Replace('\\', '/')));
        var sb = new StringBuilder();
        foreach (var ch in baseName)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ' or '.') sb.Append(ch);
            else sb.Append('_');
        }
        var cleaned = sb.ToString().Trim(' ', '.');
        if (cleaned.Length == 0) cleaned = "image";
        if (cleaned.Length > 100) cleaned = cleaned[..100];
        return cleaned + extension;
    }
}
