using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning;

/// <summary>
/// A participant's own learning: enrolments, lesson progress (resume where they left off), knowledge-check answers, the
/// "My learning" dashboard and their certificates. Every query is scoped to the caller's user id (other learners' rows
/// answer 404).
/// </summary>
public sealed class MyLearningService(
    AppDbContext db, PublicLearningService catalog, CourseContentCache cache, CertificateService certificates, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---------------------------------------------------------------- progress model

    public sealed record ProgressState(Enrolment Enrolment, IReadOnlySet<string> Completed, CourseDocument Doc, Guid? CertificateId);

    public static MyCourseProgressDto ToDto(ProgressState s)
    {
        var lessonSlugs = s.Doc.Lessons.Select(l => l.Lesson.Slug).ToList();
        var done = lessonSlugs.Where(s.Completed.Contains).ToList();
        var resume = s.Enrolment.LastLessonSlug is { } last && s.Doc.LessonsBySlug.ContainsKey(last) && !s.Completed.Contains(last)
            ? last
            : lessonSlugs.FirstOrDefault(l => !s.Completed.Contains(l)) ?? (s.Enrolment.LastLessonSlug is { } l2 && s.Doc.LessonsBySlug.ContainsKey(l2) ? l2 : lessonSlugs.FirstOrDefault());
        var percent = lessonSlugs.Count == 0 ? 0 : done.Count * 100 / lessonSlugs.Count;
        return new MyCourseProgressDto(s.Enrolment.Id, s.Enrolment.EnrolledAt, done, resume, done.Count, lessonSlugs.Count, percent,
            done.Count == lessonSlugs.Count && lessonSlugs.Count > 0, s.Enrolment.PassedAt is not null, s.Enrolment.BestScore,
            s.CertificateId, s.Enrolment.LastActivityAt);
    }

    private async Task<ProgressState?> StateAsync(Guid userId, Course course, CourseDocument doc, bool track, CancellationToken ct)
    {
        var q = db.Set<Enrolment>().Where(e => e.UserId == userId && e.CourseId == course.Id);
        var enrolment = track ? await q.FirstOrDefaultAsync(ct) : await q.AsNoTracking().FirstOrDefaultAsync(ct);
        if (enrolment is null) return null;
        var completed = await db.Set<LessonProgress>().AsNoTracking()
            .Where(p => p.EnrolmentId == enrolment.Id && p.CompletedAt != null).Select(p => p.LessonSlug).ToListAsync(ct);
        var certificateId = await certificates.ValidCertificateIdAsync(userId, course.Id, ct);
        return new ProgressState(enrolment, completed.ToHashSet(StringComparer.Ordinal), doc, certificateId);
    }

    // ---------------------------------------------------------------- catalog & course

    public async Task<PagedResult<MyCourseCardDto>> CatalogAsync(Guid userId, CatalogQuery query, CancellationToken ct)
    {
        var page = await catalog.CatalogAsync(query, ct);
        var ids = page.Items.Select(c => c.Id).ToList();
        var enrolments = await db.Set<Enrolment>().AsNoTracking().Where(e => e.UserId == userId && ids.Contains(e.CourseId)).ToListAsync(ct);
        var enrolmentIds = enrolments.Select(e => e.Id).ToList();
        var completedCounts = await db.Set<LessonProgress>().AsNoTracking()
            .Where(p => enrolmentIds.Contains(p.EnrolmentId) && p.CompletedAt != null)
            .GroupBy(p => p.EnrolmentId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var certs = await certificates.ValidCertificatesByCourseAsync(userId, ids, ct);
        return new PagedResult<MyCourseCardDto>(page.Items.Select(c =>
        {
            var e = enrolments.FirstOrDefault(x => x.CourseId == c.Id);
            var percent = e is null || c.LessonCount == 0 ? 0 : Math.Min(100, completedCounts.GetValueOrDefault(e.Id) * 100 / c.LessonCount);
            return new MyCourseCardDto(c, e is not null, percent, e?.PassedAt is not null, certs.TryGetValue(c.Id, out var cid) ? cid : null);
        }).ToList(), page.Total, page.Page, page.PageSize);
    }

    public async Task<MyCourseDto> CourseAsync(Guid userId, string slug, CancellationToken ct)
    {
        var (course, doc) = await catalog.LoadPublishedAsync(slug, ct);
        var detail = await catalog.DetailAsync(course, doc, ct);
        var state = await StateAsync(userId, course, doc, false, ct);
        return new MyCourseDto(detail, state is null ? null : ToDto(state));
    }

    /// <summary>Enrols the caller (idempotent: enrolling twice returns the existing enrolment). Free for everyone.</summary>
    public async Task<(MyCourseDto Course, bool Created)> EnrolAsync(Guid userId, string slug, CancellationToken ct)
    {
        var (course, _) = await catalog.LoadPublishedAsync(slug, ct);
        var created = false;
        if (!await db.Set<Enrolment>().AnyAsync(e => e.UserId == userId && e.CourseId == course.Id, ct))
        {
            var enrolment = new Enrolment { UserId = userId, CourseId = course.Id, EnrolledAt = Now, LastActivityAt = Now };
            db.Set<Enrolment>().Add(enrolment);
            try
            {
                await db.SaveChangesAsync(ct);
                created = true;
            }
            catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
            {
                // A concurrent enrol request won: the enrolment exists either way.
                db.Entry(enrolment).State = EntityState.Detached;
            }
        }
        return (await CourseAsync(userId, slug, ct), created);
    }

    public async Task<MyLessonDto> LessonAsync(Guid userId, string slug, string lessonSlug, CancellationToken ct)
    {
        var (course, doc) = await catalog.LoadPublishedAsync(slug, ct);
        var lesson = await catalog.LessonAsync(course, doc, lessonSlug, ct);
        var state = await StateAsync(userId, course, doc, false, ct);
        if (state is null) return new MyLessonDto(lesson, false, false, Array.Empty<KnowledgeCheckResultDto>(), null);
        var answers = await db.Set<KnowledgeCheckAnswer>().AsNoTracking()
            .Where(a => a.EnrolmentId == state.Enrolment.Id && a.LessonSlug == lessonSlug).OrderBy(a => a.QuestionIndex).ToListAsync(ct);
        var checks = doc.LessonsBySlug[lessonSlug].Lesson.KnowledgeCheck ?? new();
        return new MyLessonDto(lesson, true, state.Completed.Contains(lessonSlug),
            answers.Where(a => a.QuestionIndex < checks.Count)
                .Select(a => new KnowledgeCheckResultDto(a.QuestionIndex, a.Selected, a.IsCorrect, checks[a.QuestionIndex].Correct ?? new(),
                    checks[a.QuestionIndex].Explanation)).ToList(),
            ToDto(state));
    }

    private async Task<(Course Course, CourseDocument Doc, Enrolment Enrolment, LessonRef Lesson)> LoadForWriteAsync(
        Guid userId, string slug, string lessonSlug, CancellationToken ct)
    {
        var (course, doc) = await catalog.LoadPublishedAsync(slug, ct);
        if (!doc.LessonsBySlug.TryGetValue(lessonSlug, out var lesson)) throw DomainException.NotFound("Lesson");
        var enrolment = await db.Set<Enrolment>().FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == course.Id, ct)
                        ?? throw DomainException.Conflict("learning.not_enrolled", "Enrol in this free course to track your progress.");
        return (course, doc, enrolment, lesson);
    }

    /// <summary>Records that the learner opened a lesson (resume point). Completing is separate.</summary>
    public Task<MyCourseProgressDto> StartLessonAsync(Guid userId, string slug, string lessonSlug, CancellationToken ct) =>
        TrackAsync(userId, slug, lessonSlug, complete: false, ct);

    public Task<MyCourseProgressDto> CompleteLessonAsync(Guid userId, string slug, string lessonSlug, CancellationToken ct) =>
        TrackAsync(userId, slug, lessonSlug, complete: true, ct);

    private async Task<MyCourseProgressDto> TrackAsync(Guid userId, string slug, string lessonSlug, bool complete, CancellationToken ct)
    {
        var (course, doc, enrolment, _) = await LoadForWriteAsync(userId, slug, lessonSlug, ct);
        var now = Now;
        var progress = await db.Set<LessonProgress>().FirstOrDefaultAsync(p => p.EnrolmentId == enrolment.Id && p.LessonSlug == lessonSlug, ct);
        if (progress is null)
        {
            progress = new LessonProgress { EnrolmentId = enrolment.Id, LessonSlug = lessonSlug, StartedAt = now };
            db.Set<LessonProgress>().Add(progress);
        }
        if (complete) progress.CompletedAt ??= now;
        enrolment.LastLessonSlug = lessonSlug;
        enrolment.LastActivityAt = now;

        var completed = (await db.Set<LessonProgress>().AsNoTracking()
                .Where(p => p.EnrolmentId == enrolment.Id && p.CompletedAt != null).Select(p => p.LessonSlug).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        if (progress.CompletedAt is not null) completed.Add(lessonSlug);
        if (enrolment.LessonsCompletedAt is null && doc.Lessons.All(l => completed.Contains(l.Lesson.Slug))) enrolment.LessonsCompletedAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            // The same lesson was tracked concurrently (double click, two tabs): the other request recorded it.
            db.ChangeTracker.Clear();
            if (complete)
                await db.Set<LessonProgress>().Where(p => p.EnrolmentId == enrolment.Id && p.LessonSlug == lessonSlug && p.CompletedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.CompletedAt, now), ct);
        }
        db.ChangeTracker.Clear();
        var state = await StateAsync(userId, course, doc, false, ct);
        return ToDto(state!);
    }

    public async Task<KnowledgeCheckResultDto> AnswerCheckAsync(
        Guid userId, string slug, string lessonSlug, int index, KnowledgeCheckAnswerRequest request, CancellationToken ct)
    {
        var (_, _, enrolment, lesson) = await LoadForWriteAsync(userId, slug, lessonSlug, ct);
        var checks = lesson.Lesson.KnowledgeCheck ?? new();
        if (index < 0 || index >= checks.Count) throw DomainException.NotFound("Question");
        var check = checks[index];
        var selected = request.Selected!.Distinct().OrderBy(i => i).ToList();
        if (selected.Count == 0 || selected.Any(i => i < 0 || i >= (check.Options?.Count ?? 0)))
            throw FieldRules.FieldError("learning.invalid_answer", "selected", "Choose one or more of the listed options.");
        var correct = (check.Correct ?? new()).Distinct().OrderBy(i => i).ToList();
        var isCorrect = selected.SequenceEqual(correct);

        var row = await db.Set<KnowledgeCheckAnswer>()
            .FirstOrDefaultAsync(a => a.EnrolmentId == enrolment.Id && a.LessonSlug == lessonSlug && a.QuestionIndex == index, ct);
        if (row is null)
            db.Set<KnowledgeCheckAnswer>().Add(row = new KnowledgeCheckAnswer { EnrolmentId = enrolment.Id, LessonSlug = lessonSlug, QuestionIndex = index });
        row.Selected = selected;
        row.IsCorrect = isCorrect;
        row.AnsweredAt = Now;
        enrolment.LastActivityAt = Now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear(); // a concurrent answer to the same question was saved; this one is reported anyway
        }
        return new KnowledgeCheckResultDto(index, selected, isCorrect, correct, check.Explanation);
    }

    // ---------------------------------------------------------------- dashboard

    public async Task<MyLearningDashboardDto> DashboardAsync(Guid userId, CancellationToken ct)
    {
        var now = Now;
        var rows = await (from e in db.Set<Enrolment>().AsNoTracking()
                          join c in db.Set<Course>().AsNoTracking() on e.CourseId equals c.Id
                          where e.UserId == userId && c.Status == CourseStatus.Published && c.PublishedVersionId != null
                          select new { e, c }).ToListAsync(ct);
        var enrolmentIds = rows.Select(r => r.e.Id).ToList();
        var completed = (await db.Set<LessonProgress>().AsNoTracking()
                .Where(p => enrolmentIds.Contains(p.EnrolmentId) && p.CompletedAt != null)
                .Select(p => new { p.EnrolmentId, p.LessonSlug }).ToListAsync(ct))
            .GroupBy(p => p.EnrolmentId).ToDictionary(g => g.Key, g => g.Select(x => x.LessonSlug).ToHashSet(StringComparer.Ordinal));
        var certs = await certificates.ValidCertificatesByCourseAsync(userId, rows.Select(r => r.c.Id).ToList(), ct);

        var cards = new List<EnrolmentCardDto>();
        var minutes = 0;
        var lessonsDone = 0;
        foreach (var r in rows)
        {
            var doc = await cache.GetAsync(db, r.c.PublishedVersionId!.Value, ct);
            var state = new ProgressState(r.e, completed.GetValueOrDefault(r.e.Id) ?? new HashSet<string>(), doc, certs.TryGetValue(r.c.Id, out var cid) ? cid : null);
            var p = ToDto(state);
            lessonsDone += p.CompletedLessonCount;
            minutes += doc.Lessons.Where(l => state.Completed.Contains(l.Lesson.Slug)).Sum(l => l.Lesson.DurationMinutes);
            var next = p.ResumeLessonSlug is { } resume && doc.LessonsBySlug.TryGetValue(resume, out var nl) ? nl.Lesson : null;
            cards.Add(new EnrolmentCardDto(PublicLearningService.Card(r.c, now), p.CompletedLessonCount, p.LessonCount, p.ProgressPercent,
                next?.Slug, next?.Title, p.ExamUnlocked, p.Passed, p.BestScore, p.CertificateId, r.e.EnrolledAt, r.e.LastActivityAt));
        }
        var inProgress = cards.Where(c => !c.Passed).OrderByDescending(c => c.LastActivityAt ?? c.EnrolledAt).ToList();
        var done = cards.Where(c => c.Passed).OrderByDescending(c => c.LastActivityAt ?? c.EnrolledAt).ToList();

        var myCertificates = await certificates.MineAsync(userId, ct);
        var recommended = await RecommendAsync(rows.Select(r => r.c).ToList(), now, ct);
        return new MyLearningDashboardDto(
            new LearningStatsDto(cards.Count, inProgress.Count, done.Count, myCertificates.Count(c => !c.Revoked), lessonsDone, minutes),
            inProgress.FirstOrDefault(), inProgress, done, recommended, myCertificates);
    }

    /// <summary>
    /// Up to four published courses the learner is not enrolled in: same categories as their courses first, then the next
    /// level up, featured and curated order; beginners without enrolments get featured beginner courses.
    /// </summary>
    private async Task<IReadOnlyList<CourseCardDto>> RecommendAsync(IReadOnlyList<Course> enrolled, DateTime now, CancellationToken ct)
    {
        var enrolledIds = enrolled.Select(c => c.Id).ToHashSet();
        var candidates = await catalog.Published().ToListAsync(ct);
        var categories = enrolled.Select(c => c.Category).ToHashSet();
        var level = enrolled.Count == 0 ? CourseLevel.Beginner : enrolled.Max(c => c.Level);
        return candidates.Where(c => !enrolledIds.Contains(c.Id))
            .OrderByDescending(c => (categories.Contains(c.Category) ? 4 : 0) + (c.Level == level || c.Level == level + 1 ? 2 : 0) + (c.IsFeatured ? 1 : 0))
            .ThenBy(c => c.SortOrder).ThenBy(c => c.Title)
            .Take(4).Select(c => PublicLearningService.Card(c, now)).ToList();
    }
}
