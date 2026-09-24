using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Auth;
using OptimizeAll.IntegrationTests.Crm;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Crm.CrmBillingKit;
using static OptimizeAll.IntegrationTests.PaymentsHub.PaymentsHubKit;

namespace OptimizeAll.IntegrationTests.PaymentsHub;

/// <summary>Regression tests from the money-correctness review of the Payments hub and Billing.</summary>
public sealed class PaymentsMoneyRegressionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_payment_request_id_reused_with_other_data_is_refused_not_replayed()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var (_, other) = await api.CreateClientAsync(Role.Finance);
        var client = await api.CreateClientAccountAsync();
        var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 500m));
        var key = Guid.NewGuid();

        var first = await admin.HubRecordAsync(invoice, 100m, "TT-100", key, method: "Cash");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        // Same key, same data: a replay.
        Assert.Equal(HttpStatusCode.OK, (await admin.HubRecordAsync(invoice, 100m, "TT-100", key, method: "Cash")).StatusCode);
        // Same key but another method or date: a different payment, never answered with the first one.
        await (await admin.HubRecordAsync(invoice, 100m, "TT-100", key, method: "Cheque")).ShouldFailAsync(409, "billing.request_id_reused");
        await (await admin.HubRecordAsync(invoice, 100m, "TT-100", key, method: "Cash", paidOn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3)))
            .ShouldFailAsync(409, "billing.request_id_reused");
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<Payment>().CountAsync(p => p.InvoiceId == invoice.GetGuid("id"))));

        // A reversal retried as the other kind (entry error → refund) is refused, not reported as a refund.
        var payment = (await first.ReadJsonAsync()).GetProperty("payment");
        var reversalKey = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await other.HubReverseAsync(payment, "Error", reversalKey, "Recorded on the wrong invoice")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.HubReverseAsync(payment, "Error", reversalKey, "Recorded on the wrong invoice")).StatusCode);
        await (await other.HubReverseAsync(payment, "Refund", reversalKey, "Recorded on the wrong invoice")).ShouldFailAsync(409, "billing.request_id_reused");
        await (await other.HubReverseAsync(payment, "Error", reversalKey, "Another reason entirely")).ShouldFailAsync(409, "billing.request_id_reused");
        var stored = await api.WithDbAsync(db => db.Set<Payment>().AsNoTracking().FirstAsync(p => p.RequestId == reversalKey));
        Assert.Equal(PaymentReversalKind.Error, stored.ReversalKind);
    }

    [Fact]
    public async Task Refund_is_not_offered_to_the_person_who_recorded_the_payment()
    {
        var (_, recorder) = await api.CreateClientAsync(Role.Finance);
        var (_, other) = await api.CreateClientAsync(Role.Finance);
        var client = await api.CreateClientAccountAsync();
        var invoice = await recorder.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 250m));
        var payment = (await (await recorder.HubRecordAsync(invoice, 250m, "FOUR-EYES-1")).ReadJsonAsync()).GetProperty("payment");
        var url = $"{Hub}/records/InvoicePayment/{payment.GetGuid("id")}";

        var mine = (await (await recorder.GetAsync(url)).ReadJsonAsync()).GetProperty("record").Actions().ToList();
        Assert.Contains("reverse", mine);
        Assert.DoesNotContain("refund", mine);
        Assert.Contains("refund", (await (await other.GetAsync(url)).ReadJsonAsync()).GetProperty("record").Actions());
    }

    [Fact]
    public async Task Editing_a_payment_date_follows_the_recording_date_rules()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync();
        var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 300m));
        var payment = (await (await admin.HubRecordAsync(invoice, 300m, "EDIT-DATE-1")).ReadJsonAsync()).GetProperty("payment");
        var url = $"{Hub}/invoice-payments/{payment.GetGuid("id")}";
        var issued = DateOnly.Parse(invoice.Str("issueDate"));

        await (await admin.PatchAsJsonAsync(url, new
        {
            paidOn = issued.AddYears(-2).Iso(), reason = "Backdate to an old period", concurrencyStamp = payment.GetGuid("concurrencyStamp"),
        })).ShouldFailAsync(400, "billing.paid_on_invalid");
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), await api.WithDbAsync(db =>
            db.Set<Payment>().Where(p => p.Id == payment.GetGuid("id")).Select(p => p.PaidOn).FirstAsync()));
    }

    [Fact]
    public async Task Invoice_money_actions_are_refused_while_an_admin_views_as_a_finance_user()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var (_, issuer) = await api.CreateClientAsync(Role.Finance);
        var financeUser = await api.CreateUserAsync(new[] { Role.Finance });
        var client = await api.CreateClientAccountAsync();
        var draft = await issuer.CreateDraftInvoiceAsync(client.Id, Line("Setup", 1, 200m));
        var issued = await issuer.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 400m));
        var token = await Impersonating.TokenAsync(admin, financeUser.Id);
        const string code = "auth.impersonation_forbidden_action";
        const string invoices = "/api/v1/agency/billing/invoices";

        // Looking is fine…
        (await Impersonating.SendAsync(admin, HttpMethod.Get, $"{invoices}/{issued.GetGuid("id")}", token)).EnsureSuccessStatusCode();
        // …but the admin must not void / write off (four-eyes against the issuer) or issue as the finance user.
        var sensitive = new { reason = "Client cancelled the order", confirm = true, concurrencyStamp = issued.GetGuid("concurrencyStamp") };
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"{invoices}/{issued.GetGuid("id")}/void", token, sensitive)).ShouldFailAsync(403, code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"{invoices}/{issued.GetGuid("id")}/write-off", token, sensitive)).ShouldFailAsync(403, code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"{invoices}/{draft.GetGuid("id")}/issue", token,
            new { concurrencyStamp = draft.GetGuid("concurrencyStamp") })).ShouldFailAsync(403, code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Put, "/api/v1/agency/billing/settings", token, new { reason = "Change bank details", confirm = true }))
            .ShouldFailAsync(403, code);
        Assert.Equal(InvoiceStatus.Issued, await api.WithDbAsync(db => db.Set<Invoice>().Where(i => i.Id == issued.GetGuid("id")).Select(i => i.Status).FirstAsync()));
    }

    [Fact]
    public async Task A_reject_racing_a_confirmation_never_leaves_a_payment_on_a_rejected_report()
    {
        await using var factory = api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IEventHandler<InvoicePaid>, InvoicePaidHook>()));
        await factory.StartAsync();
        var admin = await factory.LoginAsync(await api.CreateUserAsync(new[] { Role.Admin }));
        var finance = await factory.LoginAsync(await api.CreateUserAsync(new[] { Role.Finance }));
        var client = await api.CreateClientAccountAsync();
        var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 800m));
        var invoiceId = invoice.GetGuid("id");
        var (_, billing) = await api.CreateClientMemberAsync(client.Id, ClientMemberRole.Billing);
        var claim = await (await billing.PostAsJsonAsync($"/api/v1/client/billing/invoices/{invoiceId}/payment-claims", new
        {
            requestId = Guid.NewGuid(), amount = 800m, method = "BankTransfer", reference = "RACE-800", paidOn = api.Today().Iso(),
        })).ReadJsonAsync();
        var claimId = claim.GetGuid("id");

        // The confirmation has recorded the payment (the invoice is now paid) but not yet closed the claim, when a second
        // finance user rejects the report.
        Task<HttpResponseMessage>? reject = null;
        InvoicePaidHook.OnPaid[invoiceId] = async () =>
        {
            reject = finance.PostAsJsonAsync($"{Hub}/claims/{claimId}/reject", new
            {
                reason = "Nothing on the statement", concurrencyStamp = claim.GetGuid("concurrencyStamp"),
            });
            await Task.WhenAny(reject, Task.Delay(TimeSpan.FromSeconds(2)));
        };
        var confirm = await admin.PostAsJsonAsync($"{Hub}/claims/{claimId}/confirm", new { invoiceConcurrencyStamp = invoice.GetGuid("concurrencyStamp") });
        Assert.NotNull(reject);
        var rejected = await reject!;

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        await rejected.ShouldFailAsync(409, "payments.claim_state");
        var stored = await api.WithDbAsync(db => db.Set<PaymentClaim>().AsNoTracking().FirstAsync(c => c.Id == claimId));
        Assert.Equal(PaymentClaimStatus.Confirmed, stored.Status);
        var payment = await api.WithDbAsync(db => db.Set<Payment>().AsNoTracking().SingleAsync(p => p.InvoiceId == invoiceId));
        Assert.Equal(stored.PaymentId, payment.Id);
    }
}

