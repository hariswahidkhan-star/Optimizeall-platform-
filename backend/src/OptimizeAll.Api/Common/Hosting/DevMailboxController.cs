using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MimeKit;
using OptimizeAll.Api.Common.Notifications;

namespace OptimizeAll.Api.Common.Hosting;

public sealed class DevToolsOptions
{
    public const string Section = "DevTools";

    /// <summary>Exposes the file-mode mailbox over HTTP for staging demos and E2E tests. Refused in Production.</summary>
    public bool MailboxEnabled { get; set; }
}

/// <summary>
/// Staging/demo helper: reads emails written by <see cref="FileEmailSender"/> so a demo or E2E test can follow
/// verification and reset links without a real inbox. Returns 404 unless explicitly enabled outside Production.
/// </summary>
[ApiController]
[Route("api/v1/dev/mailbox")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed partial class DevMailboxController(
    IOptions<DevToolsOptions> devTools, IOptions<EmailOptions> email, IHostEnvironment env) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Latest([FromQuery] string to, CancellationToken ct)
    {
        if (!devTools.Value.MailboxEnabled || env.IsProduction() || email.Value.Mode != "File") return NotFound();

        var dir = Path.IsPathRooted(email.Value.PickupDirectory)
            ? email.Value.PickupDirectory
            : Path.Combine(env.ContentRootPath, email.Value.PickupDirectory);
        if (!Directory.Exists(dir)) return NotFound();

        foreach (var file in Directory.GetFiles(dir, "*.eml").OrderByDescending(f => f))
        {
            var message = await MimeMessage.LoadAsync(file, ct);
            if (!message.To.Mailboxes.Any(m => string.Equals(m.Address, to, StringComparison.OrdinalIgnoreCase))) continue;
            var text = message.TextBody ?? string.Empty;
            return Ok(new
            {
                message.Subject,
                Date = message.Date.UtcDateTime,
                Text = text,
                Links = LinkRegex().Matches(text).Select(m => m.Value).ToArray(),
            });
        }
        return NotFound();
    }

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex LinkRegex();
}
