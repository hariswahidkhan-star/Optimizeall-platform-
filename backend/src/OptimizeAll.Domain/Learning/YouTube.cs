using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.Learning;

/// <summary>
/// YouTube-hosted lectures (owner decision 2026-09: lectures are published on YouTube and embedded in each lesson). The
/// accepted URL forms are https://www.youtube.com/watch?v=ID, https://youtu.be/ID and
/// https://www.youtube-nocookie.com/embed/ID with an 11-character id; the page embeds the privacy-enhanced player.
/// </summary>
public static partial class YouTube
{
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex IdRegex();

    private static readonly HashSet<string> WatchHosts = new(StringComparer.OrdinalIgnoreCase) { "www.youtube.com", "youtube.com", "m.youtube.com" };
    private static readonly HashSet<string> EmbedHosts = new(StringComparer.OrdinalIgnoreCase) { "www.youtube-nocookie.com", "youtube-nocookie.com" };

    public static bool IsId(string? id) => id is not null && IdRegex().IsMatch(id);

    /// <summary>True for any URL on a YouTube host (valid form or not): such a URL must be one of the accepted forms.</summary>
    public static bool IsYouTubeHost(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (WatchHosts.Contains(uri.Host) || EmbedHosts.Contains(uri.Host) || uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase));

    /// <summary>The video id of an accepted YouTube URL (https only), or null.</summary>
    public static string? IdFrom(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        var path = uri.AbsolutePath.TrimEnd('/');
        string? id = null;
        if (WatchHosts.Contains(uri.Host) && path == "/watch")
            id = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0] == "v").Select(p => p[1]).FirstOrDefault();
        else if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) && path.Count(c => c == '/') == 1)
            id = path[1..];
        else if (EmbedHosts.Contains(uri.Host) && path.StartsWith("/embed/", StringComparison.Ordinal) && path.Count(c => c == '/') == 2)
            id = path["/embed/".Length..];
        return IsId(id) ? id : null;
    }

    /// <summary>The privacy-enhanced embed (no cookies until the visitor plays).</summary>
    public static string EmbedUrl(string id) => $"https://www.youtube-nocookie.com/embed/{id}";

    public static string WatchUrl(string id) => $"https://www.youtube.com/watch?v={id}";

    /// <summary>A thumbnail on i.ytimg.com (hqdefault always exists; maxresdefault only for HD uploads).</summary>
    public static string Thumbnail(string id, string size = "hqdefault") => $"https://i.ytimg.com/vi/{id}/{size}.jpg";
}
