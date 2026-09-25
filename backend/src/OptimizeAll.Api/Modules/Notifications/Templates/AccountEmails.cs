using System.Net;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Hosting;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Api.Modules.Notifications.Templates;

/// <summary>
/// Sends the Auth module's emails (verification, password reset, duplicate-registration and Google-connected notices)
/// through the editable templates of the <see cref="EmailTemplateCatalog.GroupAccount"/> group. Links are built here, so
/// the Auth module only says which email to send to whom.
/// </summary>
public sealed class AccountEmails(IEmailSender email, EmailTemplateService templates, IPublicOrigin publicOrigin, ILogger<AccountEmails> logger)
{

    public static readonly int VerificationHours = 48;

    public async Task<EmailSendResult> SendVerificationAsync(User user, string rawToken, CancellationToken ct)
    {
        if (await OriginAsync(ct) is not { } origin) return OriginUnknown(user);
        var mail = await templates.RenderAsync(EmailTemplateCatalog.AuthVerifyEmail, new Dictionary<string, string>
        {
            ["verifyUrl"] = $"{origin}{AppLinks.VerifyEmail}?token={WebUtility.UrlEncode(rawToken)}",
            ["hours"] = VerificationHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, ct);
        // The display name is attacker-controlled until the address is verified, so it is not used as the recipient name.
        return await email.SendAsync(new EmailMessage(user.Email, user.Email, mail.Subject, mail.Text), ct);
    }

    public async Task<EmailSendResult> SendPasswordResetAsync(User user, string rawToken, CancellationToken ct)
    {
        if (await OriginAsync(ct) is not { } origin) return OriginUnknown(user);
        var mail = await templates.RenderAsync(EmailTemplateCatalog.AuthPasswordReset, new Dictionary<string, string>
        {
            ["resetUrl"] = $"{origin}{AppLinks.ResetPassword}?token={WebUtility.UrlEncode(rawToken)}",
            ["displayName"] = user.DisplayName,
        }, ct);
        return await email.SendAsync(new EmailMessage(user.Email, user.DisplayName, mail.Subject, mail.Text), ct);
    }

    public async Task<EmailSendResult> SendDuplicateRegistrationAsync(User user, CancellationToken ct)
    {
        if (await OriginAsync(ct) is not { } origin) return OriginUnknown(user);
        var mail = await templates.RenderAsync(EmailTemplateCatalog.AuthDuplicateRegistration, new Dictionary<string, string>
        {
            ["forgotPasswordUrl"] = $"{origin}{AppLinks.ForgotPassword}",
            ["displayName"] = user.DisplayName,
        }, ct);
        return await email.SendAsync(new EmailMessage(user.Email, user.DisplayName, mail.Subject, mail.Text), ct);
    }

    public async Task<EmailSendResult> SendGoogleLinkedAsync(User user, string googleEmail, CancellationToken ct)
    {
        if (await OriginAsync(ct) is not { } origin) return OriginUnknown(user);
        var mail = await templates.RenderAsync(EmailTemplateCatalog.AuthGoogleLinked, new Dictionary<string, string>
        {
            ["googleEmail"] = googleEmail,
            ["forgotPasswordUrl"] = $"{origin}{AppLinks.ForgotPassword}",
            ["displayName"] = user.DisplayName,
        }, ct);
        return await email.SendAsync(new EmailMessage(user.Email, user.DisplayName, mail.Subject, mail.Text), ct);
    }

    /// <summary>The public origin, or null when unknown: a root-relative link in an email is useless, so nothing is sent.</summary>
    private async Task<string?> OriginAsync(CancellationToken ct) => await publicOrigin.GetAsync(ct) is { Length: > 0 } origin ? origin : null;

    private EmailSendResult OriginUnknown(User user)
    {
        var error = new PublicOriginUnknownException().Message;
        logger.LogError("Account email to user {UserId} not sent: {Reason}", user.Id, error);
        return new EmailSendResult(false, null, error);
    }
}
