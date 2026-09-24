using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Content.Copy;

/// <summary>Page copy overrides for the public site and portals. Anonymous: the texts are public anyway.</summary>
[ApiController]
[Route("api/v1/content/copy")]
public sealed class PublicCopyController(SiteCopyService copy) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Public)]
    public Task<PublicCopyDto> Get(CancellationToken ct) => copy.GetPublicAsync(ct);
}

/// <summary>Website page copy (home, services, pricing, forms, creators page…): website marketers.</summary>
[ApiController]
[HasPermission(Permissions.SiteManage)]
[Route("api/v1/agency/website/copy")]
public sealed class WebsiteCopyController(SiteCopyService copy) : ControllerBase
{
    [HttpGet]
    public Task<CopyCatalogDto> Get(CancellationToken ct) => copy.GetCatalogAsync(CopyScope.Website, ct);

    [HttpPut]
    public Task<CopyCatalogDto> Update(UpdateCopyRequest request, CancellationToken ct) => copy.UpdateAsync(CopyScope.Website, request, ct);
}

/// <summary>Portal copy (help centre, creator home): platform content editors.</summary>
[ApiController]
[HasPermission(Permissions.ContentManage)]
[Route("api/v1/admin/content/copy")]
public sealed class PortalCopyController(SiteCopyService copy) : ControllerBase
{
    [HttpGet]
    public Task<CopyCatalogDto> Get(CancellationToken ct) => copy.GetCatalogAsync(CopyScope.Portal, ct);

    [HttpPut]
    public Task<CopyCatalogDto> Update(UpdateCopyRequest request, CancellationToken ct) => copy.UpdateAsync(CopyScope.Portal, request, ct);
}
