using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.IntegrationTests.Infrastructure;
using Xunit;

namespace OptimizeAll.IntegrationTests.Auth;

/// <summary>
/// Authorization is default-deny (FallbackPolicy = authenticated user): only the intentionally public endpoints carry
/// [AllowAnonymous]; everything else answers 401 to an anonymous caller before any handler runs.
/// </summary>
public sealed class DefaultDenyTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    /// <summary>Every endpoint that may be called without signing in ("METHOD route-pattern").</summary>
    private static readonly string[] IntendedPublic =
    {
        "POST api/v1/auth/register",
        "POST api/v1/auth/verify-email",
        "POST api/v1/auth/resend-verification",
        "POST api/v1/auth/login",
        "POST api/v1/auth/refresh",
        "POST api/v1/auth/logout",
        "POST api/v1/auth/forgot-password",
        "POST api/v1/auth/reset-password",
        "GET api/v1/public/invitations/{code}",
        "GET api/v1/public/campaigns/{slug}",
        "POST api/v1/public/conversions",
        "GET t/{code}",
        "GET api/v1/public/lp/{clientSlug}/{pageSlug}",
        "GET api/v1/public/forms/{formId:guid}",
        "POST api/v1/public/forms/{formId:guid}/submissions",
        "GET api/v1/files/{id:guid}",
        "GET api/v1/campaign-categories",
        "GET api/v1/content/faqs",
        "GET api/v1/meta/currencies",
        "GET api/v1/dev/mailbox",
        // CRM & billing: tokenized public proposal (/p/{token}) and invoice (/i/{token}) pages.
        "GET api/v1/public/proposals/{token}",
        "POST api/v1/public/proposals/{token}/accept",
        "POST api/v1/public/proposals/{token}/decline",
        "GET api/v1/public/invoices/{token}",
        "GET api/v1/public/invoices/{token}/document",
        // Public agency website (Website module): content reads, SEO files and lead forms.
        "GET api/v1/public/site",
        "GET api/v1/public/home",
        "GET api/v1/public/services",
        "GET api/v1/public/services/{slug}",
        "GET api/v1/public/pricing",
        "GET api/v1/public/industries",
        "GET api/v1/public/industries/{slug}",
        "GET api/v1/public/case-studies",
        "GET api/v1/public/case-studies/{slug}",
        "GET api/v1/public/testimonials",
        "GET api/v1/public/team",
        "GET api/v1/public/pages/{slug}",
        "GET api/v1/public/search",
        "GET api/v1/public/blog",
        "GET api/v1/public/blog/{slug}",
        "GET api/v1/public/blog/rss.xml",
        "GET api/v1/public/sitemap.xml",
        "GET robots.txt",
        "GET api/v1/public/forms/token",
        "POST api/v1/public/inquiries/contact",
        "POST api/v1/public/inquiries/audit",
        "POST api/v1/public/inquiries/quote",
        "GET api/v1/public/consultations/slots",
        "POST api/v1/public/consultations",
        "POST api/v1/public/newsletter/subscribe",
        "POST api/v1/public/newsletter/confirm",
        "POST api/v1/public/newsletter/unsubscribe",
        "GET api/v1/public/careers",
        "GET api/v1/public/careers/{slug}",
        "POST api/v1/public/careers/{slug}/applications",
        // Email & SMS marketing (M4a): tracking, one-click unsubscribe, preference center, sign-up forms, signed
        // conversion/event APIs and provider webhooks (signature-verified).
        "GET e/o/{token}.gif",
        "GET e/c/{token}",
        "GET e/u/{token}",
        "POST e/u/{token}",
        "GET api/v1/public/email/preferences/{token}",
        "PUT api/v1/public/email/preferences/{token}",
        "POST api/v1/public/email/unsubscribe/{token}",
        "GET api/v1/public/email/forms/{key}",
        "POST api/v1/public/email/forms/{key}",
        "POST api/v1/public/email/confirm/{token}",
        "POST api/v1/public/email/conversions",
        "POST api/v1/public/email/events",
        "POST api/v1/public/email/webhooks/sendgrid/{workspace}",
        "POST api/v1/public/email/webhooks/mailgun/{workspace}",
        "POST api/v1/public/sms/webhooks/twilio/{workspace}/inbound",
        "POST api/v1/public/sms/webhooks/twilio/{workspace}/status",
        "* /health/live",
        "* /health/ready",
    };

    private IReadOnlyList<(string Key, RouteEndpoint Endpoint)> Endpoints() =>
        api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e =>
            {
                var methods = e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "*" };
                return methods.Select(m => ($"{m} {e.RoutePattern.RawText}", e));
            })
            .ToList();

    [Fact]
    public void Only_the_intended_endpoints_allow_anonymous_access()
    {
        var anonymous = Endpoints().Where(e => e.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.Key).OrderBy(k => k).ToList();
        Assert.Equal(IntendedPublic.OrderBy(k => k), anonymous);
    }

    [Fact]
    public async Task Anonymous_requests_to_every_protected_endpoint_get_401()
    {
        var client = api.CreateClient();
        var checkedCount = 0;
        foreach (var (key, endpoint) in Endpoints().Where(e => e.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            var method = key.Split(' ')[0];
            var path = "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
            {
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal => literal.Content,
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart p when p.ParameterPolicies.Any(pp => pp.Content == "guid") =>
                    Guid.NewGuid().ToString(),
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart p when p.ParameterPolicies.Any(pp => pp.Content is "int" or "long") => "1",
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart => "sample",
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternSeparatorPart separator => separator.Content,
                _ => string.Empty,
            }))));
            using var request = new HttpRequestMessage(new HttpMethod(method == "*" ? "GET" : method), path);
            if (method is "POST" or "PUT" or "PATCH")
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{key} ({path}) answered {(int)response.StatusCode} to an anonymous caller");
            checkedCount++;
        }
        Assert.True(checkedCount > 150, $"only {checkedCount} protected endpoints found");
    }

    [Fact]
    public async Task Every_intended_public_endpoint_works_anonymously()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        var email = $"public-{Guid.NewGuid():N}@example.test";

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email, password = "Horizon-Tulip-42", displayName = "Public Test", countryCode = "PK", languageCode = "en",
            timeZone = "Asia/Karachi", acceptTerms = true,
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/dev/mailbox?to={Uri.EscapeDataString(email)}")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/auth/resend-verification", new { email })).StatusCode);
        await (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token = "not-a-token" })).ShouldFailAsync(400);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email })).StatusCode);
        await (await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = "not-a-token", newPassword = "Another-Tulip-43" }))
            .ShouldFailAsync(400);

        var user = await api.CreateUserAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode); // refresh cookie, no bearer
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/campaign-categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/content/faqs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/meta/currencies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

        // Handlers run (and answer with their own statuses) instead of an authentication challenge.
        await (await client.GetAsync("/api/v1/public/invitations/NOPE1234")).ShouldFailAsync(404);
        await (await client.GetAsync("/api/v1/public/campaigns/no-such-campaign")).ShouldFailAsync(404);
        await (await client.GetAsync($"/api/v1/files/{Guid.NewGuid()}")).ShouldFailAsync(404, "file.not_found");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/t/NOPE1234")).StatusCode);
        await (await client.GetAsync("/api/v1/public/lp/no-client/no-page")).ShouldFailAsync(404, "page.not_found");
        await (await client.GetAsync($"/api/v1/public/forms/{Guid.NewGuid()}")).ShouldFailAsync(404, "form.not_found");
        await (await client.PostAsync($"/api/v1/public/forms/{Guid.NewGuid()}/submissions", new StringContent("{}", Encoding.UTF8, "application/json")))
            .ShouldFailAsync(404, "form.not_found");
        await (await client.PostAsync("/api/v1/public/conversions", new StringContent("{}", Encoding.UTF8, "application/json")))
            .ShouldFailAsync(503, "tracking.postback_not_configured");
    }
}
