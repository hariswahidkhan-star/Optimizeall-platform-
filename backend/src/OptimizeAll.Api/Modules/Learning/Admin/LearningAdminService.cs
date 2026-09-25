using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning.Admin;

/// <summary>
/// The Learning admin: course list with enrolment/completion/pass-rate statistics, versions and the authoring workflow
/// (validate → save a new immutable version → publish), per-question analytics, learner progress, certificates and CSV
/// export. Every change is audited; course writes use the course's concurrency stamp.
/// </summary>
public sealed class LearningAdminService(
    AppDbContext db,
    IDatabaseDialect dialect,
    CourseContentCache cache,
    CertificateService certificates,
    IAuditLogger audit,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---------------------------------------------------------------- list & stats

    private sealed record Stats(int Enrolments, int Completions, int Attempts, int Passed, double? Average, int Certificates);

    private async Task<Dictionary<Guid, Stats>> StatsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var enrolments = await db.Set<Enrolment>().AsNoTracking().Where(e => ids.Contains(e.CourseId))
            .GroupBy(e => e.CourseId).Select(g => new { g.Key, Count = g.Count(), Done = g.Count(e => e.PassedAt != null) })
            .ToDictionaryAsync(x => x.Key, ct);
        var attempts = await db.Set<ExamAttempt>().AsNoTracking().Where(a => ids.Contains(a.CourseId) && a.Status != ExamAttemptStatus.InProgress)
            .GroupBy(a => a.CourseId).Select(g => new { g.Key, Count = g.Count(), Passed = g.Count(a => a.Passed == true), Average = g.Average(a => (double?)a.Score) })
            .ToDictionaryAsync(x => x.Key, ct);
        var certs = await db.Set<Certificate>().AsNoTracking().Where(c => ids.Contains(c.CourseId) && c.RevokedAt == null)
            .GroupBy(c => c.CourseId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return ids.ToDictionary(id => id, id =>
        {
            var e = enrolments.GetValueOrDefault(id);
            var a = attempts.GetValueOrDefault(id);
            return new Stats(e?.Count ?? 0, e?.Done ?? 0, a?.Count ?? 0, a?.Passed ?? 0, a?.Average, certs.GetValueOrDefault(id));
        });
    }

    private async Task<List<AdminCourseRowDto>> RowsAsync(List<Course> courses, CancellationToken ct)
    {
        var ids = courses.Select(c => c.Id).ToList();
        var stats = await StatsAsync(ids, ct);
        var versions = await db.Set<CourseVersion>().AsNoTracking().Where(v => ids.Contains(v.CourseId))
            .Select(v => new { v.Id, v.CourseId, v.Number, v.Source }).ToListAsync(ct);
        return courses.Select(c =>
        {
            var s = stats[c.Id];
            var mine = versions.Where(v => v.CourseId == c.Id).ToList();
            var published = mine.FirstOrDefault(v => v.Id == c.PublishedVersionId);
            var latest = mine.FirstOrDefault(v => v.Id == c.LatestVersionId);
            var latestPack = mine.Where(v => v.Source == CourseSource.Pack).MaxBy(v => v.Number);
            var packUpdate = c.Origin == CourseSource.Pack && latestPack is not null && published is not null && latestPack.Number > published.Number;
            return new AdminCourseRowDto(c.Id, c.Slug, c.Title, c.Category, c.Level, c.Status, c.Origin, c.IsFeatured, c.SortOrder, c.LessonCount,
                published?.Number, latest?.Number, packUpdate, s.Enrolments, s.Completions,
                s.Enrolments == 0 ? 0 : s.Completions * 100 / s.Enrolments, s.Attempts, s.Attempts == 0 ? 0 : s.Passed * 100 / s.Attempts,
                s.Average is { } avg ? (int)Math.Round(avg) : null, s.Certificates, c.UpdatedAt, c.ConcurrencyStamp);
        }).ToList();
    }

    public async Task<PagedResult<AdminCourseRowDto>> CoursesAsync(AdminCourseQuery query, CancellationToken ct)
    {
        var q = db.Set<Course>().AsNoTracking();
        if (query.Category is { } category) q = q.Where(c => c.Category == category);
        if (query.Status is { } status) q = q.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search.Trim());
            q = q.Where(c => EF.Functions.Like(c.Title, p, "\\") || EF.Functions.Like(c.Slug, p, "\\"));
        }
        q = (query.Sort?.ToLowerInvariant()) switch
        {
            "title" => query.Desc ? q.OrderByDescending(c => c.Title).ThenByKey(c => c.Id) : q.OrderBy(c => c.Title).ThenByKey(c => c.Id),
            "updated" => query.Desc ? q.OrderByDescending(c => c.UpdatedAt).ThenByKey(c => c.Id) : q.OrderBy(c => c.UpdatedAt).ThenByKey(c => c.Id),
            _ => q.OrderBy(c => c.Category).ThenBy(c => c.SortOrder).ThenBy(c => c.Title).ThenByKey(c => c.Id),
        };
        var total = await q.CountAsync(ct);
        var page = await q.Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<AdminCourseRowDto>(await RowsAsync(page, ct), total, query.Page, query.PageSize);
    }

    private async Task<Course> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Set<Course>().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Course");

    public async Task<AdminCourseDetailDto> CourseAsync(Guid id, CancellationToken ct)
    {
        var course = await db.Set<Course>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Course");
        var row = (await RowsAsync(new List<Course> { course }, ct))[0];
        var versions = await (from v in db.Set<CourseVersion>().AsNoTracking()
                              where v.CourseId == id
                              join u in db.Set<User>().AsNoTracking() on v.CreatedByUserId equals u.Id into users
                              from u in users.DefaultIfEmpty()
                              orderby v.Number descending
                              select new CourseVersionDto(v.Id, v.Number, v.Source, v.PackVersion, v.BasedOnVersionId, v.Note, v.CreatedAt,
                                  u == null ? null : u.DisplayName, v.Id == course.PublishedVersionId, v.Id == course.LatestVersionId))
            .ToListAsync(ct);
        var packFile = course.Origin == CourseSource.Pack
            ? CoursePackLibrary.All.FirstOrDefault(f => f.Pack?.Slug == course.Slug)?.FileName
            : null;
        return new AdminCourseDetailDto(row, versions, packFile);
    }

    public async Task<CourseVersionDocumentDto> VersionAsync(Guid courseId, Guid versionId, CancellationToken ct)
    {
        var course = await db.Set<Course>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId, ct) ?? throw DomainException.NotFound("Course");
        var v = await db.Set<CourseVersion>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == versionId && x.CourseId == courseId, ct)
                ?? throw DomainException.NotFound("CourseVersion");
        using var json = JsonDocument.Parse(v.ContentJson);
        return new CourseVersionDocumentDto(v.Id, v.CourseId, v.Number, v.Source, v.Id == course.PublishedVersionId, json.RootElement.Clone());
    }

    // ---------------------------------------------------------------- authoring

    /// <summary>Parses and validates a document with the pack contract (Authoring mode: identical structure and MCQ rules).</summary>
    public static (CoursePack? Pack, ValidationReportDto Report) Check(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object)
            return (null, new ValidationReportDto(false, new[] { new PackIssue("$", "Send the course document as a JSON object.") }, 0, 0, 0, 0));
        var (pack, error) = CoursePack.Parse(document.GetRawText());
        if (pack is null)
            return (null, new ValidationReportDto(false, new[] { new PackIssue("$", error ?? "Invalid course document.") }, 0, 0, 0, 0));
        var issues = CoursePackValidator.Validate(pack, PackValidationMode.Authoring);
        return (issues.Count == 0 ? pack : null, new ValidationReportDto(issues.Count == 0, issues, pack.Modules?.Count ?? 0, pack.LessonCount,
            pack.FinalExam?.Pool?.Count ?? 0, pack.FinalExam?.QuestionCount ?? 0));
    }

    public ValidationReportDto Validate(CourseDocumentRequest request) => Check(request.Document!.Value).Report;

    private static CoursePack Require(JsonElement document)
    {
        var (pack, report) = Check(document);
        if (pack is not null) return pack;
        throw new DomainException("learning.invalid_course", "The course document does not match the course pack contract.", DomainErrorKind.Validation,
            report.Issues.Take(50).GroupBy(i => "document." + i.Path).ToDictionary(g => g.Key, g => g.Select(i => i.Message).ToArray()));
    }

    public async Task<AdminCourseDetailDto> CreateAsync(Guid staffId, CreateCourseRequest request, CancellationToken ct)
    {
        var pack = Require(request.Document!.Value);
        await using var _ = await dialect.AcquireNamedLockAsync(db, LearningCatalogSeeder.LockName, TimeSpan.FromSeconds(30), ct);
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        if (await db.Set<Course>().AnyAsync(c => c.Slug == pack.Slug, ct))
            throw DomainException.Conflict("learning.slug_taken", $"A course with the slug '{pack.Slug}' already exists.");
        var now = Now;
        var course = new Course { Slug = pack.Slug, Origin = CourseSource.Admin, Status = CourseStatus.Draft, SortOrder = 100 };
        CourseVersioning.ApplyListing(course, pack);
        db.Set<Course>().Add(course);
        var version = CourseVersioning.NewVersion(course, pack, 1, CourseSource.Admin, null, null, "Created in the Learning admin", staffId, now);
        db.Set<CourseVersion>().Add(version);
        course.LatestVersionId = version.Id;
        if (request.Publish) CourseVersioning.Publish(course, version, pack, now);
        audit.Record("learning.course_created", nameof(Course), course.Id, null, new { course.Slug, version = 1, published = request.Publish });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await CourseAsync(course.Id, ct);
    }

    /// <summary>
    /// Saves an edited document as a new immutable version (pack-origin courses included: the pack is never overwritten;
    /// a later pack update is stored beside it and staff choose what to publish). The slug cannot change.
    /// </summary>
    public async Task<AdminCourseDetailDto> SaveVersionAsync(Guid staffId, Guid courseId, SaveCourseVersionRequest request, CancellationToken ct)
    {
        var pack = Require(request.Document!.Value);
        var course = await LoadAsync(courseId, ct);
        ConcurrencyGuard.Apply(db, course, request.ConcurrencyStamp!.Value);
        if (pack.Slug != course.Slug)
            throw FieldRules.FieldError("learning.slug_immutable", "document.slug", $"The slug of this course is '{course.Slug}' and cannot change.");
        if (request.BasedOnVersionId is { } basedOn && !await db.Set<CourseVersion>().AnyAsync(v => v.Id == basedOn && v.CourseId == courseId, ct))
            throw FieldRules.FieldError("learning.invalid_version", "basedOnVersionId", "That version does not belong to this course.");
        return await AddVersionAsync(staffId, course, pack, request.BasedOnVersionId ?? course.LatestVersionId, request.Note, request.Publish, ct);
    }

    private async Task<AdminCourseDetailDto> AddVersionAsync(Guid staffId, Course course, CoursePack pack, Guid? basedOn, string? note, bool publish,
        CancellationToken ct)
    {
        var now = Now;
        await using var _ = await dialect.AcquireNamedLockAsync(db, LearningCatalogSeeder.LockName, TimeSpan.FromSeconds(30), ct);
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        var number = await CourseVersioning.NextNumberAsync(db, course.Id, ct);
        var version = CourseVersioning.NewVersion(course, pack, number, CourseSource.Admin, null, basedOn, note?.Trim(), staffId, now);
        db.Set<CourseVersion>().Add(version);
        course.LatestVersionId = version.Id;
        if (publish) CourseVersioning.Publish(course, version, pack, now);
        else if (course.PublishedVersionId is null) CourseVersioning.ApplyListing(course, pack);
        audit.Record("learning.course_version_saved", nameof(Course), course.Id, null, new { version = number, basedOn, published = publish }, note);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await CourseAsync(course.Id, ct);
    }

    public async Task<AdminCourseDetailDto> PublishAsync(Guid courseId, PublishCourseRequest request, CancellationToken ct)
    {
        var course = await LoadAsync(courseId, ct);
        ConcurrencyGuard.Apply(db, course, request.ConcurrencyStamp!.Value);
        var version = await db.Set<CourseVersion>().FirstOrDefaultAsync(v => v.Id == request.VersionId && v.CourseId == courseId, ct)
                      ?? throw FieldRules.FieldError("learning.invalid_version", "versionId", "That version does not belong to this course.");
        var doc = await cache.GetAsync(db, version.Id, ct);
        var before = new { course.Status, course.PublishedVersionId };
        CourseVersioning.Publish(course, version, doc.Pack, Now);
        audit.Record("learning.course_published", nameof(Course), course.Id, before, new { course.Status, course.PublishedVersionId, version.Number });
        await db.SaveChangesAsync(ct);
        return await CourseAsync(courseId, ct);
    }

    public async Task<AdminCourseDetailDto> UnpublishAsync(Guid courseId, UnpublishCourseRequest request, CancellationToken ct)
    {
        var course = await LoadAsync(courseId, ct);
        ConcurrencyGuard.Apply(db, course, request.ConcurrencyStamp!.Value);
        if (course.Status == CourseStatus.Published)
        {
            course.Status = CourseStatus.Draft;
            audit.Record("learning.course_unpublished", nameof(Course), course.Id, new { status = CourseStatus.Published }, new { status = CourseStatus.Draft });
            await db.SaveChangesAsync(ct);
        }
        return await CourseAsync(courseId, ct);
    }

    public async Task<AdminCourseDetailDto> SettingsAsync(Guid courseId, CourseSettingsRequest request, CancellationToken ct)
    {
        var course = await LoadAsync(courseId, ct);
        ConcurrencyGuard.Apply(db, course, request.ConcurrencyStamp!.Value);
        var before = new { course.IsFeatured, course.SortOrder };
        course.IsFeatured = request.IsFeatured;
        course.SortOrder = request.SortOrder;
        audit.Record("learning.course_settings_changed", nameof(Course), course.Id, before, new { course.IsFeatured, course.SortOrder });
        await db.SaveChangesAsync(ct);
        return await CourseAsync(courseId, ct);
    }

    /// <summary>Sets a video lesson's source, poster and captions as a new version of the latest content (optionally published).</summary>
    public async Task<AdminCourseDetailDto> SetLessonVideoAsync(Guid staffId, Guid courseId, string lessonSlug, LessonVideoRequest request, CancellationToken ct)
    {
        var course = await LoadAsync(courseId, ct);
        ConcurrencyGuard.Apply(db, course, request.ConcurrencyStamp!.Value);
        var baseVersion = course.LatestVersionId ?? throw DomainException.NotFound("CourseVersion");
        var pack = (await cache.GetAsync(db, baseVersion, ct)).Pack.Clone();
        var lesson = pack.AllLessons.Select(x => x.Lesson).FirstOrDefault(l => l.Slug == lessonSlug) ?? throw DomainException.NotFound("Lesson");
        if (lesson.TypeValue != LessonType.Video || lesson.Video is null)
            throw DomainException.Conflict("learning.not_a_video_lesson", "Only video lessons have a video; change the lesson type in the course editor first.");
        foreach (var (field, value) in new[] { ("src", request.Src), ("poster", request.Poster), ("captions", request.Captions) })
            if (!string.IsNullOrWhiteSpace(value) && !CoursePackValidator.IsMediaUrl(value.Trim()))
                throw FieldRules.FieldError("learning.invalid_media_url", field, "Use an uploaded file (/api/v1/files/{id}) or an https URL.");
        lesson.Video.Src = Blank(request.Src);
        lesson.Video.Poster = Blank(request.Poster);
        lesson.Video.Captions = Blank(request.Captions);
        var issues = CoursePackValidator.Validate(pack, PackValidationMode.Authoring);
        if (issues.Count > 0)
            throw new DomainException("learning.invalid_course", "The change makes the course invalid: " + string.Join("; ", issues.Take(3)));
        return await AddVersionAsync(staffId, course, pack, baseVersion, $"Video for lesson '{lessonSlug}'", request.Publish, ct);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ---------------------------------------------------------------- analytics

    /// <summary>
    /// Per-question statistics over graded attempts: % correct (difficulty), % correct among attempts that passed vs. the
    /// others, and discrimination = the difference in points (a good question is answered correctly more often by passers).
    /// </summary>
    public async Task<IReadOnlyList<QuestionStatDto>> QuestionsAsync(Guid courseId, CancellationToken ct)
    {
        var course = await db.Set<Course>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId, ct) ?? throw DomainException.NotFound("Course");
        var rows = await db.Set<ExamAnswer>().AsNoTracking().Where(a => a.CourseId == courseId)
            .GroupBy(a => new { a.QuestionId, a.AttemptPassed })
            .Select(g => new { g.Key.QuestionId, g.Key.AttemptPassed, Count = g.Count(), Correct = g.Count(a => a.IsCorrect) })
            .ToListAsync(ct);
        var versionId = course.PublishedVersionId ?? course.LatestVersionId;
        var pool = versionId is { } v ? (await cache.GetAsync(db, v, ct)).Pack.FinalExam?.Pool ?? new() : new List<PackQuestion>();
        static int? Pct(int correct, int count) => count == 0 ? null : correct * 100 / count;
        var known = pool.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        var ids = pool.Select(q => q.Id).Concat(rows.Select(r => r.QuestionId).Where(id => !known.Contains(id)).Distinct());
        return ids.Select(id =>
        {
            var q = pool.FirstOrDefault(x => x.Id == id);
            var passers = rows.FirstOrDefault(r => r.QuestionId == id && r.AttemptPassed);
            var others = rows.FirstOrDefault(r => r.QuestionId == id && !r.AttemptPassed);
            var count = (passers?.Count ?? 0) + (others?.Count ?? 0);
            var correct = (passers?.Correct ?? 0) + (others?.Correct ?? 0);
            var pPass = Pct(passers?.Correct ?? 0, passers?.Count ?? 0);
            var pOther = Pct(others?.Correct ?? 0, others?.Count ?? 0);
            return new QuestionStatDto(id, q?.Module ?? "(retired)", q?.DifficultyValue ?? QuestionDifficulty.Medium, q?.TypeValue ?? QuestionType.Single,
                q?.Question ?? "(no longer in the pool)", count, correct, Pct(correct, count), pPass, pOther,
                pPass is { } a && pOther is { } b ? a - b : null);
        }).ToList();
    }

    private sealed record LearnerRow(Enrolment E, User U);

    public async Task<PagedResult<LearnerRowDto>> LearnersAsync(Guid courseId, LearnerQuery query, CancellationToken ct)
    {
        var course = await db.Set<Course>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId, ct) ?? throw DomainException.NotFound("Course");
        var q = from e in db.Set<Enrolment>().AsNoTracking()
                join u in db.Set<User>().AsNoTracking() on e.UserId equals u.Id
                where e.CourseId == courseId
                select new { e, u };
        if (query.Passed is { } passed) q = passed ? q.Where(x => x.e.PassedAt != null) : q.Where(x => x.e.PassedAt == null);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search.Trim());
            q = q.Where(x => EF.Functions.Like(x.u.Email, p, "\\") || EF.Functions.Like(x.u.DisplayName, p, "\\"));
        }
        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(x => x.e.LastActivityAt).ThenByDescending(x => x.e.Id)
            .Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<LearnerRowDto>(await LearnerRowsAsync(course, page.Select(x => new LearnerRow(x.e, x.u)).ToList(), ct), total, query.Page, query.PageSize);
    }

    private async Task<List<LearnerRowDto>> LearnerRowsAsync(Course course, List<LearnerRow> rows, CancellationToken ct)
    {
        var enrolmentIds = rows.Select(r => r.E.Id).ToList();
        var userIds = rows.Select(r => r.U.Id).ToList();
        var lessonSlugs = course.PublishedVersionId is { } v
            ? (await cache.GetAsync(db, v, ct)).Lessons.Select(l => l.Lesson.Slug).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>();
        var done = (await db.Set<LessonProgress>().AsNoTracking().Where(p => enrolmentIds.Contains(p.EnrolmentId) && p.CompletedAt != null)
                .Select(p => new { p.EnrolmentId, p.LessonSlug }).ToListAsync(ct))
            .Where(p => lessonSlugs.Contains(p.LessonSlug)).GroupBy(p => p.EnrolmentId).ToDictionary(g => g.Key, g => g.Count());
        var attempts = await db.Set<ExamAttempt>().AsNoTracking().Where(a => a.CourseId == course.Id && userIds.Contains(a.UserId))
            .GroupBy(a => a.UserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var certs = await db.Set<Certificate>().AsNoTracking().Where(c => c.CourseId == course.Id && userIds.Contains(c.UserId))
            .OrderByDescending(c => c.IssuedAt).Select(c => new { c.UserId, c.Id, c.RevokedAt }).ToListAsync(ct);
        return rows.Select(r =>
        {
            var completed = done.GetValueOrDefault(r.E.Id);
            var cert = certs.FirstOrDefault(c => c.UserId == r.U.Id);
            return new LearnerRowDto(r.U.Id, r.U.DisplayName, r.U.Email, r.E.EnrolledAt, completed, lessonSlugs.Count,
                lessonSlugs.Count == 0 ? 0 : completed * 100 / lessonSlugs.Count, attempts.GetValueOrDefault(r.U.Id), r.E.BestScore,
                r.E.PassedAt is not null, cert?.Id, cert?.RevokedAt is not null, r.E.LastActivityAt);
        }).ToList();
    }

    public async Task<Microsoft.AspNetCore.Mvc.FileContentResult> ExportResultsAsync(Guid courseId, CancellationToken ct)
    {
        var course = await db.Set<Course>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId, ct) ?? throw DomainException.NotFound("Course");
        var all = await (from e in db.Set<Enrolment>().AsNoTracking()
                         join u in db.Set<User>().AsNoTracking() on e.UserId equals u.Id
                         where e.CourseId == courseId
                         orderby e.EnrolledAt, e.Id
                         select new LearnerRow(e, u)).Take(50_000).ToListAsync(ct);
        var rows = await LearnerRowsAsync(course, all, ct);
        var codes = await db.Set<Certificate>().AsNoTracking().Where(c => c.CourseId == courseId)
            .ToDictionaryAsync(c => c.Id, c => c.VerificationCode, ct);
        audit.Record("learning.results_exported", nameof(Course), courseId, null, new { rows = rows.Count });
        await db.SaveChangesAsync(ct);
        return Csv.File($"learning-results-{course.Slug}-{Now:yyyyMMdd}.csv",
            new[] { "email", "name", "enrolled_at", "lessons_completed", "lessons_total", "progress_percent", "attempts", "best_score", "passed",
                "certificate_code", "certificate_status", "last_activity_at" },
            rows.Select(r => new object?[]
            {
                r.Email, r.DisplayName, r.EnrolledAt.ToString("u"), r.CompletedLessons, r.LessonCount, r.ProgressPercent, r.Attempts, r.BestScore,
                r.Passed ? "yes" : "no", r.CertificateId is { } id ? codes.GetValueOrDefault(id) : null,
                r.CertificateId is null ? null : r.CertificateRevoked ? "revoked" : "valid", r.LastActivityAt?.ToString("u"),
            }));
    }

    public async Task<UserLearningDto> UserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw DomainException.NotFound("User");
        var rows = await (from e in db.Set<Enrolment>().AsNoTracking()
                          join c in db.Set<Course>().AsNoTracking() on e.CourseId equals c.Id
                          where e.UserId == userId
                          orderby e.EnrolledAt descending, e.Id
                          select new { e, c }).ToListAsync(ct);
        var enrolments = new List<UserEnrolmentDto>();
        foreach (var r in rows)
        {
            var learner = (await LearnerRowsAsync(r.c, new List<LearnerRow> { new(r.e, user) }, ct))[0];
            enrolments.Add(new UserEnrolmentDto(r.c.Id, r.c.Slug, r.c.Title, r.e.EnrolledAt, learner.CompletedLessons, learner.LessonCount,
                learner.ProgressPercent, learner.Attempts, r.e.BestScore, r.e.PassedAt is not null, r.e.LastActivityAt));
        }
        var certs = await certificates.AdminQuery(db.Set<Certificate>().AsNoTracking().Where(c => c.UserId == userId)).ToListAsync(ct);
        return new UserLearningDto(user.Id, user.DisplayName, user.Email, enrolments, certs);
    }

    public async Task<PagedResult<AdminCertificateDto>> CertificatesAsync(AdminCertificateQuery query, CancellationToken ct)
    {
        var source = db.Set<Certificate>().AsNoTracking();
        if (query.CourseId is { } courseId) source = source.Where(c => c.CourseId == courseId);
        if (query.Revoked is { } revoked) source = revoked ? source.Where(c => c.RevokedAt != null) : source.Where(c => c.RevokedAt == null);
        return await certificates.AdminQuery(source, query.Search).ToPagedAsync(query, ct);
    }
}
