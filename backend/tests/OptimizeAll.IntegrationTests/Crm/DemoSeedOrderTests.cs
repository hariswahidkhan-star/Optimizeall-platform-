using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Api.Modules.Crm;
using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Crm;

/// <summary>The shared demo people and clients come from <see cref="DeliveryDemoData"/> whichever demo seeder runs first (own database).</summary>
public sealed class DemoSeedOrderTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task RunAsync<TSeeder>() where TSeeder : ISeeder
    {
        using var scope = api.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IEnumerable<ISeeder>>().OfType<TSeeder>().Single();
        await seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);
    }

    [Fact]
    public async Task Crm_billing_demo_seed_run_first_creates_the_canonical_delivery_accounts()
    {
        await RunAsync<CrmBillingDemoSeeder>();

        var data = await api.WithDbAsync(async db => new
        {
            Users = await db.Set<User>().AsNoTracking().Include(u => u.Roles).Where(u => u.Email.EndsWith("demo.optimizeall.app")).ToListAsync(),
            Clients = await db.Set<ClientAccount>().AsNoTracking().ToListAsync(),
            Members = await db.Set<ClientMember>().AsNoTracking().ToListAsync(),
            Kits = await db.Set<BrandKit>().AsNoTracking().Select(k => k.ClientAccountId).ToListAsync(),
            Onboarding = await db.Set<ClientOnboardingItem>().AsNoTracking().Select(i => i.ClientAccountId).Distinct().ToListAsync(),
        });
        foreach (var s in DeliveryDemoData.Staff)
            Assert.Contains(data.Users, u => u.Email == s.Email && u.DisplayName == s.DisplayName && u.HasRole(s.Role));
        foreach (var c in DeliveryDemoData.Clients)
        {
            var client = Assert.Single(data.Clients, x => x.Slug == c.Slug);
            Assert.Equal((c.Name, c.Status, c.Website, c.Summary), (client.Name, client.Status, client.Website, client.Summary));
            Assert.Contains(client.Id, data.Kits);
            Assert.Contains(client.Id, data.Onboarding);
        }
        foreach (var cu in DeliveryDemoData.ClientUsers)
        {
            var user = Assert.Single(data.Users, u => u.Email == cu.Email);
            Assert.Equal(cu.DisplayName, user.DisplayName);
            var client = data.Clients.Single(c => c.Slug == cu.ClientSlug);
            Assert.Contains(data.Members, m => m.ClientAccountId == client.Id && m.UserId == user.Id && m.Role == cu.Duty);
        }

        // The delivery seeder afterwards reuses everything (no duplicates) and still adds its dataset.
        await RunAsync<DeliveryDemoSeeder>();
        var after = await api.WithDbAsync(async db => new
        {
            Users = await db.Set<User>().CountAsync(u => u.Email.EndsWith("demo.optimizeall.app")),
            Clients = await db.Set<ClientAccount>().CountAsync(),
            Kits = await db.Set<BrandKit>().CountAsync(),
        });
        Assert.Equal(data.Users.Count, after.Users);
        Assert.Equal(data.Clients.Count, after.Clients);
        Assert.Equal(data.Kits.Count, after.Kits);
    }
}
