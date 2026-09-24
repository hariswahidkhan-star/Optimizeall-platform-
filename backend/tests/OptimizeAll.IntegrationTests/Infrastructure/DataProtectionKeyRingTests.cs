using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Persistence;

namespace OptimizeAll.IntegrationTests.Infrastructure;

/// <summary>
/// API instances sharing a database share one Data Protection key ring. Two instances starting on an empty key ring
/// must not each create (and activate) a first key of their own: a token protected by one would not unprotect on the
/// other once its short post-start refresh window has passed (Google sign-in state, form tokens, public links).
/// </summary>
public sealed class DataProtectionKeyRingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    /// <summary>
    /// Makes the first two key-ring reads (from whichever hosts) wait for each other, up to a timeout, before either
    /// returns: both instances then see the key ring as it was before either created a key, the worst case of a
    /// simultaneous first start.
    /// </summary>
    private sealed class RendezvousRepository(IXmlRepository inner, Rendezvous rendezvous) : IXmlRepository
    {
        public IReadOnlyCollection<XElement> GetAllElements()
        {
            var elements = inner.GetAllElements();
            rendezvous.Arrive();
            return elements;
        }

        public void StoreElement(XElement element, string friendlyName) => inner.StoreElement(element, friendlyName);
    }

    private sealed class Rendezvous
    {
        private readonly ManualResetEventSlim _both = new();
        private int _arrived;

        public void Arrive()
        {
            var arrival = Interlocked.Increment(ref _arrived);
            if (arrival == 2) _both.Set();
            else if (arrival == 1) _both.Wait(TimeSpan.FromSeconds(15));
        }
    }

    private WebApplicationFactory<Program> Instance(Rendezvous rendezvous) => api.WithWebHostBuilder(b =>
    {
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // The schema and baseline data exist already; only the startup key-ring step matters here.
            ["Database:InitializationMode"] = "None",
            ["Database:Seed:0"] = "",
        }));
        b.ConfigureServices(s => s.PostConfigure<KeyManagementOptions>(o =>
            o.XmlRepository = new RendezvousRepository(o.XmlRepository!, rendezvous)));
    });

    [Fact]
    public async Task Two_instances_starting_on_an_empty_key_ring_share_one_key()
    {
        // Start from an empty key ring, as on the very first deployment.
        if (ApiFactory.IsSqlite)
        {
            var directory = DatabaseConnection.SqliteKeyDirectory(api.Services.GetRequiredService<IConfiguration>());
            foreach (var file in Directory.GetFiles(directory, "*.xml")) File.Delete(file);
        }
        else
        {
            await api.WithDbAsync(db => db.Set<DataProtectionKey>().ExecuteDeleteAsync());
        }

        var rendezvous = new Rendezvous();
        await using var one = Instance(rendezvous);
        await using var two = Instance(rendezvous);
        await Task.WhenAll(one.StartAsync(), two.StartAsync());

        var key = Assert.Single(one.Services.GetRequiredService<IKeyManager>().GetAllKeys());
        Assert.Equal(key.KeyId, Assert.Single(two.Services.GetRequiredService<IKeyManager>().GetAllKeys()).KeyId);

        var a = one.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("tests.key-ring");
        var b = two.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("tests.key-ring");
        Assert.Equal("from one", b.Unprotect(a.Protect("from one")));
        Assert.Equal("from two", a.Unprotect(b.Protect("from two")));
    }
}
