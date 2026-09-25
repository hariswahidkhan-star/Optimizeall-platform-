using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace OptimizeAll.Api.Common.Notifications;

public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>"Smtp" sends real mail; "File" writes .eml files to <see cref="PickupDirectory"/> (development/staging demos).</summary>
    public string Mode { get; set; } = "File";
    public string FromAddress { get; set; } = "no-reply@optimizeall.local";
    public string FromName { get; set; } = "Optimize All";
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseStartTls { get; set; } = true;
    public string PickupDirectory { get; set; } = "storage/mail";

    // Email:AppBaseUrl (the web app's public URL) is optional and read only through IPublicOrigin
    // (Common/Hosting/PublicOrigin.cs), which falls back to the site URL setting and the request's origin.
}

public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string TextBody, string? HtmlBody = null);

public sealed record EmailSendResult(bool Success, string? ProviderMessageId, string? Error);

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.SmtpHost))
            return new EmailSendResult(false, null, "SMTP host is not configured.");

        var mime = EmailMime.Build(o, message);
        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(o.SmtpHost, o.SmtpPort, o.SmtpUseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
            if (!string.IsNullOrEmpty(o.SmtpUsername))
                await client.AuthenticateAsync(o.SmtpUsername, o.SmtpPassword ?? string.Empty, ct);
            var response = await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
            return new EmailSendResult(true, mime.MessageId ?? response, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SMTP send to {Domain} failed", message.ToAddress.Split('@').LastOrDefault());
            return new EmailSendResult(false, null, ex.Message);
        }
    }
}

/// <summary>Writes each message as an .eml file. Never used in production (see Program.cs guard).</summary>
public sealed class FileEmailSender(IOptions<EmailOptions> options, IHostEnvironment env, ILogger<FileEmailSender> logger) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        var dir = Path.IsPathRooted(o.PickupDirectory) ? o.PickupDirectory : Path.Combine(env.ContentRootPath, o.PickupDirectory);
        Directory.CreateDirectory(dir);
        var mime = EmailMime.Build(o, message);
        var path = Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");
        await mime.WriteToAsync(path, ct);
        logger.LogInformation("Email '{Subject}' written to {Path}", message.Subject, path);
        return new EmailSendResult(true, Path.GetFileName(path), null);
    }
}

internal static class EmailMime
{
    public static MimeMessage Build(EmailOptions o, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        var body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody };
        mime.Body = body.ToMessageBody();
        return mime;
    }
}
