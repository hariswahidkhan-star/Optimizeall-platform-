using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Api.Modules.EmailMarketing.Tracking;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.UnitTests.Notifications;

namespace OptimizeAll.UnitTests.EmailMarketing;

/// <summary>In-memory credential vault for adapter tests.</summary>
public sealed class FakeVault : ICredentialVault
{
    private readonly Dictionary<string, IntegrationCredentials> _byProvider = new();
    public List<(Guid Id, IntegrationStatus Status)> Marked { get; } = new();

    public FakeVault With(string provider, Dictionary<string, string> settings, Dictionary<string, string> secrets)
    {
        _byProvider[provider] = new IntegrationCredentials(Guid.NewGuid(), provider, null, settings, secrets, IntegrationStatus.Connected);
        return this;
    }

    public Task<IntegrationCredentials?> GetAsync(string provider, Guid? clientAccountId, CancellationToken ct = default) =>
        Task.FromResult(_byProvider.TryGetValue(provider, out var c) ? c : null);

    public string Protect(IReadOnlyDictionary<string, string> secrets) => JsonSerializer.Serialize(secrets);
    public IReadOnlyDictionary<string, string> Unprotect(string encrypted) => JsonSerializer.Deserialize<Dictionary<string, string>>(encrypted)!;

    public Task MarkStatusAsync(Guid connectionId, IntegrationStatus status, string? message, CancellationToken ct = default)
    {
        Marked.Add((connectionId, status));
        return Task.CompletedTask;
    }
}

public sealed class ProviderAdapterTests
{
    private static OutboundEmail Email() => new(null, "ann@x.test", "Ann", "news@brand.test", "Brand", "help@brand.test", "Hello", "<p>Hi</p>", "Hi",
        new Dictionary<string, string> { ["List-Unsubscribe"] = "<https://app.test/e/u/t>", ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click" },
        new Dictionary<string, string> { ["oa_ref"] = "c:abc" });

    private static FakeHandler Respond(HttpStatusCode status, string body, Action<HttpResponseMessage>? configure = null) => new((_, _) =>
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        configure?.Invoke(response);
        return response;
    });

    // ---------- SendGrid ----------

    [Fact]
    public async Task SendGrid_reports_not_configured_without_credentials_and_makes_no_request()
    {
        var handler = Respond(HttpStatusCode.Accepted, "");
        var provider = new SendGridEmailProvider(new HttpClient(handler), new FakeVault(), NullLogger<SendGridEmailProvider>.Instance);
        var result = await provider.SendAsync(Email(), default);
        Assert.Equal(ProviderOutcome.NotConfigured, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendGrid_sends_v3_payload_and_returns_the_message_id()
    {
        var handler = Respond(HttpStatusCode.Accepted, "", r => r.Headers.Add("X-Message-Id", "sg-123"));
        var vault = new FakeVault().With("sendgrid", new(), new() { ["apiKey"] = "SG.key" });
        var result = await new SendGridEmailProvider(new HttpClient(handler), vault, NullLogger<SendGridEmailProvider>.Instance).SendAsync(Email(), default);
        Assert.Equal(ProviderOutcome.Accepted, result.Outcome);
        Assert.Equal("sg-123", result.MessageId);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.sendgrid.com/v3/mail/send", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("SG.key", request.Headers.Authorization.Parameter);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("ann@x.test", root.GetProperty("personalizations")[0].GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("c:abc", root.GetProperty("personalizations")[0].GetProperty("custom_args").GetProperty("oa_ref").GetString());
        Assert.Equal("List-Unsubscribe=One-Click", root.GetProperty("headers").GetProperty("List-Unsubscribe-Post").GetString());
        Assert.False(root.GetProperty("tracking_settings").GetProperty("click_tracking").GetProperty("enable").GetBoolean());
        Assert.Equal("text/plain", root.GetProperty("content")[0].GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ProviderOutcome.PermanentFailure)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderOutcome.NotConfigured)]
    public async Task SendGrid_maps_errors(HttpStatusCode status, ProviderOutcome expected)
    {
        var vault = new FakeVault().With("sendgrid", new(), new() { ["apiKey"] = "SG.key" });
        var result = await new SendGridEmailProvider(new HttpClient(Respond(status, "{\"errors\":[{\"message\":\"bad\"}]}")), vault,
            NullLogger<SendGridEmailProvider>.Instance).SendAsync(Email(), default);
        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.MessageId);
        if (expected == ProviderOutcome.NotConfigured) Assert.Single(vault.Marked);
    }

