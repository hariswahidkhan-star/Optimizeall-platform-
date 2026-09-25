using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning.Admin;

/// <summary>Shared rules for creating and publishing immutable course versions (catalog upsert and the Learning admin).</summary>
public static class CourseVersioning
{
    /// <summary>Copies the listing metadata of a version onto the course (catalog queries never read documents).</summary>
    public static void ApplyListing(Course course, CoursePack pack)
    {
        course.Title = pack.Title.Trim();
        course.Subtitle = pack.Subtitle.Trim();
        course.Category = pack.CategoryValue;
        course.Level = pack.LevelValue;
        course.EstimatedMinutes = pack.EstimatedMinutes;
        course.ModuleCount = pack.Modules?.Count ?? 0;
        course.LessonCount = pack.LessonCount;
        course.BadgeName = pack.Badge?.Name.Trim() ?? string.Empty;
        course.Skills = (pack.Skills ?? new()).Select(s => s.Trim()).ToList();
    }

    public static async Task<int> NextNumberAsync(AppDbContext db, Guid courseId, CancellationToken ct) =>
        (await db.Set<CourseVersion>().Where(v => v.CourseId == courseId).MaxAsync(v => (int?)v.Number, ct) ?? 0) + 1;

    /// <summary>Stages a new immutable version (the document's slug/version are normalized to the course and number).</summary>
    public static CourseVersion NewVersion(Course course, CoursePack pack, int number, CourseSource source, int? packVersion,
        Guid? basedOn, string? note, Guid? createdBy, DateTime now)
    {
        var document = pack.Clone();
        document.Slug = course.Slug;
        if (source == CourseSource.Admin) document.Version = number;
        var json = document.ToJson();
        return new CourseVersion
        {
            CourseId = course.Id, Number = number, Source = source, PackVersion = packVersion, BasedOnVersionId = basedOn,
            ContentJson = json, ContentSha256 = CourseDocument.Hash(json), Note = note, CreatedAt = now, CreatedByUserId = createdBy,
        };
    }

    public static void Publish(Course course, CourseVersion version, CoursePack pack, DateTime now)
    {
        course.PublishedVersionId = version.Id;
        course.Status = CourseStatus.Published;
        course.PublishedAt ??= now;
        ApplyListing(course, pack);
    }
}
