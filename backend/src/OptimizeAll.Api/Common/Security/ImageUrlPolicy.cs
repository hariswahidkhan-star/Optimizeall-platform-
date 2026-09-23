using OptimizeAll.Api.Modules.Accounts;

namespace OptimizeAll.Api.Common.Security;

/// <summary>
/// Which image URLs admin/manager content may reference (campaign hero and image assets, homepage banners). Only
/// uploaded files (<c>/api/v1/files/{id}</c>) and https URLs on hosts in <c>Content:AllowedImageHosts</c> (default
/// empty) are accepted, so every image the web app shows is allowed by its CSP <c>img-src</c> (the nginx
/// <c>IMG_SRC_EXTRA</c> setting must list the same hosts; see docs/DEPLOYMENT.md). Configuration is read on every use
/// so hosts and tests can change it at runtime.
/// </summary>
public sealed class ImageUrlPolicy(IConfiguration configuration)
{
    public const string Section = "Content:AllowedImageHosts";

    public const string Message =
        "Use an uploaded image (/api/v1/files/…) or an https image URL on an allowed image host.";

    public IReadOnlyCollection<string> AllowedHosts =>
        configuration.GetSection(Section).Get<string[]>()?.Where(h => !string.IsNullOrWhiteSpace(h)).ToArray()
        ?? Array.Empty<string>();

    public bool IsAllowed(string? url) => FieldRules.IsAllowedImageUrl(url, AllowedHosts);
}
