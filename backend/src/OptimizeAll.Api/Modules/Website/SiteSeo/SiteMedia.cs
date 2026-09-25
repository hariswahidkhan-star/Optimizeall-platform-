using System.Text.RegularExpressions;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// Static media shipped with the web app under <c>/media/</c> (frontend/public/media): self-hosted website videos live in
/// <c>/media/videos/</c> as MP4 + WebM + a JPEG/WebP poster + WebVTT captions (docs/SEO_CRO.md § Video).
/// </summary>
public static partial class SiteMedia
{
    public static bool IsSiteMediaPath(string path, string kind) => !path.Contains("..", StringComparison.Ordinal) && !path.Contains("//", StringComparison.Ordinal) && kind switch
    {
        "mp4" => Mp4Regex().IsMatch(path),
        "webm" => WebmRegex().IsMatch(path),
        "vtt" => VttRegex().IsMatch(path),
        _ => ImageRegex().IsMatch(path),
    };

    public static string Message(string kind) => kind switch
    {
        "mp4" => "Use an .mp4 file under /media/ (for example /media/videos/intro.mp4), an upload or an allowed https host.",
        "webm" => "Use a .webm file under /media/ (for example /media/videos/intro.webm), an upload or an allowed https host.",
        "vtt" => "Use a WebVTT captions file (.vtt) under /media/, an upload or an allowed https host.",
        _ => "Use a .jpg, .png or .webp image under /media/, an upload or an allowed https host.",
    };

    [GeneratedRegex(@"^/media/[a-z0-9][a-z0-9/_.-]{0,200}\.mp4$")]
    private static partial Regex Mp4Regex();

    [GeneratedRegex(@"^/media/[a-z0-9][a-z0-9/_.-]{0,200}\.webm$")]
    private static partial Regex WebmRegex();

    [GeneratedRegex(@"^/media/[a-z0-9][a-z0-9/_.-]{0,200}\.vtt$")]
    private static partial Regex VttRegex();

    [GeneratedRegex(@"^/media/[a-z0-9][a-z0-9/_.-]{0,200}\.(jpe?g|png|webp|avif)$")]
    private static partial Regex ImageRegex();
}
