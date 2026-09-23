using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Modules.Notifications;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;

namespace OptimizeAll.UnitTests.Notifications;

public sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return respond(request, body);
    }
}

public sealed class WhatsAppChannelSenderTests
{
    private static readonly WhatsAppOptions Configured = new()
    {
        Enabled = true, PhoneNumberId = "1098765", AccessToken = "secret-token", TemplateName = "oa_notification", TemplateLanguage = "en",
        ApiBaseUrl = "https://graph.facebook.com/v20.0",
    };

    private static User OptedIn() => new() { Email = "u@example.test", DisplayName = "U", WhatsAppNumber = "+923001234567", WhatsAppOptIn = true };

    private static Notification Note(string body = "Your post was approved.\nWell done!") =>
        new() { Type = NotificationTypes.SubmissionDecision, Title = "Approved", Body = body, LinkUrl = "/app/submissions" };

    private static (WhatsAppChannelSender Sender, FakeHandler Handler) Create(WhatsAppOptions options, HttpStatusCode status, string response)
    {
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") });
        return (new WhatsAppChannelSender(new HttpClient(handler), Options.Create(options), NullLogger<WhatsAppChannelSender>.Instance), handler);
    }

    [Fact]
    public async Task Configured_success_posts_a_template_message_and_returns_the_message_id()
    {
        var (sender, handler) = Create(Configured, HttpStatusCode.OK,
            """{"messaging_product":"whatsapp","contacts":[{"input":"923001234567","wa_id":"923001234567"}],"messages":[{"id":"wamid.HBgM123"}]}""");

        var result = await sender.SendAsync(new NotificationDelivery(), Note(), OptedIn(), CancellationToken.None);

        Assert.Equal(ChannelSendStatus.Sent, result.Status);
        Assert.Equal("wamid.HBgM123", result.ProviderMessageId);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://graph.facebook.com/v20.0/1098765/messages", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", request.Headers.Authorization.Parameter);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("923001234567", root.GetProperty("to").GetString());
        Assert.Equal("template", root.GetProperty("type").GetString());
        var template = root.GetProperty("template");
        Assert.Equal("oa_notification", template.GetProperty("name").GetString());
        Assert.Equal("en", template.GetProperty("language").GetProperty("code").GetString());
        var parameters = template.GetProperty("components")[0].GetProperty("parameters");
        Assert.Equal("Approved", parameters[0].GetProperty("text").GetString());
        Assert.Equal("Your post was approved. Well done!", parameters[1].GetProperty("text").GetString()); // no new lines allowed
    }

    [Fact]
    public async Task Body_parameter_is_truncated_to_1024_characters()
    {
        var (sender, handler) = Create(Configured, HttpStatusCode.OK, """{"messages":[{"id":"wamid.1"}]}""");
        await sender.SendAsync(new NotificationDelivery(), Note(new string('a', 3000)), OptedIn(), CancellationToken.None);
        using var json = JsonDocument.Parse(handler.Requests[0].Body);
        var text = json.RootElement.GetProperty("template").GetProperty("components")[0].GetProperty("parameters")[1].GetProperty("text").GetString()!;
        Assert.Equal(1024, text.Length);
    }

    [Fact]
    public async Task Api_errors_fail_with_status_and_truncated_body()
    {
        var error = """{"error":{"message":"(#131030) Recipient phone number not in allowed list","type":"OAuthException","code":131030}}""" + new string('x', 2000);
        var (sender, _) = Create(Configured, HttpStatusCode.BadRequest, error);
        var result = await sender.SendAsync(new NotificationDelivery(), Note(), OptedIn(), CancellationToken.None);
        Assert.Equal(ChannelSendStatus.Failed, result.Status);
        Assert.Contains("400", result.Error);
        Assert.Contains("131030", result.Error);
        Assert.True(result.Error!.Length < 700);
    }

