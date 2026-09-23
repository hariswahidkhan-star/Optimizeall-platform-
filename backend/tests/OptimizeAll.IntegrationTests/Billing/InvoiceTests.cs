using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Crm;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Crm.CrmBillingKit;

namespace OptimizeAll.IntegrationTests.Billing;

public sealed class InvoiceTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Invoice_numbers_are_unique_and_gapless_under_10_concurrent_issues()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync();
        var drafts = new List<JsonElement>();
        for (var i = 0; i < 10; i++) drafts.Add(await admin.CreateDraftInvoiceAsync(client.Id, Line($"Item {i}", 1, 100m + i)));

        var responses = await Task.WhenAll(drafts.Select(d => admin.PostAsJsonAsync($"/api/v1/agency/billing/invoices/{d.GetGuid("id")}/issue",
            new { concurrencyStamp = d.GetGuid("concurrencyStamp") })));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var numbers = new List<string>();
        foreach (var r in responses) numbers.Add((await r.ReadJsonAsync()).Str("number"));
        Assert.Equal(10, numbers.Distinct().Count());

        var year = api.Today().Year;
        var all = await api.WithDbAsync(db => db.Set<Invoice>().Where(i => i.Number != null && i.Number.StartsWith($"OA-{year}-"))
            .Select(i => i.Number!).ToListAsync());
        var sequence = all.Select(n => int.Parse(n.Split('-')[2])).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(1, sequence.Count), sequence); // no gaps, no duplicates
        Assert.All(numbers, n => Assert.Matches($"^OA-{year}-\\d{{4}}$", n));
    }

    [Fact]
    public async Task Partial_payments_overpayment_double_recording_and_invoice_paid_once()
    {
        var factory = api.WithInvoicePaidCounter();
        var adminUser1 = await api.CreateUserAsync(new[] { Role.Admin });
        var adminUser2 = await api.CreateUserAsync(new[] { Role.Admin });
        var admin1 = await factory.LoginAsync(adminUser1);
        var admin2 = await factory.LoginAsync(adminUser2);
        var client = await api.CreateClientAccountAsync(currency: "USD");
        var invoice = await admin1.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 1000m));
        var id = invoice.GetGuid("id");
        Assert.Equal("Issued", invoice.Str("status"));
        Assert.Equal(1000m, invoice.Dec("balance"));

        var first = await admin1.RecordPaymentAsync(id, invoice.GetGuid("concurrencyStamp"), 400m, "WIRE-1");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var afterFirst = (await first.ReadJsonAsync()).GetProperty("invoice");
        Assert.Equal("PartiallyPaid", afterFirst.Str("status"));
        Assert.Equal(600m, afterFirst.Dec("balance"));

        await (await admin1.RecordPaymentAsync(id, afterFirst.GetGuid("concurrencyStamp"), 600.01m, "WIRE-2")).ShouldFailAsync(409, "billing.overpayment");
        await (await admin1.RecordPaymentAsync(id, afterFirst.GetGuid("concurrencyStamp"), 10.001m, "WIRE-2")).ShouldFailAsync(400, "billing.invalid_amount");
        await (await admin1.RecordPaymentAsync(id, afterFirst.GetGuid("concurrencyStamp"), 50m, "WIRE-1")).ShouldFailAsync(409, "billing.duplicate_reference");

        // Two finance users record the same bank transfer at once: exactly one wins, the other sees a conflict.
        var requestA = Guid.NewGuid();
        var race = await Task.WhenAll(
            admin1.RecordPaymentAsync(id, afterFirst.GetGuid("concurrencyStamp"), 300m, "WIRE-2", requestA),
            admin2.RecordPaymentAsync(id, afterFirst.GetGuid("concurrencyStamp"), 300m, "WIRE-2B"));
        Assert.Equal(1, race.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, race.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var current = await admin1.GetInvoiceAsync(id);
        Assert.Equal(300m, current.Dec("balance"));

        // A retried request (same requestId) returns the original payment instead of paying twice.
        var winner = race[0].StatusCode == HttpStatusCode.Created ? requestA : (Guid?)null;
        if (winner is { } w)
        {
            var retry = await admin1.RecordPaymentAsync(id, afterFirst.GetGuid("concurrencyStamp"), 300m, "WIRE-2", w);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.True((await retry.ReadJsonAsync()).GetProperty("replayed").GetBoolean());
            await (await admin1.RecordPaymentAsync(id, current.GetGuid("concurrencyStamp"), 1m, "OTHER", w)).ShouldFailAsync(409, "billing.request_id_reused");
        }

        var settle = await (await admin2.RecordPaymentAsync(id, current.GetGuid("concurrencyStamp"), 300m, "WIRE-3")).ReadJsonAsync();
        Assert.Equal("Paid", settle.GetProperty("invoice").Str("status"));
        Assert.Equal(0m, settle.GetProperty("invoice").Dec("balance"));
        Assert.Equal(3, settle.GetProperty("invoice").GetProperty("payments").GetArrayLength());
        await (await admin2.RecordPaymentAsync(id, settle.GetProperty("invoice").GetGuid("concurrencyStamp"), 1m, "WIRE-4"))
            .ShouldFailAsync(409, "billing.invoice_not_open");
        Assert.Equal(1, InvoicePaidCounter.Counts.GetValueOrDefault(id));
        Assert.Equal(3, await api.WithDbAsync(db => db.Set<AuditLog>().CountAsync(a => a.Action == "billing.payment_recorded" && a.EntityId == id.ToString())));
    }

    [Fact]
    public async Task Staff_who_belong_to_the_client_cannot_record_its_payments()
    {
        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync();
        await api.WithDbAsync(async db =>
        {
            db.Set<ClientMember>().Add(new ClientMember { ClientAccountId = client.Id, UserId = adminUser.Id, Role = ClientMemberRole.Owner });
            await db.SaveChangesAsync();
        });
        var invoice = await admin.IssuedInvoiceAsync(client.Id);
        await (await admin.RecordPaymentAsync(invoice.GetGuid("id"), invoice.GetGuid("concurrencyStamp"), 10m, "SELF-1"))
            .ShouldFailAsync(403, "billing.self_payment");
    }

    [Fact]
    public async Task Issued_invoices_are_immutable_and_corrected_with_credit_notes()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync(currency: "KWD", country: "KW");
        var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Consulting", 1, 500m));
        var id = invoice.GetGuid("id");
        await (await admin.PutAsJsonAsync($"/api/v1/agency/billing/invoices/{id}", new
        {
            clientAccountId = client.Id, lines = new[] { Line("Changed", 1, 1m) }, concurrencyStamp = invoice.GetGuid("concurrencyStamp"),
        })).ShouldFailAsync(409, "billing.invoice_not_draft");
        await (await admin.DeleteAsync($"/api/v1/agency/billing/invoices/{id}")).ShouldFailAsync(409, "billing.invoice_not_draft");

        var credit = await (await admin.PostAsJsonAsync("/api/v1/agency/billing/credit-notes", new
        {
            requestId = Guid.NewGuid(), invoiceId = id, amount = 200.125m, reason = "Scope reduced by one workshop",
        })).ReadJsonAsync();
        Assert.StartsWith("CN-", credit.Str("number"));
        Assert.Equal("Applied", credit.Str("status"));
        var afterCredit = await admin.GetInvoiceAsync(id);
        Assert.Equal(299.875m, afterCredit.Dec("balance"));
        Assert.Equal("PartiallyPaid", afterCredit.Str("status"));
        Assert.Equal(500m, afterCredit.GetProperty("totals").Dec("total")); // the invoice itself is unchanged

        await (await admin.PostAsJsonAsync("/api/v1/agency/billing/credit-notes", new
        {
            requestId = Guid.NewGuid(), invoiceId = id, amount = 300m, reason = "More than the balance",
        })).ShouldFailAsync(409, "billing.credit_exceeds_balance");

        // An unapplied credit applied later, partially then beyond what is left.
        var open = await (await admin.PostAsJsonAsync("/api/v1/agency/billing/credit-notes", new
        {
            requestId = Guid.NewGuid(), clientAccountId = client.Id, amount = 100m, reason = "Goodwill credit",
        })).ReadJsonAsync();
        Assert.Equal("Open", open.Str("status"));
        var applied = await (await admin.PostAsJsonAsync($"/api/v1/agency/billing/credit-notes/{open.GetGuid("id")}/apply",
            new { invoiceId = id, amount = 60m })).ReadJsonAsync();
        Assert.Equal(40m, applied.Dec("remaining"));
        await (await admin.PostAsJsonAsync($"/api/v1/agency/billing/credit-notes/{open.GetGuid("id")}/apply", new { invoiceId = id, amount = 50m }))
            .ShouldFailAsync(409, "billing.credit_exhausted");
        Assert.Equal(239.875m, (await admin.GetInvoiceAsync(id)).Dec("balance"));

        var otherClient = await api.CreateClientAccountAsync(currency: "KWD");
        var otherInvoice = await admin.IssuedInvoiceAsync(otherClient.Id);
        await (await admin.PostAsJsonAsync($"/api/v1/agency/billing/credit-notes/{open.GetGuid("id")}/apply",
            new { invoiceId = otherInvoice.GetGuid("id"), amount = 10m })).ShouldFailAsync(409, "billing.credit_mismatch");
    }

    [Fact]
    public async Task Void_and_write_off_need_confirmation_a_reason_and_a_second_person()
    {
        var (_, issuer) = await api.CreateClientAsync(Role.Admin);
        var (_, reviewer) = await api.CreateClientAsync(Role.Admin);
        var (_, accountManager) = await api.CreateClientAsync(Role.AccountManager);
        var client = await api.CreateClientAccountAsync();
        var invoice = await issuer.IssuedInvoiceAsync(client.Id, Line("Setup", 1, 800m));
        var id = invoice.GetGuid("id");
        var stamp = invoice.GetGuid("concurrencyStamp");

        await (await accountManager.PostAsJsonAsync($"/api/v1/agency/billing/invoices/{id}/void", new { reason = "Duplicate invoice", confirm = true, concurrencyStamp = stamp }))
            .ShouldFailAsync(403);
        await (await issuer.PostAsJsonAsync($"/api/v1/agency/billing/invoices/{id}/void", new { reason = "Duplicate invoice", confirm = true, concurrencyStamp = stamp }))
            .ShouldFailAsync(403, "billing.four_eyes");
        await (await reviewer.PostAsJsonAsync($"/api/v1/agency/billing/invoices/{id}/void", new { reason = "Duplicate invoice", confirm = false, concurrencyStamp = stamp }))
            .ShouldFailAsync(400, "request.confirm_required");
        await (await reviewer.PostAsJsonAsync($"/api/v1/agency/billing/invoices/{id}/void", new { reason = "", confirm = true, concurrencyStamp = stamp }))
            .ShouldFailAsync(400);
        var voided = await (await reviewer.PostAsJsonAsync($"/api/v1/agency/billing/invoices/{id}/void",
            new { reason = "Duplicate invoice", confirm = true, concurrencyStamp = stamp })).ReadJsonAsync();
        Assert.Equal("Void", voided.Str("status"));
        Assert.Equal(0m, voided.Dec("balance"));
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "billing.invoice_voided" && a.Reason == "Duplicate invoice")));

        var partly = await issuer.IssuedInvoiceAsync(client.Id, Line("Ads", 1, 900m));
        var paid = await (await issuer.RecordPaymentAsync(partly.GetGuid("id"), partly.GetGuid("concurrencyStamp"), 100m, "P-1")).ReadJsonAsync();
        var partlyStamp = paid.GetProperty("invoice").GetGuid("concurrencyStamp");
        await (await reviewer.PostAsJsonAsync($"/api/v1/agency/billing/invoices/{partly.GetGuid("id")}/void",
            new { reason = "Wrong client", confirm = true, concurrencyStamp = partlyStamp })).ShouldFailAsync(409, "billing.invoice_has_payments");
        var writtenOff = await (await reviewer.PostAsJsonAsync($"/api/v1/agency/billing/invoices/{partly.GetGuid("id")}/write-off",
            new { reason = "Client went out of business", confirm = true, concurrencyStamp = partlyStamp })).ReadJsonAsync();
        Assert.Equal("WrittenOff", writtenOff.Str("status"));
        Assert.Equal(800m, writtenOff.Dec("amountWrittenOff"));
        Assert.Equal(0m, writtenOff.Dec("balance"));
    }

    [Fact]
    public async Task Client_users_see_only_their_organizations_invoices_and_viewers_see_no_billing()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var orgA = await api.CreateClientAccountAsync("Tenant A");
        var orgB = await api.CreateClientAccountAsync("Tenant B");
        var invoiceA = await admin.IssuedInvoiceAsync(orgA.Id, Line("A work", 1, 100m));
        var invoiceB = await admin.IssuedInvoiceAsync(orgB.Id, Line("B work", 1, 200m));
        var draftA = await admin.CreateDraftInvoiceAsync(orgA.Id);
        var (_, billingA) = await api.CreateClientMemberAsync(orgA.Id, ClientMemberRole.Billing);
        var (_, viewerA) = await api.CreateClientMemberAsync(orgA.Id, ClientMemberRole.Viewer);
        var (_, ownerB) = await api.CreateClientMemberAsync(orgB.Id, ClientMemberRole.Owner);

        var listA = await (await billingA.GetAsync("/api/v1/client/billing/invoices")).ReadJsonAsync();
        var ids = listA.GetProperty("items").EnumerateArray().Select(i => i.GetGuid("id")).ToList();
        Assert.Contains(invoiceA.GetGuid("id"), ids);
        Assert.DoesNotContain(invoiceB.GetGuid("id"), ids);
        Assert.DoesNotContain(draftA.GetGuid("id"), ids);

        var detail = await (await billingA.GetAsync($"/api/v1/client/billing/invoices/{invoiceA.GetGuid("id")}")).ReadJsonAsync();
        Assert.Equal(100m, detail.Dec("balance"));
        await (await billingA.GetAsync($"/api/v1/client/billing/invoices/{invoiceB.GetGuid("id")}")).ShouldFailAsync(404);
        await (await billingA.GetAsync($"/api/v1/client/billing/invoices/{draftA.GetGuid("id")}")).ShouldFailAsync(404);
        await (await ownerB.GetAsync($"/api/v1/client/billing/invoices/{invoiceA.GetGuid("id")}/document")).ShouldFailAsync(404);
        await (await viewerA.GetAsync("/api/v1/client/billing/invoices")).ShouldFailAsync(403, "client.insufficient_role");
        await (await viewerA.GetAsync($"/api/v1/client/billing/invoices/{invoiceA.GetGuid("id")}")).ShouldFailAsync(403, "client.insufficient_role");
        await (await billingA.GetAsync($"/api/v1/client/billing/statement?clientAccountId={orgB.Id}")).ShouldFailAsync(404);
        // Client users never reach the staff API.
        await (await billingA.GetAsync($"/api/v1/agency/billing/invoices/{invoiceA.GetGuid("id")}")).ShouldFailAsync(403);

        var statement = await (await billingA.GetAsync($"/api/v1/client/billing/statement?clientAccountId={orgA.Id}")).ReadJsonAsync();
        Assert.Equal(100m, statement.Dec("closingBalance"));
        var pay = await (await billingA.PostAsync($"/api/v1/client/billing/invoices/{invoiceA.GetGuid("id")}/pay", null)).ReadJsonAsync();
        Assert.False(pay.GetProperty("available").GetBoolean()); // no gateway configured: never pretends to take payment

        var document = await billingA.GetAsync($"/api/v1/client/billing/invoices/{invoiceA.GetGuid("id")}/document");
        Assert.Equal("text/html", document.Content.Headers.ContentType?.MediaType);
        Assert.Contains("A work", await document.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sending_an_invoice_emails_a_tokenized_public_link()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync(billingEmail: $"ap.{Guid.NewGuid():N}@client.example");
        var draft = await admin.CreateDraftInvoiceAsync(client.Id, Line("Content <script>alert(1)</script>", 2, 150m));
        var issued = await (await admin.PostAsJsonAsync($"/api/v1/agency/billing/invoices/{draft.GetGuid("id")}/issue",
            new { concurrencyStamp = draft.GetGuid("concurrencyStamp"), send = true })).ReadJsonAsync();
        var url = issued.Str("publicUrl");
        Assert.StartsWith("http://app.test/i/", url);
        var token = url[(url.LastIndexOf('/') + 1)..];
        Assert.NotEqual(JsonValueKind.Null, issued.GetProperty("sentAt").ValueKind);
        Assert.Contains(Directory.GetFiles(api.MailDirectory).Select(File.ReadAllText), m => m.Contains(token, StringComparison.Ordinal));

        var anonymous = api.CreateClient();
        var view = await (await anonymous.GetAsync($"/api/v1/public/invoices/{token}")).ReadJsonAsync();
        Assert.Equal(issued.Str("number"), view.Str("number"));
        Assert.Equal(300m, view.GetProperty("totals").Dec("total"));
        var html = await (await anonymous.GetAsync($"/api/v1/public/invoices/{token}/document")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        await (await anonymous.GetAsync($"/api/v1/public/invoices/{new string('x', 43)}")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Billing_settings_are_sensitive_and_tax_rates_snapshot_onto_lines()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var (_, manager) = await api.CreateClientAsync(Role.AccountManager);
        var settings = await (await manager.GetAsync("/api/v1/agency/billing/settings")).ReadJsonAsync();
        Assert.Equal("OA", settings.Str("invoicePrefix"));
        var update = new { settings = new { invoicePrefix = "OA", paymentTermsDays = 21, defaultCurrency = "USD", companyName = "Optimize All", bankDetails = "IBAN GB00 TEST 0000" }, reason = "Net 21", confirm = true };
        await (await manager.PutAsJsonAsync("/api/v1/agency/billing/settings", update)).ShouldFailAsync(403);
        await (await admin.PutAsJsonAsync("/api/v1/agency/billing/settings", update with { confirm = false })).ShouldFailAsync(400, "request.confirm_required");
        var saved = await (await admin.PutAsJsonAsync("/api/v1/agency/billing/settings", update)).ReadJsonAsync();
        Assert.Equal(21, saved.GetProperty("paymentTermsDays").GetInt32());

        var rates = await (await manager.GetAsync("/api/v1/agency/billing/tax-rates")).ReadJsonAsync();
        Assert.All(rates.EnumerateArray(), r => Assert.True(r.GetProperty("needsReview").GetBoolean()));
        var rate = await (await admin.PostAsJsonAsync("/api/v1/agency/billing/tax-rates", new { name = "Test GST 10%", ratePercent = 10m, inclusive = true })).ReadJsonAsync();
        var client = await api.CreateClientAccountAsync();
        var invoice = await admin.CreateDraftInvoiceAsync(client.Id, Line("Inclusive", 1, 110m, taxRateId: rate.GetGuid("id")));
        Assert.Equal(21, invoice.GetProperty("paymentTermsDays").GetInt32());
        Assert.Equal(100m, invoice.GetProperty("totals").Dec("subtotal"));
        Assert.Equal(10m, invoice.GetProperty("totals").Dec("taxTotal"));
        await (await admin.PutAsJsonAsync($"/api/v1/agency/billing/tax-rates/{rate.GetGuid("id")}", new
        {
            name = "Test GST 12%", ratePercent = 12m, inclusive = true, isActive = true, concurrencyStamp = rate.GetGuid("concurrencyStamp"),
        })).ReadJsonAsync();
        Assert.Equal(10m, (await admin.GetInvoiceAsync(invoice.GetGuid("id"))).GetProperty("lines")[0].Dec("taxPercent"));
        await api.WithDbAsync(async db =>
        {
            db.Set<Domain.Settings.SystemSetting>().Remove(await db.Set<Domain.Settings.SystemSetting>().FirstAsync(s => s.Key == "billing.settings"));
            await db.SaveChangesAsync();
        });
    }
}
