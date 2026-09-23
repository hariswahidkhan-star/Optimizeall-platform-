using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.Crm;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Crm;

public sealed class CrmBillingDemoSeedTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task SeedAsync()
    {
        using var scope = api.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IEnumerable<OptimizeAll.Api.Common.Persistence.ISeeder>>().OfType<CrmBillingDemoSeeder>().Single();
        await seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);
    }

    [Fact]
    public async Task Demo_seed_creates_shared_accounts_and_consistent_billing_data_once()
    {
        // Another module's seeder may have created one of the shared clients first: it is reused, not duplicated.
        await api.WithDbAsync(async db =>
        {
            db.Set<ClientAccount>().Add(new ClientAccount { Name = "Nimbus Fitness", Slug = "nimbus-fitness", Currency = "USD", CountryCode = "US" });
            await db.SaveChangesAsync();
        });
        await SeedAsync();
        await SeedAsync(); // idempotent

        var data = await api.WithDbAsync(async db => new
        {
            Clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => new[] { "nimbus-fitness", "wanderly-travel", "aurora-skincare", "karachi-eats" }
                .Contains(c.Slug)).ToListAsync(),
            Staff = await db.Set<User>().AsNoTracking().Include(u => u.Roles).Where(u => u.Email.EndsWith("@demo.optimizeall.app")).ToListAsync(),
            NimbusMembers = await db.Set<ClientMember>().AsNoTracking().Where(m => db.Set<ClientAccount>().Any(c => c.Slug == "nimbus-fitness" && c.Id == m.ClientAccountId))
                .ToListAsync(),
            Invoices = await db.Set<Invoice>().AsNoTracking().ToListAsync(),
            Payments = await db.Set<Payment>().AsNoTracking().ToListAsync(),
            Deals = await db.Set<CrmDeal>().CountAsync(),
            Contracts = await db.Set<Contract>().CountAsync(c => c.Status == ContractStatus.Active),
            Proposals = await db.Set<Proposal>().CountAsync(),
        });
        Assert.Equal(4, data.Clients.Count);
        Assert.Contains(data.Clients, c => c is { Slug: "wanderly-travel", Name: "Wanderly Travel", CountryCode: "GB", Currency: "GBP", Industry: "Travel" });
        Assert.Contains(data.Clients, c => c is { Slug: "karachi-eats", Currency: "PKR", CountryCode: "PK" });
        foreach (var (local, role) in new[] { ("am", Role.AccountManager), ("sales", Role.SalesRep), ("seo", Role.SeoSpecialist), ("social", Role.SocialMediaManager) })
            Assert.Contains(data.Staff, u => u.Email == $"{local}@demo.optimizeall.app" && u.HasRole(role));
        Assert.Equal(3, data.NimbusMembers.Count);
        Assert.Contains(data.NimbusMembers, m => m.Role == ClientMemberRole.Billing);
        Assert.Equal(11, data.Deals);
        Assert.Equal(4, data.Contracts);
        Assert.Equal(3, data.Proposals);

        // Invoice numbers come from the real sequence: unique and gapless per year; every balance adds up.
        var issued = data.Invoices.Where(i => i.Number is not null).ToList();
        foreach (var year in issued.GroupBy(i => i.Number!.Split('-')[1]))
            Assert.Equal(Enumerable.Range(1, year.Count()), year.Select(i => int.Parse(i.Number!.Split('-')[2])).OrderBy(n => n));
        foreach (var invoice in data.Invoices)
            Assert.Equal(invoice.Total - invoice.AmountPaid - invoice.AmountCredited - invoice.AmountWrittenOff, invoice.Balance);
        foreach (var invoice in issued)
            Assert.Equal(data.Payments.Where(p => p.InvoiceId == invoice.Id).Sum(p => p.Amount), invoice.AmountPaid);
        Assert.Contains(data.Invoices, i => i.Status == InvoiceStatus.Overdue);
        Assert.Contains(data.Invoices, i => i.Status == InvoiceStatus.Draft);

        // The demo client user sees their organization's billing.
        var billing = await api.LoginAsync(new TestUser(Guid.Empty, "billing@nimbus.demo.optimizeall.app", CrmBillingDemoSeeder.Password));
        var summary = await (await billing.GetAsync("/api/v1/client/billing/summary")).ReadJsonAsync();
        Assert.True(summary.GetProperty("openInvoices").GetInt32() >= 1);
        var approver = await api.LoginAsync(new TestUser(Guid.Empty, "approver@nimbus.demo.optimizeall.app", CrmBillingDemoSeeder.Password));
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.GetAsync("/api/v1/client/billing/summary")).StatusCode);
        var sales = await api.LoginAsync(new TestUser(Guid.Empty, "sales@demo.optimizeall.app", CrmBillingDemoSeeder.Password));
        var board = await (await sales.GetAsync("/api/v1/agency/crm/deals/board")).ReadJsonAsync();
        Assert.True(board.GetProperty("columns").GetArrayLength() >= 8);
    }
}
