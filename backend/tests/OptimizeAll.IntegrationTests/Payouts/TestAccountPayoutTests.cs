using System.Net.Http.Json;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Payouts;

public sealed class TestAccountPayoutTests : FreshDatabaseTest
{
    [Fact]
    public async Task Test_accounts_are_never_paid_they_are_listed_as_excluded_and_their_earnings_stay_unattached()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var (_, admin) = await Api.CreateClientAsync(Role.Admin);
        var created = await (await admin.PostAsJsonAsync("/api/v1/admin/test-users", new { roles = new[] { "Participant" } })).ReadJsonAsync();
        var testUserId = created.Id();
        await Api.AddPayoutProfileAsync(testUserId, "PK36SCBL0000009999999999");
        var real = await Api.ParticipantAsync();

        var testEarning = await Api.EarnAsync(testUserId, 40m);
        var realEarning = await Api.EarnAsync(real.Id, 12m);

        Api.SetNow(period.CutoffUtc.AddMinutes(5));
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        var prepared = await finance.PrepareAsync();
        var batch = prepared.GetProperty("batch");
        Assert.Equal(1, batch.GetProperty("itemCount").GetInt32());
        Assert.Equal(12m, batch.Dec("totalAmount"));

        var items = await Api.ItemsAsync(batch.Id());
        Assert.Equal(real.Id, Assert.Single(items).UserId);
        Assert.Contains(prepared.GetProperty("exclusions").EnumerateArray(),
            e => e.GetProperty("user").Id() == testUserId && e.Str("reason") == "TestAccount");
        var detail = await finance.BatchAsync(batch.Id());
        Assert.Contains(detail.GetProperty("warnings").GetProperty("exclusions").EnumerateArray(),
            e => e.GetProperty("user").Id() == testUserId && e.Str("reason") == "TestAccount");

        Assert.Null((await Api.EarningAsync(testEarning.Id)).PayoutItemId);
        Assert.Equal(EarningStatus.Approved, (await Api.EarningAsync(testEarning.Id)).Status);
        Assert.NotNull((await Api.EarningAsync(realEarning.Id)).PayoutItemId);
    }
}
