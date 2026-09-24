using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;
using Xunit;

namespace OptimizeAll.UnitTests.Admin;

/// <summary>Effective permission resolution (built-in ∪ custom roles) and its version-keyed cache, on a throwaway SQLite file.</summary>
public sealed class PermissionResolverTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"oa-resolver-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly CustomRolePermissionCache _cache;
    private readonly DbContextOptions<AppDbContext> _options;

    public PermissionResolverTests()
    {
        _cache = new CustomRolePermissionCache(_clock);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:SqlitePath"] = _file,
        }).Build();
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseConnection.Configure(builder, config);
        _options = builder.Options;
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            try { File.Delete(_file + suffix); } catch (IOException) { }
    }

    private AppDbContext NewDb() => new(_options, _clock);

    private (PermissionResolver Resolver, HttpContext Http) NewResolver(AppDbContext db)
    {
        var http = new DefaultHttpContext();
        return (new PermissionResolver(db, _cache, new HttpContextAccessor { HttpContext = http }), http);
    }

    private async Task<User> UserAsync(params Role[] roles)
    {
        await using var db = NewDb();
        var user = new User
        {
            Email = $"{Guid.NewGuid():N}@example.test", NormalizedEmail = Guid.NewGuid().ToString("N"), DisplayName = "U",
            CountryCode = "PK", ReferralCode = Guid.NewGuid().ToString("N")[..10], PasswordHash = "x",
        };
        foreach (var r in roles) user.Roles.Add(new UserRole { UserId = user.Id, Role = r, GrantedAt = DateTime.UtcNow });
        db.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<CustomRole> RoleAsync(params string[] permissions)
    {
        await using var db = NewDb();
        var name = "R" + Guid.NewGuid().ToString("N")[..8];
        var role = new CustomRole { Name = name, NormalizedName = CustomRole.Normalize(name), Permissions = permissions.ToList() };
        db.Add(role);
        await db.SaveChangesAsync();
        return role;
    }

    private async Task AssignAsync(Guid userId, Guid roleId, bool bump = true)
    {
        await using var db = NewDb();
        db.Add(new UserCustomRole { UserId = userId, CustomRoleId = roleId, AssignedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        if (bump) await BumpAsync(userId);
    }

    private async Task BumpAsync(Guid userId)
    {
        await using var db = NewDb();
        await db.Set<User>().Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.PermissionVersion, u => u.PermissionVersion + 1));
    }

    private static ClaimsPrincipal Principal(Guid userId, params Role[] roles) =>
        new(new ClaimsIdentity(
            new[] { new Claim(AppClaims.UserId, userId.ToString()) }.Concat(roles.Select(r => new Claim(AppClaims.Role, r.ToString()))),
            authenticationType: "test"));

    [Fact]
    public async Task Effective_permissions_are_the_union_of_built_in_and_custom_roles()
    {
        var user = await UserAsync(Role.ContentCreator);
        var crm = await RoleAsync(Permissions.CrmView, Permissions.CrmManage);
        var support = await RoleAsync(Permissions.SupportManage, Permissions.BlogWrite);
        await AssignAsync(user.Id, crm.Id);
        await AssignAsync(user.Id, support.Id);

        await using var db = NewDb();
        var (resolver, _) = NewResolver(db);
        var set = await resolver.ResolveAsync(Principal(user.Id, Role.ContentCreator));
        var expected = RolePermissions.For(new[] { Role.ContentCreator }).Concat(new[] { Permissions.CrmView, Permissions.CrmManage, Permissions.SupportManage }).ToHashSet();
        Assert.Equal(expected.OrderBy(p => p), set.OrderBy(p => p));
        Assert.Equal(expected.OrderBy(p => p), (await resolver.ForUserAsync(user.Id, new[] { Role.ContentCreator })).OrderBy(p => p));
        Assert.Equal(expected.OrderBy(p => p), resolver.ForUser(user.Id, new[] { Role.ContentCreator }).OrderBy(p => p));
    }

    [Fact]
    public async Task Anonymous_principals_have_no_permissions_and_unknown_permissions_are_ignored()
    {
        await using var db = NewDb();
        var (resolver, _) = NewResolver(db);
        Assert.Empty(await resolver.ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Empty(resolver.Resolve(new ClaimsPrincipal(new ClaimsIdentity())));

        var user = await UserAsync();
        var legacy = await RoleAsync(Permissions.CrmView, "removed.permission");
        await AssignAsync(user.Id, legacy.Id);
        Assert.Equal(new[] { Permissions.CrmView }, (await resolver.ResolveAsync(Principal(user.Id))).ToArray());
    }

    [Fact]
    public async Task Cache_is_keyed_by_permission_version_so_a_bump_applies_on_the_next_request()
    {
        var user = await UserAsync();
        var role = await RoleAsync(Permissions.CrmView);
        await AssignAsync(user.Id, role.Id);

        async Task<IReadOnlySet<string>> RequestAsync()
        {
            await using var db = NewDb();
            var (resolver, http) = NewResolver(db);
            var version = await db.Set<User>().Where(u => u.Id == user.Id).Select(u => u.PermissionVersion).FirstAsync();
            http.Items[PermissionResolver.PermissionVersionItem] = (user.Id, version);
            return await resolver.ResolveAsync(Principal(user.Id));
        }

        Assert.Contains(Permissions.CrmView, await RequestAsync());

        // A change without a version bump is not seen (served from the cache: no database read for custom roles)...
        await using (var db = NewDb())
            await db.Set<UserCustomRole>().Where(a => a.UserId == user.Id).ExecuteDeleteAsync();
        Assert.Contains(Permissions.CrmView, await RequestAsync());

        // ...and the bump that every admin change makes in the same transaction applies it on the next request.
        await BumpAsync(user.Id);
        Assert.Empty(await RequestAsync());

        await AssignAsync(user.Id, role.Id);
        Assert.Contains(Permissions.CrmView, await RequestAsync());
    }

    [Fact]
    public async Task Cache_entries_expire_after_the_ttl_and_can_be_invalidated()
    {
        var user = await UserAsync();
        _cache.Set(user.Id, 0, new HashSet<string> { Permissions.AuditView });
        Assert.True(_cache.TryGet(user.Id, 0, out var hit));
        Assert.Contains(Permissions.AuditView, hit);
        Assert.False(_cache.TryGet(user.Id, 1, out _));

        _clock.Advance(CustomRolePermissionCache.Ttl + TimeSpan.FromSeconds(1));
        Assert.False(_cache.TryGet(user.Id, 0, out _));

        _cache.Set(user.Id, 0, new HashSet<string> { Permissions.AuditView });
        _cache.Invalidate(new[] { user.Id });
        Assert.False(_cache.TryGet(user.Id, 0, out _));
    }

    [Fact]
    public async Task Results_are_memoized_per_request()
    {
        var user = await UserAsync(Role.Reviewer);
        var role = await RoleAsync(Permissions.CrmView);
        await AssignAsync(user.Id, role.Id);
        await using var db = NewDb();
        var (resolver, _) = NewResolver(db);
        var principal = Principal(user.Id, Role.Reviewer);
        var first = await resolver.ResolveAsync(principal);
        await using (var other = NewDb())
            await other.Set<UserCustomRole>().Where(a => a.UserId == user.Id).ExecuteDeleteAsync();
        await BumpAsync(user.Id);
        Assert.Same(first, resolver.Resolve(principal));
        Assert.Same(first, await resolver.ResolveAsync(principal));
    }
}
