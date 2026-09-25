using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.Api.Modules.Learning;

// ---------------------------------------------------------------- Catalog (public and portal)

public sealed class CatalogQuery : PageQuery
{
    public CourseCategory? Category { get; set; }
    public CourseLevel? Level { get; set; }

    /// <summary>Only courses of at most this many minutes.</summary>
    [Range(1, 6000)]
    public int? MaxMinutes { get; set; }

    /// <summary>Only featured courses.</summary>
    public bool? Featured { get; set; }
}

public sealed record CourseCardDto(
    Guid Id, string Slug, string Title, string Subtitle, CourseCategory Category, CourseLevel Level, int EstimatedMinutes,
    int ModuleCount, int LessonCount, string BadgeName, IReadOnlyList<string> Skills, bool IsFeatured, bool IsNew,
    string BadgeImageUrl, DateTime? PublishedAt);

public sealed record CategorySummaryDto(CourseCategory Category, string Label, int CourseCount);

public sealed record PrerequisiteDto(string Slug, string Title);

public sealed record BadgeDto(string Name, string Description, string Criteria, string ImageUrl);

public sealed record ExamInfoDto(int QuestionCount, int TimeLimitMinutes, int MaxAttemptsPerDay, int PassingScore);

public sealed record LessonSummaryDto(string Slug, string Title, LessonType Type, int DurationMinutes, bool HasVideo);

public sealed record ModuleDto(string Slug, string Title, string Summary, IReadOnlyList<LessonSummaryDto> Lessons);

/// <summary>SEO block for the public course/lesson pages (title, description, canonical path, robots).</summary>
public sealed record LearningSeoDto(string Title, string Description, string CanonicalPath, string? ImageUrl, bool NoIndex);