    [Fact]
    public async Task Success_status_without_a_message_id_is_a_failure()
    {
        var (sender, _) = Create(Configured, HttpStatusCode.OK, """{"messages":[]}""");
        var result = await sender.SendAsync(new NotificationDelivery(), Note(), OptedIn(), CancellationToken.None);
        Assert.Equal(ChannelSendStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Network_errors_fail()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("connection refused"));
        var sender = new WhatsAppChannelSender(new HttpClient(handler), Options.Create(Configured), NullLogger<WhatsAppChannelSender>.Instance);
        var result = await sender.SendAsync(new NotificationDelivery(), Note(), OptedIn(), CancellationToken.None);
        Assert.Equal(ChannelSendStatus.Failed, result.Status);
        Assert.Contains("connection refused", result.Error);
    }

    [Theory]
    [InlineData(false, "1", "t", "tpl")]
    [InlineData(true, null, "t", "tpl")]
    [InlineData(true, "1", "", "tpl")]
    [InlineData(true, "1", "t", null)]
    public async Task Not_configured_is_skipped_without_any_http_call(bool enabled, string? phoneId, string? token, string? template)
    {
        var options = new WhatsAppOptions { Enabled = enabled, PhoneNumberId = phoneId, AccessToken = token, TemplateName = template };
        var (sender, handler) = Create(options, HttpStatusCode.OK, """{"messages":[{"id":"x"}]}""");
        var result = await sender.SendAsync(new NotificationDelivery(), Note(), OptedIn(), CancellationToken.None);
        Assert.Equal(ChannelSendStatus.Skipped, result.Status);
        Assert.Equal("WhatsApp Business credentials are not configured", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Users_without_opt_in_are_skipped()
    {
        var (sender, handler) = Create(Configured, HttpStatusCode.OK, """{"messages":[{"id":"x"}]}""");
        var user = OptedIn();
        user.WhatsAppOptIn = false;
        var result = await sender.SendAsync(new NotificationDelivery(), Note(), user, CancellationToken.None);
        Assert.Equal(ChannelSendStatus.Skipped, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Suspended_users_only_receive_essential_messages()
    {
        var (sender, handler) = Create(Configured, HttpStatusCode.OK, """{"messages":[{"id":"x"}]}""");
        var user = OptedIn();
        user.Status = UserStatus.Suspended;
        Assert.Equal(ChannelSendStatus.Skipped, (await sender.SendAsync(new NotificationDelivery(), Note(), user, CancellationToken.None)).Status);
        var essential = Note();
        essential.Type = NotificationTypes.AccountStatusChanged;
        Assert.Equal(ChannelSendStatus.Sent, (await sender.SendAsync(new NotificationDelivery(), essential, user, CancellationToken.None)).Status);
        Assert.Single(handler.Requests);
    }
}

public sealed class EmailComposeTests
{
    [Fact]
    public void Html_part_encodes_user_content_and_links_use_the_app_base_url()
    {
        var user = new User { Email = "u@example.test", DisplayName = "<b>Sara</b>" };
        var n = new Notification { Title = "Reply on <script>alert(1)</script>", Body = "Line 1\nLine \"2\"", LinkUrl = "/app/support/1" };
        var message = EmailChannelSender.Compose(n, user, "https://app.example.com/");

        Assert.Equal("Reply on <script>alert(1)</script>", message.Subject); // subjects are plain text
        Assert.DoesNotContain("<script>", message.HtmlBody);
        Assert.DoesNotContain("<b>Sara</b>", message.HtmlBody);
        Assert.Contains("&lt;b&gt;Sara&lt;/b&gt;", message.HtmlBody);
        Assert.Contains("https://app.example.com/app/support/1", message.TextBody);
        Assert.Contains("href=\"https://app.example.com/app/support/1\"", message.HtmlBody);
    }

    [Theory]
    [InlineData("/app/x", "https://app.example.com/app/x")]
    [InlineData("https://docs.example.com/x", "https://docs.example.com/x")]
    [InlineData("//evil.example.com", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData(null, null)]
    public void Links(string? link, string? expected) => Assert.Equal(expected, EmailChannelSender.BuildLink("https://app.example.com", link));
}

public sealed class DispatchBackoffTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 30)]
    [InlineData(4, 120)]
    [InlineData(5, 720)]
    public void Backoff_schedule(int attempts, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), NotificationDispatchJob.RetryDelay(attempts));

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void Fails_permanently_after_six_attempts(int attempts) => Assert.Null(NotificationDispatchJob.RetryDelay(attempts));
}
