using System.Globalization;
using System.Text.Json;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.Api.Modules.Learning;

/// <summary>
/// schema.org JSON-LD for the public academy pages: <c>Course</c> (provider, offers price 0 / isAccessibleForFree,
/// hasCourseInstance with courseMode + courseWorkload, syllabus), lessons as <c>LearningResource</c> + <c>Article</c>,
/// <c>VideoObject</c> for produced lesson videos, <c>BreadcrumbList</c>, and the certificate's
/// <c>EducationalOccupationalCredential</c>. The web app writes each object with textContent (never as HTML); the
/// server-rendered verification page serializes it with HTML-safe escaping.
/// </summary>
public static class LearningJsonLd
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static JsonElement Element(Dictionary<string, object?> value) =>
        JsonSerializer.SerializeToElement(value.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value), Options);

    private static Dictionary<string, object?> Provider(LearningIssuer issuer, LearningLinks links) => new()
    {
        ["@type"] = "Organization",
        ["name"] = issuer.Name,
        ["url"] = links.Absolute("/learn"),
    };

    /// <summary>ISO 8601 duration (PT2H30M).</summary>
    public static string Duration(int minutes) =>
        minutes >= 60 ? $"PT{minutes / 60}H{(minutes % 60 > 0 ? $"{minutes % 60}M" : string.Empty)}" : $"PT{Math.Max(1, minutes)}M";

    public static JsonElement Course(CoursePack pack, LearningLinks links, LearningIssuer issuer, Course course) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "Course",
        ["@id"] = links.Course(pack.Slug) + "#course",
        ["name"] = pack.Title,
        ["description"] = pack.Subtitle,
        ["url"] = links.Course(pack.Slug),
        ["image"] = links.BadgeImage(pack.Slug),
        ["provider"] = Provider(issuer, links),
        ["inLanguage"] = "en",
        ["educationalLevel"] = pack.LevelValue.ToString(),
        ["isAccessibleForFree"] = true,
        ["timeRequired"] = Duration(pack.EstimatedMinutes),
        ["teaches"] = pack.Outcomes,
        ["keywords"] = string.Join(", ", pack.Skills ?? new List<string>()),
        ["about"] = PublicLearningService.CategoryLabels[pack.CategoryValue],
        ["coursePrerequisites"] = pack.Prerequisites is { Count: > 0 } p ? p.Select(links.Course).ToArray() : null,
        ["educationalCredentialAwarded"] = new Dictionary<string, object?>
        {
            ["@type"] = "EducationalOccupationalCredential",
            ["name"] = pack.Badge?.Name,
            ["credentialCategory"] = "certificate",
            ["url"] = links.OpenBadgeClass(pack.Slug),
        },
        ["offers"] = new Dictionary<string, object?>
        {
            ["@type"] = "Offer",
            ["price"] = 0,
            ["priceCurrency"] = "USD",
            ["category"] = "Free",
            ["availability"] = "https://schema.org/InStock",
            ["url"] = links.Course(pack.Slug),
        },
        ["hasCourseInstance"] = new Dictionary<string, object?>
        {
            ["@type"] = "CourseInstance",
            ["courseMode"] = "Online",
            ["courseWorkload"] = Duration(pack.EstimatedMinutes),
            ["inLanguage"] = "en",
        },
        ["syllabusSections"] = (pack.Modules ?? new()).Select(m => new Dictionary<string, object?>
        {
            ["@type"] = "Syllabus",
            ["name"] = m.Title,
            ["description"] = m.Summary,
            ["timeRequired"] = Duration((m.Lessons ?? new()).Sum(l => l.DurationMinutes)),
        }).ToArray(),
        ["numberOfLessons"] = pack.LessonCount,
        ["dateModified"] = course.UpdatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    });

    public static JsonElement Lesson(CoursePack pack, PackLesson lesson, string excerpt, LearningLinks links, LearningIssuer issuer, Course course) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = new[] { "LearningResource", "Article" },
        ["name"] = lesson.Title,
        ["headline"] = lesson.Title,
        ["description"] = excerpt,
        ["url"] = links.Absolute(LearningLinks.LessonPath(pack.Slug, lesson.Slug)),
        ["image"] = links.BadgeImage(pack.Slug),
        ["learningResourceType"] = lesson.TypeValue == LessonType.Video ? "Video lesson" : "Lesson",
        ["educationalLevel"] = pack.LevelValue.ToString(),
        ["timeRequired"] = Duration(lesson.DurationMinutes),
        ["inLanguage"] = "en",
        ["isAccessibleForFree"] = true,
        ["keywords"] = string.Join(", ", pack.Skills ?? new List<string>()),
        ["teaches"] = lesson.KeyTakeaways,
        ["author"] = Provider(issuer, links),
        ["publisher"] = Provider(issuer, links),
        ["datePublished"] = (course.PublishedAt ?? course.UpdatedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["dateModified"] = course.UpdatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["isPartOf"] = new Dictionary<string, object?>
        {
            ["@type"] = "Course",
            ["@id"] = links.Course(pack.Slug) + "#course",
            ["name"] = pack.Title,
            ["url"] = links.Course(pack.Slug),
        },
    });

    /// <summary>VideoObject for a produced lesson video (Google requires a thumbnail, so only with a poster).</summary>
    public static JsonElement? Video(CoursePack pack, PackLesson lesson, string excerpt, LearningLinks links, Course course)
    {
        if (lesson.TypeValue != LessonType.Video || lesson.Video?.Src is null || lesson.Video.Poster is null) return null;
        return Element(new()
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "VideoObject",
            ["name"] = lesson.Title,
            ["description"] = excerpt,
            ["thumbnailUrl"] = links.Absolute(lesson.Video.Poster),
            ["contentUrl"] = links.Absolute(lesson.Video.Src),
            ["uploadDate"] = (course.PublishedAt ?? course.UpdatedAt).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["duration"] = Duration(lesson.DurationMinutes),
            ["transcript"] = lesson.Video.Script,
            ["isAccessibleForFree"] = true,
        });
    }

    public static JsonElement Breadcrumbs(LearningLinks links, params (string Name, string Path)[] items) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "BreadcrumbList",
        ["itemListElement"] = items.Select((item, i) => new Dictionary<string, object?>
        {
            ["@type"] = "ListItem",
            ["position"] = i + 1,
            ["name"] = item.Name,
            ["item"] = links.Absolute(item.Path),
        }).ToArray(),
    });

    /// <summary>The certificate as a schema.org credential (verification page).</summary>
    public static JsonElement Credential(Certificate c, LearningLinks links, LearningIssuer issuer) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "EducationalOccupationalCredential",
        ["name"] = $"{c.BadgeName} — {c.CourseTitle}",
        ["credentialCategory"] = "certificate",
        ["url"] = links.Verify(c.Id),
        ["identifier"] = c.VerificationCode,
        ["image"] = links.BadgeImage(c.CourseSlug),
        ["competencyRequired"] = c.Skills,
        ["dateCreated"] = c.IssuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["recognizedBy"] = Provider(issuer, links),
        ["about"] = new Dictionary<string, object?> { ["@type"] = "Course", ["name"] = c.CourseTitle, ["url"] = links.Course(c.CourseSlug) },
    });
}
