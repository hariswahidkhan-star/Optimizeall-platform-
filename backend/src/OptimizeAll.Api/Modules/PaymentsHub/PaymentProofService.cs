using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.PaymentsHub;

/// <summary>
/// Proof-of-payment files (bank slips, receipts) in the platform's private file storage (<see cref="IFileStorage"/>).
/// Identified by magic bytes like every upload: PNG, JPEG, WebP (metadata stripped) or PDF, at most 10 MB. Staff read
/// every proof of the clients they may see; client users only the proofs of their own organization's claims.
/// </summary>
public sealed class PaymentProofService(AppDbContext db, IFileStorage storage, ICurrentUser currentUser, IAuditLogger audit, TimeProvider clock)
{
    public const long MaxBytes = 10L * 1024 * 1024;

    public static PaymentProofDto ToDto(PaymentProof f) =>
        new(f.Id, f.OriginalFileName, f.ContentType, f.SizeBytes, f.CreatedAt, $"/api/v1/admin/payments/proofs/{f.Id}");

    /// <summary>Validates, stores and saves a proof for a payment or a claim (exactly one of them).</summary>
    public async Task<PaymentProofDto> SaveAsync(IFormFile? upload, Guid clientAccountId, Guid invoiceId, Guid? paymentId, Guid? claimId, CancellationToken ct)
    {
        if (upload is null || upload.Length == 0)
            throw new DomainException("file.empty", "Choose a file to upload.");
        if (upload.Length > MaxBytes)
            throw new DomainException("file.too_large", "Proof files must be 10 MB or smaller.");
        using var buffer = new MemoryStream((int)upload.Length);
        await using (var input = upload.OpenReadStream()) await input.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxBytes)
            throw new DomainException("file.too_large", "Proof files must be 10 MB or smaller.");
        var kind = DeliveryFileValidator.Identify(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        if (kind is null || kind.ContentType == "video/mp4")
            throw new DomainException("file.unsupported_type", "Upload a PDF, PNG, JPEG or WebP file.");
        var bytes = kind.ContentType.StartsWith("image/", StringComparison.Ordinal) ? ImageMetadataStripper.Strip(buffer.ToArray()) : buffer.ToArray();

        var now = clock.GetUtcNow().UtcDateTime;
        var key = storage.NewKey(now, kind.Extension);
        await storage.WriteAsync(key, bytes, ct);
        var proof = new PaymentProof
        {
            ClientAccountId = clientAccountId,
            InvoiceId = invoiceId,
            PaymentId = paymentId,
            PaymentClaimId = claimId,
            StorageKey = key,
            ContentType = kind.ContentType,
            SizeBytes = bytes.Length,
            Sha256 = Normalization.Sha256Hex(bytes),
            OriginalFileName = FileService.SanitizeFileName(upload.FileName, kind.Extension),
            UploadedByUserId = currentUser.Id,
            CreatedAt = now,
        };
        db.Set<PaymentProof>().Add(proof);
        audit.Record("payments.proof_uploaded", paymentId is not null ? nameof(Payment) : nameof(PaymentClaim), (paymentId ?? claimId)!.Value,
            after: new { ProofId = proof.Id, proof.ContentType, proof.SizeBytes, proof.Sha256, InvoiceId = invoiceId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            try { storage.Delete(key); } catch (IOException) { }
            throw;
        }
        return ToDto(proof);
    }

    public IActionResult Stream(ControllerBase controller, PaymentProof proof)
    {
        var stream = storage.OpenRead(proof.StorageKey) ?? throw DomainException.NotFound("File");
        var headers = controller.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        headers.CacheControl = "private, no-store";
        headers.ContentDisposition = $"inline; filename=\"{proof.OriginalFileName.Replace("\"", string.Empty)}\"";
        return controller.File(stream, proof.ContentType, enableRangeProcessing: false);
    }

    public Task<PaymentProof?> FindAsync(Guid id, CancellationToken ct) =>
        db.Set<PaymentProof>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
}
