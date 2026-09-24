using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Website.Redirects;

/// <summary>
/// Website → Redirects (<c>site.manage</c>): automatic redirects recorded when live addresses change, plus manual ones.
/// Adding and deleting are denied while impersonating: like the site settings, redirects decide where every visitor
/// (and search engine) of a public address ends up.
/// </summary>
[ApiController]
[HasPermission(Permissions.SiteManage)]
[Route("api/v1/agency/website/redirects")]
public sealed class WebsiteRedirectsController(RedirectService redirects) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<RedirectDto>> List([FromQuery] RedirectQuery query, CancellationToken ct) => redirects.ListAsync(query, ct);

    [HttpPost]
    [DeniedWhileImpersonating]
    public async Task<IActionResult> Create(RedirectInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await redirects.CreateAsync(input, ct));

    [HttpDelete("{id:guid}")]
    [DeniedWhileImpersonating]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await redirects.DeleteAsync(id, ct);
        return NoContent();
    }
}

public sealed record RedirectLookupDto(string Location, int StatusCode);

/// <summary>
/// Public redirect lookups. <c>GET /public/redirects?path=</c> answers the web app when a public page is not found (it
/// then navigates client-side); <c>GET /public/redirects/gate</c> answers the web server before it serves the app shell,
/// with a real <c>301</c> (see frontend/nginx/default.conf.template and the Vite dev/preview server).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/public/redirects")]
public sealed class PublicRedirectsController(RedirectService redirects) : ControllerBase
{
    /// <summary>Header carrying the original request target ("/path?query") when the web server asks the gate.</summary>
    public const string OriginalUriHeader = "X-Original-URI";

    /// <summary>Where an old public address now lives (404 <c>website.redirect_not_found</c> when it is not redirected).</summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.Public)]
    public async Task<RedirectLookupDto> Lookup([FromQuery] string? path, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var location = await redirects.ResolveAsync(path, ct)
                       ?? throw new DomainException("website.redirect_not_found", "This address is not redirected.", DomainErrorKind.NotFound);
        return new RedirectLookupDto(location, StatusCodes.Status301MovedPermanently);
    }

    /// <summary>
    /// For the web server: <c>301</c> with <c>Location</c> when the address in <c>X-Original-URI</c> is redirected, else an
    /// empty <c>404</c> (the web server then serves the app shell). Same limits as the tracking redirects: it runs on
    /// every full page load of the public site.
    /// </summary>
    [HttpGet("gate")]
    [HttpHead("gate")]
    [EnableRateLimiting(RateLimitPolicies.Tracking)]
    public async Task<IActionResult> Gate(CancellationToken ct)
    {
        var location = await redirects.ResolveAsync(Request.Headers[OriginalUriHeader].ToString(), ct);
        if (location is null) return NotFound();
        // Browsers may cache a 301 indefinitely; an hour keeps a mistaken redirect correctable.
        Response.Headers.CacheControl = "public, max-age=3600";
        return RedirectPermanent(location);
    }
}