    [Fact]
    public async Task SendGrid_without_a_message_id_is_not_treated_as_sent()
    {
        var vault = new FakeVault().With("sendgrid", new(), new() { ["apiKey"] = "SG.key" });
        var result = await new SendGridEmailProvider(new HttpClient(Respond(HttpStatusCode.Accepted, "")), vault, NullLogger<SendGridEmailProvider>.Instance)
            .SendAsync(Email(), default);
        Assert.NotEqual(ProviderOutcome.Accepted, result.Outcome);
    }

    // ---------- Ambiguous outcomes (never retried: a retry could deliver a duplicate) ----------

    [Fact]
    public async Task A_2xx_without_a_message_id_is_an_unknown_outcome_not_a_retryable_failure()
    {
        var sendGrid = await new SendGridEmailProvider(new HttpClient(Respond(HttpStatusCode.Accepted, "")),
            new FakeVault().With("sendgrid", new(), new() { ["apiKey"] = "SG.key" }), NullLogger<SendGridEmailProvider>.Instance).SendAsync(Email(), default);
        Assert.Equal(ProviderOutcome.Unknown, sendGrid.Outcome);

        var mailgun = await new MailgunEmailProvider(new HttpClient(Respond(HttpStatusCode.OK, "{\"message\":\"Queued\"}")),
            new FakeVault().With("mailgun", new() { ["domain"] = "mg.brand.test" }, new() { ["apiKey"] = "k" }), NullLogger<MailgunEmailProvider>.Instance).SendAsync(Email(), default);
        Assert.Equal(ProviderOutcome.Unknown, mailgun.Outcome);

        var twilio = await new TwilioSmsProvider(new HttpClient(Respond(HttpStatusCode.Created, "{\"status\":\"queued\"}")),
            new FakeVault().With("twilio", new() { ["accountSid"] = "AC1", ["fromNumber"] = "+15005550006" }, new() { ["authToken"] = "tok" }),
            NullLogger<TwilioSmsProvider>.Instance).SendAsync(null, "+14155550123", "Hi", null, default);
        Assert.Equal(ProviderOutcome.Unknown, twilio.Outcome);

        var whatsApp = await new WhatsAppCloudTemplateProvider(new HttpClient(Respond(HttpStatusCode.OK, "{}")),
            new FakeVault().With("whatsapp", new() { ["phoneNumberId"] = "123" }, new() { ["accessToken"] = "EAAB" }),
            NullLogger<WhatsAppCloudTemplateProvider>.Instance).SendTemplateAsync(null, "+923001234567", "promo", "en", new[] { "Ann" }, default);
        Assert.Equal(ProviderOutcome.Unknown, whatsApp.Outcome);
    }

