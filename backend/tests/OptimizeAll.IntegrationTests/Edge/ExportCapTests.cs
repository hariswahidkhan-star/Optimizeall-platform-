using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Website;
using OptimizeAll.IntegrationTests.Clients;
using OptimizeAll.IntegrationTests.Crm;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Payouts;

namespace OptimizeAll.IntegrationTests.Edge;

/// <summary>
/// CSV exports never cut the file short without saying so: when more rows match than the export's cap
/// (<c>Exports:*</c>), the export is refused with 422 <c>export.too_large</c> naming the count and the cap, and the same
/// filters narrowed below the cap export every matching row. The caps are lowered for this test on a second host.
/// </summary>
public sealed class ExportCapTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string TooLarge = "export.too_large";

    private WebApplicationFactory<Program> CappedHost() => api.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
        c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Exports:Ledger"] = "2",
            ["Exports:AuditLog"] = "2",
            ["Exports:Users"] = "2",
            ["Exports:PaymentsHub"] = "1",
            ["Exports:Inquiries"] = "2",
            ["Exports:NewsletterSubscribers"] = "1",
            ["Exports:CrmContacts"] = "1",
        })));

    private static async Task<string[]> CsvLinesAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task RefusedAsync(HttpResponseMessage response, int matching, int cap)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == 422, $"Expected 422 but got {(int)response.StatusCode}: {text}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(text);
        Assert.Equal(TooLarge, problem.GetProperty("code").GetString());
        // The message names the count and the cap and says what to do.
        var title = problem.GetProperty("title").GetString();
        Assert.Contains($"{matching:N0} rows match", title);
        Assert.Contains($"at most {cap:N0}", title);
        Assert.Contains("Narrow the filters", title);
    }

    [Fact]
    public async Task Exports_over_their_cap_are_refused_with_422_and_narrower_filters_export_every_row()
    {
        await using var capped = CappedHost();
        await capped.StartAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];

        var adminUser = await api.CreateUserAsync(new[] { Role.Admin });
        var admin = await capped.LoginOnAsync(adminUser);

        // Ledger: three entries for one participant (cap 2), one of them pending approval.
        var participant = await api.CreateUserAsync(new[] { Role.Participant });
        await api.EarnAsync(participant.Id, 10m);
        await api.EarnAsync(participant.Id, 11m);
        await api.EarnAsync(participant.Id, 12m, type: OptimizeAll.Domain.Ledger.EarningType.QualityBonus, requiresApproval: true);
        await RefusedAsync(await admin.GetAsync($"/api/v1/finance/ledger/export.csv?userId={participant.Id}"), 3, 2);
        Assert.Equal(2, (await CsvLinesAsync(await admin.GetAsync($"/api/v1/finance/ledger/export.csv?userId={participant.Id}&status=PendingApproval"))).Length);

        // Audit log: three rows about one entity (cap 2).
        await api.WithDbAsync(async db =>
        {
            for (var i = 0; i < 3; i++)
                db.Set<AuditLog>().Add(new AuditLog { CreatedAt = DateTime.UtcNow, ActorType = "system", Action = $"test.cap_{tag}_{i}", EntityType = "Test", EntityId = tag });
            await db.SaveChangesAsync();
        });
        await RefusedAsync(await admin.GetAsync($"/api/v1/admin/audit-logs/export.csv?entityId={tag}"), 3, 2);
        Assert.Equal(2, (await CsvLinesAsync(await admin.GetAsync($"/api/v1/admin/audit-logs/export.csv?entityId={tag}&action=test.cap_{tag}_1"))).Length);

        // Users: three with one email domain (cap 2).
        var domain = $"cap{tag}.example";
        foreach (var name in new[] { "a", "b", "c" }) await api.CreateUserAsync(email: $"{name}@{domain}");
        await RefusedAsync(await admin.GetAsync($"/api/v1/admin/users/export.csv?search={domain}"), 3, 2);
        Assert.Equal(2, (await CsvLinesAsync(await admin.GetAsync($"/api/v1/admin/users/export.csv?search=a@{domain}"))).Length);

        // Payments hub: two open invoices of one client (cap 1).
        var client = await api.CreateClientAccountAsync();
        var first = await admin.IssuedInvoiceAsync(client.Id);
        await admin.IssuedInvoiceAsync(client.Id);
        await RefusedAsync(await admin.GetAsync($"/api/v1/admin/payments/export.csv?clientAccountId={client.Id}"), 2, 1);
        Assert.Equal(2, (await CsvLinesAsync(await admin.GetAsync($"/api/v1/admin/payments/export.csv?invoiceId={first.GetGuid("id")}"))).Length);

        // Website inquiries (cap 2) and newsletter subscribers (cap 1).
        await api.WithDbAsync(async db =>
        {
            for (var i = 0; i < 3; i++)
                db.Set<WebsiteInquiry>().Add(new WebsiteInquiry
                {
                    Type = InquiryType.Contact, Name = $"Cap {tag} {i}", Email = $"lead{i}@{domain}", Company = $"Cap{tag}", ConsentVersion = "v1", ConsentAt = DateTime.UtcNow,
                });
            for (var i = 0; i < 2; i++)
                db.Set<NewsletterSubscriber>().Add(new NewsletterSubscriber
                {
                    Email = $"news{i}@{domain}", NormalizedEmail = $"news{i}@{domain}", Status = NewsletterStatus.Confirmed,
                    UnsubscribeTokenHash = Guid.NewGuid().ToString("N"), ConsentVersion = "v1", ConsentAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        });
        await RefusedAsync(await admin.GetAsync($"/api/v1/agency/website/inquiries/export.csv?search=Cap{tag}"), 3, 2);
        Assert.Equal(2, (await CsvLinesAsync(await admin.GetAsync($"/api/v1/agency/website/inquiries/export.csv?search=lead1@{domain}"))).Length);
        await RefusedAsync(await admin.GetAsync($"/api/v1/agency/website/newsletter/subscribers/export.csv?search={domain}"), 2, 1);
        Assert.Equal(2, (await CsvLinesAsync(await admin.GetAsync($"/api/v1/agency/website/newsletter/subscribers/export.csv?search=news0@{domain}"))).Length);

        // CRM contacts: two with one tag (cap 1).
        foreach (var n in new[] { "x", "y" })
            (await admin.PostAsJsonAsync("/api/v1/agency/crm/contacts", new { firstName = n, lastName = "Cap", email = $"{n}@{domain}", tags = new[] { $"cap{tag}" } }))
                .EnsureSuccessStatusCode();
        await RefusedAsync(await admin.GetAsync($"/api/v1/agency/crm/contacts/export.csv?tag=cap{tag}"), 2, 1);
        Assert.Equal(2, (await CsvLinesAsync(await admin.GetAsync($"/api/v1/agency/crm/contacts/export.csv?tag=cap{tag}&search=x@{domain}"))).Length);
    }
}
