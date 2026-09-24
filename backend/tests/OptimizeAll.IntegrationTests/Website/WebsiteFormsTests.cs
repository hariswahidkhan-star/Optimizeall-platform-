using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Website;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Website;

/// <summary>Captures published website events (registered only in this test class's host).</summary>
public sealed class CapturedWebsiteEvents : IEventHandler<WebsiteInquiryReceived>, IEventHandler<NewsletterSubscribed>
{
    public static readonly ConcurrentBag<WebsiteInquiryReceived> Inquiries = new();
    public static readonly ConcurrentBag<NewsletterSubscribed> Subscriptions = new();

    public Task HandleAsync(WebsiteInquiryReceived domainEvent, CancellationToken cancellationToken)
    {
        Inquiries.Add(domainEvent);
        return Task.CompletedTask;
    }

    public Task HandleAsync(NewsletterSubscribed domainEvent, CancellationToken cancellationToken)
    {
        Subscriptions.Add(domainEvent);
        return Task.CompletedTask;
    }
}

/// <summary>Public lead forms, anti-spam checks, events, newsletter double opt-in, consultation booking and careers.</summary>
public sealed class WebsiteFormsTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _api;
    private readonly WebApplicationFactory<Program> _host;

    public WebsiteFormsTests(ApiFactory api)
    {
        _api = api;
        _host = api.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.AddScoped<IEventHandler<WebsiteInquiryReceived>, CapturedWebsiteEvents>();
            services.AddScoped<IEventHandler<NewsletterSubscribed>, CapturedWebsiteEvents>();
        }));
    }

    public Task InitializeAsync() => _host.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private HttpClient Client()
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        return client;
    }

    private async Task<string> TokenAsync(HttpClient client) => await _api.FormTokenAsync(client);

    [Fact]
    public async Task Contact_form_stores_the_inquiry_publishes_one_event_and_notifies_staff()
    {
        var (admin, _) = await _api.CreateClientAsync(Role.Admin);
        var client = Client();
        var email = $"lead-{Guid.NewGuid():N}@example.test";
        var accepted = await client.PostJsonAsync("/api/v1/public/inquiries/contact", WebsiteTestKit.Form(await TokenAsync(client), WebsiteTestKit.Contact(email)), 202);
        Assert.StartsWith("OA-", accepted.GetProperty("reference").GetString());

        var inquiry = await _api.WithDbAsync(db => db.Set<WebsiteInquiry>().SingleAsync(i => i.Email == email));
        Assert.Equal(InquiryType.Contact, inquiry.Type);
        Assert.Equal("google", inquiry.UtmSource);
        Assert.Equal("autumn", inquiry.UtmCampaign);
        Assert.Equal("https://www.google.com/", inquiry.Referrer);
        Assert.Equal("/services/seo", inquiry.LandingPath);
        Assert.Equal(ConsentTexts.FormVersion, inquiry.ConsentVersion);

        var events = CapturedWebsiteEvents.Inquiries.Where(e => e.Email == email).ToList();
        var single = Assert.Single(events);
        Assert.Equal(inquiry.Id, single.InquiryId);
        Assert.Equal("Contact", single.InquiryType);
        Assert.Equal(new[] { "seo" }, single.ServiceSlugs);
        Assert.Equal("Analytical Engines Ltd", single.Company);

        var link = WebsiteLinks.Inquiry(inquiry.Id);
        Assert.True(await _api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == admin.Id && n.LinkUrl == link)));
    }

    [Fact]
    public async Task Honeypot_submissions_look_accepted_but_store_nothing()
    {
        var client = Client();
        var email = $"bot-{Guid.NewGuid():N}@example.test";
        var form = WebsiteTestKit.Form(await TokenAsync(client), WebsiteTestKit.Contact(email));
        form["nickname"] = "I am a bot";
        await client.PostJsonAsync("/api/v1/public/inquiries/contact", form, 202);
        Assert.False(await _api.WithDbAsync(db => db.Set<WebsiteInquiry>().AnyAsync(i => i.Email == email)));
        Assert.DoesNotContain(CapturedWebsiteEvents.Inquiries, e => e.Email == email);
    }

    [Fact]
    public async Task Forms_filled_too_fast_or_with_a_forged_token_are_rejected()
    {
        var client = Client();
        var email = $"fast-{Guid.NewGuid():N}@example.test";
        var token = await _api.FormTokenAsync(client, wait: false);
        var fast = await client.PostJsonAsync("/api/v1/public/inquiries/contact", WebsiteTestKit.Form(token, WebsiteTestKit.Contact(email)), 400);
        Assert.Equal("website.form_too_fast", fast.Code());

        var forged = await client.PostJsonAsync("/api/v1/public/inquiries/contact", WebsiteTestKit.Form("forged-token", WebsiteTestKit.Contact(email)), 400);
        Assert.Equal("website.form_expired", forged.Code());

        // The same token works once the minimum fill time has passed; it expires after a day.
        _api.Clock.Advance(TimeSpan.FromSeconds(5));
        await client.PostJsonAsync("/api/v1/public/inquiries/contact", WebsiteTestKit.Form(token, WebsiteTestKit.Contact(email)), 202);
        _api.Clock.Advance(TimeSpan.FromHours(25));
        Assert.Equal("website.form_expired",
            (await client.PostJsonAsync("/api/v1/public/inquiries/contact", WebsiteTestKit.Form(token, WebsiteTestKit.Contact(email)), 400)).Code());
    }

    [Fact]
    public async Task Form_tokens_are_single_use_for_successful_submissions()
    {
        var client = Client();
        var email = $"replay-{Guid.NewGuid():N}@example.test";
        var token = await TokenAsync(client);

        // A failed validation does not spend the token.
        await client.PostJsonAsync("/api/v1/public/inquiries/contact", WebsiteTestKit.Form(token, WebsiteTestKit.Contact("not-an-email")), 400);
        await client.PostJsonAsync("/api/v1/public/inquiries/contact", WebsiteTestKit.Form(token, WebsiteTestKit.Contact(email)), 202);
        // Replaying the successful request (or reusing the token for another form) is rejected.
        var replay = await client.PostAsJsonAsync("/api/v1/public/inquiries/contact", WebsiteTestKit.Form(token, WebsiteTestKit.Contact(email)));
        await replay.ShouldFailAsync(409, "website.form_already_submitted");
        var quote = WebsiteTestKit.Form(token, new Dictionary<string, object?>
        {
            ["name"] = "Quinn", ["email"] = email, ["serviceSlugs"] = new[] { "seo" }, ["budgetRange"] = "3k-10k",
            ["timeline"] = "asap", ["message"] = "Please quote for SEO for our store.",
        });
        await (await client.PostAsJsonAsync("/api/v1/public/inquiries/quote", quote)).ShouldFailAsync(409, "website.form_already_submitted");
        Assert.Equal(1, await _api.WithDbAsync(db => db.Set<WebsiteInquiry>().CountAsync(i => i.Email == email)));

        // The same request sent several times at once is stored once.
        var burstEmail = $"burst-{Guid.NewGuid():N}@example.test";
        var burstToken = await TokenAsync(client);
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Client().PostAsJsonAsync("/api/v1/public/inquiries/contact", WebsiteTestKit.Form(burstToken, WebsiteTestKit.Contact(burstEmail)))));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Accepted));
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Accepted))
            await rejected.ShouldFailAsync(409, "website.form_already_submitted");
        Assert.Equal(1, await _api.WithDbAsync(db => db.Set<WebsiteInquiry>().CountAsync(i => i.Email == burstEmail)));

        // Spent-token rows are removed by the cleanup job once the token has expired anyway.
        var now = _api.Clock.GetUtcNow().UtcDateTime;
        Task<int> LiveAsync() => _api.WithDbAsync(db => db.Set<UsedFormToken>().CountAsync(t => t.ExpiresAt > now));
        var live = await LiveAsync();
        Assert.True(live >= 2);
        await _api.RunJobAsync<UsedFormTokenCleanupJob>();
        Assert.Equal(live, await LiveAsync());
        _api.Clock.Advance(FormGuard.TokenLifetime + TimeSpan.FromMinutes(10));
        await _api.RunJobAsync<UsedFormTokenCleanupJob>();
        Assert.Equal(0, await _api.WithDbAsync(db => db.Set<UsedFormToken>().CountAsync()));
    }

    [Fact]
    public async Task Forms_validate_fields_consent_services_and_options()
    {
        var client = Client();
        var token = await TokenAsync(client);

        var invalid = WebsiteTestKit.Form(token, WebsiteTestKit.Contact("not-an-email"));
        invalid["serviceSlugs"] = new[] { "no-such-service" };
        invalid["website"] = "javascript:alert(1)";
        var problem = await client.PostJsonAsync("/api/v1/public/inquiries/contact", invalid, 400);
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("serviceSlugs", out _));
        Assert.True(errors.TryGetProperty("website", out _));

        var noConsent = WebsiteTestKit.Form(token, WebsiteTestKit.Contact("ok@example.test"));
        noConsent["consent"] = false;
        Assert.Equal("website.consent_required", (await client.PostJsonAsync("/api/v1/public/inquiries/contact", noConsent, 400)).Code());
        var oldConsent = WebsiteTestKit.Form(token, WebsiteTestKit.Contact("ok@example.test"), consentVersion: "forms-2020-01");
        Assert.Equal("website.consent_required", (await client.PostJsonAsync("/api/v1/public/inquiries/contact", oldConsent, 400)).Code());

        // Quote: budget/timeline must be known options; audit needs a website and at least one service.
        var quote = WebsiteTestKit.Form(token, new Dictionary<string, object?>
        {
            ["name"] = "Quinn", ["email"] = "quinn@example.test", ["serviceSlugs"] = new[] { "seo" }, ["budgetRange"] = "a-lot",
            ["timeline"] = "asap", ["message"] = "Please quote for SEO for our store.",
        });
        Assert.True((await client.PostJsonAsync("/api/v1/public/inquiries/quote", quote, 400)).GetProperty("errors").TryGetProperty("budgetRange", out _));
        quote["budgetRange"] = "3k-10k";
        await client.PostJsonAsync("/api/v1/public/inquiries/quote", quote, 202);

        token = await TokenAsync(client); // the quote spent the previous token (single-use)
        var audit = WebsiteTestKit.Form(token, new Dictionary<string, object?>
        {
            ["name"] = "Avery", ["email"] = "avery@example.test", ["goals"] = "More qualified leads", ["budgetRange"] = "1k-3k",
        });
        var auditErrors = (await client.PostJsonAsync("/api/v1/public/inquiries/audit", audit, 400)).GetProperty("errors");
        Assert.True(auditErrors.TryGetProperty("website", out _));
        Assert.True(auditErrors.TryGetProperty("serviceSlugs", out _));
        audit["website"] = "avery-shop.example";
        audit["serviceSlugs"] = new[] { "seo", "google-ads-ppc" };
        await client.PostJsonAsync("/api/v1/public/inquiries/audit", audit, 202);
        var stored = await _api.WithDbAsync(db => db.Set<WebsiteInquiry>().SingleAsync(i => i.Email == "avery@example.test"));
        Assert.Equal("https://avery-shop.example", stored.Website);
        Assert.Contains("More qualified leads", stored.PayloadJson);
    }

    [Fact]
    public async Task Public_forms_are_rate_limited_per_ip()
    {
        await using var limited = _api.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "true" })));
        await limited.StartAsync();
        var client = limited.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 125; i++) statuses.Add((await client.GetAsync("/api/v1/public/forms/token")).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.True(statuses.Count(s => s == HttpStatusCode.OK) <= 120);
    }

    [Fact]
    public async Task Newsletter_uses_double_opt_in_and_unsubscribe_links()
    {
        var client = Client();
        var email = $"news-{Guid.NewGuid():N}@example.test";
        var form = WebsiteTestKit.Form(await TokenAsync(client), new Dictionary<string, object?> { ["email"] = email, ["source"] = "footer" },
            ConsentTexts.NewsletterVersion);
        await client.PostJsonAsync("/api/v1/public/newsletter/subscribe", form, 202);
        var sub = await _api.WithDbAsync(db => db.Set<NewsletterSubscriber>().SingleAsync(s => s.Email == email));
        Assert.Equal(NewsletterStatus.Pending, sub.Status);
        Assert.Equal("footer", sub.Source);
        Assert.Equal(ConsentTexts.NewsletterVersion, sub.ConsentVersion);
        Assert.DoesNotContain(CapturedWebsiteEvents.Subscriptions, e => e.Email == email);

        var mail = await client.GetJsonAsync($"/api/v1/dev/mailbox?to={Uri.EscapeDataString(email)}");
        var links = mail.GetProperty("links").EnumerateArray().Select(l => l.GetString()!).ToList();
        string TokenOf(string path) => Uri.UnescapeDataString(links.Single(l => l.Contains(path)).Split("token=")[1]);
        var confirmToken = TokenOf("/newsletter/confirm");
        var unsubscribeToken = TokenOf("/newsletter/unsubscribe");

        Assert.Equal("website.newsletter_invalid_token", (await client.PostJsonAsync("/api/v1/public/newsletter/confirm", new { token = "nope" }, 400)).Code());
        var confirmed = await client.PostJsonAsync("/api/v1/public/newsletter/confirm", new { token = confirmToken }, 200);
        Assert.Equal("confirmed", confirmed.GetProperty("status").GetString());
        Assert.Single(CapturedWebsiteEvents.Subscriptions, e => e.Email == email);
        await client.PostJsonAsync("/api/v1/public/newsletter/confirm", new { token = confirmToken }, 400); // single use

        // Subscribing again while confirmed reveals nothing and changes nothing.
        await client.PostJsonAsync("/api/v1/public/newsletter/subscribe", form, 202);
        Assert.Equal(NewsletterStatus.Confirmed, (await _api.WithDbAsync(db => db.Set<NewsletterSubscriber>().SingleAsync(s => s.Email == email))).Status);

        var unsubscribed = await client.PostJsonAsync("/api/v1/public/newsletter/unsubscribe", new { token = unsubscribeToken }, 200);
        Assert.Equal("unsubscribed", unsubscribed.GetProperty("status").GetString());
        var after = await _api.WithDbAsync(db => db.Set<NewsletterSubscriber>().SingleAsync(s => s.Email == email));
        Assert.Equal(NewsletterStatus.Unsubscribed, after.Status);
        Assert.NotNull(after.UnsubscribedAt);
        Assert.Single(CapturedWebsiteEvents.Subscriptions, e => e.Email == email);
    }

    [Fact]
    public async Task Two_parallel_bookings_of_one_slot_let_exactly_one_succeed()
    {
        var admin = (await _api.CreateClientAsync(Role.Admin)).Client;
        var settings = await admin.GetJsonAsync("/api/v1/agency/website/bookings/settings");
        var everyDay = Enum.GetNames<DayOfWeek>().Select(d => new { day = d, start = "00:00", end = "24:00" }).ToArray();
        await admin.PutJsonAsync("/api/v1/agency/website/bookings/settings", new
        {
            timeZone = "UTC", slotMinutes = 30, minNoticeHours = 1, maxDaysAhead = 14, isEnabled = true, weeklyAvailability = everyDay,
            concurrencyStamp = settings.GetProperty("concurrencyStamp").GetGuid(),
        });

        var client = Client();
        var slots = await client.GetJsonAsync("/api/v1/public/consultations/slots?days=3");
        var slot = slots.GetProperty("slots")[5].GetDateTime().ToUniversalTime();
        var tokens = new Dictionary<string, string> { ["Alice"] = await TokenAsync(client), ["Bruno"] = await TokenAsync(client) };
        object Booking(string name) => WebsiteTestKit.Form(tokens[name], new Dictionary<string, object?>
        {
            ["name"] = name, ["email"] = $"{name.ToLowerInvariant()}@example.test", ["slotStart"] = slot, ["visitorTimeZone"] = "Europe/Berlin",
            ["serviceSlugs"] = new[] { "seo" }, ["notes"] = "Keen to chat.",
        });

        var results = await Task.WhenAll(
            Client().PostAsJsonAsync("/api/v1/public/consultations", Booking("Alice")),
            Client().PostAsJsonAsync("/api/v1/public/consultations", Booking("Bruno")));
        var codes = results.Select(r => (int)r.StatusCode).OrderBy(c => c).ToArray();
        Assert.Equal(new[] { 201, 409 }, codes);
        var loser = results.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        await loser.ShouldFailAsync(409, "website.slot_taken");
        var key = ConsultationBooking.KeyFor(slot);
        Assert.Equal(1, await _api.WithDbAsync(db => db.Set<ConsultationBooking>().CountAsync(b => b.SlotKey == key)));

        // The slot is gone from the public list; the confirmation went out; a consultation inquiry was published.
        var after = await client.GetJsonAsync("/api/v1/public/consultations/slots?days=3");
        Assert.DoesNotContain(after.GetProperty("slots").EnumerateArray(), s => s.GetDateTime().ToUniversalTime() == slot);
        var winner = results.Single(r => r.StatusCode == HttpStatusCode.Created);
        var confirmation = await winner.ReadJsonAsync();
        Assert.Equal("Europe/Berlin", confirmation.GetProperty("visitorTimeZone").GetString());
        Assert.Contains(CapturedWebsiteEvents.Inquiries, e => e.InquiryType == "Consultation" && (e.Name == "Alice" || e.Name == "Bruno"));

        // Staff cancel frees the slot again.
        var booking = await _api.WithDbAsync(db => db.Set<ConsultationBooking>().SingleAsync(b => b.SlotKey == key));
        await admin.PostJsonAsync($"/api/v1/agency/website/bookings/{booking.Id}/cancel",
            new { reason = "Strategist unavailable", concurrencyStamp = booking.ConcurrencyStamp, notifyVisitor = false }, 200);
        var reopened = await client.GetJsonAsync("/api/v1/public/consultations/slots?days=3");
        Assert.Contains(reopened.GetProperty("slots").EnumerateArray(), s => s.GetDateTime().ToUniversalTime() == slot);
    }

    [Fact]
    public async Task Job_applications_accept_only_real_pdf_cvs()
    {
        var admin = (await _api.CreateClientAsync(Role.Admin)).Client;
        await admin.PostJsonAsync("/api/v1/agency/website/careers/jobs", new
        {
            slug = "growth-marketer", title = "Growth Marketer", department = "Growth", location = "Remote", workplace = "Remote",
            employmentType = "FullTime", summary = "Run experiments.", descriptionMarkdown = "## The role\n\nRun growth experiments.", status = "Open",
            salaryMin = 50000, salaryMax = 70000, salaryCurrency = "USD", salaryPeriod = "Year", requirements = new[] { "3 years' experience" },
        }, 201);
        var client = Client();
        var job = await client.GetJsonAsync("/api/v1/public/careers/growth-marketer");
        Assert.Contains(job.GetProperty("jsonLd").EnumerateArray(), j => j.GetProperty("@type").GetString() == "JobPosting");

        async Task<HttpResponseMessage> ApplyAsync(byte[] cv, string fileName, string contentType)
        {
            var token = await TokenAsync(client);
            var form = new MultipartFormDataContent
            {
                { new StringContent("Grace Hopper"), "name" },
                { new StringContent("grace@example.test"), "email" },
                { new StringContent(token), "formToken" },
                { new StringContent("true"), "consent" },
                { new StringContent(ConsentTexts.CareersVersion), "consentVersion" },
                { new StringContent("https://grace.example.test"), "portfolioUrl" },
            };
            var file = new ByteArrayContent(cv);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(file, "cv", fileName);
            return await client.PostAsync("/api/v1/public/careers/growth-marketer/applications", form);
        }

        var png = await ApplyAsync(WebsiteTestKit.Png(), "cv.pdf", "application/pdf");
        var problem = await png.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.BadRequest, png.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("cv", out _));
        var truncated = WebsiteTestKit.Pdf()[..30];
        Assert.Equal(HttpStatusCode.BadRequest, (await ApplyAsync(truncated, "cv.pdf", "application/pdf")).StatusCode);

        var ok = await ApplyAsync(WebsiteTestKit.Pdf(), "../../etc/Grace CV.pdf", "application/octet-stream");
        Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        var application = await _api.WithDbAsync(db => db.Set<JobApplication>().SingleAsync(a => a.Email == "grace@example.test"));
        Assert.Equal(ApplicationStage.New, application.Stage);

        // Staff pipeline: move with a note and download the CV.
        var detail = await admin.GetJsonAsync($"/api/v1/agency/website/careers/applications/{application.Id}");
        Assert.Equal("Grace CV.pdf", detail.GetProperty("cvFileName").GetString());
        var moved = await admin.PostJsonAsync($"/api/v1/agency/website/careers/applications/{application.Id}/move",
            new { stage = "Screening", note = "Strong portfolio", concurrencyStamp = detail.GetProperty("concurrencyStamp").GetGuid() }, 200);
        Assert.Equal("Screening", moved.GetProperty("stage").GetString());
        Assert.Contains(moved.GetProperty("notes").EnumerateArray(), n => n.GetProperty("body").GetString() == "Strong portfolio");
        var cv = await admin.GetAsync($"/api/v1/agency/website/careers/applications/{application.Id}/cv");
        Assert.Equal("application/pdf", cv.Content.Headers.ContentType!.MediaType);
        Assert.Equal(WebsiteTestKit.Pdf(), await cv.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Staff_inbox_filters_and_updates_inquiries()
    {
        var (_, admin) = await _api.CreateClientAsync(Role.Admin);
        var client = Client();
        var email = $"inbox-{Guid.NewGuid():N}@example.test";
        await client.PostJsonAsync("/api/v1/public/inquiries/contact", WebsiteTestKit.Form(await TokenAsync(client), WebsiteTestKit.Contact(email)), 202);
        var list = await admin.GetJsonAsync($"/api/v1/agency/website/inquiries?type=Contact&status=New&search={Uri.EscapeDataString(email)}");
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        var id = item.GetProperty("id").GetGuid();
        var detail = await admin.GetJsonAsync($"/api/v1/agency/website/inquiries/{id}");
        var updated = await admin.PutJsonAsync($"/api/v1/agency/website/inquiries/{id}",
            new { status = "Qualified", staffNotes = "Good fit", concurrencyStamp = detail.GetProperty("concurrencyStamp").GetGuid() });
        Assert.Equal("Qualified", updated.GetProperty("status").GetString());
        await admin.PutJsonAsync($"/api/v1/agency/website/inquiries/{id}",
            new { status = "Closed", concurrencyStamp = detail.GetProperty("concurrencyStamp").GetGuid() }, 409);
        var overview = await admin.GetJsonAsync("/api/v1/agency/website/overview");
        Assert.True(overview.GetProperty("inquiriesLast30Days").GetInt32() >= 1);
        var csv = await (await admin.GetAsync("/api/v1/agency/website/inquiries/export.csv")).Content.ReadAsStringAsync();
        Assert.Contains(email, csv);
    }
}
