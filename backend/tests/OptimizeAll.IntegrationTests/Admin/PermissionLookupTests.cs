using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Admin;

/// <summary>
/// "Who holds X" lookups use effective permissions (built-in + custom roles). Owns its database, so the round-robin pool
/// contains only the users created here.
/// </summary>
public sealed class PermissionLookupTests : IAsyncLifetime
{
    private const string Roles = "/api/v1/admin/roles";
    private readonly ApiFactory api = new();

    public Task InitializeAsync() => api.InitializeAsync();

    public Task DisposeAsync() => api.DisposeAsync();

    private static async Task<Guid> CreateRoleAsync(HttpClient admin, params string[] permissions)
    {
        var response = await admin.PostAsJsonAsync(Roles, new { name = "Role " + Guid.NewGuid().ToString("N")[..8], permissions });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("role").GetProperty("id").GetGuid();
    }

    private static async Task AssignAsync(HttpClient admin, Guid roleId, Guid userId) =>
        (await admin.PutAsync($"{Roles}/{roleId}/users/{userId}", null)).EnsureSuccessStatusCode();

    [Fact]
    public async Task Pickers_and_routing_include_custom_role_holders_and_routing_skips_admin_only_holders()
    {
        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);

        // Inbound leads: the only job-role holder of crm.manage is a custom-role user; the admin (who holds everything)
        // is never put into the rotation.
        var seller = await api.CreateUserAsync(Array.Empty<Role>(), email: $"seller{Guid.NewGuid():N}"[..20] + "@example.test");
        await AssignAsync(admin, await CreateRoleAsync(admin, Permissions.CrmView, Permissions.CrmManage), seller.Id);
        var owners = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var inquiry = new WebsiteInquiryReceived(Guid.NewGuid(), "contact", "Jane Lead", $"lead{i}.{Guid.NewGuid():N}@lead{i}.example", null,
                $"Lead Co {i}", null, "Hello", Array.Empty<string>(), null, null, null, null, null, api.Clock.GetUtcNow().UtcDateTime);
            await api.Services.GetRequiredService<IEventPublisher>().PublishAsync(inquiry);
            owners.Add(await api.WithDbAsync(db => db.Set<CrmInboundEvent>().Where(x => x.Key == $"inquiry:{inquiry.InquiryId}")
                .Select(x => x.AssignedUserId!.Value).FirstAsync()));
        }
        Assert.All(owners, o => Assert.Equal(seller.Id, o));
        Assert.DoesNotContain(adminUser.Id, owners);

        // Delivery staff picker and the ads owner picker.
        var designer = await api.CreateUserAsync(Array.Empty<Role>(), email: $"dlv{Guid.NewGuid():N}"[..14] + "@example.test");
        await AssignAsync(admin, await CreateRoleAsync(admin, Permissions.ProjectsView, Permissions.DeliverablesSubmit), designer.Id);
        var staff = await (await admin.GetAsync("/api/v1/agency/staff")).ReadJsonAsync();
        Assert.Contains(staff.EnumerateArray(), s => s.GetProperty("id").GetGuid() == designer.Id);
        Assert.Contains(staff.EnumerateArray(), s => s.GetProperty("id").GetGuid() == seller.Id); // crm.manage (sales)
        var participant = await api.CreateUserAsync();
        Assert.DoesNotContain(staff.EnumerateArray(), s => s.GetProperty("id").GetGuid() == participant.Id);

        var adsPerson = await api.CreateUserAsync(Array.Empty<Role>());
        await AssignAsync(admin, await CreateRoleAsync(admin, Permissions.AdsManage), adsPerson.Id);
        var adsStaff = await (await admin.GetAsync("/api/v1/agency/ads/staff")).ReadJsonAsync();
        Assert.Contains(adsStaff.EnumerateArray(), s => s.GetProperty("id").GetGuid() == adsPerson.Id);
        Assert.DoesNotContain(adsStaff.EnumerateArray(), s => s.GetProperty("id").GetGuid() == participant.Id);

        // The directory primitives behind finance notifications and "is staff" checks.
        var finance = await api.CreateUserAsync(new[] { Role.Finance });
        var approver = await api.CreateUserAsync(Array.Empty<Role>());
        await AssignAsync(admin, await CreateRoleAsync(admin, Permissions.PayoutsView, Permissions.PayoutsFinalize), approver.Id);
        using var scope = api.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<IPermissionDirectory>();
        var workers = await (await directory.WorkersWithAnyPermissionAsync(new[] { Permissions.PayoutsFinalize })).Select(u => u.Id).ToListAsync();
        Assert.Contains(finance.Id, workers);
        Assert.Contains(approver.Id, workers);
        Assert.DoesNotContain(adminUser.Id, workers);
        var holders = await (await directory.UsersWithAnyPermissionAsync(new[] { Permissions.PayoutsFinalize })).Select(u => u.Id).ToListAsync();
        Assert.Contains(adminUser.Id, holders);

        var staffIds = await directory.StaffAmongAsync(new[] { adminUser.Id, approver.Id, participant.Id, seller.Id });
        Assert.Equal(new[] { adminUser.Id, approver.Id, seller.Id }.OrderBy(i => i), staffIds.OrderBy(i => i));
    }
}
