using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Learning.Certificates;

namespace OptimizeAll.Api.Modules.Learning.Admin;

/// <summary>Learning admin: courses, statistics, analytics, learners and exports (learning.view); authoring (learning.manage).</summary>
[ApiController]
[HasPermission(Permissions.LearningView)]
[Route("api/v1/admin/learning")]
public sealed class LearningAdminController(LearningAdminService admin, LearningMediaService media, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("courses")]
    public Task<PagedResult<AdminCourseRowDto>> Courses([FromQuery] AdminCourseQuery query, CancellationToken ct) => admin.CoursesAsync(query, ct);

    [HttpGet("courses/{courseId:guid}")]
    public Task<AdminCourseDetailDto> Course(Guid courseId, CancellationToken ct) => admin.CourseAsync(courseId, ct);

    /// <summary>A version's full document (final-exam answers included), for the course editor.</summary>
    [HttpGet("courses/{courseId:guid}/versions/{versionId:guid}")]
    [HasPermission(Permissions.LearningManage)]
    public Task<CourseVersionDocumentDto> Version(Guid courseId, Guid versionId, CancellationToken ct) => admin.VersionAsync(courseId, versionId, ct);

    /// <summary>Per-question statistics: % correct and discrimination (passers vs. others).</summary>
    [HttpGet("courses/{courseId:guid}/questions")]
    public Task<IReadOnlyList<QuestionStatDto>> Questions(Guid courseId, CancellationToken ct) => admin.QuestionsAsync(courseId, ct);

    [HttpGet("courses/{courseId:guid}/learners")]
    public Task<PagedResult<LearnerRowDto>> Learners(Guid courseId, [FromQuery] LearnerQuery query, CancellationToken ct) =>
        admin.LearnersAsync(courseId, query, ct);

    /// <summary>CSV of every learner's progress and result in the course (audited).</summary>
    [HttpGet("courses/{courseId:guid}/results.csv")]
    [Produces("text/csv")]
    public Task<FileContentResult> Export(Guid courseId, CancellationToken ct) => admin.ExportResultsAsync(courseId, ct);

    [HttpGet("users/{userId:guid}")]
    public Task<UserLearningDto> Learner(Guid userId, CancellationToken ct) => admin.UserAsync(userId, ct);

    /// <summary>Checks a course document against the course pack contract without saving it.</summary>
    [HttpPost("courses/validate")]
    [HasPermission(Permissions.LearningManage)]
    public ValidationReportDto Validate(CourseDocumentRequest request) => admin.Validate(request);

    [HttpPost("courses")]
    [HasPermission(Permissions.LearningManage)]
    [ProducesResponseType(typeof(AdminCourseDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCourseRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await admin.CreateAsync(currentUser.Id, request, ct));

    /// <summary>Saves an edited document as a new version (never overwrites an existing version or the course pack).</summary>
    [HttpPost("courses/{courseId:guid}/versions")]
    [HasPermission(Permissions.LearningManage)]
    public Task<AdminCourseDetailDto> SaveVersion(Guid courseId, SaveCourseVersionRequest request, CancellationToken ct) =>
        admin.SaveVersionAsync(currentUser.Id, courseId, request, ct);

    [HttpPost("courses/{courseId:guid}/publish")]
    [HasPermission(Permissions.LearningManage)]
    public Task<AdminCourseDetailDto> Publish(Guid courseId, PublishCourseRequest request, CancellationToken ct) =>
        admin.PublishAsync(courseId, request, ct);

    [HttpPost("courses/{courseId:guid}/unpublish")]
    [HasPermission(Permissions.LearningManage)]
    public Task<AdminCourseDetailDto> Unpublish(Guid courseId, UnpublishCourseRequest request, CancellationToken ct) =>
        admin.UnpublishAsync(courseId, request, ct);

    /// <summary>Featured flag and catalog order.</summary>
    [HttpPut("courses/{courseId:guid}/settings")]
    [HasPermission(Permissions.LearningManage)]
    public Task<AdminCourseDetailDto> Settings(Guid courseId, CourseSettingsRequest request, CancellationToken ct) =>
        admin.SettingsAsync(courseId, request, ct);

    /// <summary>Sets a video lesson's source, poster and captions (a new version of the latest content).</summary>
    [HttpPut("courses/{courseId:guid}/lessons/{lessonSlug}/video")]
    [HasPermission(Permissions.LearningManage)]
    public Task<AdminCourseDetailDto> Video(Guid courseId, string lessonSlug, LessonVideoRequest request, CancellationToken ct) =>
        admin.SetLessonVideoAsync(currentUser.Id, courseId, lessonSlug, request, ct);

    /// <summary>Uploads lesson media (MP4 video, WebVTT captions or a poster image) through the Files module.</summary>
    [HttpPost("media")]
    [HasPermission(Permissions.LearningManage)]
    [RequestSizeLimit(52 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 52 * 1024 * 1024)]
    [ProducesResponseType(typeof(StoredFileDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Media([FromForm] LearningMediaForm form, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await media.UploadAsync(currentUser.Id, form, ct));
}

/// <summary>Certificates in the Learning admin: list (learning.view); issue and revoke (learning.certify, audited, never while impersonating).</summary>
[ApiController]
[HasPermission(Permissions.LearningView)]
[Route("api/v1/admin/learning/certificates")]
public sealed class LearningCertificatesAdminController(LearningAdminService admin, CertificateService certificates, ICurrentUser currentUser)
    : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AdminCertificateDto>> List([FromQuery] AdminCertificateQuery query, CancellationToken ct) => admin.CertificatesAsync(query, ct);

    [HttpPost]
    [HasPermission(Permissions.LearningCertify)]
    [DeniedWhileImpersonating]
    [ProducesResponseType(typeof(AdminCertificateDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Issue(IssueCertificateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await certificates.IssueManuallyAsync(currentUser.Id, request, ct));

    [HttpPost("{certificateId:guid}/revoke")]
    [HasPermission(Permissions.LearningCertify)]
    [DeniedWhileImpersonating]
    public Task<AdminCertificateDto> Revoke(Guid certificateId, RevokeCertificateRequest request, CancellationToken ct) =>
        certificates.RevokeAsync(currentUser.Id, certificateId, request, ct);
}