public sealed record CourseDetailDto(
    CourseCardDto Card, string Description, IReadOnlyList<string> Outcomes, IReadOnlyList<PrerequisiteDto> Prerequisites,
    BadgeDto Badge, ExamInfoDto Exam, IReadOnlyList<ModuleDto> Modules, int Version, DateTime UpdatedAt,
    LearningSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

public sealed record LessonVideoDto(string? Src, string? Poster, string? Captions, string Transcript);

/// <summary>A knowledge-check question. Not graded for the certificate, so the answer and explanation are included.</summary>
public sealed record KnowledgeCheckDto(int Index, string Question, IReadOnlyList<string> Options, IReadOnlyList<int> Correct, string Explanation, bool Multiple);

public sealed record LessonNavDto(string Slug, string Title);

public sealed record LessonDto(
    string CourseSlug, string CourseTitle, CourseCategory Category, string ModuleSlug, string ModuleTitle, string Slug, string Title,
    LessonType Type, int DurationMinutes, string Body, LessonVideoDto? Video, IReadOnlyList<string> KeyTakeaways,
    IReadOnlyList<KnowledgeCheckDto> KnowledgeCheck, string? Activity, LessonNavDto? Previous, LessonNavDto? Next,
    int Position, int LessonCount, LearningSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

// ---------------------------------------------------------------- My learning (participant portal)

public sealed record MyCourseProgressDto(
    Guid EnrolmentId, DateTime EnrolledAt, IReadOnlyList<string> CompletedLessons, string? ResumeLessonSlug, int CompletedLessonCount,
    int LessonCount, int ProgressPercent, bool ExamUnlocked, bool Passed, int? BestScore, Guid? CertificateId, DateTime? LastActivityAt);

public sealed record MyCourseCardDto(CourseCardDto Course, bool Enrolled, int ProgressPercent, bool Passed, Guid? CertificateId);

public sealed record MyCourseDto(CourseDetailDto Course, MyCourseProgressDto? Progress);

public sealed record EnrolmentCardDto(
    CourseCardDto Course, int CompletedLessons, int LessonCount, int ProgressPercent, string? NextLessonSlug, string? NextLessonTitle,
    bool ExamUnlocked, bool Passed, int? BestScore, Guid? CertificateId, DateTime EnrolledAt, DateTime? LastActivityAt);

public sealed record LearningStatsDto(int Enrolled, int InProgress, int Completed, int Certificates, int LessonsCompleted, int MinutesLearned);

public sealed record MyLearningDashboardDto(
    LearningStatsDto Stats, EnrolmentCardDto? Continue, IReadOnlyList<EnrolmentCardDto> InProgress, IReadOnlyList<EnrolmentCardDto> Completed,
    IReadOnlyList<CourseCardDto> Recommended, IReadOnlyList<MyCertificateDto> Certificates);

public sealed record KnowledgeCheckResultDto(int Index, IReadOnlyList<int> Selected, bool IsCorrect, IReadOnlyList<int> Correct, string Explanation);

public sealed record MyLessonDto(LessonDto Lesson, bool Enrolled, bool Completed, IReadOnlyList<KnowledgeCheckResultDto> Answers, MyCourseProgressDto? Progress);

public sealed class KnowledgeCheckAnswerRequest
{
    [Required, MaxLength(10)]
    public List<int>? Selected { get; set; }
}

// ---------------------------------------------------------------- Exam

public sealed record AttemptSummaryDto(Guid Id, ExamAttemptStatus Status, DateTime StartedAt, DateTime? SubmittedAt, int? Score, bool? Passed, int QuestionCount);

public sealed record ExamOverviewDto(
    string CourseSlug, string CourseTitle, ExamInfoDto Rules, bool Enrolled, bool LessonsComplete, bool Passed, Guid? CertificateId,
    int AttemptsRemaining, DateTime? NextAttemptAt, Guid? ActiveAttemptId, bool CanStart, string? BlockedReason,
    IReadOnlyList<AttemptSummaryDto> Attempts);

/// <summary>After submission only: whether the answer was right, the correct options (shown positions) and why.</summary>
public sealed record QuestionReviewDto(bool IsCorrect, IReadOnlyList<int> Correct, string Explanation);

/// <summary>A question of an attempt. <see cref="Options"/> are in this attempt's shuffled order; indices are positions.</summary>
public sealed record AttemptQuestionDto(string Id, int Number, QuestionType Type, string Question, IReadOnlyList<string> Options,
    IReadOnlyList<int> Selected, QuestionReviewDto? Review);

public sealed record ExamResultDto(int Score, int CorrectCount, int QuestionCount, bool Passed, int PassingScore, Guid? CertificateId,
    int AttemptsRemaining, DateTime? NextAttemptAt);

public sealed record ExamAttemptDto(
    Guid Id, string CourseSlug, string CourseTitle, ExamAttemptStatus Status, DateTime StartedAt, DateTime DeadlineAt, DateTime ServerNow,
    int SecondsRemaining, int QuestionCount, int PassingScore, IReadOnlyList<AttemptQuestionDto> Questions, ExamResultDto? Result);

public sealed class AnswerInput
{
    [Required, MaxLength(64)]
    public string QuestionId { get; set; } = string.Empty;

    /// <summary>Shown positions (0-based) of the selected options.</summary>
    [Required, MaxLength(10)]
    public List<int>? Selected { get; set; }
}

public sealed class SubmitAttemptRequest
{
    /// <summary>Final answers (optional: answers already saved with PUT …/answers count too).</summary>
    [MaxLength(100)]
    public List<AnswerInput>? Answers { get; set; }
}

// ---------------------------------------------------------------- Certificates

public sealed record CertificateLinksDto(
    string VerificationUrl, string PdfUrl, string ImageUrl, string BadgeImageUrl, string OpenBadgeAssertionUrl,
    string LinkedInAddToProfileUrl, string LinkedInShareUrl);

public sealed record MyCertificateDto(
    Guid Id, string VerificationCode, string CourseSlug, string CourseTitle, string BadgeName, IReadOnlyList<string> Skills, int? Score,
    DateTime IssuedAt, bool Revoked, DateTime? RevokedAt, CertificateLinksDto Links);

public sealed record CertificateVerificationDto(
    Guid Id, string VerificationCode, string Status, bool IsValid, string HolderName, string CourseSlug, string CourseTitle,
    string BadgeName, string? BadgeDescription, string? Criteria, IReadOnlyList<string> Skills, DateTime IssuedAt, DateTime? RevokedAt,
    string IssuerName, CertificateLinksDto Links, LearningSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

// ---------------------------------------------------------------- Admin

public sealed class AdminCourseQuery : PageQuery
{
    public CourseCategory? Category { get; set; }
    public CourseStatus? Status { get; set; }
}

public sealed record AdminCourseRowDto(
    Guid Id, string Slug, string Title, CourseCategory Category, CourseLevel Level, CourseStatus Status, CourseSource Origin,
    bool IsFeatured, int SortOrder, int LessonCount, int? PublishedVersionNumber, int? LatestVersionNumber, bool PackUpdateAvailable,
    int Enrolments, int Completions, int CompletionRate, int Attempts, int PassRate, int? AverageScore, int Certificates,
    DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed record CourseVersionDto(
    Guid Id, int Number, CourseSource Source, int? PackVersion, Guid? BasedOnVersionId, string? Note, DateTime CreatedAt,
    string? CreatedBy, bool IsPublished, bool IsLatest);

public sealed record AdminCourseDetailDto(AdminCourseRowDto Summary, IReadOnlyList<CourseVersionDto> Versions, string? PackFile);

/// <summary>A full course document (answers included): staff with learning.manage only.</summary>
public sealed record CourseVersionDocumentDto(Guid Id, Guid CourseId, int Number, CourseSource Source, bool IsPublished, JsonElement Document);

public sealed record ValidationReportDto(bool Valid, IReadOnlyList<PackIssue> Issues, int ModuleCount, int LessonCount, int PoolSize, int QuestionCount);

public sealed class CourseDocumentRequest
{
    /// <summary>A course document in the course pack format (docs/LEARNING.md).</summary>
    [Required]
    public JsonElement? Document { get; set; }
}

public sealed class CreateCourseRequest
{
    [Required]
    public JsonElement? Document { get; set; }

    public bool Publish { get; set; }
}

public sealed class SaveCourseVersionRequest
{
    [Required]
    public JsonElement? Document { get; set; }

    /// <summary>The version the edit started from (the editor warns when it is not the latest).</summary>
    public Guid? BasedOnVersionId { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>Publish the new version right away.</summary>
    public bool Publish { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class PublishCourseRequest
{
    [Required]
    public Guid? VersionId { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class UnpublishCourseRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class CourseSettingsRequest
{
    public bool IsFeatured { get; set; }

    [Range(0, 10_000)]
    public int SortOrder { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class LessonVideoRequest
{
    /// <summary>Uploaded file URL (/api/v1/files/{id}) or https URL; null clears it.</summary>
    [MaxLength(500)]
    public string? Src { get; set; }

    [MaxLength(500)]
    public string? Poster { get; set; }

    /// <summary>WebVTT captions (uploaded file URL or https URL).</summary>
    [MaxLength(500)]
    public string? Captions { get; set; }

    public bool Publish { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record QuestionStatDto(
    string QuestionId, string Module, QuestionDifficulty Difficulty, QuestionType Type, string Question, int Answered, int Correct,
    int? PercentCorrect, int? PercentCorrectPassers, int? PercentCorrectOthers, int? Discrimination);

public sealed class LearnerQuery : PageQuery
{
    public bool? Passed { get; set; }
}

public sealed record LearnerRowDto(
    Guid UserId, string DisplayName, string Email, DateTime EnrolledAt, int CompletedLessons, int LessonCount, int ProgressPercent,
    int Attempts, int? BestScore, bool Passed, Guid? CertificateId, bool CertificateRevoked, DateTime? LastActivityAt);

public sealed record UserEnrolmentDto(
    Guid CourseId, string CourseSlug, string CourseTitle, DateTime EnrolledAt, int CompletedLessons, int LessonCount, int ProgressPercent,
    int Attempts, int? BestScore, bool Passed, DateTime? LastActivityAt);

public sealed record AdminCertificateDto(
    Guid Id, string VerificationCode, Guid UserId, string HolderName, string Email, Guid CourseId, string CourseTitle, int? Score,
    DateTime IssuedAt, bool Manual, DateTime? RevokedAt, string? RevocationReason, Guid ConcurrencyStamp);

public sealed record UserLearningDto(Guid UserId, string DisplayName, string Email, IReadOnlyList<UserEnrolmentDto> Enrolments,
    IReadOnlyList<AdminCertificateDto> Certificates);

public sealed class AdminCertificateQuery : PageQuery
{
    public Guid? CourseId { get; set; }
    public bool? Revoked { get; set; }
}

public sealed class IssueCertificateRequest
{
    [Required]
    public Guid? UserId { get; set; }

    [Required]
    public Guid? CourseId { get; set; }

    [Required, MinLength(3), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Must be true: issuing a credential is a deliberate, audited action.</summary>
    public bool Confirm { get; set; }
}

public sealed class RevokeCertificateRequest
{
    [Required, MinLength(3), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public bool Confirm { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class LearningMediaForm
{
    /// <summary>MP4 video (max 50 MB), WebVTT captions (max 1 MB) or a PNG/JPEG/WebP poster image (max 10 MB).</summary>
    public Microsoft.AspNetCore.Http.IFormFile? File { get; set; }
}

public sealed record LearningIssuerDto(string OrganizationName, string? LinkedInOrganizationId, string IssuerUrl);
