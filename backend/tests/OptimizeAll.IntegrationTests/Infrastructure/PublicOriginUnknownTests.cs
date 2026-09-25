using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Crm;

namespace OptimizeAll.IntegrationTests.Infrastructure;

/// <summary>
/// No public origin at all (no Site URL, no Email:AppBaseUrl, no request through a trusted proxy yet, nothing remembered):
/// links that leave the site must be absolute, so they are refused or not sent instead of going out root-relative. One test
/// on its own fixture (a fresh database): the proxied request at the end remembers an origin for the rest of the host's life.
/// </summary>
public sealed class PublicOriginUnknownTests(PublicOriginFixture f) : IClassFixture<PublicOriginFixture>
{
    private int MailsTo(string address) =>
        Directory.Exists(f.Api.MailDirectory)
            ? Directory.GetFiles(f.Api.MailDirectory).Count(file => File.ReadAllText(file).Contains(address, StringComparison.OrdinalIgnoreCase))
            : 0;

    private static async Task<JsonElement> ProposalAsync(HttpClient sales, DateOnly today)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var company = await (await sales.PostAsJsonAsync("/api/v1/agency/crm/companies", new { name = $"Origin {suffix}", countryCode = "GB" }))
            .ReadJsonAsync();
        var contact = await (await sales.PostAsJsonAsync("/api/v1/agency/crm/contacts", new
        {
            firstName = "Olive", lastName = "Origin", email = $"olive@origin-{suffix}.example", companyId = company.GetGuid("id"),
        })).ReadJsonAsync();
        var deal = await (await sales.PostAsJsonAsync("/api/v1/agency/crm/deals", new
        {
            title = $"Origin {suffix}", currency = "USD", value = 5000m, companyId = company.GetGuid("id"), primaryContactId = contact.GetGuid("id"),
        })).ReadJsonAsync();
        return await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", new
        {
            title = "Retainer", dealId = deal.GetGuid("id"), validUntil = today.AddDays(14).Iso(),
            lines = new[] { CrmBillingKit.Line("SEO retainer", 1, 2500m, "Monthly") },
        })).ReadJsonAsync();
    }

    [Fact]
    public async Task Without_a_public_origin_emails_and_emailed_links_are_refused_until_one_is_known()
    {
        // Not through the trusted proxy (no X-Forwarded-*): the request's own host is never used.
        var sales = await f.Host.LoginAsync(await f.Api.CreateUserAsync(new[] { Role.SalesRep }));
        var proposal = await ProposalAsync(sales, f.Api.Today());
        var id = proposal.GetGuid("id");

        // Emailing the proposal link: 422 before anything changes.
        await (await sales.PostAsJsonAsync($"/api/v1/agency/proposals/{id}/send",
            new { concurrencyStamp = proposal.GetGuid("concurrencyStamp"), email = true })).ShouldFailAsync(422, "hosting.public_origin_unknown");
        var unchanged = await (await sales.GetAsync($"/api/v1/agency/proposals/{id}")).ReadJsonAsync();
        Assert.Equal("Draft", unchanged.Str("status"));

        // Publishing without email still works; the web app shows the root-relative link on its own origin.
        var published = await (await sales.PostAsJsonAsync($"/api/v1/agency/proposals/{id}/send",
            new { concurrencyStamp = unchanged.GetGuid("concurrencyStamp"), email = false })).ReadJsonAsync();
        Assert.StartsWith("/p/", published.Str("shareUrl"));
        Assert.False(published.GetProperty("emailed").GetBoolean());

        // A password reset email is not sent with a root-relative link.
        var user = await f.Api.CreateUserAsync();
        var anon = f.Host.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = user.Email })).StatusCode);
        Assert.Equal(0, MailsTo(user.Email));

        // Once a request arrives through the trusted proxy, the origin is known and the email goes out with an absolute link.
        // (Another user: a reset request within two minutes of the last one is ignored.)
        var other = await f.Api.CreateUserAsync();
        var proxied = PublicOriginFixture.Proxied(f.Host);
        Assert.Equal(HttpStatusCode.Accepted, (await proxied.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = other.Email })).StatusCode);
        Assert.Equal(1, MailsTo(other.Email));
        var mail = Directory.GetFiles(f.Api.MailDirectory).Select(File.ReadAllText).Single(m => m.Contains(other.Email, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(PublicOriginFixture.WebOrigin + "/reset-password", mail.Replace("=\r\n", string.Empty));
    }
}
