using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Api.Modules.Seed;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning;

/// <summary>
/// "Demo" learners (docs/DEMO.md § Learning), after the main demo seed: Sara completed "Getting started on Optimize All"
/// (a failed first attempt, then a pass) and holds its certificate; she is part-way through up to two more courses; the new
/// participant has started the getting-started course; a handful of other demo participants have progress and attempts so
/// the Learning admin shows real statistics. Built with the real exam rules (<see cref="ExamEngine"/>) and backdated.
/// Idempotent: skipped when Sara already has an enrolment or the Demo seed did not run.
/// </summary>
public sealed class LearningDemoSeeder(
    IDatabaseDialect dialect, CourseContentCache cache, CertificateService certificates, TimeProvider clock, ILogger<LearningDemoSeeder> logger) : ISeeder
{
    public const string GettingStarted = "platform-getting-started";

    public string Profile => "Demo";
    public int Order => 150;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var saraEmail = Normalization.Email(DemoAccounts.Sara);
        var sara = await db.Set<User>().FirstOrDefaultAsync(u => u.NormalizedEmail == saraEmail, ct);
        if (sara is null) return;
        if (await db.Set<Enrolment>().AnyAsync(e => e.UserId == sara.Id, ct))
        {
            logger.LogInformation("Learning demo data already present; skipping");
            return;
        }
        var courses = await db.Set<Course>().Where(c => c.Status == CourseStatus.Published && c.PublishedVersionId != null)
            .OrderBy(c => c.Category).ThenBy(c => c.Slug).ToListAsync(ct);
        var starter = courses.FirstOrDefault(c => c.Slug == GettingStarted);
        if (starter is null)
        {
            logger.LogWarning("Learning demo data skipped: the {Slug} course pack is not in the catalog", GettingStarted);
            return;
        }

        var random = new Random(DemoSeeder.RandomSeed);
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);

        // Curated catalog: the platform course first, then the first course of each other category.
        starter.IsFeatured = true;
        starter.SortOrder = 1;
        foreach (var first in courses.Where(c => c.Id != starter.Id).GroupBy(c => c.Category).Select(g => g.First()).Take(5))
            first.IsFeatured = true;

        var starterDoc = await cache.GetAsync(db, starter.PublishedVersionId!.Value, ct);
        // Sara: finished and certified (a failed attempt 10 days ago, a pass 9 days ago).
        var saraEnrolment = Enrol(db, sara.Id, starter, now.AddDays(-14));
        Complete(db, saraEnrolment, starterDoc, starterDoc.Lessons.Count, now.AddDays(-13), random);
        Attempt(db, saraEnrolment, starterDoc, now.AddDays(-10), correctShare: 0.5, random);
        var pass = Attempt(db, saraEnrolment, starterDoc, now.AddDays(-9), correctShare: 1.0, random);
        await db.SaveChangesAsync(ct);
        await certificates.IssueAsync(sara.Id, starter.Id, starterDoc.VersionId, pass.Id, pass.Score, null, pass.SubmittedAt!.Value, ct);

        // Sara: two more courses in progress (when the catalog has them).
        var others = courses.Where(c => c.Id != starter.Id).OrderByDescending(c => c.IsFeatured).ThenBy(c => c.SortOrder).Take(2).ToList();
        for (var i = 0; i < others.Count; i++)
        {
            var doc = await cache.GetAsync(db, others[i].PublishedVersionId!.Value, ct);
            var enrolment = Enrol(db, sara.Id, others[i], now.AddDays(-6 + i * 2));
            Complete(db, enrolment, doc, Math.Max(1, doc.Lessons.Count / (i + 2)), now.AddDays(-5 + i * 2), random);
        }

        // The new participant has just started; a few other participants give the admin statistics something to show.
        var newEmail = Normalization.Email(DemoAccounts.NewParticipant);
        var newcomer = await db.Set<User>().FirstOrDefaultAsync(u => u.NormalizedEmail == newEmail, ct);
        var learners = await db.Set<User>()
            .Where(u => u.Email.EndsWith("@" + DemoAccounts.Domain) && u.Status == UserStatus.Active && u.Id != sara.Id &&
                        u.NormalizedEmail != newEmail &&
                        u.Roles.Any(r => r.Role == Role.Participant) && !u.Roles.Any(r => r.Role != Role.Participant))
            .OrderBy(u => u.Email).Take(8).ToListAsync(ct);
        if (newcomer is not null)
        {
            var e = Enrol(db, newcomer.Id, starter, now.AddDays(-1));
            Complete(db, e, starterDoc, 1, now.AddHours(-20), random);
        }
        var passedCount = 0;
        foreach (var (learner, index) in learners.Select((u, i) => (u, i)))
        {
            var enrolledAt = now.AddDays(-25 + index * 2);
            var e = Enrol(db, learner.Id, starter, enrolledAt);
            var lessons = index % 3 == 0 ? starterDoc.Lessons.Count : 1 + index % starterDoc.Lessons.Count;
            Complete(db, e, starterDoc, lessons, enrolledAt.AddDays(1), random);
            if (lessons < starterDoc.Lessons.Count) continue;
            var attempt = Attempt(db, e, starterDoc, enrolledAt.AddDays(2), correctShare: index % 2 == 0 ? 0.9 : 0.6, random);
            if (attempt.Passed == true && passedCount++ < 2)
            {
                await db.SaveChangesAsync(ct);
                await certificates.IssueAsync(learner.Id, starter.Id, starterDoc.VersionId, attempt.Id, attempt.Score, null, attempt.SubmittedAt!.Value, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        logger.LogWarning("Learning demo data created (featured courses, Sara's certificate, demo learners). Demo/staging data only.");
    }

    private static Enrolment Enrol(AppDbContext db, Guid userId, Course course, DateTime at)
    {
        var e = new Enrolment { UserId = userId, CourseId = course.Id, EnrolledAt = at, LastActivityAt = at };
        db.Set<Enrolment>().Add(e);
        return e;
    }

    /// <summary>Completes the first <paramref name="count"/> lessons (answering their knowledge checks) and sets the resume point.</summary>
    private static void Complete(AppDbContext db, Enrolment e, CourseDocument doc, int count, DateTime from, Random random)
    {
        var at = from;
        foreach (var lesson in doc.Lessons.Take(count))
        {
            db.Set<LessonProgress>().Add(new LessonProgress { EnrolmentId = e.Id, LessonSlug = lesson.Lesson.Slug, StartedAt = at, CompletedAt = at.AddMinutes(lesson.Lesson.DurationMinutes) });
            var checks = lesson.Lesson.KnowledgeCheck ?? new();
            for (var i = 0; i < checks.Count; i++)
            {
                var right = random.NextDouble() < 0.8;
                var selected = right ? (checks[i].Correct ?? new()).ToList() : new List<int> { (checks[i].Correct![0] + 1) % checks[i].Options!.Count };
                db.Set<KnowledgeCheckAnswer>().Add(new KnowledgeCheckAnswer
                {
                    EnrolmentId = e.Id, LessonSlug = lesson.Lesson.Slug, QuestionIndex = i, Selected = selected, IsCorrect = right, AnsweredAt = at,
                });
            }
            at = at.AddMinutes(lesson.Lesson.DurationMinutes + 5);
        }
        var next = doc.Lessons.Skip(count).FirstOrDefault() ?? doc.Lessons[Math.Max(0, count - 1)];
        e.LastLessonSlug = next.Lesson.Slug;
        e.LastActivityAt = at;
        if (count >= doc.Lessons.Count) e.LessonsCompletedAt = at;
    }

    /// <summary>A graded, finished attempt answering about <paramref name="correctShare"/> of the drawn questions correctly.</summary>
    private static ExamAttempt Attempt(AppDbContext db, Enrolment e, CourseDocument doc, DateTime startedAt, double correctShare, Random random)
    {
        var exam = doc.Pack.FinalExam!;
        var draw = ExamEngine.Draw(exam.Pool!, exam.QuestionCount, random);
        var rightCount = (int)Math.Round(draw.Count * correctShare);
        var answers = new Dictionary<string, int[]>(StringComparer.Ordinal);
        for (var i = 0; i < draw.Count; i++)
        {
            var q = doc.Pool[draw[i].QuestionId];
            answers[q.Id] = i < rightCount ? q.Correct!.ToArray() : new[] { Enumerable.Range(0, q.Options!.Count).First(o => !q.Correct!.Contains(o)) };
        }
        var grade = ExamEngine.Grade(draw, answers, doc.Pool, doc.Pack.PassingScore);
        var submitted = startedAt.AddMinutes(Math.Min(exam.TimeLimitMinutes - 1, 9));
        var attempt = new ExamAttempt
        {
            EnrolmentId = e.Id, UserId = e.UserId, CourseId = e.CourseId, CourseVersionId = doc.VersionId, Status = ExamAttemptStatus.Submitted,
            StartedAt = startedAt, DeadlineAt = ExamEngine.Deadline(startedAt, exam.TimeLimitMinutes), SubmittedAt = submitted,
            QuestionCount = draw.Count, PassingScore = doc.Pack.PassingScore, DrawJson = JsonSerializer.Serialize(draw, Json),
            AnswersJson = JsonSerializer.Serialize(answers, Json), CorrectCount = grade.CorrectCount, Score = grade.Score, Passed = grade.Passed,
        };
        db.Set<ExamAttempt>().Add(attempt);
        foreach (var g in grade.Questions)
            db.Set<ExamAnswer>().Add(new ExamAnswer
            {
                AttemptId = attempt.Id, CourseId = e.CourseId, QuestionId = g.QuestionId, Selected = g.Selected.ToList(), IsCorrect = g.IsCorrect,
                AttemptPassed = grade.Passed, AnsweredAt = submitted,
            });
        e.BestScore = Math.Max(e.BestScore ?? 0, grade.Score);
        e.LastActivityAt = submitted;
        if (grade.Passed) e.PassedAt ??= submitted;
        return attempt;
    }
}
