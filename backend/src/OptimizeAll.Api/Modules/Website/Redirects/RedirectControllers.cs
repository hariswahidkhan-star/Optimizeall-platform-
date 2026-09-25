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
/// Public redirect lookup. <c>GET /public/redirects?path=</c> answers the web app when a public page is not found during
/// client-side navigation (it then navigates to the new address). Full page loads get a real <c>301</c> from the
/// server-rendered page itself (<c>/_document{path}</c> via <see cref="WebsiteRedirectLookup"/>; nginx @document and the
/// Vite dev/preview server's seoShell plugin).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/public/redirects")]
public sealed class PublicRedirectsController(RedirectService redirects) : ControllerBase
{
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
}
