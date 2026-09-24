using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Automations;
using OptimizeAll.Api.Modules.EmailMarketing.Templates;

namespace OptimizeAll.Api.Modules.EmailMarketing;

/// <summary>
/// Restore/duplicate/delete actions and workspace tag &amp; custom-field management (<c>email.manage</c>). Archived lists,
/// templates and journeys can be listed and restored; tags and fields can be renamed (merging) or removed from every contact.
/// </summary>
[ApiController]
[Route("api/v1/agency/email")]
[HasPermission(Permissions.EmailManage)]
public sealed class EmailManageController(
    AudienceService audiences, AudienceCatalogService catalog, TemplateService templates, AutomationService automations) : ControllerBase
{
    // ----- Lists & templates -----

    [HttpPost("lists/{id:guid}/restore")]
    public Task<EmailListDto> RestoreList(Guid id, CancellationToken ct) => audiences.RestoreListAsync(id, ct);

    /// <summary>Templates including archived ones (the regular list endpoint hides them).</summary>
    [HttpGet("templates/all")]
    public Task<IReadOnlyList<TemplateListItem>> AllTemplates([FromQuery] Guid? clientId, [FromQuery] bool includeGlobal = true, CancellationToken ct = default) =>
        templates.ListAsync(clientId, includeGlobal, ct, includeArchived: true);

    [HttpPost("templates/{id:guid}/restore")]
    public Task<TemplateDto> RestoreTemplate(Guid id, CancellationToken ct) => templates.RestoreAsync(id, ct);

    // ----- Journeys -----

    [HttpGet("automations/all")]
    public Task<IReadOnlyList<AutomationListItem>> AllAutomations([FromQuery] Guid? clientId, CancellationToken ct) =>
        automations.ListAsync(clientId, ct, includeArchived: true);

    [HttpPost("automations/{id:guid}/restore")]
    public Task<AutomationDto> RestoreAutomation(Guid id, CancellationToken ct) => automations.RestoreAsync(id, ct);

    [HttpPost("automations/{id:guid}/duplicate")]
    public Task<AutomationDto> DuplicateAutomation(Guid id, CancellationToken ct) => automations.DuplicateAsync(id, ct);

    [HttpDelete("automations/{id:guid}")]
    public async Task<IActionResult> DeleteAutomation(Guid id, CancellationToken ct)
    {
        await automations.DeleteAsync(id, ct);
        return NoContent();
    }

    // ----- Tags & custom fields -----

    [HttpGet("tags")]
    public Task<IReadOnlyList<AudienceKeyDto>> Tags([FromQuery] Guid? clientId, CancellationToken ct) => catalog.TagsAsync(clientId, ct);

    [HttpPost("tags/rename")]
    public Task<RenameResult> RenameTag(RenameKeyRequest request, CancellationToken ct) => catalog.RenameTagAsync(request, ct);

    [HttpDelete("tags")]
    public async Task<IActionResult> DeleteTag([FromQuery] Guid? clientId, [FromQuery] string tag, CancellationToken ct) =>
        Ok(new { removed = await catalog.DeleteTagAsync(clientId, tag, ct) });

    [HttpGet("fields")]
    public Task<IReadOnlyList<AudienceKeyDto>> Fields([FromQuery] Guid? clientId, CancellationToken ct) => catalog.FieldsAsync(clientId, ct);

    [HttpPost("fields/rename")]
    public Task<RenameResult> RenameField(RenameKeyRequest request, CancellationToken ct) => catalog.RenameFieldAsync(request, ct);

    [HttpDelete("fields")]
    public async Task<IActionResult> DeleteField([FromQuery] Guid? clientId, [FromQuery] string key, CancellationToken ct) =>
        Ok(new { removed = await catalog.DeleteFieldAsync(clientId, key, ct) });
}
