using System.Text.Json;
using OptimizeAll.Api.Modules.Website.Pages;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>A video placed on a built-in page in code (see <c>site-videos.json</c>).</summary>
public sealed record SiteVideoEntry(string Path, VideoBlock Block, DateOnly UploadDate);

/// <summary>
/// Videos on built-in public pages, loaded from the embedded <c>site-videos.json</c> (kept identical to the web app's
/// <c>siteVideos.json</c>; SiteVideoCatalogTests enforces it). Entries point at self-hosted files in <c>/media/videos/</c>.
/// </summary>
public static class SiteVideoCatalog
{
    public const string ResourceName = "OptimizeAll.SiteVideos.json";

    private static readonly Lazy<IReadOnlyList<SiteVideoEntry>> LazyEntries = new(Load);

    public static IReadOnlyList<SiteVideoEntry> Entries => LazyEntries.Value;

    public static IEnumerable<SiteVideoEntry> ForPath(string path) => Entries.Where(e => e.Path == path);

    public static IReadOnlyList<SiteVideoEntry> Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<Document>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                  ?? throw new InvalidOperationException("The site video catalog is empty.");
        return (doc.Videos ?? new List<Entry>()).Select(e =>
        {
            if (string.IsNullOrWhiteSpace(e.Path) || !e.Path.StartsWith('/')) throw new InvalidOperationException($"Video '{e.Title}' needs a page path.");
            if (string.IsNullOrWhiteSpace(e.Title)) throw new InvalidOperationException("Every video needs a title.");
            if (e.Mp4Url is null && e.WebmUrl is null) throw new InvalidOperationException($"Video '{e.Title}' needs an MP4 or WebM file.");
            if (e.PosterUrl is null) throw new InvalidOperationException($"Video '{e.Title}' needs a poster image.");
            if (e.CaptionsUrl is null) throw new InvalidOperationException($"Video '{e.Title}' needs a WebVTT captions file.");
            foreach (var (url, kind) in new[] { (e.Mp4Url, "mp4"), (e.WebmUrl, "webm"), (e.PosterUrl, "image"), (e.CaptionsUrl, "vtt") })
                if (url is not null && url.StartsWith('/') && !SiteMedia.IsSiteMediaPath(url, kind))
                    throw new InvalidOperationException($"Video '{e.Title}': {SiteMedia.Message(kind)}");
            return new SiteVideoEntry(e.Path, new VideoBlock(e.Title, e.Description, e.Mp4Url, e.WebmUrl, e.PosterUrl, e.CaptionsUrl,
                e.CaptionsLanguage ?? "en", e.DurationSeconds, e.UploadDate, e.Transcript), e.UploadDate ?? new DateOnly(2026, 1, 1));
        }).ToList();
    }

    private static IReadOnlyList<SiteVideoEntry> Load()
    {
        using var stream = typeof(SiteVideoCatalog).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private sealed class Document
    {
        public List<Entry>? Videos { get; set; }
    }

    private sealed class Entry
    {
        public string Path { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Mp4Url { get; set; }
        public string? WebmUrl { get; set; }
        public string? PosterUrl { get; set; }
        public string? CaptionsUrl { get; set; }
        public string? CaptionsLanguage { get; set; }
        public int? DurationSeconds { get; set; }
        public DateOnly? UploadDate { get; set; }
        public string? Transcript { get; set; }
    }
}
