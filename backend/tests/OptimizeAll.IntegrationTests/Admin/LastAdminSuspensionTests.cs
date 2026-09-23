using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Accounts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Admin;

/// <summary>
/// Suspending an administrator must never leave the platform without an active admin. Uses its own database
/// (class fixture) because it suspends every other admin to set up the "last admin" state.
/// </summary>
public sealed class LastAdminSuspensionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private Task SuspendAllAdminsExceptAsync(params Guid[] keep) => api.WithDbAsync(db => db.Set<User>()
        .Where(u => u.Roles.Any(r => r.Role == Role.Admin) && !keep.Contains(u.Id))
        .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Suspended)));

    private Task<int> ActiveAdminCountAsync() => api.WithDbAsync(db =>
        db.Set<User>().CountAsync(u => u.Status == UserStatus.Active && u.Roles.Any(r => r.Role == Role.Admin)));

    [Fact]
    public async Task Suspending_the_last_active_admin_is_rejected()
    {
        // The caller still holds a token issued while it was an Admin, but its Admin role has since been removed,
        // so the target is the only active administrator left.
        var (caller, callerClient) = await api.AdminAsync();
        var target = await api.CreateUserAsync(new[] { Role.Admin });
        await api.WithDbAsync(db => db.Set<UserRole>().Where(r => r.UserId == caller.Id && r.Role == Role.Admin).ExecuteDeleteAsync());
        await SuspendAllAdminsExceptAsync(target.Id);
        Assert.Equal(1, await ActiveAdminCountAsync());

        await (await callerClient.PostAsJsonAsync($"/api/v1/admin/users/{target.Id}/suspend", new { reason = "Leaving the company", confirm = true }))
            .ShouldFailAsync(409, "admin.last_admin");
        Assert.Equal(UserStatus.Active, (await api.ReloadUserAsync(target.Id)).Status);

        // With another active admin in place the suspension goes through.
        await api.CreateUserAsync(new[] { Role.Admin });
        var suspended = await (await callerClient.PostAsJsonAsync($"/api/v1/admin/users/{target.Id}/suspend", new
        {
            reason = "Leaving the company", confirm = true,
        })).ReadJsonAsync();
        Assert.Equal("Suspended", suspended.GetProperty("profile").GetProperty("status").GetString());
        Assert.Equal(1, await ActiveAdminCountAsync());
    }

    [Fact]
    public async Task Two_admins_suspending_each_other_concurrently_leave_one_active_admin()
    {
        for (var round = 0; round < 3; round++)
        {
            var (a, aClient) = await api.AdminAsync();
            var (b, bClient) = await api.AdminAsync();
            await SuspendAllAdminsExceptAsync(a.Id, b.Id);

            var responses = await Task.WhenAll(
                aClient.PostAsJsonAsync($"/api/v1/admin/users/{b.Id}/suspend", new { reason = "Mutual suspension", confirm = true }),
                bClient.PostAsJsonAsync($"/api/v1/admin/users/{a.Id}/suspend", new { reason = "Mutual suspension", confirm = true }));

            Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
            var loser = responses.Single(r => !r.IsSuccessStatusCode);
            // The loser either waited on the admin lock (409) or its token was already revoked by the winner (401).
            Assert.Contains(loser.StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.Unauthorized });
            if (loser.StatusCode == HttpStatusCode.Conflict) await loser.ShouldFailAsync(409, "admin.last_admin");
            Assert.Equal(1, await ActiveAdminCountAsync());
        }
    }
}
