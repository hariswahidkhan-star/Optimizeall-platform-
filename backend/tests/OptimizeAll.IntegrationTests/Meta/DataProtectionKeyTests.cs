using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Meta;

public sealed class DataProtectionKeyTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    /// <summary>
    /// Data Protection writes a key when the ring is first used (e.g. the demo seed encrypting payout destinations
    /// before the app has started) and on rotation, possibly while the same request holds a write transaction. On
    /// SQLite (one writer at a time) that must not need the database, so the key ring is stored in files next to it.
    /// </summary>
    [Fact]
    public async Task A_key_can_be_created_while_a_write_transaction_is_open()
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dialect = scope.ServiceProvider.GetRequiredService<IDatabaseDialect>();
        Assert.Equal(ApiFactory.Provider, dialect.Provider);
        var keys = scope.ServiceProvider.GetRequiredService<IKeyManager>();

        await using var tx = await dialect.BeginWriteTransactionAsync(db, CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var create = Task.Run(() => keys.CreateNewKey(now, now.AddDays(90)));
        Assert.Same(create, await Task.WhenAny(create, Task.Delay(TimeSpan.FromSeconds(8))));
        var key = await create;
        Assert.Contains(keys.GetAllKeys(), k => k.KeyId == key.KeyId);

        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("tests.key-creation");
        Assert.Equal("secret", protector.Unprotect(protector.Protect("secret")));
        await tx.CommitAsync();
    }
}
