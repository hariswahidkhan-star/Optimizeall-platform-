using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Learning.Certificates;

namespace OptimizeAll.Api.Modules.Learning;

/// <summary>
/// A participant's own learning (enrolments, progress, knowledge checks, certificates). Staff "viewing as" a participant
/// (impersonation) can look but not act: enrolling, progress and exam writes act as the learner and are denied.
/// </summary>
[ApiController]
[HasPermission(Permissions.ParticipantPortal)]
[DeniedWhileImpersonating(WritesOnly = true)]
[Route("api/v1/me/learning")]
public sealed class MyLearningController(MyLearningService learning, CertificateService certificates, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>"My learning": stats, continue where you left off, in-progress and completed courses, recommendations, certificates.</summary>
    [HttpGet]
    public Task<MyLearningDashboardDto> Dashboard(CancellationToken ct) => learning.DashboardAsync(currentUser.Id, ct);

    /// <summary>The catalog with the caller's enrolment and progress on each course.</summary>
    [HttpGet("courses")]
    public Task<PagedResult<MyCourseCardDto>> Courses([FromQuery] CatalogQuery query, CancellationToken ct) =>
        learning.CatalogAsync(currentUser.Id, query, ct);

    [HttpGet("courses/{slug}")]
    public Task<MyCourseDto> Course(string slug, CancellationToken ct) => learning.CourseAsync(currentUser.Id, slug, ct);

    /// <summary>Enrols the caller (free; idempotent: 200 when already enrolled, 201 when created).</summary>
    [HttpPost("courses/{slug}/enrol")]
    [EnableRateLimiting(RateLimitPolicies.Learning)]
    [ProducesResponseType(typeof(MyCourseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MyCourseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Enrol(string slug, CancellationToken ct)
    {
        var (course, created) = await learning.EnrolAsync(currentUser.Id, slug, ct);
        return created ? StatusCode(StatusCodes.Status201Created, course) : Ok(course);
    }

    [HttpGet("courses/{slug}/lessons/{lessonSlug}")]
    public Task<MyLessonDto> Lesson(string slug, string lessonSlug, CancellationToken ct) =>
        learning.LessonAsync(currentUser.Id, slug, lessonSlug, ct);

    /// <summary>Records that the lesson was opened (the "resume" point).</summary>
    [HttpPost("courses/{slug}/lessons/{lessonSlug}/start")]
    [EnableRateLimiting(RateLimitPolicies.Learning)]
    public Task<MyCourseProgressDto> Start(string slug, string lessonSlug, CancellationToken ct) =>
        learning.StartLessonAsync(currentUser.Id, slug, lessonSlug, ct);

    [HttpPost("courses/{slug}/lessons/{lessonSlug}/complete")]
    [EnableRateLimiting(RateLimitPolicies.Learning)]
    public Task<MyCourseProgressDto> Complete(string slug, string lessonSlug, CancellationToken ct) =>
        learning.CompleteLessonAsync(currentUser.Id, slug, lessonSlug, ct);

    /// <summary>Answers a knowledge-check question (not graded for the certificate; the latest answer is kept).</summary>
    [HttpPost("courses/{slug}/lessons/{lessonSlug}/checks/{index:int}")]
    [EnableRateLimiting(RateLimitPolicies.Learning)]
    public Task<KnowledgeCheckResultDto> Check(string slug, string lessonSlug, int index, KnowledgeCheckAnswerRequest request, CancellationToken ct) =>
        learning.AnswerCheckAsync(currentUser.Id, slug, lessonSlug, index, request, ct);

    [HttpGet("certificates")]
    public Task<IReadOnlyList<MyCertificateDto>> Certificates(CancellationToken ct) => certificates.MineAsync(currentUser.Id, ct);

    /// <summary>One of the caller's certificates with its download, verification, Open Badge and LinkedIn links.</summary>
    [HttpGet("certificates/{certificateId:guid}")]
    public Task<MyCertificateDto> Certificate(Guid certificateId, CancellationToken ct) =>
        certificates.GetMineAsync(currentUser.Id, certificateId, ct);
}

/// <summary>
/// Final exams (server-timed, server-graded). Answers are never sent before an attempt is finished; attempts belong to the
/// caller (others answer 404). Writes are denied while impersonating.
/// </summary>
[ApiController]
[HasPermission(Permissions.ParticipantPortal)]
[DeniedWhileImpersonating(WritesOnly = true)]
[Route("api/v1/me/learning")]
public sealed class MyExamController(ExamService exams, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Exam rules, attempts left in the rolling 24 hours, the attempt in progress and past attempts.</summary>
    [HttpGet("courses/{slug}/exam")]
    public Task<ExamOverviewDto> Overview(string slug, CancellationToken ct) => exams.OverviewAsync(currentUser.Id, slug, ct);

    /// <summary>Starts an attempt: questions drawn at random from the pool, options shuffled, deadline set on the server.</summary>
    [HttpPost("courses/{slug}/exam/attempts")]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    [ProducesResponseType(typeof(ExamAttemptDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Start(string slug, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await exams.StartAsync(currentUser.Id, slug, ct));

    /// <summary>The attempt: questions (+ saved answers and seconds left) while in progress; score and per-question review once finished.</summary>
    [HttpGet("attempts/{attemptId:guid}")]
    public Task<ExamAttemptDto> Get(Guid attemptId, CancellationToken ct) => exams.GetAsync(currentUser.Id, attemptId, ct);

    /// <summary>Saves the answer to one question (autosave) until the deadline.</summary>
    [HttpPut("attempts/{attemptId:guid}/answers")]
    [EnableRateLimiting(RateLimitPolicies.Learning)]
    public Task<ExamAttemptDto> Answer(Guid attemptId, AnswerInput request, CancellationToken ct) =>
        exams.SaveAnswerAsync(currentUser.Id, attemptId, request, ct);

    /// <summary>Submits and grades the attempt (after the deadline only the saved answers count).</summary>
    [HttpPost("attempts/{attemptId:guid}/submit")]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    public Task<ExamAttemptDto> Submit(Guid attemptId, SubmitAttemptRequest request, CancellationToken ct) =>
        exams.SubmitAsync(currentUser.Id, attemptId, request, ct);
}
