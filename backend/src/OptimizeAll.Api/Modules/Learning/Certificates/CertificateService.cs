using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning.Certificates;

/// <summary>
/// Course certificates: issued when a learner passes the final exam (or manually by staff with learning.certify),
/// verifiable by anyone by id or verification code, revocable by staff (audited, the holder is notified). Holder name,
/// course title, badge and skills are snapshots taken at issue time.
/// </summary>
public sealed class CertificateService(
    AppDbContext db,
    IDatabaseDialect dialect,
    IAuditLogger audit,
    INotificationService notifications,
    LearningIssuerProvider issuers,
    CourseContentCache cache,
    TimeProvider clock)
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>"OA-XXXX-XXXX" from an unambiguous alphabet (no 0/O, 1/I).</summary>
    public static string NewVerificationCode()
    {
        Span<char> c = stackalloc char[8];
        for (var i = 0; i < c.Length; i++) c[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return $"OA-{new string(c[..4])}-{new string(c[4..])}";
    }

    public static string ActiveKey(Guid userId, Guid courseId) => $"{userId:N}:{courseId:N}";

    public Task<Guid?> ValidCertificateIdAsync(Guid userId, Guid courseId, CancellationToken ct)
    {
        var key = ActiveKey(userId, courseId);
        return db.Set<Certificate>().AsNoTracking().Where(c => c.ActiveKey == key).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<Dictionary<Guid, Guid>> ValidCertificatesByCourseAsync(Guid userId, IReadOnlyCollection<Guid> courseIds, CancellationToken ct) =>
        await db.Set<Certificate>().AsNoTracking()
            .Where(c => c.UserId == userId && c.RevokedAt == null && courseIds.Contains(c.CourseId))
            .ToDictionaryAsync(c => c.CourseId, c => c.Id, ct);

    /// <summary>
    /// Stages a certificate for the learner (saved by the caller's transaction) unless they already hold a valid one for the
    /// course, and stages the "certificate issued" notification.
    /// </summary>
    public async Task<Certificate?> IssueAsync(Guid userId, Guid courseId, Guid versionId, Guid? attemptId, int? score, Guid? issuedBy,
        DateTime now, CancellationToken ct)
    {
        if (await ValidCertificateIdAsync(userId, courseId, ct) is not null) return null;
        var doc = await cache.GetAsync(db, versionId, ct);
        var holder = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstAsync(ct);
        var pack = doc.Pack;
        var certificate = new Certificate
        {
            UserId = userId, CourseId = courseId, CourseVersionId = versionId, AttemptId = attemptId,
            VerificationCode = NewVerificationCode(), ActiveKey = ActiveKey(userId, courseId),
            HolderName = holder, CourseSlug = pack.Slug, CourseTitle = pack.Title, BadgeName = pack.Badge!.Name,
            Skills = (pack.Skills ?? new()).ToList(), Score = score, IssuedAt = now, IssuedByUserId = issuedBy,
            RecipientSalt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
        };
        db.Set<Certificate>().Add(certificate);
        await notifications.StageAsync(new NotificationRequest(
            userId, NotificationTypes.LearningCertificateIssued,
            $"You earned the {pack.Badge.Name} certificate",
            $"Congratulations on completing \"{pack.Title}\". Download your certificate, add it to your LinkedIn profile and share your verification link.",
            AppLinks.LearningCertificate(certificate.Id),
            new[] { NotificationChannel.InApp, NotificationChannel.Email }), ct);
        return certificate;
    }

    // ---------------------------------------------------------------- holder

    public async Task<IReadOnlyList<MyCertificateDto>> MineAsync(Guid userId, CancellationToken ct)
    {
        var issuer = await issuers.GetAsync(ct);
        var links = new LearningLinks(issuer.BaseUrl);
        var rows = await db.Set<Certificate>().AsNoTracking().Where(c => c.UserId == userId)
            .OrderByDescending(c => c.IssuedAt).ThenByDescending(c => c.Id).ToListAsync(ct);
        return rows.Select(c => Mine(c, links, issuer)).ToList();
    }

    public async Task<MyCertificateDto> GetMineAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var c = await db.Set<Certificate>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct)
                ?? throw DomainException.NotFound("Certificate");
        var issuer = await issuers.GetAsync(ct);
        return Mine(c, new LearningLinks(issuer.BaseUrl), issuer);
    }

    private static MyCertificateDto Mine(Certificate c, LearningLinks links, LearningIssuer issuer) => new(
        c.Id, c.VerificationCode, c.CourseSlug, c.CourseTitle, c.BadgeName, c.Skills, c.Score, c.IssuedAt, c.IsRevoked, c.RevokedAt,
        links.For(c, issuer));

    // ---------------------------------------------------------------- public verification

    public async Task<Certificate> FindAsync(Guid id, CancellationToken ct) =>
        await db.Set<Certificate>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Certificate");

    public async Task<CertificateVerificationDto> VerifyAsync(Guid id, CancellationToken ct) => await VerificationAsync(await FindAsync(id, ct), ct);

    public async Task<CertificateVerificationDto> VerifyByCodeAsync(string? code, CancellationToken ct)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is < 6 or > 20) throw DomainException.NotFound("Certificate");
        if (!normalized.StartsWith("OA-", StringComparison.Ordinal)) normalized = "OA-" + normalized;
        var c = await db.Set<Certificate>().AsNoTracking().FirstOrDefaultAsync(x => x.VerificationCode == normalized, ct)
                ?? throw DomainException.NotFound("Certificate");
        return await VerificationAsync(c, ct);
    }

    public async Task<CertificateVerificationDto> VerificationAsync(Certificate c, CancellationToken ct)
    {
        var issuer = await issuers.GetAsync(ct);
        var links = new LearningLinks(issuer.BaseUrl);
        var badge = await BadgeTextAsync(c, ct);
        var title = c.IsRevoked
            ? $"Revoked certificate {c.VerificationCode}"
            : $"{c.HolderName} — {c.BadgeName}";
        var description = c.IsRevoked
            ? $"This {c.CourseTitle} certificate issued by {issuer.Name} has been revoked and is no longer valid."
            : $"{c.HolderName} earned the {c.BadgeName} certificate from {issuer.Name} by completing \"{c.CourseTitle}\" and passing its final assessment. Verify it here.";
        return new CertificateVerificationDto(c.Id, c.VerificationCode, c.IsRevoked ? "revoked" : "valid", !c.IsRevoked, c.HolderName,
            c.CourseSlug, c.CourseTitle, c.BadgeName, badge?.Description, badge?.Criteria, c.Skills, c.IssuedAt, c.RevokedAt, issuer.Name,
            links.For(c, issuer),
            new LearningSeoDto(PublicLearningService.SeoTitle(title, $"{c.BadgeName} certificate", c.BadgeName),
                PublicLearningService.Truncate(description, PublicLearningService.SeoDescriptionMax),
                LearningLinks.VerifyPath(c.Id), links.BadgeImage(c.CourseSlug), c.IsRevoked),
            c.IsRevoked ? Array.Empty<System.Text.Json.JsonElement>() : new[] { LearningJsonLd.Credential(c, links, issuer) });
    }

    /// <summary>The badge description and criteria of the version the certificate was issued for.</summary>
    public async Task<PackBadge?> BadgeTextAsync(Certificate c, CancellationToken ct)
    {
        try
        {
            return (await cache.GetAsync(db, c.CourseVersionId, ct)).Pack.Badge;
        }
        catch (DomainException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- staff

    public async Task<AdminCertificateDto> IssueManuallyAsync(Guid staffId, IssueCertificateRequest request, CancellationToken ct)
    {
        if (!request.Confirm)
            throw FieldRules.FieldError("admin.confirmation_required", "confirm", "Confirm this change by sending \"confirm\": true.");
        var userId = request.UserId!.Value;
        var course = await db.Set<Course>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CourseId, ct) ?? throw DomainException.NotFound("Course");
        var versionId = course.PublishedVersionId ?? course.LatestVersionId ?? throw DomainException.NotFound("Course");
        if (!await db.Set<User>().AnyAsync(u => u.Id == userId, ct)) throw DomainException.NotFound("User");

        await using var _ = await dialect.AcquireNamedLockAsync(db, $"learning-exam:{userId:N}:{course.Id:N}", TimeSpan.FromSeconds(15), ct);
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var certificate = await IssueAsync(userId, course.Id, versionId, null, null, staffId, now, ct)
                          ?? throw DomainException.Conflict("learning.already_certified", "This person already holds a valid certificate for the course.");
        audit.Record("learning.certificate_issued", nameof(Certificate), certificate.Id, null,
            new { certificate.UserId, certificate.CourseId, certificate.VerificationCode, manual = true }, request.Reason.Trim());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await AdminViewAsync(certificate.Id, ct);
    }

    public async Task<AdminCertificateDto> RevokeAsync(Guid staffId, Guid id, RevokeCertificateRequest request, CancellationToken ct)
    {
        if (!request.Confirm)
            throw FieldRules.FieldError("admin.confirmation_required", "confirm", "Confirm this change by sending \"confirm\": true.");
        var certificate = await db.Set<Certificate>().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Certificate");
        if (certificate.IsRevoked)
            throw DomainException.Conflict("learning.certificate_revoked", "This certificate is already revoked.");
        ConcurrencyGuard.Apply(db, certificate, request.ConcurrencyStamp!.Value);
        var now = clock.GetUtcNow().UtcDateTime;
        certificate.RevokedAt = now;
        certificate.RevokedByUserId = staffId;
        certificate.RevocationReason = request.Reason.Trim();
        certificate.ActiveKey = null;
        audit.Record("learning.certificate_revoked", nameof(Certificate), certificate.Id,
            new { revoked = false }, new { revoked = true, certificate.VerificationCode, certificate.UserId, certificate.CourseId }, certificate.RevocationReason);
        await notifications.StageAsync(new NotificationRequest(
            certificate.UserId, NotificationTypes.LearningCertificateRevoked,
            $"Your {certificate.BadgeName} certificate was revoked",
            $"The certificate for \"{certificate.CourseTitle}\" ({certificate.VerificationCode}) is no longer valid. Contact support if you have questions.",
            AppLinks.LearningHome, new[] { NotificationChannel.InApp, NotificationChannel.Email }), ct);
        await db.SaveChangesAsync(ct);
        return await AdminViewAsync(id, ct);
    }

    public async Task<AdminCertificateDto> AdminViewAsync(Guid id, CancellationToken ct) =>
        await AdminQuery(db.Set<Certificate>().AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Certificate");

    public IQueryable<AdminCertificateDto> AdminQuery(IQueryable<Certificate>? source = null, string? search = null)
    {
        var q = from c in source ?? db.Set<Certificate>().AsNoTracking()
                join u in db.Set<User>().AsNoTracking() on c.UserId equals u.Id
                select new { c, u };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var p = OptimizeAll.Api.Common.Http.PagingExtensions.LikePattern(search.Trim());
            q = q.Where(x => EF.Functions.Like(x.c.HolderName, p, "\\") || EF.Functions.Like(x.u.Email, p, "\\") ||
                             EF.Functions.Like(x.c.VerificationCode, p, "\\") || EF.Functions.Like(x.c.CourseTitle, p, "\\"));
        }
        return q.OrderByDescending(x => x.c.IssuedAt).ThenByDescending(x => x.c.Id).Select(x => new AdminCertificateDto(x.c.Id, x.c.VerificationCode, x.c.UserId, x.c.HolderName, x.u.Email, x.c.CourseId, x.c.CourseTitle, x.c.Score,
            x.c.IssuedAt, x.c.IssuedByUserId != null, x.c.RevokedAt, x.c.RevocationReason, x.c.ConcurrencyStamp));
    }
}
