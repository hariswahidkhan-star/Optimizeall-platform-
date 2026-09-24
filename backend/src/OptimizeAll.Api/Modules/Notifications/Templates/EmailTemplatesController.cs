using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Notifications.Templates;

/// <summary>Editable transactional email templates: list, edit, preview with sample values, reset to default.</summary>
[ApiController]
[HasPermission(Permissions.ContentManage)]
[Route("api/v1/admin/email-templates")]
public sealed class EmailTemplatesController(EmailTemplateService templates) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<EmailTemplateSummaryDto>> List(CancellationToken ct) => templates.ListAsync(ct);

    [HttpGet("{key}")]
    public Task<EmailTemplateDto> Get(string key, CancellationToken ct) => templates.GetAsync(key, ct);

    [HttpPut("{key}")]
    public Task<EmailTemplateDto> Update(string key, UpdateEmailTemplateRequest request, CancellationToken ct) =>
        templates.UpdateAsync(key, request, ct);

    /// <summary>Removes the override so the shipped default applies again.</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Reset(string key, [FromQuery] Guid? concurrencyStamp, CancellationToken ct)
    {
        await templates.ResetAsync(key, concurrencyStamp, ct);
        return NoContent();
    }

    [HttpPost("{key}/preview")]
    public Task<EmailPreviewDto> Preview(string key, EmailTemplateInput input, CancellationToken ct) => templates.PreviewAsync(key, input, ct);
}
