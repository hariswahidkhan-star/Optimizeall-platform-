using System.Text;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning.Admin;

/// <summary>
/// Uploads for lesson videos through the Files module (<see cref="StoredFile"/> + private storage, served publicly at
/// <c>/api/v1/files/{id}</c> with purpose <see cref="FilePurpose.LearningMedia"/>): MP4 videos (max 50 MB — larger
/// ElevenLabs/HeyGen renders are linked by https URL instead), WebVTT captions (max 1 MB) and poster images (PNG, JPEG,
/// WebP via <see cref="IFileService.SaveImageAsync"/>, metadata stripped). Types are identified by magic bytes, never by the
/// client's file name or Content-Type.
/// </summary>
public sealed class LearningMediaService(AppDbContext db, IFileService files, IFileStorage storage, IAuditLogger audit, TimeProvider clock)
{
    public const long MaxVideoBytes = 50L * 1024 * 1024;
    public const long MaxCaptionBytes = 1024 * 1024;

    public async Task<StoredFileDto> UploadAsync(Guid staffId, LearningMediaForm form, CancellationToken ct)
    {
        var upload = form.File ?? throw new DomainException("file.required", "Choose a video, captions file or poster image to upload.");
        if (upload.Length == 0) throw new DomainException("file.empty", "The uploaded file is empty.");
        if (upload.Length > MaxVideoBytes) throw new DomainException("file.too_large", "Videos must be 50 MB or smaller; link larger videos by https URL.");

        var head = new byte[Math.Min(64, (int)upload.Length)];
        await using (var peek = upload.OpenReadStream())
            _ = await peek.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);

        StoredFile file;
        if (ImageInspector.Inspect(head) is not null || (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8))
        {
            file = await files.SaveImageAsync(upload, FilePurpose.LearningMedia, staffId, isPublic: true, ct);
        }
        else
        {
            var bytes = new byte[upload.Length];
            await using (var input = upload.OpenReadStream())
                await input.ReadExactlyAsync(bytes, ct);
            var (contentType, extension) = Identify(bytes) ??
                throw new DomainException("file.unsupported_type", "Upload an MP4 video, a WebVTT (.vtt) captions file or a PNG/JPEG/WebP poster.");
            if (extension == ".vtt" && bytes.Length > MaxCaptionBytes)
                throw new DomainException("file.too_large", "Captions files must be 1 MB or smaller.");
            var now = clock.GetUtcNow().UtcDateTime;
            var key = storage.NewKey(now, extension);
            await storage.WriteAsync(key, bytes, ct);
            file = new StoredFile
            {
                OwnerUserId = staffId, Purpose = FilePurpose.LearningMedia, StorageKey = key, ContentType = contentType, SizeBytes = bytes.Length,
                Sha256 = Normalization.Sha256Hex(bytes), OriginalFileName = FileService.SanitizeFileName(upload.FileName, extension),
                CreatedAt = now, IsPublic = true,
            };
            db.Set<StoredFile>().Add(file);
        }
        audit.Record("learning.media_uploaded", nameof(StoredFile), file.Id, null, new { file.ContentType, file.SizeBytes, file.Sha256 });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            files.Discard(file);
            throw;
        }
        return StoredFileDto.From(file);
    }

    /// <summary>MP4 (ISO BMFF "ftyp" with a known brand) or WebVTT (UTF-8 text starting with "WEBVTT").</summary>
    public static (string ContentType, string Extension)? Identify(byte[] bytes)
    {
        if (DeliveryFileValidator.Identify(bytes) is { ContentType: "video/mp4" }) return ("video/mp4", ".mp4");
        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        if (bytes.Length >= offset + 6 && Encoding.ASCII.GetString(bytes, offset, 6) == "WEBVTT" &&
            (bytes.Length == offset + 6 || bytes[offset + 6] is (byte)'\n' or (byte)'\r' or (byte)' ' or (byte)'\t'))
        {
            try
            {
                _ = new UTF8Encoding(false, true).GetString(bytes);
                return ("text/vtt", ".vtt");
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }
        return null;
    }
}
