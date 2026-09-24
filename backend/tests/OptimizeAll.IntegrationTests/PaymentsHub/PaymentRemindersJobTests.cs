using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Crm;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Crm.CrmBillingKit;

namespace OptimizeAll.IntegrationTests.PaymentsHub;

/// <summary>Scheduled payment reminders with per-client schedules (own database: these tests move the clock).</summary>
public sealed class PaymentRemindersJobTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Hub = PaymentsHubKit.Hub;

    private Task<List<string>> KindsAsync(Guid invoiceId) =>
        api.WithDbAsync(db => db.Set<InvoiceReminder>().Where(r => r.InvoiceId == invoiceId).OrderBy(r => r.SentAt).Select(r => r.Kind).ToListAsync());

    private void At(DateOnly day) => api.Clock.SetUtcNow(new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero));

    [Fact]
    public async Task Per_client_cadence_sends_each_stage_once_and_a_disabled_client_gets_none()
    {
        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);
        var custom = await api.CreateClientAccountAsync();
        var muted = await api.CreateClientAccountAsync();
        var standard = await api.CreateClientAccountAsync();
        foreach (var c in new[] { custom, muted, standard }) await api.CreateClientMemberAsync(c.Id, ClientMemberRole.Billing);

        var policyUrl = $"{Hub}/reminder-policies/{custom.Id}";
        var initial = await (await admin.GetAsync(policyUrl)).ReadJsonAsync();
        Assert.True(initial.GetProperty("usesAgencyDefault").GetBoolean());
        await (await admin.PutAsJsonAsync(policyUrl, new { useAgencyDefault = false, enabled = true, offsetsDays = new[] { 3, 3 }, reason = "Duplicate days" }))
            .ShouldFailAsync(400, "billing.invalid_settings");
        var saved = await (await admin.PutAsJsonAsync(policyUrl, new { useAgencyDefault = false, enabled = true, offsetsDays = new[] { 14, 3, 7 }, reason = "Agreed cadence 3/7/14" }))
            .ReadJsonAsync();
        Assert.Equal(new[] { 3, 7, 14 }, saved.GetProperty("offsetsDays").EnumerateArray().Select(d => d.GetInt32()));
        // A stale stamp is refused.
        await (await admin.PutAsJsonAsync(policyUrl, new { useAgencyDefault = false, enabled = false, offsetsDays = new[] { 3 }, reason = "Stale edit", concurrencyStamp = Guid.NewGuid() }))
            .ShouldFailAsync(409, "concurrency.conflict");
        (await admin.PutAsJsonAsync($"{Hub}/reminder-policies/{muted.Id}", new { useAgencyDefault = false, enabled = false, offsetsDays = Array.Empty<int>(), reason = "Client pays by direct debit" }))
            .EnsureSuccessStatusCode();

        var customInvoice = (await admin.IssuedInvoiceAsync(custom.Id)).GetGuid("id");
        var mutedInvoice = (await admin.IssuedInvoiceAsync(muted.Id)).GetGuid("id");
        var standardInvoice = (await admin.IssuedInvoiceAsync(standard.Id)).GetGuid("id");
        var due = await api.WithDbAsync(db => db.Set<Invoice>().Where(i => i.Id == customInvoice).Select(i => i.DueDate!.Value).FirstAsync());

        // Before the due date the custom client (no "before" stage) gets nothing; the agency default sends "before-3".
        At(due.AddDays(-3));
        await api.RunJobAsync<InvoiceOverdueJob>();
        Assert.Empty(await KindsAsync(customInvoice));
        Assert.Equal(new[] { "before-3" }, await KindsAsync(standardInvoice));

        // The dry-run preview shows what would go out, without sending.
        At(due.AddDays(3));
        admin = await api.LoginAsync(adminUser); // the clock moved: sign in again
        var preview = await (await admin.GetAsync($"{Hub}/reminders/preview")).ReadJsonAsync();
        Assert.Contains(preview.EnumerateArray(), r => r.GetGuid("invoiceId") == customInvoice && r.Str("kind") == "after-3" && !r.GetProperty("alreadySent").GetBoolean());
        Assert.Empty(await KindsAsync(customInvoice));

        // Each stage once, even when the job runs repeatedly.
        await api.RunJobAsync<InvoiceOverdueJob>();
        api.Clock.Advance(TimeSpan.FromHours(1));
        await api.RunJobAsync<InvoiceOverdueJob>();
        Assert.Equal(new[] { "after-3" }, await KindsAsync(customInvoice));
        At(due.AddDays(7));
        await api.RunJobAsync<InvoiceOverdueJob>();
        await api.RunJobAsync<InvoiceOverdueJob>();
        Assert.Equal(new[] { "after-3", "after-7" }, await KindsAsync(customInvoice));
        At(due.AddDays(20));
        await api.RunJobAsync<InvoiceOverdueJob>();
        Assert.Equal(new[] { "after-3", "after-7", "after-14" }, await KindsAsync(customInvoice));
        Assert.Empty(await KindsAsync(mutedInvoice));
        Assert.Equal(InvoiceStatus.Overdue, await api.WithDbAsync(db => db.Set<Invoice>().Where(i => i.Id == mutedInvoice).Select(i => i.Status).FirstAsync()));

        // Back to the agency schedule.
        admin = await api.LoginAsync(adminUser);
        var current = await (await admin.GetAsync(policyUrl)).ReadJsonAsync();
        var reset = await (await admin.PutAsJsonAsync(policyUrl, new
        {
            useAgencyDefault = true, reason = "Back to the standard schedule", concurrencyStamp = current.GetGuid("concurrencyStamp"),
        })).ReadJsonAsync();
        Assert.True(reset.GetProperty("usesAgencyDefault").GetBoolean());
        Assert.False(await api.WithDbAsync(db => db.Set<ClientReminderPolicy>().AnyAsync(p => p.ClientAccountId == custom.Id)));
    }

    [Fact]
    public void Offsets_for_a_client_prefer_its_policy()
    {
        var agency = new[] { -3, 0, 7, 14 };
        var id = Guid.NewGuid();
        var policies = new Dictionary<Guid, ClientReminderPolicy>
        {
            [id] = new() { ClientAccountId = id, Enabled = true, OffsetsDays = new() { 3, 7, 14 } },
        };
        Assert.Equal(new[] { 3, 7, 14 }, InvoiceOverdueJob.OffsetsFor(id, policies, agency));
        Assert.Equal(agency, InvoiceOverdueJob.OffsetsFor(Guid.NewGuid(), policies, agency));
        policies[id].Enabled = false;
        Assert.Empty(InvoiceOverdueJob.OffsetsFor(id, policies, agency));
    }
}
