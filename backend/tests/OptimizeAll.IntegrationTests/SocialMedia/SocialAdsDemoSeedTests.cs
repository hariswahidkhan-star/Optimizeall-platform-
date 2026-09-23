using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.SocialMedia;

[Collection(SocialAdsCollection.Name)]
public sealed class SocialAdsDemoSeedTests(ApiFactory api)
{
    private async Task SeedAsync()
    {
        using var scope = api.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetServices<ISeeder>().OfType<SocialAdsDemoSeeder>().Single();
        Assert.Equal(("Demo", 300), (seeder.Profile, seeder.Order));
        await seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);
    }

    [Fact]
    public async Task Demo_seed_is_idempotent_and_covers_every_state()
    {
        await SeedAsync();
        await SeedAsync();

        await api.WithDbAsync(async db =>
        {
            var slugs = SocialAdsDemoSeeder.Clients.Select(c => c.Slug).ToList();
            var clients = await db.Set<ClientAccount>().Where(c => slugs.Contains(c.Slug)).ToListAsync();
            Assert.Equal(4, clients.Count);
            var karachi = clients.Single(c => c.Slug == "karachi-eats");
            Assert.Equal(("Karachi Eats", "PK", "PKR"), (karachi.Name, karachi.CountryCode, karachi.Currency));

            var staff = await db.Set<User>().Include(u => u.Roles)
                .Where(u => u.NormalizedEmail == "SOCIAL@DEMO.OPTIMIZEALL.APP" || u.NormalizedEmail == "ADS@DEMO.OPTIMIZEALL.APP").ToListAsync();
            Assert.Equal(2, staff.Count);
            Assert.Contains(staff, u => u.HasRole(Role.SocialMediaManager));
            Assert.Contains(staff, u => u.HasRole(Role.AdsSpecialist));

            var ids = clients.Select(c => c.Id).ToList();
            var statuses = await db.Set<SocialPost>().Where(p => ids.Contains(p.ClientAccountId)).Select(p => p.Status).Distinct().ToListAsync();
            Assert.Equal(Enum.GetValues<SocialPostStatus>().OrderBy(s => s), statuses.OrderBy(s => s));
            Assert.Equal(1, await db.Set<BrandProfile>().CountAsync(p => p.Handle == SocialAdsDemoSeeder.MarkerHandle));
            Assert.DoesNotContain(await db.Set<SocialPostVariant>().Where(v => ids.Contains(v.ClientAccountId) && v.PublishStatus == VariantPublishStatus.Published)
                .ToListAsync(), v => !v.PublishedManually);

            var metrics = await db.Set<AdDailyMetric>().Where(m => ids.Contains(m.ClientAccountId)).ToListAsync();
            Assert.Equal(90, metrics.Select(m => m.Date).Distinct().Count());
            Assert.All(metrics, m => Assert.Equal(AdMetricSource.CsvImport, m.Source));
            Assert.NotEmpty(await db.Set<AdAlert>().Where(a => ids.Contains(a.ClientAccountId)).ToListAsync());
            Assert.Equal(5, await db.Set<AdBudget>().CountAsync(b => ids.Contains(b.ClientAccountId)));
            return true;
        });
    }
}