    [Fact]
    public async Task A_timeout_or_lost_connection_is_unknown_but_a_connect_failure_is_retryable()
    {
        var vault = new FakeVault().With("sendgrid", new(), new() { ["apiKey"] = "SG.key" });
        Task<ProviderResult> Send(Exception thrown) =>
            new SendGridEmailProvider(new HttpClient(new FakeHandler((_, _) => throw thrown)), vault, NullLogger<SendGridEmailProvider>.Instance).SendAsync(Email(), default);

        // HttpClient timeout: the request was sent, the answer never came.
        Assert.Equal(ProviderOutcome.Unknown, (await Send(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"))).Outcome);
        // Connection dropped while waiting for the response.
        Assert.Equal(ProviderOutcome.Unknown, (await Send(new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended prematurely."))).Outcome);
        // Nothing could have been transmitted: safe to retry.
        Assert.Equal(ProviderOutcome.TransientFailure, (await Send(new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused"))).Outcome);
        Assert.Equal(ProviderOutcome.TransientFailure, (await Send(new HttpRequestException(HttpRequestError.NameResolutionError, "No such host"))).Outcome);

        var twilio = await new TwilioSmsProvider(new HttpClient(new FakeHandler((_, _) => throw new TaskCanceledException("timeout"))),
            new FakeVault().With("twilio", new() { ["accountSid"] = "AC1", ["fromNumber"] = "+15005550006" }, new() { ["authToken"] = "tok" }),
            NullLogger<TwilioSmsProvider>.Instance).SendAsync(null, "+14155550123", "Hi", null, default);
        Assert.Equal(ProviderOutcome.Unknown, twilio.Outcome);
    }

    // ---------- Mailgun ----------

    [Fact]
    public async Task Mailgun_posts_form_with_basic_auth_and_headers()
    {
        var handler = Respond(HttpStatusCode.OK, "{\"id\":\"<20260923.1@mg.brand.test>\",\"message\":\"Queued. Thank you.\"}");
        var vault = new FakeVault().With("mailgun", new() { ["domain"] = "mg.brand.test" }, new() { ["apiKey"] = "key-1" });
        var result = await new MailgunEmailProvider(new HttpClient(handler), vault, NullLogger<MailgunEmailProvider>.Instance).SendAsync(Email(), default);
        Assert.Equal(ProviderOutcome.Accepted, result.Outcome);
        Assert.Equal("20260923.1@mg.brand.test", result.MessageId);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.mailgun.net/v3/mg.brand.test/messages", request.RequestUri!.ToString());
        Assert.Contains("h%3AList-Unsubscribe-Post=List-Unsubscribe%3DOne-Click", body);
        Assert.Contains("v%3Aoa_ref=c%3Aabc", body);
        Assert.Equal(ProviderOutcome.NotConfigured, (await new MailgunEmailProvider(new HttpClient(handler), new FakeVault(), NullLogger<MailgunEmailProvider>.Instance).SendAsync(Email(), default)).Outcome);
    }

    // ---------- Twilio ----------

    [Fact]
    public async Task Twilio_not_configured_configured_and_error()
    {
        var none = await new TwilioSmsProvider(new HttpClient(Respond(HttpStatusCode.Created, "{}")), new FakeVault(), NullLogger<TwilioSmsProvider>.Instance)
            .SendAsync(null, "+14155550123", "Hi", null, default);
        Assert.Equal(ProviderOutcome.NotConfigured, none.Outcome);

        var handler = Respond(HttpStatusCode.Created, "{\"sid\":\"SM123\",\"status\":\"queued\"}");
        var vault = new FakeVault().With("twilio", new() { ["accountSid"] = "AC1", ["fromNumber"] = "+15005550006" }, new() { ["authToken"] = "tok" });
        var ok = await new TwilioSmsProvider(new HttpClient(handler), vault, NullLogger<TwilioSmsProvider>.Instance)
            .SendAsync(null, "+14155550123", "Hi there", "https://app.test/status", default);
        Assert.Equal(ProviderOutcome.Accepted, ok.Outcome);
        Assert.Equal("SM123", ok.MessageId);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.twilio.com/2010-04-01/Accounts/AC1/Messages.json", request.RequestUri!.ToString());
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("AC1:tok")), request.Headers.Authorization!.Parameter);
        Assert.Contains("To=%2B14155550123", body);
        Assert.Contains("From=%2B15005550006", body);
        Assert.Contains("StatusCallback=", body);

        var error = await new TwilioSmsProvider(new HttpClient(Respond(HttpStatusCode.BadRequest, "{\"code\":21211,\"message\":\"Invalid 'To' Phone Number\"}")), vault,
            NullLogger<TwilioSmsProvider>.Instance).SendAsync(null, "+14155550123", "Hi", null, default);
        Assert.Equal(ProviderOutcome.PermanentFailure, error.Outcome);
        Assert.Contains("21211", error.Error);
    }

    [Fact]
    public void Twilio_signature_validation()
    {
        var form = new[] { new KeyValuePair<string, string>("From", "+14155550123"), new KeyValuePair<string, string>("Body", "STOP") };
        const string url = "https://app.test/api/v1/public/sms/webhooks/twilio/agency/inbound";
        var signature = TwilioSmsProvider.ComputeSignature("tok", url, form);
        Assert.True(TwilioSmsProvider.IsValidSignature("tok", url, form, signature));
        Assert.False(TwilioSmsProvider.IsValidSignature("other", url, form, signature));
        Assert.False(TwilioSmsProvider.IsValidSignature("tok", url + "?x=1", form, signature));
        Assert.False(TwilioSmsProvider.IsValidSignature("tok", url, form, null));
    }

    // ---------- WhatsApp ----------

    [Fact]
    public async Task WhatsApp_template_messages_use_vault_credentials()
    {
        Assert.Equal(ProviderOutcome.NotConfigured, (await new WhatsAppCloudTemplateProvider(new HttpClient(Respond(HttpStatusCode.OK, "{}")), new FakeVault(),
            NullLogger<WhatsAppCloudTemplateProvider>.Instance).SendTemplateAsync(null, "+923001234567", "promo", "en", new[] { "Ann" }, default)).Outcome);
        var handler = Respond(HttpStatusCode.OK, "{\"messages\":[{\"id\":\"wamid.1\"}]}");
        var vault = new FakeVault().With("whatsapp", new() { ["phoneNumberId"] = "123" }, new() { ["accessToken"] = "EAAB" });
        var result = await new WhatsAppCloudTemplateProvider(new HttpClient(handler), vault, NullLogger<WhatsAppCloudTemplateProvider>.Instance)
            .SendTemplateAsync(null, "+923001234567", "promo", "en", new[] { "Ann\nNew line" }, default);
        Assert.Equal(ProviderOutcome.Accepted, result.Outcome);
        var (_, body) = Assert.Single(handler.Requests);
        Assert.Contains("\"to\":\"923001234567\"", body);
        Assert.Contains("\"text\":\"Ann New line\"", body);
    }

    // ---------- SMTP ----------

    [Fact]
    public void Smtp_mime_carries_one_click_unsubscribe_headers_and_sender()
    {
        var mime = SmtpEmailMarketingProvider.BuildMime(Email());
        Assert.Equal("<https://app.test/e/u/t>", mime.Headers["List-Unsubscribe"]);
        Assert.Equal("List-Unsubscribe=One-Click", mime.Headers["List-Unsubscribe-Post"]);
        Assert.Equal("news@brand.test", mime.From.Mailboxes.Single().Address);
        Assert.Equal("help@brand.test", mime.ReplyTo.Mailboxes.Single().Address);
        Assert.False(string.IsNullOrEmpty(mime.MessageId));
    }
}

public sealed class TrackingTokenAndWebhookSignatureTests
{
    private static TrackingTokens Tokens(string salt = "unit-salt") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:HashSalt"] = salt }).Build());

    [Fact]
    public void Tokens_round_trip_and_reject_forgery_and_purpose_confusion()
    {
        var tokens = Tokens();
        var id = Guid.NewGuid();
        var link = Guid.NewGuid();
        var click = tokens.Create(TokenPurpose.Click, TokenSource.CampaignRecipient, id, link);
        var read = tokens.Read(click, TokenPurpose.Click);
        Assert.NotNull(read);
        Assert.Equal(id, read!.Value.MessageId);
        Assert.Equal(link, read.Value.Extra);
        Assert.Null(tokens.Read(click, TokenPurpose.Unsubscribe)); // purpose is signed
        Assert.Null(Tokens("other-salt").Read(click, TokenPurpose.Click)); // other key
        var tampered = (click[0] == 'A' ? "B" : "A") + click[1..];
        Assert.Null(tokens.Read(tampered, TokenPurpose.Click));
        Assert.Null(tokens.Read("garbage", TokenPurpose.Click));
        Assert.Null(tokens.Read(click.Split('.')[0] + ".AAAAAAAAAAAAAAAAAAAAAA", TokenPurpose.Click));
    }

    [Fact]
    public void Expiry_is_encoded_in_confirmation_tokens()
    {
        var at = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(at, TrackingTokens.ExpiryFrom(TrackingTokens.ExpiryGuid(at)));
    }

    [Fact]
    public void SendGrid_signed_event_webhook_verification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var body = Encoding.UTF8.GetBytes("[{\"event\":\"bounce\",\"email\":\"a@x.test\"}]");
        const string timestamp = "1727000000";
        var signature = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(timestamp).Concat(body).ToArray(), HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));
        Assert.True(WebhookService.VerifySendGridSignature(publicKey, signature, timestamp, body));
        Assert.False(WebhookService.VerifySendGridSignature(publicKey, signature, "1727000001", body));
        Assert.False(WebhookService.VerifySendGridSignature(publicKey, signature, timestamp, Encoding.UTF8.GetBytes("[]")));
        Assert.False(WebhookService.VerifySendGridSignature(publicKey, "not-base64!", timestamp, body));
        var events = WebhookService.ParseSendGrid(Encoding.UTF8.GetBytes(
            "[{\"event\":\"bounce\",\"type\":\"bounce\",\"email\":\"a@x.test\",\"sg_event_id\":\"e1\",\"sg_message_id\":\"m1.filter\",\"oa_ref\":\"c:1\",\"timestamp\":1727000000}," +
            "{\"event\":\"bounce\",\"type\":\"blocked\",\"email\":\"b@x.test\"},{\"event\":\"spamreport\",\"email\":\"c@x.test\"},{\"event\":\"open\",\"email\":\"d@x.test\"}]"));
        Assert.Equal(new[] { ProviderEventKind.HardBounce, ProviderEventKind.SoftBounce, ProviderEventKind.Complaint, ProviderEventKind.Ignored }, events.Select(e => e.Kind));
        Assert.Equal("m1", events[0].ProviderMessageId);
    }

    [Fact]
    public void Mailgun_signature_and_replay_window()
    {
        var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        var ts = new DateTimeOffset(now).ToUnixTimeSeconds().ToString();
        var sig = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("signing-key"), Encoding.UTF8.GetBytes(ts + "token-1"))).ToLowerInvariant();
        Assert.True(WebhookService.IsValidMailgunSignature("signing-key", ts, "token-1", sig, now));
        Assert.False(WebhookService.IsValidMailgunSignature("signing-key", ts, "token-2", sig, now));
        Assert.False(WebhookService.IsValidMailgunSignature("signing-key", ts, "token-1", sig, now.AddHours(1)));
        // An out-of-range timestamp is rejected, not an unhandled exception (500).
        Assert.False(WebhookService.IsValidMailgunSignature("signing-key", "99999999999999999", "token-1", sig, now));
    }

    [Fact]
    public void Webhook_timestamps_must_be_fresh()
    {
        var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        string Ts(DateTime at) => new DateTimeOffset(at).ToUnixTimeSeconds().ToString();
        Assert.True(WebhookService.IsFreshTimestamp(Ts(now), now, WebhookService.SendGridTolerance));
        Assert.True(WebhookService.IsFreshTimestamp(Ts(now.AddMinutes(-10)), now, WebhookService.SendGridTolerance));
        Assert.True(WebhookService.IsFreshTimestamp(Ts(now.AddMinutes(2)), now, WebhookService.SendGridTolerance));
        Assert.False(WebhookService.IsFreshTimestamp(Ts(now.AddHours(-1)), now, WebhookService.SendGridTolerance));
        Assert.False(WebhookService.IsFreshTimestamp(Ts(now.AddHours(1)), now, WebhookService.SendGridTolerance));
        Assert.False(WebhookService.IsFreshTimestamp("1727000000", now, WebhookService.SendGridTolerance)); // 2024: a replay
        Assert.False(WebhookService.IsFreshTimestamp("99999999999999999", now, WebhookService.SendGridTolerance));
        Assert.False(WebhookService.IsFreshTimestamp("-5", now, WebhookService.SendGridTolerance));
        Assert.False(WebhookService.IsFreshTimestamp("", now, WebhookService.SendGridTolerance));
        Assert.False(WebhookService.IsFreshTimestamp(null, now, WebhookService.SendGridTolerance));
    }
}
