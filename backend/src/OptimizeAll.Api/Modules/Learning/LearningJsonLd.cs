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

    /// <summary>
    /// The content's modification date: the pack's review month (v2, "2026-09" → 2026-09-01) when it is later than the
    /// course row's update, else the course's last update.
    /// </summary>
    public static string DateModified(CoursePack pack, Course course)
    {
        var updated = course.UpdatedAt.Date;
        if (pack.LastReviewed is { } month &&
            DateTime.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var reviewed) &&
            (reviewed > updated || course.UpdatedAt == default))
            return reviewed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return updated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>What the course teaches (schema.org <c>teaches</c>): its skills plus the tools it teaches hands-on.</summary>
    public static IReadOnlyList<string> Teaches(CoursePack pack) =>
        (pack.Skills ?? new()).Concat(pack.Tools ?? new()).Select(s => s.Trim()).Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The course's credential (badge + certificate) as a schema.org EducationalOccupationalCredential.</summary>
    public static Dictionary<string, object?> BadgeCredential(CoursePack pack, LearningLinks links, LearningIssuer issuer) => new()
    {
        ["@type"] = "EducationalOccupationalCredential",
        ["name"] = pack.Badge?.Name,
        ["description"] = pack.Badge?.Description,
        ["credentialCategory"] = "certificate",
        ["url"] = links.OpenBadgeClass(pack.Slug),
        ["image"] = links.BadgeImage(pack.Slug),
        ["competencyRequired"] = Teaches(pack),
        ["recognizedBy"] = Provider(issuer, links),
    };

    public static JsonElement Course(CoursePack pack, LearningLinks links, LearningIssuer issuer, Course course) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "Course",
        ["@id"] = links.Course(pack.Slug) + "#course",
        ["name"] = pack.Title,
        ["description"] = pack.Subtitle,
        ["abstract"] = PublicLearningService.Truncate(PublicLearningService.PlainText(pack.Description), 500),
        ["url"] = links.Course(pack.Slug),
        ["image"] = links.BadgeImage(pack.Slug),
        ["provider"] = Provider(issuer, links),
        ["publisher"] = Provider(issuer, links),
        ["inLanguage"] = "en",
        ["educationalLevel"] = pack.LevelValue.ToString(),
        ["isAccessibleForFree"] = true,
        ["timeRequired"] = Duration(pack.EstimatedMinutes),
        ["teaches"] = Teaches(pack),
        ["keywords"] = string.Join(", ", Teaches(pack)),
        ["about"] = PublicLearningService.CategoryLabels[pack.CategoryValue],
        ["coursePrerequisites"] = pack.Prerequisites is { Count: > 0 } p ? p.Select(links.Course).ToArray() : null,
        ["educationalCredentialAwarded"] = BadgeCredential(pack, links, issuer),
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
            ["instructor"] = Provider(issuer, links),
        },
        ["syllabusSections"] = (pack.Modules ?? new()).Select(m => new Dictionary<string, object?>
        {
            ["@type"] = "Syllabus",
            ["name"] = m.Title,
            ["description"] = m.Summary,
            ["timeRequired"] = Duration((m.Lessons ?? new()).Sum(l => l.DurationMinutes)),
        }).ToArray(),
        ["numberOfLessons"] = pack.LessonCount,
        ["datePublished"] = course.PublishedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["dateModified"] = DateModified(pack, course),
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
        ["learningResourceType"] = lesson.TypeValue == LessonType.Video || lesson.Lecture?.Src is not null ? "Video lesson" : "Lesson",
        ["educationalLevel"] = pack.LevelValue.ToString(),
        ["timeRequired"] = Duration(lesson.DurationMinutes),
        ["inLanguage"] = "en",
        ["isAccessibleForFree"] = true,
        ["keywords"] = string.Join(", ", pack.Skills ?? new List<string>()),
        ["teaches"] = lesson.KeyTakeaways,
        ["author"] = Provider(issuer, links),
        ["publisher"] = Provider(issuer, links),
        ["datePublished"] = (course.PublishedAt ?? course.UpdatedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["dateModified"] = DateModified(pack, course),
        ["isPartOf"] = new Dictionary<string, object?>
        {
            ["@type"] = "Course",
            ["@id"] = links.Course(pack.Slug) + "#course",
            ["name"] = pack.Title,
            ["url"] = links.Course(pack.Slug),
        },
        // The lecture script is part of the lesson (and server-rendered as a transcript) even before the video exists.
        ["hasPart"] = lesson.Lecture?.Scenes is { Count: > 0 } scenes
            ? scenes.Where(s => s is not null).Select((s, i) => new Dictionary<string, object?>
            {
                ["@type"] = "CreativeWork",
                ["name"] = s.ChapterTitle.Length > 0 ? s.ChapterTitle : $"Part {i + 1}",
                ["position"] = i + 1,
            }).ToArray()
            : null,
    });

    /// <summary>ISO 8601 duration from seconds (PT7M30S).</summary>
    public static string DurationSeconds(int seconds)
    {
        seconds = Math.Max(1, seconds);
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        return "PT" + (h > 0 ? $"{h}H" : string.Empty) + (m > 0 ? $"{m}M" : string.Empty) + (s > 0 ? $"{s}S" : string.Empty);
    }

    /// <summary>
    /// VideoObject for a produced lesson video: the v2 lecture when it has a source (thumbnail = its poster, else the course
    /// badge; duration from the scene plan; transcript = the narration), else a produced v1 video block (only with a poster,
    /// which Google requires). Nothing for lectures that are not produced yet.
    /// </summary>
    public static JsonElement? Video(CoursePack pack, PackLesson lesson, string excerpt, LearningLinks links, Course course)
    {
        var uploaded = (course.PublishedAt ?? course.UpdatedAt).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        if (lesson.Lecture is { Src: { } src } lecture)
        {
            var chapters = new List<Dictionary<string, object?>>();
            var start = 0;
            foreach (var (scene, i) in (lecture.Scenes ?? new()).Where(s => s is not null).Select((s, i) => (s, i)))
            {
                chapters.Add(new Dictionary<string, object?>
                {
                    ["@type"] = "Clip",
                    ["name"] = scene.ChapterTitle.Length > 0 ? scene.ChapterTitle : $"Part {i + 1}",
                    ["startOffset"] = start,
                    ["endOffset"] = start + scene.Seconds,
                    ["url"] = links.Absolute(LearningLinks.LessonPath(pack.Slug, lesson.Slug)) + $"#t={start}",
                });
                start += scene.Seconds;
            }
            return Element(new()
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "VideoObject",
                ["name"] = lecture.Title ?? lesson.Title,
                ["description"] = excerpt,
                ["thumbnailUrl"] = lecture.Poster is { } poster ? links.Absolute(poster) : links.BadgeImage(pack.Slug),
                ["contentUrl"] = links.Absolute(src),
                ["uploadDate"] = uploaded,
                ["duration"] = DurationSeconds(lecture.TotalSeconds),
                ["transcript"] = lecture.Transcript,
                ["inLanguage"] = "en",
                ["isAccessibleForFree"] = true,
                ["hasPart"] = chapters.Count > 0 ? chapters : null,
                ["isPartOf"] = new Dictionary<string, object?> { ["@type"] = "Course", ["@id"] = links.Course(pack.Slug) + "#course", ["name"] = pack.Title },
            });
        }
        if (lesson.TypeValue != LessonType.Video || lesson.Video?.Src is null || lesson.Video.Poster is null) return null;
        return Element(new()
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "VideoObject",
            ["name"] = lesson.Title,
            ["description"] = excerpt,
            ["thumbnailUrl"] = links.Absolute(lesson.Video.Poster),
            ["contentUrl"] = links.Absolute(lesson.Video.Src),
            ["uploadDate"] = uploaded,
            ["duration"] = Duration(lesson.DurationMinutes),
            ["transcript"] = lesson.Video.Script,
            ["isAccessibleForFree"] = true,
        });
    }

    /// <summary>A learning path as a schema.org ItemList of its courses (in order), with the path's credentials.</summary>
    public static JsonElement PathItemList(string name, string description, string path, IEnumerable<(string Slug, string Title, string Subtitle)> courses,
        LearningLinks links, LearningIssuer issuer) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "ItemList",
        ["@id"] = links.Absolute(path) + "#path",
        ["name"] = name,
        ["description"] = description,
        ["url"] = links.Absolute(path),
        ["itemListOrder"] = "https://schema.org/ItemListOrderAscending",
        ["itemListElement"] = courses.Select((c, i) => new Dictionary<string, object?>
        {
            ["@type"] = "ListItem",
            ["position"] = i + 1,
            ["item"] = new Dictionary<string, object?>
            {
                ["@type"] = "Course",
                ["@id"] = links.Course(c.Slug) + "#course",
                ["name"] = c.Title,
                ["description"] = c.Subtitle,
                ["url"] = links.Course(c.Slug),
                ["provider"] = Provider(issuer, links),
                ["offers"] = new Dictionary<string, object?> { ["@type"] = "Offer", ["price"] = 0, ["priceCurrency"] = "USD", ["category"] = "Free" },
            },
        }).ToArray(),
    });

    /// <summary>A plain ItemList of links (the learning paths index).</summary>
    public static JsonElement LinkList(string name, string path, IEnumerable<(string Name, string Path)> items, LearningLinks links) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "ItemList",
        ["name"] = name,
        ["url"] = links.Absolute(path),
        ["itemListElement"] = items.Select((x, i) => new Dictionary<string, object?>
        {
            ["@type"] = "ListItem",
            ["position"] = i + 1,
            ["name"] = x.Name,
            ["url"] = links.Absolute(x.Path),
        }).ToArray(),
    });

    /// <summary>The badges earned along a path, as credentials.</summary>
    public static JsonElement Credential(CoursePack pack, LearningLinks links, LearningIssuer issuer)
    {
        var value = BadgeCredential(pack, links, issuer);
        value["@context"] = "https://schema.org";
        return Element(value);
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