/// <summary>Runs a test's action when an invoice becomes paid (inside the request that paid it).</summary>
public sealed class InvoicePaidHook : IEventHandler<InvoicePaid>
{
    public static readonly ConcurrentDictionary<Guid, Func<Task>> OnPaid = new();

    public async Task HandleAsync(InvoicePaid e, CancellationToken ct)
    {
        if (OnPaid.TryRemove(e.InvoiceId, out var action)) await action();
    }
}

/// <summary>An email sender that runs a test's action when a message goes to a given address.</summary>
public sealed class HookEmailSender : IEmailSender
{
    public static readonly ConcurrentDictionary<string, Func<Task>> OnSend = new(StringComparer.OrdinalIgnoreCase);

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (OnSend.TryRemove(message.ToAddress, out var action)) await action();
        return new EmailSendResult(true, "test", null);
    }
}

/// <summary>Scheduled reminders against a changing world (own database: these tests move the clock).</summary>
public sealed class PaymentRemindersRegressionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private Task<List<string>> KindsAsync(Guid invoiceId) =>
        api.WithDbAsync(db => db.Set<InvoiceReminder>().Where(r => r.InvoiceId == invoiceId).Select(r => r.Kind).ToListAsync());

    [Fact]
    public async Task An_invoice_paid_while_the_job_runs_gets_no_reminder()
    {
        await using var factory = api.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender, HookEmailSender>();
        }));
        await factory.StartAsync();
        var admin = await factory.LoginAsync(await api.CreateUserAsync(new[] { Role.Admin }));
        var first = await api.CreateClientAccountAsync(billingEmail: $"ap-{Guid.NewGuid():N}@client.test");
        var second = await api.CreateClientAccountAsync();
        await api.CreateClientMemberAsync(second.Id, ClientMemberRole.Billing);
        var a = (await admin.IssuedInvoiceAsync(first.Id, Line("Retainer", 1, 100m))).GetGuid("id");
        var b = (await admin.IssuedInvoiceAsync(second.Id, Line("Retainer", 1, 100m))).GetGuid("id");
        var due = await api.WithDbAsync(db => db.Set<Invoice>().Where(i => i.Id == a).Select(i => i.DueDate!.Value).FirstAsync());
        // B is due a day later, so the job reaches it after A.
        await api.WithDbAsync(db => db.Set<Invoice>().Where(i => i.Id == b).ExecuteUpdateAsync(s => s.SetProperty(i => i.DueDate, due.AddDays(1))));

        // While A's reminder is being emailed, B is paid in full (by another request).
        HookEmailSender.OnSend[first.BillingEmail!] = () => api.WithDbAsync(db => db.Set<Invoice>().Where(i => i.Id == b)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Paid).SetProperty(i => i.AmountPaid, i => i.Total)
                .SetProperty(i => i.Balance, 0m).SetProperty(i => i.ConcurrencyStamp, Guid.NewGuid())));
        api.Clock.SetUtcNow(new DateTimeOffset(due.AddDays(8).ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero));
        await factory.Services.GetRequiredService<JobRunner>().RunAsync<InvoiceOverdueJob>();

        Assert.Equal(new[] { "after-7" }, await KindsAsync(a));
        Assert.Empty(await KindsAsync(b));
    }

    [Fact]
    public async Task Thousands_of_old_reminded_invoices_do_not_starve_a_newer_one()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync();
        await api.CreateClientMemberAsync(client.Id, ClientMemberRole.Billing);
        var today = api.Today();
        // 2,001 long-overdue invoices that already received their last reminder.
        await api.WithDbAsync(async db =>
        {
            for (var n = 0; n < 2001; n++)
            {
                var old = new Invoice
                {
                    Number = $"OLD-{Guid.NewGuid():N}", ClientAccountId = client.Id, Status = InvoiceStatus.Overdue, Currency = "USD",
                    IssueDate = today.AddDays(-400), DueDate = today.AddDays(-380), Total = 10m, Subtotal = 10m, GrossTotal = 10m, Balance = 10m,
                    IssuedAt = DateTime.UtcNow.AddDays(-400),
                };
                db.Set<Invoice>().Add(old);
                db.Set<InvoiceReminder>().Add(new InvoiceReminder { InvoiceId = old.Id, Kind = "after-14", SentAt = DateTime.UtcNow.AddDays(-366) });
            }
            await db.SaveChangesAsync();
        });
        var fresh = (await admin.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 100m))).GetGuid("id");
        await api.WithDbAsync(db => db.Set<Invoice>().Where(i => i.Id == fresh)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.IssueDate, today.AddDays(-30)).SetProperty(i => i.DueDate, today.AddDays(-8))));

        await api.RunJobAsync<InvoiceOverdueJob>();

        Assert.Equal(new[] { "after-7" }, await KindsAsync(fresh));
    }
}
