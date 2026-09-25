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
public sealed class AccountEmails(IEmailSender email, EmailTemplateService templates, IPublicOrigin publicOrigin)
{
    private string AppBaseUrl => publicOrigin.Current;

    public static readonly int VerificationHours = 48;

    public async Task<EmailSendResult> SendVerificationAsync(User user, string rawToken, CancellationToken ct)
    {
        var mail = await templates.RenderAsync(EmailTemplateCatalog.AuthVerifyEmail, new Dictionary<string, string>
        {
            ["verifyUrl"] = $"{AppBaseUrl}{AppLinks.VerifyEmail}?token={WebUtility.UrlEncode(rawToken)}",
            ["hours"] = VerificationHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, ct);
        // The display name is attacker-controlled until the address is verified, so it is not used as the recipient name.
        return await email.SendAsync(new EmailMessage(user.Email, user.Email, mail.Subject, mail.Text), ct);
    }

    public async Task<EmailSendResult> SendPasswordResetAsync(User user, string rawToken, CancellationToken ct)
    {
        var mail = await templates.RenderAsync(EmailTemplateCatalog.AuthPasswordReset, new Dictionary<string, string>
        {
            ["resetUrl"] = $"{AppBaseUrl}{AppLinks.ResetPassword}?token={WebUtility.UrlEncode(rawToken)}",
            ["displayName"] = user.DisplayName,
        }, ct);
        return await email.SendAsync(new EmailMessage(user.Email, user.DisplayName, mail.Subject, mail.Text), ct);
    }

    public async Task<EmailSendResult> SendDuplicateRegistrationAsync(User user, CancellationToken ct)
    {
        var mail = await templates.RenderAsync(EmailTemplateCatalog.AuthDuplicateRegistration, new Dictionary<string, string>
        {
            ["forgotPasswordUrl"] = $"{AppBaseUrl}{AppLinks.ForgotPassword}",
            ["displayName"] = user.DisplayName,
        }, ct);
        return await email.SendAsync(new EmailMessage(user.Email, user.DisplayName, mail.Subject, mail.Text), ct);
    }

    public async Task<EmailSendResult> SendGoogleLinkedAsync(User user, string googleEmail, CancellationToken ct)
    {
        var mail = await templates.RenderAsync(EmailTemplateCatalog.AuthGoogleLinked, new Dictionary<string, string>
        {
            ["googleEmail"] = googleEmail,
            ["forgotPasswordUrl"] = $"{AppBaseUrl}{AppLinks.ForgotPassword}",
            ["displayName"] = user.DisplayName,
        }, ct);
        return await email.SendAsync(new EmailMessage(user.Email, user.DisplayName, mail.Subject, mail.Text), ct);
    }
}
