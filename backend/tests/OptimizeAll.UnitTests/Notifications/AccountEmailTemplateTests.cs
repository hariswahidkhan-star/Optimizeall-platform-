using OptimizeAll.Api.Modules.Notifications.Templates;

namespace OptimizeAll.UnitTests.Notifications;

/// <summary>
/// The Auth module's emails became editable templates. Their defaults must render exactly the texts the Auth module
/// sent before (the expected strings below are the former hard-coded texts, copied verbatim).
/// </summary>
public sealed class AccountEmailTemplateTests
{
    private const string App = "https://app.example.com";

    private static (string Subject, string Text) RenderDefault(string key, Dictionary<string, string> values)
    {
        var d = EmailTemplateCatalog.Find(key)!;
        values["siteName"] = "Optimize All";
        return (EmailTemplateRenderer.RenderText(d.Subject, values), EmailTemplateRenderer.RenderText(d.Body, values));
    }

    [Fact]
    public void Verification_default_matches_the_former_text()
    {
        var link = $"{App}/verify-email?token=abc%2B1";
        var r = RenderDefault(EmailTemplateCatalog.AuthVerifyEmail, new() { ["verifyUrl"] = link, ["hours"] = "48" });
        Assert.Equal("Verify your Optimize All email", r.Subject);
        Assert.Equal(
            $"Welcome to Optimize All!\n\nConfirm your email address to start joining paid campaigns:\n{link}\n\n" +
            "This link expires in 48 hours.", r.Text);
    }

    [Fact]
    public void Password_reset_default_matches_the_former_text()
    {
        var link = $"{App}/reset-password?token=xyz";
        var r = RenderDefault(EmailTemplateCatalog.AuthPasswordReset, new() { ["resetUrl"] = link, ["displayName"] = "Ada" });
        Assert.Equal("Reset your Optimize All password", r.Subject);
        Assert.Equal(
            $"Hi Ada,\n\nUse this link within one hour to choose a new password:\n{link}\n\n" +
            "If you didn't ask for this, you can ignore this email; your password won't change.", r.Text);
    }

    [Fact]
    public void Duplicate_registration_default_matches_the_former_text()
    {
        var r = RenderDefault(EmailTemplateCatalog.AuthDuplicateRegistration,
            new() { ["forgotPasswordUrl"] = $"{App}/forgot-password", ["displayName"] = "Ada" });
        Assert.Equal("Someone tried to register with your email", r.Subject);
        Assert.Equal(
            "Hi Ada,\n\nSomeone tried to create an Optimize All account with this email address. " +
            $"If it was you, sign in or reset your password at {App}/forgot-password.\n\n" +
            "If it wasn't you, you can ignore this message.", r.Text);
    }

    [Fact]
    public void Google_linked_notice_renders_the_google_address_and_recovery_link()
    {
        var r = RenderDefault(EmailTemplateCatalog.AuthGoogleLinked,
            new() { ["forgotPasswordUrl"] = $"{App}/forgot-password", ["displayName"] = "Ada", ["googleEmail"] = "ada@gmail.com" });
        Assert.Equal("Google sign-in was connected to your Optimize All account", r.Subject);
        Assert.StartsWith("Hi Ada,\n\nThe Google account ada@gmail.com can now be used to sign in", r.Text);
        Assert.Contains($"{App}/forgot-password", r.Text);
    }

    [Theory]
    [InlineData(EmailTemplateCatalog.AuthVerifyEmail, "verifyUrl")]
    [InlineData(EmailTemplateCatalog.AuthPasswordReset, "resetUrl")]
    [InlineData(EmailTemplateCatalog.AuthDuplicateRegistration, "forgotPasswordUrl")]
    [InlineData(EmailTemplateCatalog.AuthGoogleLinked, "forgotPasswordUrl")]
    public void Account_emails_require_their_action_link(string key, string variable)
    {
        var d = EmailTemplateCatalog.Find(key)!;
        Assert.Equal(EmailTemplateCatalog.GroupAccount, d.Group);
        Assert.True(d.Variables.Single(v => v.Name == variable).Required);
        Assert.Contains("{{" + variable + "}}", d.Body);
    }

    [Fact]
    public void Verification_email_does_not_offer_the_unverified_display_name()
    {
        var d = EmailTemplateCatalog.Find(EmailTemplateCatalog.AuthVerifyEmail)!;
        Assert.DoesNotContain(d.Variables, v => v.Name == "displayName");
    }
}
