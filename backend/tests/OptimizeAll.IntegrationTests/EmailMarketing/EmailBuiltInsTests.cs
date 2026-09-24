using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Seed;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.EmailMarketing;

/// <summary>Built-in (seeded) journeys survive deletion + restart, and the workspace tag list stays cheap to build.</summary>
[Collection(EmailCollection.Name)]
public sealed class EmailBuiltInsTests(EmailFixture fx)
{
    [Fact]
    public async Task Deleting_a_ready_made_agency_journey_archives_it_so_the_seeder_does_not_bring_it_back()
    {
        var staff = await fx.StaffAsync();
        var journey = await fx.Db(db => db.Set<Automation>().AsNoTracking()
            .FirstAsync(a => a.ScopeKey == Workspace.AgencyKey && a.SeedKey == "journey-reengagement"));
        Assert.NotEqual(AutomationStatus.Active, journey.Status);

        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/v1/agency/email/automations/{journey.Id}")).StatusCode);
        var listed = await (await staff.GetAsync("/api/v1/agency/email/automations")).ReadJsonAsync();
        Assert.DoesNotContain(listed.EnumerateArray(), a => a.GetProperty("id").GetGuid() == journey.Id);

        // Next start: the baseline seeder runs again.
        await fx.Db(db => new EmailBaselineSeeder().SeedAsync(db, CancellationToken.None));

        var rows = await fx.Db(db => db.Set<Automation>().AsNoTracking()
            .Where(a => a.ScopeKey == Workspace.AgencyKey && a.SeedKey == "journey-reengagement").ToListAsync());
        var row = Assert.Single(rows);
        Assert.Equal(AutomationStatus.Archived, row.Status);
        listed = await (await staff.GetAsync("/api/v1/agency/email/automations")).ReadJsonAsync();
        Assert.DoesNotContain(listed.EnumerateArray(), a => a.GetProperty("name").GetString() == journey.Name);

        // It can still be brought back from the archive.
        (await staff.PostAsync($"/api/v1/agency/email/automations/{journey.Id}/restore", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Listing_workspace_tags_uses_a_fixed_number_of_queries()
    {
        var ws = await fx.CreateWorkspaceAsync();
        var ids = await fx.AddSubscribersAsync(ws, 2);
        await fx.Db(async db =>
        {
            for (var i = 0; i < 30; i++) db.Add(new SubscriberTag { SubscriberId = ids[i % 2], Tag = $"tag-{i:00}", AddedAt = fx.Now });
            await db.SaveChangesAsync();
        });
        var admin = await fx.StaffUserAsync();

        using var scope = fx.App.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(AppClaims.UserId, admin.Id.ToString()), new Claim(AppClaims.Role, nameof(Role.Admin)),
            }, "test")),
        };
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<AudienceCatalogService>();
        var commands = 0;
        IReadOnlyList<AudienceKeyDto> tags;
        using (new SqlCommandCounter(db, () => Interlocked.Increment(ref commands)))
            tags = await catalog.TagsAsync(ws.ClientId, CancellationToken.None);

        Assert.Equal(30, tags.Count);
        Assert.True(commands < 10, $"listing 30 tags ran {commands} SQL commands");
    }
}
