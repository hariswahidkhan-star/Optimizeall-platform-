using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Api.Modules.Crm;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Crm.CrmBillingKit;

namespace OptimizeAll.IntegrationTests.Crm;

/// <summary>
/// Archived CRM records stay out of the way of new work (inbound leads, proposals), bulk actions stay cheap, and the sales
/// catalog seeder does not bring back defaults an agency removed.
/// </summary>
public sealed class CrmArchiveIntegrityTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Crm = "/api/v1/agency/crm";

    [Fact]
    public async Task A_new_inquiry_opens_a_visible_deal_instead_of_reusing_an_archived_one()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var email = $"returning.{Guid.NewGuid():N}@archived-deal.example";
        WebsiteInquiryReceived Inquiry() => new(Guid.NewGuid(), "quote", "Rita Return", email, null, "Returning Ltd", null, "Hello again",
            new[] { "seo" }, null, null, null, null, null, api.UtcNow());

        await api.PublishAsync(Inquiry());
        var first = await api.WithDbAsync(db => db.Set<CrmDeal>().AsNoTracking()
            .SingleAsync(d => d.PrimaryContactId != null && db.Set<CrmContact>().Any(c => c.Id == d.PrimaryContactId && c.NormalizedEmail == email.ToUpperInvariant())));
        var deal = await (await sales.GetAsync($"{Crm}/deals/{first.Id}")).ReadJsonAsync();
        (await sales.PostAsJsonAsync($"{Crm}/deals/{first.Id}/archive", new { concurrencyStamp = deal.GetGuid("concurrencyStamp") })).EnsureSuccessStatusCode();

        await api.PublishAsync(Inquiry());

        var deals = await api.WithDbAsync(db => db.Set<CrmDeal>().AsNoTracking().Where(d => d.PrimaryContactId == first.PrimaryContactId).ToListAsync());
        Assert.Equal(2, deals.Count);
        Assert.NotNull(deals.Single(d => d.Id == first.Id).ArchivedAt);
        var fresh = deals.Single(d => d.Id != first.Id);
        Assert.Null(fresh.ArchivedAt);
        Assert.Equal(DealStatus.Open, fresh.Status);
    }

    [Fact]
    public async Task Proposals_cannot_be_created_or_sent_on_an_archived_deal()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var deal = await (await sales.PostAsJsonAsync($"{Crm}/deals", new { title = "Parked deal", value = 900m, currency = "USD", source = "Outbound" }))
            .ReadJsonAsync();
        var dealId = deal.GetGuid("id");
        object Body() => new
        {
            title = "Parked proposal", dealId, validUntil = api.Today().AddDays(14).Iso(), recipientName = "Pat", recipientEmail = "pat@example.test",
            lines = new[] { Line("Audit", 1, 900m) },
        };
        var draft = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", Body())).ReadJsonAsync();

        (await sales.PostAsJsonAsync($"{Crm}/deals/{dealId}/archive", new { concurrencyStamp = deal.GetGuid("concurrencyStamp") })).EnsureSuccessStatusCode();

        await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", Body())).ShouldFailAsync(409, "crm.archived");
        await (await sales.PostAsync($"/api/v1/agency/proposals/{draft.GetGuid("id")}/duplicate", null)).ShouldFailAsync(409, "crm.archived");
        await (await sales.PostAsJsonAsync($"/api/v1/agency/proposals/{draft.GetGuid("id")}/send",
            new { concurrencyStamp = draft.GetGuid("concurrencyStamp"), email = false })).ShouldFailAsync(409, "crm.archived");
        Assert.Equal("Draft", (await (await sales.GetAsync($"/api/v1/agency/proposals/{draft.GetGuid("id")}")).ReadJsonAsync()).Str("status"));
    }

    [Fact]
    public async Task Bulk_contact_actions_do_not_run_queries_per_contact()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var ids = new List<Guid>();
        for (var i = 0; i < 25; i++)
            ids.Add((await (await sales.PostAsJsonAsync($"{Crm}/contacts", new { firstName = "Bulk", lastName = $"N{i}", email = $"bulk{i}.{Guid.NewGuid():N}@example.test" }))
                .ReadJsonAsync()).GetGuid("id"));

        async Task<(CrmBulkResultDto Result, int Commands)> RunAsync(CrmBulkRequest request)
        {
            using var scope = api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var crm = scope.ServiceProvider.GetRequiredService<CrmService>();
            var commands = 0;
            using var counter = new SqlCommandCounter(db, () => Interlocked.Increment(ref commands));
            var result = await crm.BulkContactsAsync(request, CancellationToken.None);
            return (result, commands);
        }

        // SaveChanges may send one UPDATE per changed row (SQLite doesn't batch); anything beyond that plus a handful of
        // queries means per-contact work (re-scoring used to cost four or five queries per contact).
        var budget = ids.Count + 10;
        var tagged = await RunAsync(new CrmBulkRequest { Ids = ids, Action = "addTag", Tag = "bulk-perf" });
        Assert.Equal(25, tagged.Result.Updated);
        Assert.True(tagged.Commands < budget, $"adding a tag to 25 contacts ran {tagged.Commands} SQL commands");

        var staged = await RunAsync(new CrmBulkRequest { Ids = ids, Action = "setLifecycle", LifecycleStage = LifecycleStage.MarketingQualifiedLead });
        Assert.Equal(25, staged.Result.Updated);
        Assert.True(staged.Commands < budget, $"re-staging 25 contacts ran {staged.Commands} SQL commands");

        // The batch re-score gives the same result as scoring one contact at a time.
        using (var scope = api.Services.CreateScope())
        {
            var scoring = scope.ServiceProvider.GetRequiredService<LeadScoringService>();
            var stored = await api.WithDbAsync(db => db.Set<CrmContact>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Score));
            foreach (var id in ids.Take(3))
                Assert.Equal((await scoring.ComputeAsync(id, CancellationToken.None)).Score, stored[id]);
        }
    }

    [Fact]
    public async Task Sales_catalog_seeder_does_not_restore_items_and_templates_the_agency_deleted()
    {
        await api.WithDbAsync(async db =>
        {
            await db.Set<ServiceCatalogItem>().ExecuteDeleteAsync();
            await db.Set<ProposalTemplate>().ExecuteDeleteAsync();
            return true;
        });

        await api.WithDbAsync(async db =>
        {
            await new SalesCatalogSeeder().SeedAsync(db, CancellationToken.None);
            return true;
        });

        Assert.Equal(0, await api.WithDbAsync(db => db.Set<ServiceCatalogItem>().CountAsync()));
        Assert.Equal(0, await api.WithDbAsync(db => db.Set<ProposalTemplate>().CountAsync()));
        // The ledger is bookkeeping, not an admin setting.
        Assert.True(await api.WithDbAsync(db => db.Set<SystemSetting>().AnyAsync(s => s.Key == SeedLedger.KeyPrefix + "sales_catalog")));
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var settings = await (await admin.GetAsync("/api/v1/admin/settings")).ReadJsonAsync();
        Assert.DoesNotContain(settings.EnumerateArray(), s => s.GetProperty("key").GetString()!.StartsWith(SeedLedger.KeyPrefix));
    }
}
