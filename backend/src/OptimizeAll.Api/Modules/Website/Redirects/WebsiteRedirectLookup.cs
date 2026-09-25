using OptimizeAll.Api.Modules.Website.SiteSeo;

namespace OptimizeAll.Api.Modules.Website.Redirects;

/// <summary>
/// Plugs the redirect manager (Website → Redirects) into the server-rendered pages: <c>/_document{path}</c> answers a moved
/// public address with a real 301 to its new address (query parameters carried over), so nginx and the Vite dev/preview
/// server need no separate redirect lookup before rendering. Addresses serving live content, built-in pages and the
/// portals are never redirected (<see cref="RedirectService.ResolveAsync"/>).
/// </summary>
public sealed class WebsiteRedirectLookup(RedirectService redirects) : ISeoRedirectLookup
{
    public async Task<SeoRedirect?> FindAsync(string path, string query, CancellationToken ct) =>
        await redirects.ResolveAsync(query.Length == 0 ? path : path + "?" + query, ct) is { } location
            ? new SeoRedirect(301, location)
            : null;
}
