using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Learning;

public enum CourseCategory
{
    Sales,
    Marketing,
    Seo,
    Ai,
    Business,
    Design,
    Data,
    Platform,
}

public enum CourseLevel
{
    Beginner,
    Intermediate,
    Advanced,
}

public enum LessonType
{
    Article,
    Video,
}

public enum QuestionType
{
    Single,
    Multiple,
}

public enum QuestionDifficulty
{
    Easy,
    Medium,
    Hard,
}

/// <summary>Where a course version's content came from.</summary>
public enum CourseSource
{
    /// <summary>A JSON course pack shipped with the API (Modules/Learning/Catalog), upserted on startup.</summary>
    Pack,
    /// <summary>Written or edited by staff in the Learning admin.</summary>
    Admin,
}

public enum CourseStatus
{
    /// <summary>Not visible to learners (no published version yet, or unpublished by staff).</summary>
    Draft,
    Published,
}

public enum ExamAttemptStatus
{
    InProgress,
    /// <summary>Submitted before the deadline and graded.</summary>
    Submitted,
    /// <summary>The time limit passed: graded from the answers saved before the deadline.</summary>
    Expired,
}

/// <summary>Pack enum spellings (lower-case in JSON, e.g. "seo", "beginner").</summary>
public static class LearningEnums
{
    public static readonly string[] Categories = { "sales", "marketing", "seo", "ai", "business", "design", "data", "platform" };
    public static readonly string[] Levels = { "beginner", "intermediate", "advanced" };
    public static readonly string[] Difficulties = { "easy", "medium", "hard" };

    public static CourseCategory? ParseCategory(string? value) => value switch
    {
        "sales" => CourseCategory.Sales,
        "marketing" => CourseCategory.Marketing,
        "seo" => CourseCategory.Seo,
        "ai" => CourseCategory.Ai,
        "business" => CourseCategory.Business,
        "design" => CourseCategory.Design,
        "data" => CourseCategory.Data,
        "platform" => CourseCategory.Platform,
        _ => null,
    };

    public static string ToPack(CourseCategory category) => category.ToString().ToLowerInvariant();

    public static CourseLevel? ParseLevel(string? value) => value switch
    {
        "beginner" => CourseLevel.Beginner,
        "intermediate" => CourseLevel.Intermediate,
        "advanced" => CourseLevel.Advanced,
        _ => null,
    };

    public static QuestionDifficulty? ParseDifficulty(string? value) => value switch
    {
        "easy" => QuestionDifficulty.Easy,
        "medium" => QuestionDifficulty.Medium,
        "hard" => QuestionDifficulty.Hard,
        _ => null,
    };
}

