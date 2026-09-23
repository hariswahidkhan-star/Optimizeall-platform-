using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.EmailMarketing;

[Collection(EmailCollection.Name)]
public sealed partial class TrackingAndWebhookTests(EmailFixture fx)
{
    private const string BrowserUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    private async Task<(EmailFixture.Workspace Ws, Guid CampaignId, OutboundEmail Message, List<Guid> Subscribers)> SendOneAsync(string link = "https://shop.example.com/offer?e={{email}}")
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var subscribers = await fx.AddSubscribersAsync(ws, 1);
        var campaign = await fx.CreateCampaignAsync(staff, ws, design: EmailFixture.Design(link: link));
        await EmailFixture.ConfirmSendAsync(staff, campaign);
        await fx.RunJobAsync<CampaignSendJob>();
        return (ws, Guid.Parse(campaign.GetProperty("id").GetString()!), fx.Email.For(ws.ClientId).Last(), subscribers);
    }

    [GeneratedRegex("http://app\\.test(/e/[ocu]/[A-Za-z0-9_.\\-]+(?:\\.gif)?)")]
    private static partial Regex TrackingUrl();

    private static string Path(OutboundEmail m, string kind) =>
        TrackingUrl().Matches(m.Html).Select(x => x.Groups[1].Value).First(p => p.StartsWith($"/e/{kind}/", StringComparison.Ordinal));

    private static HttpRequestMessage Get(string path, string ua = BrowserUa)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("User-Agent", ua);
        return request;
    }

    [Fact]
    public async Task Opens_and_clicks_are_recorded_with_bot_filtering_and_redirect_only_to_stored_links()
    {
        var (ws, campaignId, message, subscribers) = await SendOneAsync();
        var anonymous = fx.Anonymous();
        var open = Path(message, "o");
        var click = Path(message, "c");

        // Apple Mail Privacy Protection prefetch → machine open, not a human open.
        var pixel = await anonymous.SendAsync(Get(open, "Mozilla/5.0"));
        Assert.Equal("image/gif", pixel.Content.Headers.ContentType!.MediaType);
        var r = Assert.Single(await fx.RecipientsAsync(campaignId));
        Assert.Null(r.OpenedAt);
        Assert.Equal(1, r.MachineOpenCount);

        fx.Api.Clock.Advance(TimeSpan.FromMinutes(5));
        await anonymous.SendAsync(Get(open));
        var redirect = await anonymous.SendAsync(Get(click));
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        var email = (await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.Id == subscribers[0]))).NormalizedEmail!;
        Assert.Equal("https://shop.example.com/offer?e=" + Uri.EscapeDataString(email), redirect.Headers.Location!.ToString());
        r = Assert.Single(await fx.RecipientsAsync(campaignId));
        Assert.NotNull(r.OpenedAt);
        Assert.NotNull(r.ClickedAt);
        Assert.Equal(1, r.ClickCount);

        // Forged / tampered tokens: pixel still answers (nothing recorded), clicks 404 and never redirect.
        var tampered = click[..^3] + (click[^3] == 'A' ? "B" : "A") + click[^2..];
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.SendAsync(Get(tampered))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.SendAsync(Get("/e/c/not-a-token"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.SendAsync(Get("/e/c/" + open["/e/o/".Length..^".gif".Length]))).StatusCode); // open token as click
        Assert.Equal(HttpStatusCode.OK, (await anonymous.SendAsync(Get("/e/o/forged.gif"))).StatusCode);

        // A validly signed token for this message pointing at another campaign's link is refused (no open redirect).
        var (_, _, other, _) = await SendOneAsync("https://evil.example.net/phish");
        var otherLinkId = await fx.Db(db => db.Set<TrackedLink>().AsNoTracking().Where(l => l.Url == "https://evil.example.net/phish").Select(l => l.Id).FirstAsync());
        var tokens = fx.App.Services.GetRequiredService<TrackingTokens>();
        var crossToken = tokens.Create(TokenPurpose.Click, TokenSource.CampaignRecipient, r.Id, otherLinkId);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.SendAsync(Get("/e/c/" + crossToken))).StatusCode);

        // A scanner hitting the link is recorded as a machine click and still redirected, but does not count.
        await anonymous.SendAsync(Get(click, "Mozilla/5.0 (compatible; Proofpoint URL Defense)"));
        r = Assert.Single(await fx.RecipientsAsync(campaignId));
        Assert.Equal(1, r.ClickCount);

        var staff = await fx.StaffAsync();
        var report = await (await staff.GetAsync($"/api/v1/agency/email/campaigns/{campaignId}/report")).ReadJsonAsync();
        Assert.Equal(1, report.GetProperty("uniqueOpens").GetInt32());
        Assert.Equal(1, report.GetProperty("uniqueClicks").GetInt32());
        Assert.Equal(1, report.GetProperty("machineOpens").GetInt32());
        Assert.Equal(1.0, report.GetProperty("clickToOpenRate").GetDouble());
        Assert.True(report.GetProperty("deliveredIsEstimated").GetBoolean());
        var link = Assert.Single(report.GetProperty("links").EnumerateArray(), l => l.GetProperty("totalClicks").GetInt32() > 0);
        Assert.Equal(1, link.GetProperty("uniqueClicks").GetInt32());
        Assert.NotEmpty(report.GetProperty("devices").EnumerateArray());
        Assert.NotEqual(Guid.Empty, ws.ClientId);
        Assert.NotEmpty(other.Html);
    }

    [Fact]
    public async Task Preference_center_updates_topics_and_frequency_and_can_unsubscribe_everything()
    {
        var (ws, _, message, subscribers) = await SendOneAsync();
        var topic = new EmailList { ClientAccountId = ws.ClientId, ScopeKey = Workspace.Key(ws.ClientId), Name = "Product news", PublicKey = Guid.NewGuid().ToString("N")[..16] };
        await fx.Db(async db => { db.Add(topic); await db.SaveChangesAsync(); });
        var preferences = message.Html.Split('"').First(p => p.StartsWith("http://app.test/email/preferences/", StringComparison.Ordinal));
        var token = preferences.Split('/').Last();
        var anonymous = fx.Anonymous();

        var prefs = await (await anonymous.GetAsync($"/api/v1/public/email/preferences/{token}")).ReadJsonAsync();
        Assert.Contains("•", prefs.GetProperty("maskedEmail").GetString());
        Assert.Equal(2, prefs.GetProperty("topics").GetArrayLength());
        var updated = await (await anonymous.PutAsJsonAsync($"/api/v1/public/email/preferences/{token}", new
        {
            frequency = "Monthly", topics = new[] { new { listId = ws.ListId, subscribed = false }, new { listId = topic.Id, subscribed = true } },
        })).ReadJsonAsync();
        Assert.Equal("Monthly", updated.GetProperty("frequency").GetString());
        var topics = updated.GetProperty("topics").EnumerateArray().ToDictionary(t => t.GetProperty("listId").GetGuid(), t => t.GetProperty("subscribed").GetBoolean());
        Assert.False(topics[ws.ListId]);
        Assert.True(topics[topic.Id]);

        var all = await (await anonymous.PutAsJsonAsync($"/api/v1/public/email/preferences/{token}", new { unsubscribeAll = true })).ReadJsonAsync();
        Assert.True(all.GetProperty("unsubscribedFromAll").GetBoolean());
        var s = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(x => x.Id == subscribers[0]));
        Assert.Equal(SubscriberStatus.Unsubscribed, s.Status);
        await (await anonymous.GetAsync($"/api/v1/public/email/preferences/{token}tampered")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task SendGrid_webhook_is_signature_checked_and_hard_bounces_reach_the_suppression_list()
    {
        var (ws, campaignId, _, subscribers) = await SendOneAsync();
        var recipient = Assert.Single(await fx.RecipientsAsync(campaignId));
        var anonymous = fx.Anonymous();
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[]
        {
            new Dictionary<string, object> { ["event"] = "delivered", ["email"] = recipient.Address, ["sg_event_id"] = "evt-d1", ["oa_ref"] = "c:" + recipient.Id.ToString("N"), ["timestamp"] = 1727000000 },
            new Dictionary<string, object> { ["event"] = "bounce", ["type"] = "bounce", ["email"] = recipient.Address, ["sg_event_id"] = "evt-b1", ["oa_ref"] = "c:" + recipient.Id.ToString("N"), ["reason"] = "550 5.1.1 unknown user", ["timestamp"] = 1727000100 },
        }));
        HttpRequestMessage Request(string signature, string ts) => new(HttpMethod.Post, $"/api/v1/public/email/webhooks/sendgrid/{ws.ClientId}")
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } },
            Headers = { { "X-Twilio-Email-Event-Webhook-Signature", signature }, { "X-Twilio-Email-Event-Webhook-Timestamp", ts } },
        };

        // Not configured → 503; bad signature → 401; nothing applied.
        await (await anonymous.SendAsync(Request("sig", "1"))).ShouldFailAsync(503, "email.webhook_not_configured");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await fx.AddIntegrationAsync(ws.ClientId, "sendgrid", new() { ["webhookPublicKey"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) }, new() { ["apiKey"] = "SG.x" });
        // SendGrid signs each delivery attempt when it is sent: the timestamp is "now".
        var ts = new DateTimeOffset(fx.Now).ToUnixTimeSeconds().ToString();
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var forged = Convert.ToBase64String(otherKey.SignData(Encoding.UTF8.GetBytes(ts).Concat(body).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        await (await anonymous.SendAsync(Request(forged, ts))).ShouldFailAsync(401, "email.invalid_signature");
        Assert.False(await fx.Db(db => db.Set<Suppression>().AnyAsync(x => x.Value == recipient.Address)));
        // A genuine signature over a stale timestamp (a captured request replayed later) is rejected too.
        var staleTs = new DateTimeOffset(fx.Now.AddHours(-2)).ToUnixTimeSeconds().ToString();
        var stale = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(staleTs).Concat(body).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        await (await anonymous.SendAsync(Request(stale, staleTs))).ShouldFailAsync(401, "email.invalid_signature");
        Assert.False(await fx.Db(db => db.Set<Suppression>().AnyAsync(x => x.Value == recipient.Address)));

        var signature = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(ts).Concat(body).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var ok = await (await anonymous.SendAsync(Request(signature, ts))).ReadJsonAsync();
        Assert.Equal(2, ok.GetProperty("applied").GetInt32());
        var suppression = await fx.Db(db => db.Set<Suppression>().AsNoTracking().FirstAsync(x => x.Value == recipient.Address));
        Assert.Equal(SuppressionReason.HardBounce, suppression.Reason);
        var r = Assert.Single(await fx.RecipientsAsync(campaignId));
        Assert.Equal(BounceType.Hard, r.BounceType);
        Assert.NotNull(r.DeliveredAt);
        Assert.Equal(SubscriberStatus.Bounced, (await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.Id == subscribers[0]))).Status);
        // Replays are idempotent.
        var replay = await (await anonymous.SendAsync(Request(signature, ts))).ReadJsonAsync();
        Assert.Equal(0, replay.GetProperty("applied").GetInt32());
    }

    [Fact]
    public async Task Mailgun_complaints_suppress_after_hmac_verification()
    {
        var (ws, campaignId, _, _) = await SendOneAsync();
        var recipient = Assert.Single(await fx.RecipientsAsync(campaignId));
        await fx.AddIntegrationAsync(ws.ClientId, "mailgun", new() { ["domain"] = "mg.example.com" }, new() { ["apiKey"] = "k", ["webhookSigningKey"] = "mg-signing" });
        var ts = new DateTimeOffset(fx.Now).ToUnixTimeSeconds().ToString();
        string Sign(string key) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(ts + "tok-1"))).ToLowerInvariant();
        var anonymous = fx.Anonymous();
        var badBody = new Dictionary<string, object>
        {
            ["signature"] = new { timestamp = ts, token = "tok-1", signature = Sign("wrong") },
            ["event-data"] = new Dictionary<string, object> { ["event"] = "complained", ["recipient"] = recipient.Address, ["id"] = "mg-1" },
        };
        await (await anonymous.PostAsJsonAsync($"/api/v1/public/email/webhooks/mailgun/{ws.ClientId}", badBody)).ShouldFailAsync(401);
        badBody["signature"] = new { timestamp = ts, token = "tok-1", signature = Sign("mg-signing") };
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync($"/api/v1/public/email/webhooks/mailgun/{ws.ClientId}", badBody)).StatusCode);
        var suppression = await fx.Db(db => db.Set<Suppression>().AsNoTracking().FirstAsync(x => x.Value == recipient.Address));
        Assert.Equal(SuppressionReason.Complaint, suppression.Reason);
    }

    [Fact]
    public async Task Signed_conversion_api_attributes_revenue_to_the_last_clicked_campaign()
    {
        var (ws, campaignId, message, subscribers) = await SendOneAsync();
        var anonymous = fx.Anonymous();
        fx.Api.Clock.Advance(TimeSpan.FromMinutes(3));
        await anonymous.SendAsync(Get(Path(message, "c")));
        var email = (await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.Id == subscribers[0]))).NormalizedEmail;
        var body = JsonSerializer.Serialize(new { clientAccountId = ws.ClientId, email, externalReference = "ORDER-1001", value = 49.999m, currency = "usd" });

        HttpRequestMessage Signed(string json, string secret)
        {
            var sig = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            return new HttpRequestMessage(HttpMethod.Post, "/api/v1/public/email/conversions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                Headers = { { "X-OA-Signature", sig } },
            };
        }

        await (await anonymous.SendAsync(Signed(body, "wrong-secret"))).ShouldFailAsync(401, "email.invalid_signature");
        var result = await (await anonymous.SendAsync(Signed(body, EmailFixture.SigningSecret))).ReadJsonAsync();
        Assert.Equal(campaignId, result.GetProperty("campaignId").GetGuid());
        Assert.False(result.GetProperty("duplicate").GetBoolean());
        var duplicate = await (await anonymous.SendAsync(Signed(body, EmailFixture.SigningSecret))).ReadJsonAsync();
        Assert.True(duplicate.GetProperty("duplicate").GetBoolean());

        var staff = await fx.StaffAsync();
        var report = await (await staff.GetAsync($"/api/v1/agency/email/campaigns/{campaignId}/report")).ReadJsonAsync();
        Assert.Equal(1, report.GetProperty("conversions").GetInt32());
        var revenue = Assert.Single(report.GetProperty("revenue").EnumerateArray());
        Assert.Equal("USD", revenue.GetProperty("currency").GetString());
        Assert.Equal(50.00m, revenue.GetProperty("amount").GetDecimal());

        var kpis = await (await staff.GetAsync($"/api/v1/agency/email/clients/{ws.ClientId}/kpis")).ReadJsonAsync();
        Assert.Equal(1, kpis.GetProperty("emailsSent").GetInt32());
        Assert.Equal(1, kpis.GetProperty("conversions").GetInt32());
    }

    [Fact]
    public async Task Twilio_stop_opts_out_after_signature_verification_and_start_opts_back_in()
    {
        var ws = await fx.CreateWorkspaceAsync();
        var ids = await fx.AddSubscribersAsync(ws, 1, (s, _) => { s.Phone = "+923001112233"; s.SmsConsent = ConsentStatus.Granted; });
        await fx.AddIntegrationAsync(ws.ClientId, "twilio", new() { ["accountSid"] = "AC1", ["fromNumber"] = "+15005550006" }, new() { ["authToken"] = "twilio-token" });
        var path = $"/api/v1/public/sms/webhooks/twilio/{ws.ClientId}/inbound";
        HttpRequestMessage Inbound(string text, string token)
        {
            var form = new Dictionary<string, string> { ["From"] = "+923001112233", ["To"] = "+15005550006", ["Body"] = text, ["MessageSid"] = "SM1" };
            var signature = TwilioSmsProvider.ComputeSignature(token, "http://app.test" + path, form);
            return new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form), Headers = { { "X-Twilio-Signature", signature } } };
        }
        var anonymous = fx.Anonymous();
        await (await anonymous.SendAsync(Inbound("STOP", "forged-token"))).ShouldFailAsync(401);
        var reply = await anonymous.SendAsync(Inbound("Stop", "twilio-token"));
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        Assert.Contains("<Response>", await reply.Content.ReadAsStringAsync());
        var s = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(x => x.Id == ids[0]));
        Assert.Equal(ConsentStatus.Withdrawn, s.SmsConsent);
        Assert.True(await fx.Db(db => db.Set<Suppression>().AnyAsync(x => x.Channel == MessageChannel.Sms && x.Value == "+923001112233" && x.Reason == SuppressionReason.StopKeyword)));

        Assert.Equal(HttpStatusCode.OK, (await anonymous.SendAsync(Inbound("START", "twilio-token"))).StatusCode);
        s = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(x => x.Id == ids[0]));
        Assert.Equal(ConsentStatus.Granted, s.SmsConsent);
        Assert.False(await fx.Db(db => db.Set<Suppression>().AnyAsync(x => x.Channel == MessageChannel.Sms && x.Value == "+923001112233")));
    }
}
