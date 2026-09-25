using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Learning.Admin;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning;

/// <summary>
/// Upserts the course packs shipped with the API (Modules/Learning/Catalog/*.json) on startup ("Baseline" seed profile, so
/// every environment gets the catalog). Idempotent by slug + pack version, under the named lock "learning-catalog"
/// (several API instances may start at once):
/// <list type="bullet">
/// <item>New slug → a pack-origin course with version 1 (pack version N), published.</item>
/// <item>Known slug, higher pack version → a new course version. It is published automatically only while the live
/// version is itself pack-sourced; if staff published an edited version, the pack update is stored but NOT published
/// (the Learning admin shows "pack update available" and staff decide). Nothing is ever overwritten.</item>
/// <item>Same pack version with different content → ignored with a warning (bump <c>version</c> to ship changes).</item>
/// <item>A pack that fails validation (structural rules, <see cref="PackValidationMode.Authoring"/>) is skipped with an error
/// log; CI's <c>CoursePackTests</c> applies every rule (<see cref="PackValidationMode.Strict"/>) so this should not happen.</item>
/// </list>
/// </summary>
public sealed class LearningCatalogSeeder(IDatabaseDialect dialect, TimeProvider clock, ILogger<LearningCatalogSeeder> logger) : ISeeder
{
    public const string LockName = "learning-catalog";

    public string Profile => "Baseline";
    public int Order => 40;

    // Streams the packs one at a time: the whole parsed catalog is never in memory at once.
    public Task SeedAsync(AppDbContext db, CancellationToken ct) => UpsertAsync(db, CoursePackLibrary.Enumerate(), ct);

    /// <summary>
    /// Upserts each pack under its own savepoint, clearing the change tracker after each one (the content JSON of every
    /// course version would otherwise stay tracked until the end). A pack that fails is rolled back, logged and skipped;
    /// the others are still applied.
    /// </summary>
    public async Task<int> UpsertAsync(AppDbContext db, IEnumerable<PackFile> files, CancellationToken ct)
    {
        await using var _ = await dialect.AcquireNamedLockAsync(db, LockName, TimeSpan.FromSeconds(120), ct);
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        int changes = 0, packs = 0, failed = 0;
        foreach (var file in files)
        {
            packs++;
            if (file.Pack is not { } pack)
            {
                logger.LogError("Course pack {File} is not valid JSON: {Error}", file.FileName, file.ParseError);
                continue;
            }
            var issues = CoursePackValidator.Validate(pack, PackValidationMode.Authoring);
            if (issues.Count > 0)
            {
                logger.LogError("Course pack {File} is invalid and was skipped: {Issues}", file.FileName, string.Join("; ", issues.Take(10)));
                continue;
            }
            await tx.CreateSavepointAsync("course_pack", ct);
            try
            {
                if (await UpsertAsync(db, pack, now, ct)) changes++;
                await tx.ReleaseSavepointAsync("course_pack", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                await tx.RollbackToSavepointAsync("course_pack", ct);
                logger.LogError(ex, "Course pack {File} could not be applied and was skipped", file.FileName);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        await tx.CommitAsync(ct);
        logger.LogInformation("Learning catalog: {Packs} course pack(s) checked, {Count} added or updated, {Failed} failed", packs, changes, failed);
        return changes;
    }

    private async Task<bool> UpsertAsync(AppDbContext db, CoursePack pack, DateTime now, CancellationToken ct)
    {
        var course = await db.Set<Course>().FirstOrDefaultAsync(c => c.Slug == pack.Slug, ct);
        if (course is null)
        {
            course = new Course { Slug = pack.Slug, Origin = CourseSource.Pack, SortOrder = 100 };
            CourseVersioning.ApplyListing(course, pack);
            db.Set<Course>().Add(course);
            var first = CourseVersioning.NewVersion(course, pack, 1, CourseSource.Pack, pack.Version, null, $"Course pack v{pack.Version}", null, now);
            db.Set<CourseVersion>().Add(first);
            course.LatestVersionId = first.Id;
            CourseVersioning.Publish(course, first, pack, now);
            await db.SaveChangesAsync(ct);
            return true;
        }
        if (course.Origin != CourseSource.Pack)
        {
            logger.LogWarning("Course pack {Slug} skipped: the slug belongs to a staff-authored course", pack.Slug);
            return false;
        }

        var packVersions = await db.Set<CourseVersion>().Where(v => v.CourseId == course.Id && v.Source == CourseSource.Pack)
            .Select(v => new { v.Id, v.PackVersion, v.ContentSha256 }).ToListAsync(ct);
        var latestPack = packVersions.MaxBy(v => v.PackVersion ?? 0);
        if (latestPack is not null && pack.Version <= (latestPack.PackVersion ?? 0))
        {
            var same = packVersions.FirstOrDefault(v => v.PackVersion == pack.Version);
            var candidate = CourseVersioning.NewVersion(course, pack, 0, CourseSource.Pack, pack.Version, null, null, null, now);
            if (same is not null && same.ContentSha256 != candidate.ContentSha256)
                logger.LogWarning("Course pack {Slug} v{Version} changed without a version bump; the change is ignored (bump \"version\")",
                    pack.Slug, pack.Version);
            return false;
        }

        var publishedSource = course.PublishedVersionId is { } pid
            ? await db.Set<CourseVersion>().Where(v => v.Id == pid).Select(v => (CourseSource?)v.Source).FirstOrDefaultAsync(ct)
            : null;
        var number = await CourseVersioning.NextNumberAsync(db, course.Id, ct);
        var version = CourseVersioning.NewVersion(course, pack, number, CourseSource.Pack, pack.Version, latestPack?.Id,
            $"Course pack v{pack.Version}", null, now);
        db.Set<CourseVersion>().Add(version);
        course.LatestVersionId = version.Id;
        if (publishedSource is null or CourseSource.Pack)
        {
            // Staff may have unpublished the course: keep it unpublished, but point it at the new content.
            var status = course.Status;
            CourseVersioning.Publish(course, version, pack, now);
            if (publishedSource is not null) course.Status = status;
            logger.LogInformation("Course pack {Slug} updated to pack v{Version} (course version {Number})", pack.Slug, pack.Version, number);
        }
        else
        {
            logger.LogWarning("Course pack {Slug} v{Version} stored as course version {Number} but not published: staff published an edited version",
                pack.Slug, pack.Version, number);
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}
