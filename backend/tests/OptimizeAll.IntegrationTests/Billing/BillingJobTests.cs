using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.IntegrationTests.Crm;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Crm.CrmBillingKit;

namespace OptimizeAll.IntegrationTests.Billing;

/// <summary>Time-driven billing jobs (own database: these tests move the clock).</summary>
public sealed class BillingJobTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task<JsonElement> ActiveContractAsync(HttpClient manager, Guid clientId, bool autoIssue, DateOnly start)
    {
        var contract = await (await manager.PostAsJsonAsync("/api/v1/agency/contracts", new
        {
            clientAccountId = clientId, title = "Monthly retainer", startDate = start.Iso(), billingFrequency = "Monthly", autoRenew = false,
            endDate = start.AddMonths(2).AddDays(-1).Iso(), autoIssueInvoices = autoIssue,
            lines = new[] { Line("SEO retainer", 1, 2000m, serviceSlug: "seo"), Line("Reporting", 1, 250m) },
        })).ReadJsonAsync();
        Assert.Equal("Draft", contract.Str("status"));
        return await (await manager.PostAsJsonAsync($"/api/v1/agency/contracts/{contract.GetGuid("id")}/activate",
            new { concurrencyStamp = contract.GetGuid("concurrencyStamp") })).ReadJsonAsync();
    }

    private Task<List<Invoice>> ContractInvoicesAsync(Guid contractId) =>
        api.WithDbAsync(db => db.Set<Invoice>().AsNoTracking().Where(i => i.ContractId == contractId).OrderBy(i => i.PeriodStart).ToListAsync());

    [Fact]
    public async Task Recurring_invoices_are_idempotent_across_retries_and_periods()
    {
        var (managerUser, manager) = await api.CreateClientAsync(Role.AccountManager);
        var client = await api.CreateClientAccountAsync();
        var start = api.Today();
        var contract = await ActiveContractAsync(manager, client.Id, autoIssue: false, start);
        var contractId = contract.GetGuid("id");

        await api.RunJobAsync<RecurringInvoiceJob>();
        api.Clock.Advance(TimeSpan.FromSeconds(1));
        await api.RunJobAsync<RecurringInvoiceJob>();
        var invoices = await ContractInvoicesAsync(contractId);
        var first = Assert.Single(invoices);
        Assert.Equal(InvoiceStatus.Draft, first.Status);
        Assert.Equal(start, first.PeriodStart);
        Assert.Equal(BillingPeriods.InvoiceKey(contractId, start), first.IdempotencyKey);
        Assert.Equal(2250m, first.Total);

        // Simulate a crash after the invoice was written but before the period index advanced: the retry must not duplicate.
        await api.WithDbAsync(db => db.Set<Contract>().Where(c => c.Id == contractId).ExecuteUpdateAsync(s => s.SetProperty(c => c.NextPeriodIndex, 0)));
        await api.RunJobAsync<RecurringInvoiceJob>();
        Assert.Single(await ContractInvoicesAsync(contractId));
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<Contract>().Where(c => c.Id == contractId).Select(c => c.NextPeriodIndex).FirstAsync()));

        // Next period.
        api.Clock.SetUtcNow(new DateTimeOffset(start.AddMonths(1).ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero));
        await api.RunJobAsync<RecurringInvoiceJob>();
        api.Clock.Advance(TimeSpan.FromMinutes(1));
        await api.RunJobAsync<RecurringInvoiceJob>();
        invoices = await ContractInvoicesAsync(contractId);
        Assert.Equal(2, invoices.Count);
        Assert.Equal(start.AddMonths(1), invoices[1].PeriodStart);

        // After the end date a non-renewing contract ends instead of billing again.
        api.Clock.SetUtcNow(new DateTimeOffset(start.AddMonths(2).ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero));
        await api.RunJobAsync<RecurringInvoiceJob>();
        Assert.Equal(2, (await ContractInvoicesAsync(contractId)).Count);
        Assert.Equal(ContractStatus.Ended, await api.WithDbAsync(db => db.Set<Contract>().Where(c => c.Id == contractId).Select(c => c.Status).FirstAsync()));

        // Auto-issued contracts get a number straight away (and catch up missed periods).
        manager = await api.LoginAsync(managerUser);
        var issuing = await ActiveContractAsync(manager, client.Id, autoIssue: true, api.Today().AddMonths(-1));
        await api.RunJobAsync<RecurringInvoiceJob>();
        var issued = await ContractInvoicesAsync(issuing.GetGuid("id"));
        Assert.Equal(2, issued.Count);
        Assert.All(issued, i => Assert.Equal(InvoiceStatus.Issued, i.Status));
        Assert.All(issued, i => Assert.NotNull(i.Number));
    }

    [Fact]
    public async Task Overdue_job_marks_invoices_and_sends_each_reminder_once()
    {
        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync();
        var (billingUser, _) = await api.CreateClientMemberAsync(client.Id, ClientMemberRole.Billing);
        var draft = await (await admin.PostAsJsonAsync("/api/v1/agency/billing/invoices", new
        {
            clientAccountId = client.Id, paymentTermsDays = 10, lines = new[] { Line("Retainer", 1, 500m) },
        })).ReadJsonAsync();
        var invoice = await admin.IssueInvoiceAsync(draft);
        var id = invoice.GetGuid("id");
        var due = DateOnly.Parse(invoice.Str("dueDate"));

        async Task RunAt(DateOnly day)
        {
            api.Clock.SetUtcNow(new DateTimeOffset(day.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero));
            await api.RunJobAsync<InvoiceOverdueJob>();
            api.Clock.Advance(TimeSpan.FromHours(1));
            await api.RunJobAsync<InvoiceOverdueJob>(); // a second run the same day sends nothing new
        }
        Task<List<string>> Kinds() => api.WithDbAsync(db => db.Set<InvoiceReminder>().Where(r => r.InvoiceId == id).OrderBy(r => r.SentAt).Select(r => r.Kind).ToListAsync());

        await RunAt(due.AddDays(-5));
        Assert.Empty(await Kinds());
        await RunAt(due.AddDays(-3));
        Assert.Equal(new[] { "before-3" }, await Kinds());
        await RunAt(due);
        Assert.Equal(new[] { "before-3", "due" }, await Kinds());
        await RunAt(due.AddDays(1));
        Assert.Equal(InvoiceStatus.Overdue, await api.WithDbAsync(db => db.Set<Invoice>().Where(i => i.Id == id).Select(i => i.Status).FirstAsync()));
        Assert.Equal(new[] { "before-3", "due" }, await Kinds());
        await RunAt(due.AddDays(20)); // the job was down for a while: only the latest reminder (+14) goes out
        Assert.Equal(new[] { "before-3", "due", "after-14" }, await Kinds());
        await RunAt(due.AddDays(21));
        Assert.Equal(3, (await Kinds()).Count);
        Assert.Equal(3, await api.WithDbAsync(db => db.Set<Notification>().CountAsync(n => n.UserId == billingUser.Id &&
            n.Type == BillingNotificationTypes.InvoiceReminder)));

        // A payment after the due date keeps it overdue until settled.
        admin = await api.LoginAsync(adminUser);
        var current = await admin.GetInvoiceAsync(id);
        Assert.True(current.GetProperty("daysOverdue").GetInt32() >= 21);
        var paid = await (await admin.RecordPaymentAsync(id, current.GetGuid("concurrencyStamp"), 200m, "LATE-1", paidOn: api.Today())).ReadJsonAsync();
        Assert.Equal("Overdue", paid.GetProperty("invoice").Str("status"));
    }

    [Fact]
    public async Task Aging_report_buckets_open_balances_per_client_and_currency()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync("Aging Co", currency: "PKR", country: "PK");
        var today = api.Today();
        foreach (var (days, amount) in new[] { (-5, 100m), (10, 200m), (45, 300m), (75, 400m), (120, 500m) })
        {
            var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Work", 1, amount));
            var id = invoice.GetGuid("id");
            await api.WithDbAsync(db => db.Set<Invoice>().Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.DueDate, today.AddDays(-days)).SetProperty(i => i.IssueDate, today.AddDays(-days - 14))));
        }
        var report = await (await admin.GetAsync($"/api/v1/agency/billing/reports/aging?asOf={today.Iso()}")).ReadJsonAsync();
        var row = report.GetProperty("rows").EnumerateArray().Single(r => r.GetGuid("clientAccountId") == client.Id);
        Assert.Equal("PKR", row.Str("currency"));
        Assert.Equal(100m, row.Dec("current"));
        Assert.Equal(200m, row.Dec("days1To30"));
        Assert.Equal(300m, row.Dec("days31To60"));
        Assert.Equal(400m, row.Dec("days61To90"));
        Assert.Equal(500m, row.Dec("over90"));
        Assert.Equal(1500m, row.Dec("total"));

        var csv = await (await admin.GetAsync("/api/v1/agency/billing/reports/aging.csv")).Content.ReadAsStringAsync();
        Assert.Contains("Aging Co,PKR,100", csv);
        var overview = await (await admin.GetAsync("/api/v1/agency/billing/overview")).ReadJsonAsync();
        Assert.Contains(overview.GetProperty("outstanding").EnumerateArray(), c => c.Str("currency") == "PKR" && c.Dec("amount") >= 1500m);
        var revenue = await (await admin.GetAsync("/api/v1/agency/billing/reports/revenue?groupBy=client")).ReadJsonAsync();
        Assert.Contains(revenue.GetProperty("rows").EnumerateArray(), r => r.Str("label") == "Aging Co" && r.Dec("invoiced") == 1500m);
        await (await admin.GetAsync("/api/v1/agency/billing/reports/mrr")).ReadJsonAsync();
        await (await admin.GetAsync("/api/v1/agency/billing/reports/collections")).ReadJsonAsync();
    }
}
