using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Crm;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Crm.CrmBillingKit;

namespace OptimizeAll.IntegrationTests.Billing;

/// <summary>Auto-renewal of month-end contracts (own database: runs the recurring-invoice job).</summary>
public sealed class ContractRenewalEdgeTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Monthly_auto_renewal_keeps_a_month_end_contract_on_month_ends()
    {
        var (_, manager) = await api.CreateClientAsync(Role.AccountManager);
        var client = await api.CreateClientAccountAsync();
        var today = api.Today();
        // A one-month contract for December, renewed month by month since: Dec 31 → Jan 31 → Feb 28/29 → Mar 31 → ...
        var start = new DateOnly(today.Year - 2, 12, 1);
        var draft = await (await manager.PostAsJsonAsync("/api/v1/agency/contracts", new
        {
            clientAccountId = client.Id, title = "Month-end retainer", startDate = start.Iso(), endDate = new DateOnly(today.Year - 2, 12, 31).Iso(),
            billingFrequency = "Monthly", autoRenew = true, renewalTermMonths = 1, autoIssueInvoices = false,
            lines = new[] { Line("Retainer", 1, 1000m) },
        })).ReadJsonAsync();
        var contract = await (await manager.PostAsJsonAsync($"/api/v1/agency/contracts/{draft.GetGuid("id")}/activate",
            new { concurrencyStamp = draft.GetGuid("concurrencyStamp") })).ReadJsonAsync();
        var contractId = contract.GetGuid("id");

        for (var run = 0; run < 3; run++) await api.RunJobAsync<RecurringInvoiceJob>();

        var saved = await api.WithDbAsync(db => db.Set<Contract>().AsNoTracking().FirstAsync(c => c.Id == contractId));
        var invoices = await api.WithDbAsync(db => db.Set<Invoice>().AsNoTracking().Where(i => i.ContractId == contractId).OrderBy(i => i.PeriodStart).ToListAsync());
        var lastPeriodStart = new DateOnly(today.Year, today.Month, 1);
        Assert.Equal(lastPeriodStart, invoices[^1].PeriodStart);
        // The renewed term ends on the last day of the month being billed, not on a day that drifted to the 28th.
        Assert.Equal(lastPeriodStart.AddMonths(1).AddDays(-1), saved.EndDate);
        Assert.All(invoices, i => Assert.True(i.PeriodEnd <= saved.EndDate, $"Period {i.PeriodStart}–{i.PeriodEnd} runs past the contract end {saved.EndDate}"));
    }
}
