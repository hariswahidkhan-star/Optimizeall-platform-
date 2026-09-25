using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning;

/// <summary>
/// Final exams with server-side integrity: questions are drawn and shuffled on the server when an attempt starts, the
/// deadline is stored with the attempt and enforced on every write (plus <see cref="ExamEngine.SubmitGrace"/>), answers
/// are saved and graded on the server against the attempt's own course version, correct answers and explanations are only
/// returned once the attempt is finished, one attempt can be in progress per course (unique <c>ActiveKey</c>) and the
/// number of attempts per rolling 24 hours is capped by the course. Passing issues the certificate in the same transaction.
/// </summary>
public sealed class ExamService(
    AppDbContext db,
    IDatabaseDialect dialect,
    PublicLearningService catalog,
    CourseContentCache cache,
    CertificateService certificates,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public static string ActiveKey(Guid userId, Guid courseId) => $"{userId:N}:{courseId:N}";

    public static List<DrawnQuestion> ReadDraw(ExamAttempt a) => JsonSerializer.Deserialize<List<DrawnQuestion>>(a.DrawJson, Json) ?? new();

    public static Dictionary<string, int[]> ReadAnswers(ExamAttempt a) =>
        JsonSerializer.Deserialize<Dictionary<string, int[]>>(a.AnswersJson, Json) ?? new(StringComparer.Ordinal);

    // ---------------------------------------------------------------- overview

    public async Task<ExamOverviewDto> OverviewAsync(Guid userId, string slug, CancellationToken ct)
    {
        var (course, doc) = await catalog.LoadPublishedAsync(slug, ct);
        var pack = doc.Pack;
        var rules = PublicLearningService.ExamInfo(pack);
        var enrolment = await db.Set<Enrolment>().AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == course.Id, ct);
        var completed = enrolment is null
            ? new HashSet<string>()
            : (await db.Set<LessonProgress>().AsNoTracking().Where(p => p.EnrolmentId == enrolment.Id && p.CompletedAt != null)
                .Select(p => p.LessonSlug).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var lessonsComplete = doc.Lessons.All(l => completed.Contains(l.Lesson.Slug));
        var attempts = await db.Set<ExamAttempt>().AsNoTracking().Where(a => a.UserId == userId && a.CourseId == course.Id)
            .OrderByDescending(a => a.StartedAt).ThenByDescending(a => a.Id).Take(50).ToListAsync(ct);
        var now = Now;
        var (remaining, next) = ExamEngine.AttemptBudget(attempts.Select(a => a.StartedAt), now, rules.MaxAttemptsPerDay);
        var active = attempts.FirstOrDefault(a => a.Status == ExamAttemptStatus.InProgress && ExamEngine.AcceptsAnswers(now, a.DeadlineAt));
        var certificateId = await certificates.ValidCertificateIdAsync(userId, course.Id, ct);

        string? blocked = enrolment is null ? "learning.not_enrolled"
            : !lessonsComplete ? "learning.lessons_incomplete"
            : certificateId is not null ? "learning.already_certified"
            : active is not null ? "learning.attempt_in_progress"
            : remaining == 0 ? "learning.attempt_limit"
            : null;
        return new ExamOverviewDto(pack.Slug, pack.Title, rules, enrolment is not null, lessonsComplete, enrolment?.PassedAt is not null,
            certificateId, remaining, next, active?.Id, blocked is null, blocked,
            attempts.Select(a => new AttemptSummaryDto(a.Id, a.Status, a.StartedAt, a.SubmittedAt, a.Score, a.Passed, a.QuestionCount)).ToList());
    }

    // ---------------------------------------------------------------- start

    public async Task<ExamAttemptDto> StartAsync(Guid userId, string slug, CancellationToken ct)
    {
        var (course, doc) = await catalog.LoadPublishedAsync(slug, ct);
        var exam = doc.Pack.FinalExam!;

        await using var _ = await dialect.AcquireNamedLockAsync(db, $"learning-exam:{userId:N}:{course.Id:N}", TimeSpan.FromSeconds(15), ct);
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        var now = Now;

        var enrolment = await db.Set<Enrolment>().FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == course.Id, ct)
                        ?? throw DomainException.Conflict("learning.not_enrolled", "Enrol in the course before taking the final exam.");
        var completed = (await db.Set<LessonProgress>().Where(p => p.EnrolmentId == enrolment.Id && p.CompletedAt != null)
            .Select(p => p.LessonSlug).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        if (!doc.Lessons.All(l => completed.Contains(l.Lesson.Slug)))
            throw DomainException.Conflict("learning.lessons_incomplete", "Complete every lesson to unlock the final exam.");
        if (await certificates.ValidCertificateIdAsync(userId, course.Id, ct) is not null)
            throw DomainException.Conflict("learning.already_certified", "You have already passed this course and hold its certificate.");

        // An abandoned attempt past its deadline is graded from its saved answers before a new one can start.
        var key = ActiveKey(userId, course.Id);
        var active = await db.Set<ExamAttempt>().FirstOrDefaultAsync(a => a.ActiveKey == key, ct);
        if (active is not null)
        {
            if (ExamEngine.AcceptsAnswers(now, active.DeadlineAt))
                throw DomainException.Conflict("learning.attempt_in_progress", "You already have an exam attempt in progress. Continue it instead.");
            await FinalizeAsync(active, enrolment, ReadAnswers(active), ExamAttemptStatus.Expired, now, ct);
            if (active.Passed == true)
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                throw DomainException.Conflict("learning.already_certified", "Your previous attempt passed: your certificate has been issued.");
            }
        }

        var starts = await db.Set<ExamAttempt>().Where(a => a.UserId == userId && a.CourseId == course.Id && a.StartedAt > now - ExamEngine.AttemptWindow)
            .Select(a => a.StartedAt).ToListAsync(ct);
        var (remaining, next) = ExamEngine.AttemptBudget(starts, now, exam.MaxAttemptsPerDay);
        if (remaining == 0)
            throw new DomainException("learning.attempt_limit",
                $"You have used all {exam.MaxAttemptsPerDay} attempts for the last 24 hours. Try again after {next:yyyy-MM-dd HH:mm} UTC.",
                DomainErrorKind.Conflict);

        var random = new Random(RandomNumberGenerator.GetInt32(int.MaxValue));
        var draw = ExamEngine.Draw(exam.Pool!, exam.QuestionCount, random);
        var attempt = new ExamAttempt
        {
            EnrolmentId = enrolment.Id, UserId = userId, CourseId = course.Id, CourseVersionId = doc.VersionId,
            Status = ExamAttemptStatus.InProgress, ActiveKey = key, StartedAt = now,
            DeadlineAt = ExamEngine.Deadline(now, exam.TimeLimitMinutes), QuestionCount = draw.Count, PassingScore = doc.Pack.PassingScore,
            DrawJson = JsonSerializer.Serialize(draw, Json), AnswersJson = "{}",
        };
        db.Set<ExamAttempt>().Add(attempt);
        enrolment.LastActivityAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("learning.attempt_in_progress", "You already have an exam attempt in progress. Continue it instead.");
        }
        await tx.CommitAsync(ct);
        return await ViewAsync(attempt, doc, ct);
    }

    // ---------------------------------------------------------------- read

    public async Task<ExamAttemptDto> GetAsync(Guid userId, Guid attemptId, CancellationToken ct)
    {
        var attempt = await db.Set<ExamAttempt>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct)
                      ?? throw DomainException.NotFound("ExamAttempt");
        return await ViewAsync(attempt, await cache.GetAsync(db, attempt.CourseVersionId, ct), ct);
    }

    private async Task<ExamAttemptDto> ViewAsync(ExamAttempt a, CourseDocument doc, CancellationToken ct)
    {
        var now = Now;
        var finished = a.Status != ExamAttemptStatus.InProgress;
        var draw = ReadDraw(a);
        var answers = ReadAnswers(a);
        var questions = draw.Select((d, i) =>
        {
            var q = doc.Pool[d.QuestionId];
            var options = d.OptionOrder.Select(o => q.Options![o]).ToList();
            var selected = answers.TryGetValue(d.QuestionId, out var s) ? ExamEngine.ToShown(d, s) : Array.Empty<int>();
            QuestionReviewDto? review = null;
            if (finished)
            {
                var correct = ExamEngine.ToShown(d, q.Correct ?? new());
                review = new QuestionReviewDto(selected.SequenceEqual(correct), correct, q.Explanation);
            }
            return new AttemptQuestionDto(d.QuestionId, i + 1, q.TypeValue, q.Question, options, selected, review);
        }).ToList();

        ExamResultDto? result = null;
        if (finished)
        {
            var starts = await db.Set<ExamAttempt>().AsNoTracking()
                .Where(x => x.UserId == a.UserId && x.CourseId == a.CourseId && x.StartedAt > now - ExamEngine.AttemptWindow)
                .Select(x => x.StartedAt).ToListAsync(ct);
            var (remaining, next) = ExamEngine.AttemptBudget(starts, now, doc.Pack.FinalExam!.MaxAttemptsPerDay);
            var certificateId = a.Passed == true ? await certificates.ValidCertificateIdAsync(a.UserId, a.CourseId, ct) : null;
            result = new ExamResultDto(a.Score ?? 0, a.CorrectCount ?? 0, a.QuestionCount, a.Passed == true, a.PassingScore, certificateId,
                remaining, next);
        }
        return new ExamAttemptDto(a.Id, doc.Pack.Slug, doc.Pack.Title, a.Status, a.StartedAt, a.DeadlineAt, now,
            finished ? 0 : ExamEngine.SecondsRemaining(now, a.DeadlineAt), a.QuestionCount, a.PassingScore, questions, result);
    }

    // ---------------------------------------------------------------- answer & submit

    /// <summary>Validates an answer against the attempt's draw and maps shown positions to original option indices.</summary>
    private static (string QuestionId, int[] Original) Map(List<DrawnQuestion> draw, CourseDocument doc, AnswerInput input)
    {
        var drawn = draw.FirstOrDefault(d => d.QuestionId == input.QuestionId)
                    ?? throw FieldRules.FieldError("learning.unknown_question", "questionId", "That question is not part of this attempt.");
        var positions = input.Selected!;
        var original = ExamEngine.ToOriginal(drawn, positions)
                       ?? throw FieldRules.FieldError("learning.invalid_answer", "selected", "Choose options listed for this question (each once).");
        if (doc.Pool[drawn.QuestionId].TypeValue == QuestionType.Single && original.Length > 1)
            throw FieldRules.FieldError("learning.invalid_answer", "selected", "Choose one option for this question.");
        return (drawn.QuestionId, original);
    }

    private async Task<(ExamAttempt Attempt, CourseDocument Doc, IAsyncDisposable Lock)> LockAttemptAsync(Guid userId, Guid attemptId, CancellationToken ct)
    {
        var owner = await db.Set<ExamAttempt>().AsNoTracking().Where(a => a.Id == attemptId && a.UserId == userId)
            .Select(a => new { a.CourseVersionId }).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("ExamAttempt");
        var doc = await cache.GetAsync(db, owner.CourseVersionId, ct);
        var handle = await dialect.AcquireNamedLockAsync(db, $"learning-attempt:{attemptId:N}", TimeSpan.FromSeconds(15), ct);
        var attempt = await db.Set<ExamAttempt>().FirstAsync(a => a.Id == attemptId, ct);
        return (attempt, doc, handle);
    }

    /// <summary>Saves (autosaves) the answer to one question of an attempt in progress, until the deadline (+ grace).</summary>
    public async Task<ExamAttemptDto> SaveAnswerAsync(Guid userId, Guid attemptId, AnswerInput input, CancellationToken ct)
    {
        var (attempt, doc, handle) = await LockAttemptAsync(userId, attemptId, ct);
        await using (handle)
        {
            if (attempt.Status != ExamAttemptStatus.InProgress)
                throw DomainException.Conflict("learning.attempt_closed", "This attempt has already been submitted.");
            if (!ExamEngine.AcceptsAnswers(Now, attempt.DeadlineAt))
                throw DomainException.Conflict("learning.time_expired", "Time is up for this attempt: submit it to see your result.");
            var (questionId, original) = Map(ReadDraw(attempt), doc, input);
            var answers = ReadAnswers(attempt);
            if (original.Length == 0) answers.Remove(questionId);
            else answers[questionId] = original;
            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            attempt.AnswersJson = JsonSerializer.Serialize(answers, Json);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await ViewAsync(attempt, doc, ct);
        }
    }

    /// <summary>
    /// Submits and grades an attempt. Within the time limit (+ grace) the submitted answers are merged over the saved ones;
    /// after it the submitted answers are ignored and the saved ones are graded (status Expired). Submitting a finished
    /// attempt again returns its result (idempotent retries).
    /// </summary>
    public async Task<ExamAttemptDto> SubmitAsync(Guid userId, Guid attemptId, SubmitAttemptRequest request, CancellationToken ct)
    {
        var (attempt, doc, handle) = await LockAttemptAsync(userId, attemptId, ct);
        await using (handle)
        {
            if (attempt.Status != ExamAttemptStatus.InProgress) return await ViewAsync(attempt, doc, ct);
            var now = Now;
            var answers = ReadAnswers(attempt);
            var status = ExamAttemptStatus.Expired;
            if (ExamEngine.AcceptsAnswers(now, attempt.DeadlineAt))
            {
                status = ExamAttemptStatus.Submitted;
                var draw = ReadDraw(attempt);
                foreach (var input in request.Answers ?? new List<AnswerInput>())
                {
                    var (questionId, original) = Map(draw, doc, input);
                    if (original.Length == 0) answers.Remove(questionId);
                    else answers[questionId] = original;
                }
            }

            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            var enrolment = await db.Set<Enrolment>().FirstAsync(e => e.Id == attempt.EnrolmentId, ct);
            await FinalizeAsync(attempt, enrolment, answers, status, now, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await ViewAsync(attempt, doc, ct);
        }
    }

    /// <summary>Grades an attempt, records per-question outcomes, updates the enrolment and issues the certificate on a pass.</summary>
    private async Task FinalizeAsync(ExamAttempt attempt, Enrolment enrolment, Dictionary<string, int[]> answers, ExamAttemptStatus status,
        DateTime now, CancellationToken ct)
    {
        var doc = await cache.GetAsync(db, attempt.CourseVersionId, ct);
        var grade = ExamEngine.Grade(ReadDraw(attempt), answers, doc.Pool, attempt.PassingScore);
        attempt.AnswersJson = JsonSerializer.Serialize(answers, Json);
        attempt.Status = status;
        attempt.ActiveKey = null;
        attempt.SubmittedAt = status == ExamAttemptStatus.Submitted ? now : attempt.DeadlineAt;
        attempt.CorrectCount = grade.CorrectCount;
        attempt.Score = grade.Score;
        attempt.Passed = grade.Passed;
        foreach (var q in grade.Questions)
        {
            db.Set<ExamAnswer>().Add(new ExamAnswer
            {
                AttemptId = attempt.Id, CourseId = attempt.CourseId, QuestionId = q.QuestionId, Selected = q.Selected.ToList(),
                IsCorrect = q.IsCorrect, AttemptPassed = grade.Passed, AnsweredAt = attempt.SubmittedAt.Value,
            });
        }
        enrolment.BestScore = Math.Max(enrolment.BestScore ?? 0, grade.Score);
        enrolment.LastActivityAt = now;
        if (grade.Passed)
        {
            enrolment.PassedAt ??= now;
            await certificates.IssueAsync(attempt.UserId, attempt.CourseId, attempt.CourseVersionId, attempt.Id, grade.Score, null, now, ct);
        }
    }
}
