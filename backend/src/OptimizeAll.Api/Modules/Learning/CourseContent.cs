using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning;

/// <summary>A course pack file shipped with the API (embedded resource from Modules/Learning/Catalog).</summary>
public sealed record PackFile(string FileName, string Json, CoursePack? Pack, string? ParseError)
{
    public string Sha256 => CourseDocument.Hash(Json);
}

/// <summary>
/// The course packs compiled into the API (<c>Modules/Learning/Catalog/*.json</c>, ≈ 6.5 MB of JSON for 52 packs).
/// The runtime streams them one at a time (<see cref="Enumerate"/>) and keeps only a slug → file-name index, so the
/// parsed catalog is never held in memory for the life of the process (Render starter has 512 MB).
/// </summary>
public static class CoursePackLibrary
{
    private const string Prefix = "OptimizeAll.Learning.Catalog.";
    private static readonly Lazy<IReadOnlyList<PackFile>> Files = new(() => Enumerate().ToList());
    private static readonly Lazy<IReadOnlyDictionary<string, string>> FileNamesBySlug = new(() =>
        Enumerate().Where(f => f.Pack is not null)
            .GroupBy(f => f.Pack!.Slug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().FileName, StringComparer.Ordinal));

    /// <summary>Every pack, parsed and cached. For tests and tools: the API itself uses <see cref="Enumerate"/>.</summary>
    public static IReadOnlyList<PackFile> All => Files.Value;

    /// <summary>The pack file names in catalog order, without reading them.</summary>
    public static IReadOnlyList<string> Names => ResourceNames().Select(n => n[Prefix.Length..]).ToList();

    /// <summary>Reads and parses the packs one at a time (each can be collected once the caller moves on).</summary>
    public static IEnumerable<PackFile> Enumerate()
    {
        foreach (var name in ResourceNames())
            yield return Read(name);
    }

    /// <summary>The file a pack-origin course comes from, or null.</summary>
    public static string? FileNameForSlug(string slug) => FileNamesBySlug.Value.GetValueOrDefault(slug);

    private static IEnumerable<string> ResourceNames() => typeof(CoursePackLibrary).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal);

    private static PackFile Read(string name)
    {
        using var stream = typeof(CoursePackLibrary).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();
        var (pack, error) = CoursePack.Parse(json);
        return new PackFile(name[Prefix.Length..], json, pack, error);
    }
}

/// <summary>A parsed course version with the lookups the services need.</summary>
public sealed class CourseDocument
{
    public CourseDocument(Guid versionId, CoursePack pack)
    {
        VersionId = versionId;
        Pack = pack;
        Lessons = pack.AllLessons.Select((x, i) => new LessonRef(x.Module, x.Lesson, i)).ToList();
        LessonsBySlug = Lessons.ToDictionary(l => l.Lesson.Slug, StringComparer.Ordinal);
        Pool = (pack.FinalExam?.Pool ?? new List<PackQuestion>()).ToDictionary(q => q.Id, StringComparer.Ordinal);
    }

    public Guid VersionId { get; }
    public CoursePack Pack { get; }
    public IReadOnlyList<LessonRef> Lessons { get; }
    public IReadOnlyDictionary<string, LessonRef> LessonsBySlug { get; }
    public IReadOnlyDictionary<string, PackQuestion> Pool { get; }

    public static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
}

public sealed record LessonRef(PackModule Module, PackLesson Lesson, int Index);

/// <summary>
/// Parsed course versions by id. Versions are immutable, so a parsed document is cached for the life of the process
/// (bounded: least recently used entries are dropped beyond <see cref="Capacity"/>). Catalog and list endpoints never
/// load documents; they read the listing columns of <see cref="Course"/>.
/// </summary>
public sealed class CourseContentCache
{
    public const int Capacity = 256;
    private readonly ConcurrentDictionary<Guid, (CourseDocument Doc, long Touched)> _items = new();
    private long _clock;

    public async Task<CourseDocument> GetAsync(AppDbContext db, Guid versionId, CancellationToken ct)
    {
        if (_items.TryGetValue(versionId, out var hit))
        {
            _items[versionId] = (hit.Doc, Interlocked.Increment(ref _clock));
            return hit.Doc;
        }
        var json = await db.Set<CourseVersion>().AsNoTracking().Where(v => v.Id == versionId).Select(v => v.ContentJson).FirstOrDefaultAsync(ct)
                   ?? throw DomainException.NotFound("Course");
        var (pack, error) = CoursePack.Parse(json);
        if (pack is null) throw new InvalidOperationException($"Course version {versionId} is not a valid course document: {error}");
        var doc = new CourseDocument(versionId, pack);
        _items[versionId] = (doc, Interlocked.Increment(ref _clock));
        if (_items.Count > Capacity)
            foreach (var old in _items.OrderBy(kv => kv.Value.Touched).Take(_items.Count - Capacity).ToList())
                _items.TryRemove(old.Key, out _);
        return doc;
    }
}