/// <summary>
/// A course (stable identity, slug and listing metadata). Its content lives in immutable <see cref="CourseVersion"/>
/// documents; the listing columns are copied from the published version so catalog queries never read lesson bodies.
/// </summary>
public class Course : AuditedEntity, IConcurrencyStamped
{
    public string Slug { get; set; } = string.Empty;
    /// <summary>Pack when the course came from a course pack (even if staff later published an edited version).</summary>
    public CourseSource Origin { get; set; }
    public CourseStatus Status { get; set; } = CourseStatus.Draft;
    public Guid? PublishedVersionId { get; set; }
    /// <summary>The newest version (draft or published).</summary>
    public Guid? LatestVersionId { get; set; }
    public DateTime? PublishedAt { get; set; }
    /// <summary>Shown first in the catalog and in the "Free courses" section of the website.</summary>
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }

    // Listing metadata of the published (or, before publication, latest) version.
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public CourseCategory Category { get; set; }
    public CourseLevel Level { get; set; }
    public int EstimatedMinutes { get; set; }
    public int ModuleCount { get; set; }
    public int LessonCount { get; set; }
    public string BadgeName { get; set; } = string.Empty;
    public List<string> Skills { get; set; } = new();
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>An immutable content version of a course: the full course pack document.</summary>
public class CourseVersion : Entity
{
    public Guid CourseId { get; set; }
    /// <summary>1, 2, 3… per course (independent of the pack's own version number).</summary>
    public int Number { get; set; }
    public CourseSource Source { get; set; }
    /// <summary>The pack's <c>version</c> for pack-sourced versions.</summary>
    public int? PackVersion { get; set; }
    /// <summary>The version an admin edit started from.</summary>
    public Guid? BasedOnVersionId { get; set; }
    public string ContentJson { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>A learner's enrolment in a course. Progress is kept per lesson slug, so it survives new content versions.</summary>
public class Enrolment : AuditedEntity
{
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public DateTime EnrolledAt { get; set; }
    public string? LastLessonSlug { get; set; }
    public DateTime? LastActivityAt { get; set; }
    /// <summary>When every lesson of the published version was completed (the exam unlocks).</summary>
    public DateTime? LessonsCompletedAt { get; set; }
    /// <summary>When the learner passed the final exam.</summary>
    public DateTime? PassedAt { get; set; }
    public int? BestScore { get; set; }
}

public class LessonProgress : Entity
{
    public Guid EnrolmentId { get; set; }
    public string LessonSlug { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>The learner's latest answer to one knowledge-check question (not graded for the certificate).</summary>
public class KnowledgeCheckAnswer : Entity
{
    public Guid EnrolmentId { get; set; }
    public string LessonSlug { get; set; } = string.Empty;
    public int QuestionIndex { get; set; }
    public List<int> Selected { get; set; } = new();
    public bool IsCorrect { get; set; }
    public DateTime AnsweredAt { get; set; }
}

/// <summary>
/// One final-exam attempt. The drawn questions and option order are fixed at start (<see cref="DrawJson"/>); answers are
/// saved server-side as the learner goes and graded on submission or expiry against the attempt's own course version.
/// </summary>
public class ExamAttempt : Entity
{
    public Guid EnrolmentId { get; set; }
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public Guid CourseVersionId { get; set; }
    public ExamAttemptStatus Status { get; set; }
    /// <summary>"{userId}:{courseId}" while in progress, null afterwards: a unique index allows one active attempt per course.</summary>
    public string? ActiveKey { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime DeadlineAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public int QuestionCount { get; set; }
    public int PassingScore { get; set; }
    /// <summary>JSON list of <see cref="DrawnQuestion"/>.</summary>
    public string DrawJson { get; set; } = "[]";
    /// <summary>JSON map questionId → selected option indices (original, unshuffled).</summary>
    public string AnswersJson { get; set; } = "{}";
    public int? CorrectCount { get; set; }
    public int? Score { get; set; }
    public bool? Passed { get; set; }
}

/// <summary>Per-question outcome of a graded attempt (question analytics: % correct per question id).</summary>
public class ExamAnswer : Entity
{
    public Guid AttemptId { get; set; }
    public Guid CourseId { get; set; }
    public string QuestionId { get; set; } = string.Empty;
    public List<int> Selected { get; set; } = new();
    public bool IsCorrect { get; set; }
    /// <summary>Whether the attempt passed (used for discrimination: % correct among passers vs. non-passers).</summary>
    public bool AttemptPassed { get; set; }
    public DateTime AnsweredAt { get; set; }
}

/// <summary>A course certificate (and the Open Badge assertion behind it). Holder and course details are snapshots.</summary>
public class Certificate : AuditedEntity, IConcurrencyStamped
{
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public Guid CourseVersionId { get; set; }
    public Guid? AttemptId { get; set; }
    /// <summary>"OA-XXXX-XXXX": printed on the certificate, used as the LinkedIn credential id and for manual verification.</summary>
    public string VerificationCode { get; set; } = string.Empty;
    /// <summary>"{userId}:{courseId}" while valid, null once revoked: a unique index allows one valid certificate per course.</summary>
    public string? ActiveKey { get; set; }
    public string HolderName { get; set; } = string.Empty;
    public string CourseSlug { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string BadgeName { get; set; } = string.Empty;
    public List<string> Skills { get; set; } = new();
    public int? Score { get; set; }
    public DateTime IssuedAt { get; set; }
    /// <summary>Staff member who issued it manually (null when earned through the exam).</summary>
    public Guid? IssuedByUserId { get; set; }
    /// <summary>Salt of the hashed recipient identity in the Open Badges assertion.</summary>
    public string RecipientSalt { get; set; } = string.Empty;
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool IsRevoked => RevokedAt is not null;
}
