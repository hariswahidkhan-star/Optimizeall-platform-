using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Crm;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Crm.CrmBillingKit;

namespace OptimizeAll.IntegrationTests.Billing;

/// <summary>Service catalog, tax-rate deletion, invoice duplication, draft contract deletion and payment-term options.</summary>
public sealed class BillingEditingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Billing = "/api/v1/agency/billing";

    [Fact]
    public async Task Service_catalog_crud_permissions_validation_and_concurrency()
    {
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep); // billing.view only

        var seeded = await (await sales.GetAsync($"{Billing}/catalog")).ReadJsonAsync();
        Assert.Contains(seeded.EnumerateArray(), i => i.Str("name") == "SEO retainer");

        var body = new { name = "Audit sprint", description = "Two-week CRO audit", serviceSlug = "cro", currency = "GBP", unitPrice = 1800m, quantity = 1m,
            recurrence = "OneTime", sortOrder = 1, isActive = true };
        await (await sales.PostAsJsonAsync($"{Billing}/catalog", body)).ShouldFailAsync(403);
        var created = await (await finance.PostAsJsonAsync($"{Billing}/catalog", body)).ReadJsonAsync();
        Assert.Equal("GBP", created.Str("currency"));
        await (await finance.PostAsJsonAsync($"{Billing}/catalog", new { name = "Bad", description = "x", currency = "XXX", unitPrice = 1m })).ShouldFailAsync(400);
        await (await finance.PostAsJsonAsync($"{Billing}/catalog", new { name = "Bad", description = "x", currency = "USD", unitPrice = -10m }))
            .ShouldFailAsync(400);

        var id = created.GetGuid("id");
        var updated = await (await finance.PutAsJsonAsync($"{Billing}/catalog/{id}", new
        {
            name = "Audit sprint", description = "Two-week CRO audit", currency = "GBP", unitPrice = 2000m, quantity = 1m, recurrence = "OneTime",
            isActive = false, concurrencyStamp = created.GetGuid("concurrencyStamp"),
        })).ReadJsonAsync();
        Assert.Equal(2000m, updated.Dec("unitPrice"));
        await (await finance.PutAsJsonAsync($"{Billing}/catalog/{id}", new
        {
            name = "Stale", description = "x", currency = "GBP", unitPrice = 1m, concurrencyStamp = created.GetGuid("concurrencyStamp"),
        })).ShouldFailAsync(409, "concurrency.conflict");
        Assert.DoesNotContain((await (await sales.GetAsync($"{Billing}/catalog")).ReadJsonAsync()).EnumerateArray(), i => i.GetGuid("id") == id);
        Assert.Contains((await (await sales.GetAsync($"{Billing}/catalog?includeInactive=true")).ReadJsonAsync()).EnumerateArray(), i => i.GetGuid("id") == id);

        Assert.Equal(HttpStatusCode.NoContent, (await finance.DeleteAsync($"{Billing}/catalog/{id}")).StatusCode);
        await (await finance.DeleteAsync($"{Billing}/catalog/{id}")).ShouldFailAsync(404);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "billing.catalog_item_deleted" && a.EntityId == id.ToString())));
    }

    [Fact]
    public async Task Unused_tax_rates_can_be_deleted_and_used_ones_must_be_deactivated()
    {
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var unused = await (await finance.PostAsJsonAsync($"{Billing}/tax-rates", new { name = "Temp 5", ratePercent = 5m, isActive = true })).ReadJsonAsync();
        var used = await (await finance.PostAsJsonAsync($"{Billing}/tax-rates", new { name = "Used 7", ratePercent = 7m, isActive = true })).ReadJsonAsync();
        var client = await api.CreateClientAccountAsync();
        await finance.CreateDraftInvoiceAsync(client.Id, Line("Taxed", 1, 100m, taxRateId: used.GetGuid("id")));

        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        await (await sales.DeleteAsync($"{Billing}/tax-rates/{unused.GetGuid("id")}")).ShouldFailAsync(403);
        Assert.Equal(HttpStatusCode.NoContent, (await finance.DeleteAsync($"{Billing}/tax-rates/{unused.GetGuid("id")}")).StatusCode);
        await (await finance.DeleteAsync($"{Billing}/tax-rates/{used.GetGuid("id")}")).ShouldFailAsync(409, "billing.tax_rate_in_use");
        await (await finance.DeleteAsync($"{Billing}/tax-rates/{Guid.NewGuid()}")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Invoices_can_be_duplicated_into_a_new_draft_in_any_status()
    {
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var client = await api.CreateClientAccountAsync();
        var issued = await finance.IssuedInvoiceAsync(client.Id, Line("Strategy workshop", 2, 750m));
        Assert.Equal("Issued", issued.Str("status"));

        var copy = await (await finance.PostAsync($"{Billing}/invoices/{issued.GetGuid("id")}/duplicate", null)).ReadJsonAsync();
        Assert.Equal("Draft", copy.Str("status"));
        Assert.Equal(JsonValueKind.Null, copy.GetProperty("number").ValueKind);
        Assert.Equal(1500m, copy.GetProperty("totals").Dec("total"));
        Assert.NotEqual(issued.GetGuid("id"), copy.GetGuid("id"));
        // The issued original is untouched.
        Assert.Equal("Issued", (await finance.GetInvoiceAsync(issued.GetGuid("id"))).Str("status"));

        var (_, sales) = await api.CreateClientAsync(Role.SalesRep); // billing.view, not billing.manage
        await (await sales.PostAsync($"{Billing}/invoices/{issued.GetGuid("id")}/duplicate", null)).ShouldFailAsync(403);
        await (await finance.PostAsync($"{Billing}/invoices/{Guid.NewGuid()}/duplicate", null)).ShouldFailAsync(404);
    }

    private static object ContractBody(Guid clientId, DateOnly start) => new
    {
        clientAccountId = clientId, title = "Retainer", currency = "USD", startDate = start.Iso(), billingFrequency = "Monthly", autoRenew = false,
        renewalTermMonths = 6, noticePeriodDays = 30, paymentTermsDays = 7, lines = new[] { Line("Monthly retainer", 1, 1000m) },
    };

    [Fact]
    public async Task Draft_contracts_can_be_deleted_until_they_are_activated()
    {
        var (_, am) = await api.CreateClientAsync(Role.AccountManager);
        var client = await api.CreateClientAccountAsync();
        var draft = await (await am.PostAsJsonAsync("/api/v1/agency/contracts", ContractBody(client.Id, api.Today().AddDays(30)))).ReadJsonAsync();
        Assert.Equal(6, draft.GetProperty("renewalTermMonths").GetInt32());
        Assert.Equal(7, draft.GetProperty("paymentTermsDays").GetInt32());
        var id = draft.GetGuid("id");

        await (await am.DeleteAsync($"/api/v1/agency/contracts/{id}?concurrencyStamp={Guid.NewGuid()}")).ShouldFailAsync(409, "concurrency.conflict");
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep); // no contracts.manage
        await (await sales.DeleteAsync($"/api/v1/agency/contracts/{id}?concurrencyStamp={draft.GetGuid("concurrencyStamp")}")).ShouldFailAsync(403);
        Assert.Equal(HttpStatusCode.NoContent,
            (await am.DeleteAsync($"/api/v1/agency/contracts/{id}?concurrencyStamp={draft.GetGuid("concurrencyStamp")}")).StatusCode);
        await (await am.GetAsync($"/api/v1/agency/contracts/{id}")).ShouldFailAsync(404);

        var active = await (await am.PostAsJsonAsync("/api/v1/agency/contracts", ContractBody(client.Id, api.Today().AddDays(30)))).ReadJsonAsync();
        var activated = await (await am.PostAsJsonAsync($"/api/v1/agency/contracts/{active.GetGuid("id")}/activate",
            new { concurrencyStamp = active.GetGuid("concurrencyStamp") })).ReadJsonAsync();
        await (await am.DeleteAsync($"/api/v1/agency/contracts/{active.GetGuid("id")}?concurrencyStamp={activated.GetGuid("concurrencyStamp")}"))
            .ShouldFailAsync(409, "billing.contract_not_deletable");
        await (await am.DeleteAsync($"/api/v1/agency/contracts/{Guid.NewGuid()}?concurrencyStamp={Guid.NewGuid()}")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Payment_term_options_are_saved_with_the_default_included_and_validated()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var settings = await (await admin.GetAsync($"{Billing}/settings")).ReadJsonAsync();
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settings.GetRawText())!;
        dict["paymentTermsOptions"] = JsonSerializer.SerializeToElement(new[] { 30, 7 });
        dict["paymentTermsDays"] = JsonSerializer.SerializeToElement(21);
        var saved = await (await admin.PutAsJsonAsync($"{Billing}/settings", new { settings = dict, reason = "Offer net 21", confirm = true })).ReadJsonAsync();
        Assert.Equal(new[] { 7, 21, 30 }, saved.GetProperty("paymentTermsOptions").EnumerateArray().Select(e => e.GetInt32()));

        dict["paymentTermsOptions"] = JsonSerializer.SerializeToElement(new[] { 400 });
        await (await admin.PutAsJsonAsync($"{Billing}/settings", new { settings = dict, reason = "Too long", confirm = true }))
            .ShouldFailAsync(400, "billing.invalid_settings");
    }
}
