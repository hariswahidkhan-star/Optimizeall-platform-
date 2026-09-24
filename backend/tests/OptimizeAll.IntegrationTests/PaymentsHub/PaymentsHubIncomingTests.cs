using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.IntegrationTests.Crm;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Crm.CrmBillingKit;
using static OptimizeAll.IntegrationTests.PaymentsHub.PaymentsHubKit;

namespace OptimizeAll.IntegrationTests.PaymentsHub;

/// <summary>Payments hub, incoming money: manual payments, corrections, reversals, client "I've paid" reports, reminders, list/KPIs/CSV.</summary>
public sealed class PaymentsHubIncomingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private Task<int> PaymentRowsAsync(Guid invoiceId) => api.WithDbAsync(db => db.Set<Payment>().CountAsync(p => p.InvoiceId == invoiceId));

    [Fact]
    public async Task Recording_a_manual_payment_twice_with_the_same_key_records_it_once()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync();
        var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 900m));
        var key = Guid.NewGuid();

        var first = await admin.HubRecordAsync(invoice, 400m, "CASH-001", key, method: "Cash");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        // Double click / retry after a timeout: same key, same stale stamp → the original payment, not a second one.
        var second = await admin.HubRecordAsync(invoice, 400m, "CASH-001", key, method: "Cash");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await second.ReadJsonAsync();
        Assert.True(body.GetProperty("replayed").GetBoolean());
        Assert.Equal(500m, body.GetProperty("invoice").Dec("balance"));
        Assert.Equal(1, await PaymentRowsAsync(invoice.GetGuid("id")));
        Assert.Equal("Cash", (await first.ReadJsonAsync()).GetProperty("payment").Str("method"));

        // Overpayment follows the existing rule (refused) — through the hub and through "I've paid" confirmation alike.
        var current = await admin.GetInvoiceAsync(invoice.GetGuid("id"));
        await (await admin.HubRecordAsync(current, 500.01m, "CASH-002")).ShouldFailAsync(409, "billing.overpayment");
        await (await admin.HubRecordAsync(current, 10m, "CASH-001")).ShouldFailAsync(409, "billing.duplicate_reference");
    }

    [Fact]
    public async Task Two_admins_recording_the_last_balance_at_once_one_wins_and_mark_paid_is_idempotent()
    {
        var factory = api.WithInvoicePaidCounter();
        var admin1 = await factory.LoginAsync(await api.CreateUserAsync(new[] { Role.Admin }));
        var admin2 = await factory.LoginAsync(await api.CreateUserAsync(new[] { Role.Finance }));
        var client = await api.CreateClientAccountAsync();
        var invoice = await admin1.IssuedInvoiceAsync(client.Id, Line("Setup", 1, 750m));
        var id = invoice.GetGuid("id");

        var keyA = Guid.NewGuid();
        var race = await Task.WhenAll(admin1.HubMarkPaidAsync(invoice, "WIRE-A", keyA), admin2.HubMarkPaidAsync(invoice, "WIRE-B"));
        Assert.Equal(1, race.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, race.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var paid = await admin1.GetInvoiceAsync(id);
        Assert.Equal("Paid", paid.Str("status"));
        Assert.Equal(0m, paid.Dec("balance"));
        Assert.Equal(750m, Assert.Single(paid.GetProperty("payments").EnumerateArray()).Dec("amount"));
        Assert.Equal(1, InvoicePaidCounter.Counts.GetValueOrDefault(id));

        // Retrying the winning request (balance is now 0) replays the original payment.
        if (race[0].StatusCode == HttpStatusCode.Created)
        {
            var retry = await admin1.HubMarkPaidAsync(invoice, "WIRE-A", keyA);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.True((await retry.ReadJsonAsync()).GetProperty("replayed").GetBoolean());
        }
        await (await admin1.HubMarkPaidAsync(paid, "WIRE-C")).ShouldFailAsync(409, "billing.invoice_not_open");
        Assert.Equal(1, await PaymentRowsAsync(id));
    }

    [Fact]
    public async Task Reversal_restores_the_balance_reopens_the_invoice_and_lets_the_reference_be_recorded_again()
    {
        var recorderUser = await api.CreateUserAsync(new[] { Role.Finance });
        var recorder = await api.LoginAsync(recorderUser);
        var (_, other) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync(currency: "GBP", country: "GB");
        var invoice = await recorder.IssuedInvoiceAsync(client.Id, Line("Audit", 1, 1200m));
        var id = invoice.GetGuid("id");
        var recorded = await (await recorder.HubRecordAsync(invoice, 1200m, "BACS-77")).ReadJsonAsync();
        Assert.Equal("Paid", recorded.GetProperty("invoice").Str("status"));
        var payment = recorded.GetProperty("payment");

        // Amount corrections are reverse + re-record: a reversal by the recorder (entry error) is allowed, a refund is not (four-eyes).
        await (await recorder.HubReverseAsync(payment, "Refund")).ShouldFailAsync(403, "billing.four_eyes");
        await (await recorder.PostAsJsonAsync($"{Hub}/invoice-payments/{payment.GetGuid("id")}/reverse", new
        {
            requestId = Guid.NewGuid(), kind = "Error", reason = "Typo in the amount", confirm = false, concurrencyStamp = payment.GetGuid("concurrencyStamp"),
        })).ShouldFailAsync(400, "request.confirm_required");

        var key = Guid.NewGuid();
        var reversed = await recorder.HubReverseAsync(payment, "Error", key, "Amount was 1,000 not 1,200");
        Assert.Equal(HttpStatusCode.Created, reversed.StatusCode);
        var afterReverse = (await reversed.ReadJsonAsync()).GetProperty("invoice");
        Assert.Equal(1200m, afterReverse.Dec("balance"));
        Assert.Equal(0m, afterReverse.Dec("amountPaid"));
        Assert.NotEqual("Paid", afterReverse.Str("status"));
        Assert.Equal(JsonValueKind.Null, afterReverse.GetProperty("paidAt").ValueKind);
        var rows = afterReverse.GetProperty("payments").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Dec("amount") == -1200m && r.Str("reversalKind") == "Error");

        // Retry → replay; another request → already reversed.
        var replay = await recorder.HubReverseAsync(payment, "Error", key, "Amount was 1,000 not 1,200");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await (await other.HubReverseAsync(payment, "Error")).ShouldFailAsync(409, "billing.payment_already_reversed");
        Assert.Equal(2, await PaymentRowsAsync(id));

        // The same bank reference can now be recorded again with the right amount.
        var corrected = await (await recorder.HubRecordAsync(afterReverse, 1000m, "BACS-77")).ReadJsonAsync();
        Assert.Equal(200m, corrected.GetProperty("invoice").Dec("balance"));
        var second = corrected.GetProperty("payment");

        // A refund by someone else: balance goes back up, audited with the reason.
        var refunded = await other.HubReverseAsync(second, "Refund", reason: "Client paid twice, refunded by BACS");
        Assert.Equal(HttpStatusCode.Created, refunded.StatusCode);
        Assert.Equal(1200m, (await refunded.ReadJsonAsync()).GetProperty("invoice").Dec("balance"));
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a =>
            a.Action == "billing.payment_refunded" && a.EntityId == id.ToString() && a.Reason == "Client paid twice, refunded by BACS")));

        // The hub shows the originals as Voided / Refunded; reversal rows are not listed separately; net received is 0.
        var list = await other.HubListAsync($"&invoiceId={id}");
        var statuses = list.Items().Where(r => r.Str("kind") == "InvoicePayment").Select(r => r.Str("status")).OrderBy(s => s).ToList();
        Assert.Equal(new[] { "Refunded", "Voided" }, statuses);
        var summary = await (await other.GetAsync($"{Hub}/summary")).ReadJsonAsync();
        var gbp = summary.GetProperty("incoming").GetProperty("receivedThisMonth").EnumerateArray().Single(c => c.Str("currency") == "GBP");
        Assert.Equal(0m, gbp.Dec("amount"));

        // The client statement shows the reversals as debits and ends at the full balance.
        var statement = await (await other.GetAsync($"/api/v1/agency/billing/clients/{client.Id}/statement?currency=GBP")).ReadJsonAsync();
        Assert.Equal(1200m, statement.Dec("closingBalance"));
    }

    [Fact]
    public async Task Editing_a_payment_changes_details_only_with_a_reason_and_the_current_stamp()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync();
        var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Ads", 1, 300m));
        var payment = (await (await admin.HubRecordAsync(invoice, 100m, "REF-OLD")).ReadJsonAsync()).GetProperty("payment");
        var url = $"{Hub}/invoice-payments/{payment.GetGuid("id")}";
        var stamp = payment.GetGuid("concurrencyStamp");

        await (await admin.PatchAsJsonAsync(url, new { reference = "REF-NEW", concurrencyStamp = stamp })).ShouldFailAsync(400);
        var edit = new { reference = "REF-NEW", method = "Cheque", notes = "Cheque 000123", reason = "Bank statement shows another id", concurrencyStamp = stamp };
        var edited = await (await admin.PatchAsJsonAsync(url, edit)).ReadJsonAsync();
        Assert.Equal("REF-NEW", edited.Str("reference"));
        Assert.Equal("Cheque", edited.Str("method"));
        Assert.Equal(100m, edited.Dec("amount"));
        Assert.NotEqual(stamp, edited.GetGuid("concurrencyStamp"));

        // The same edit sent again with the old stamp (double click) is accepted as a replay; a different one is a conflict.
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync(url, edit)).StatusCode);
        await (await admin.PatchAsJsonAsync(url, edit with { reference = "REF-OTHER" })).ShouldFailAsync(409, "concurrency.conflict");
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "billing.payment_updated" &&
            a.EntityId == payment.GetGuid("id").ToString() && a.Reason == "Bank statement shows another id")));

        // Reversed payments can't be edited.
        var reversed = await (await admin.HubReverseAsync(edited, "Error")).ReadJsonAsync();
        await (await admin.PatchAsJsonAsync(url, new { notes = "x", reason = "Late note after reversal", concurrencyStamp = reversed.GetProperty("payment").GetGuid("concurrencyStamp") }))
            .ShouldFailAsync(409, "billing.payment_reversed");
    }

    [Fact]
    public async Task Permission_matrix_for_reading_and_changing_payments()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync();
        var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Seo", 1, 200m));
        var payment = (await (await admin.HubRecordAsync(invoice, 50m, "PERM-1")).ReadJsonAsync()).GetProperty("payment");

        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var (_, manager) = await api.CreateClientAsync(Role.AccountManager);
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var (_, participant) = await api.CreateClientAsync(Role.Participant);
        var (_, campaigns) = await api.CreateClientAsync(Role.CampaignManager);
        var (_, clientUser) = await api.CreateClientMemberAsync(client.Id, ClientMemberRole.Owner);

        // Reading: billing.view (incoming) or payouts.view (outgoing) is needed.
        foreach (var c in new[] { admin, finance, manager, sales })
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Hub)).StatusCode);
        foreach (var c in new[] { participant, campaigns, clientUser })
        {
            await (await c.GetAsync(Hub)).ShouldFailAsync(403);
            await (await c.GetAsync($"{Hub}/export.csv")).ShouldFailAsync(403);
            await (await c.GetAsync($"{Hub}/summary")).ShouldFailAsync(403);
        }
        // An account manager sees incoming records only (no payouts.view) and can't change anything.
        var managerList = await manager.HubListAsync();
        Assert.All(managerList.Items(), r => Assert.Equal("Incoming", r.Str("direction")));
        var managerRow = managerList.Items().First(r => r.GetGuid("id") == payment.GetGuid("id"));
        Assert.Empty(managerRow.Actions());
        var summary = await (await manager.GetAsync($"{Hub}/summary")).ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("outgoing").ValueKind);

        var current = await admin.GetInvoiceAsync(invoice.GetGuid("id"));
        foreach (var c in new[] { manager, sales, participant, clientUser })
        {
            await (await c.HubRecordAsync(current, 1m, "NOPE-1")).ShouldFailAsync(403);
            await (await c.HubReverseAsync(payment, "Error")).ShouldFailAsync(403);
            await (await c.PostAsJsonAsync($"{Hub}/invoices/{current.GetGuid("id")}/reminders", new { requestId = Guid.NewGuid() })).ShouldFailAsync(403);
            await (await c.PostAsJsonAsync($"{Hub}/payouts/{Guid.NewGuid()}/mark-paid", new { paymentReference = "X-123", paidAt = DateTime.UtcNow }))
                .ShouldFailAsync(403);
        }
        // Reminder schedules need billing.settings (Finance and Admin).
        var policy = new { useAgencyDefault = false, enabled = true, offsetsDays = new[] { 3, 7, 14 }, reason = "Client asked for weekly nudges" };
        await (await manager.PutAsJsonAsync($"{Hub}/reminder-policies/{client.Id}", policy)).ShouldFailAsync(403);
        Assert.Equal(HttpStatusCode.OK, (await finance.PutAsJsonAsync($"{Hub}/reminder-policies/{client.Id}", policy)).StatusCode);

        // Finance can act; the row advertises the actions.
        var financeRow = (await finance.HubListAsync()).Items().First(r => r.GetGuid("id") == payment.GetGuid("id"));
        Assert.Contains("reverse", financeRow.Actions());
        Assert.Contains("edit", financeRow.Actions());
    }

    [Fact]
    public async Task List_filters_detail_summary_per_currency_and_formula_safe_csv()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var usdClient = await api.CreateClientAccountAsync(name: "Filter Co " + Guid.NewGuid().ToString("N")[..6]);
        var aedClient = await api.CreateClientAccountAsync(currency: "AED", country: "AE");
        var usd = await admin.IssuedInvoiceAsync(usdClient.Id, Line("Retainer", 1, 1000m));
        var aed = await admin.IssuedInvoiceAsync(aedClient.Id, Line("Retainer", 1, 3000m));
        var usdPayment = (await (await admin.HubRecordAsync(usd, 250m, "=HYPERLINK(\"http://x\")")).ReadJsonAsync()).GetProperty("payment");
        await admin.HubRecordAsync(aed, 3000m, "AED-TT-1");

        var byClient = await admin.HubListAsync($"&clientAccountId={usdClient.Id}");
        Assert.Equal(2, byClient.GetProperty("total").GetInt32()); // the payment + the open balance
        var due = byClient.Items().Single(r => r.Str("kind") == "InvoiceDue");
        Assert.Equal("Pending", due.Str("status"));
        Assert.Equal(750m, due.Dec("amount"));
        Assert.Contains("mark_paid_in_full", due.Actions());
        Assert.Contains("send_reminder", due.Actions());

        var paidOnly = await admin.HubListAsync($"&clientAccountId={usdClient.Id}&status=Paid");
        Assert.Equal(usdPayment.GetGuid("id"), Assert.Single(paidOnly.Items()).GetGuid("id"));
        Assert.Empty((await admin.HubListAsync($"&clientAccountId={usdClient.Id}&direction=Outgoing")).Items());
        Assert.Single((await admin.HubListAsync("&search=AED-TT-1")).Items());
        Assert.Single((await admin.HubListAsync($"&clientAccountId={usdClient.Id}&method=BankTransfer")).Items());

        // Paging is exact across the merged sources.
        var page1 = await (await admin.GetAsync($"{Hub}?pageSize=1&page=1")).ReadJsonAsync();
        var page2 = await (await admin.GetAsync($"{Hub}?pageSize=1&page=2")).ReadJsonAsync();
        var total = page1.GetProperty("total").GetInt32();
        Assert.True(total >= 3);
        var all = (await admin.HubListAsync()).Items().Select(r => r.Str("key")).ToList();
        var page3 = await (await admin.GetAsync($"{Hub}?pageSize=1&page=3")).ReadJsonAsync();
        Assert.Equal(all.Take(3), new[] { page1, page2, page3 }.Select(p => p.Items().Single().Str("key")));
        var ascending = (await admin.HubListAsync("&desc=false")).Items().Select(r => r.Str("key")).ToList();
        Assert.Equal(all.AsEnumerable().Reverse(), ascending);

        var detail = await (await admin.GetAsync($"{Hub}/records/InvoicePayment/{usdPayment.GetGuid("id")}")).ReadJsonAsync();
        Assert.Single(detail.GetProperty("invoicePayments").EnumerateArray());
        Assert.Contains(detail.GetProperty("history").EnumerateArray(), h => h.Str("action") == "billing.payment_recorded");
        await (await admin.GetAsync($"{Hub}/records/InvoicePayment/{Guid.NewGuid()}")).ShouldFailAsync(404);

        var summary = await (await admin.GetAsync($"{Hub}/summary")).ReadJsonAsync();
        var received = summary.GetProperty("incoming").GetProperty("receivedThisMonth").EnumerateArray().ToList();
        Assert.Contains(received, c => c.Str("currency") == "AED" && c.Dec("amount") >= 3000m);
        Assert.Contains(received, c => c.Str("currency") == "USD" && c.Dec("amount") >= 250m);
        Assert.Contains(summary.GetProperty("incoming").GetProperty("outstanding").EnumerateArray(), c => c.Str("currency") == "USD");

        var csv = await admin.GetAsync($"{Hub}/export.csv?clientAccountId={usdClient.Id}");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.Contains("'=HYPERLINK", text);
        Assert.DoesNotContain(",=HYPERLINK", text);
        Assert.Equal(3, text.Trim().Split('\n').Length); // header + 2 rows
    }

    [Fact]
    public async Task Client_reports_a_payment_staff_confirm_it_once_and_the_invoice_balance_updates()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var managerUser = await api.CreateUserAsync(new[] { Role.AccountManager });
        var client = await api.CreateClientAccountAsync();
        await api.WithDbAsync(db => db.Set<ClientAccount>().Where(c => c.Id == client.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.AccountManagerUserId, managerUser.Id)));
        var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 600m));
        var invoiceId = invoice.GetGuid("id");
        var (billingUser, billing) = await api.CreateClientMemberAsync(client.Id, ClientMemberRole.Billing);
        var (_, viewer) = await api.CreateClientMemberAsync(client.Id, ClientMemberRole.Viewer);
        var otherClient = await api.CreateClientAccountAsync();
        var (_, stranger) = await api.CreateClientMemberAsync(otherClient.Id, ClientMemberRole.Owner);
        var claimsUrl = $"/api/v1/client/billing/invoices/{invoiceId}/payment-claims";
        object Claim(Guid key, decimal amount, string reference) =>
            new { requestId = key, amount, method = "BankTransfer", reference, paidOn = api.Today().Iso(), note = "Paid from our HSBC account" };

        await (await viewer.PostAsJsonAsync(claimsUrl, Claim(Guid.NewGuid(), 600m, "HSBC-1"))).ShouldFailAsync(403);
        await (await stranger.PostAsJsonAsync(claimsUrl, Claim(Guid.NewGuid(), 600m, "HSBC-1"))).ShouldFailAsync(404);
        await (await billing.PostAsJsonAsync(claimsUrl, Claim(Guid.NewGuid(), 600.01m, "HSBC-1"))).ShouldFailAsync(409, "billing.overpayment");

        var key = Guid.NewGuid();
        var created = await billing.PostAsJsonAsync(claimsUrl, Claim(key, 600m, "HSBC-1"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await billing.PostAsJsonAsync(claimsUrl, Claim(key, 600m, "HSBC-1"))).StatusCode);
        await (await billing.PostAsJsonAsync(claimsUrl, Claim(Guid.NewGuid(), 600m, "HSBC-1"))).ShouldFailAsync(409, "payments.claim_duplicate");
        var claim = await created.ReadJsonAsync();
        var claimId = claim.GetGuid("id");
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<PaymentClaim>().CountAsync(c => c.InvoiceId == invoiceId)));

        // Nothing changes on the invoice yet; staff (finance + the account manager) are notified.
        Assert.Equal(600m, (await admin.GetInvoiceAsync(invoiceId)).Dec("balance"));
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == managerUser.Id && n.Type == "billing.payment_claimed")));

        // A proof of payment from the client, readable by staff.
        var proof = await billing.PostAsync($"/api/v1/client/billing/payment-claims/{claimId}/proofs", ProofForm(PdfProof()));
        Assert.Equal(HttpStatusCode.Created, proof.StatusCode);
        var proofId = (await proof.ReadJsonAsync()).GetGuid("id");
        await (await billing.PostAsync($"/api/v1/client/billing/payment-claims/{claimId}/proofs",
            ProofForm(new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }), "evil.exe"))).ShouldFailAsync(400, "file.unsupported_type");
        var staffProof = await admin.GetAsync($"{Hub}/proofs/{proofId}");
        Assert.Equal(HttpStatusCode.OK, staffProof.StatusCode);
        Assert.Equal("application/pdf", staffProof.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await billing.GetAsync($"/api/v1/client/billing/payment-proofs/{proofId}")).StatusCode);
        await (await stranger.GetAsync($"/api/v1/client/billing/payment-proofs/{proofId}")).ShouldFailAsync(404);

        // The claim shows in the hub as Pending with confirm/reject.
        var row = (await admin.HubListAsync($"&kind=PaymentClaim&invoiceId={invoiceId}")).Items().Single();
        Assert.Equal("Pending", row.Str("status"));
        Assert.True(row.GetProperty("hasProof").GetBoolean());
        Assert.Contains("confirm_claim", row.Actions());

        // Two staff confirm at once: one payment is recorded (the claim id is the idempotency key).
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var confirmBody = new { invoiceConcurrencyStamp = invoice.GetGuid("concurrencyStamp") };
        var confirms = await Task.WhenAll(
            admin.PostAsJsonAsync($"{Hub}/claims/{claimId}/confirm", confirmBody),
            finance.PostAsJsonAsync($"{Hub}/claims/{claimId}/confirm", confirmBody));
        Assert.All(confirms, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var after = await admin.GetInvoiceAsync(invoiceId);
        Assert.Equal("Paid", after.Str("status"));
        Assert.Equal(1, await PaymentRowsAsync(invoiceId));
        var stored = await api.WithDbAsync(db => db.Set<PaymentClaim>().AsNoTracking().FirstAsync(c => c.Id == claimId));
        Assert.Equal(PaymentClaimStatus.Confirmed, stored.Status);
        Assert.NotNull(stored.PaymentId);
        Assert.Equal(stored.PaymentId, await api.WithDbAsync(db => db.Set<PaymentProof>().Where(f => f.Id == proofId).Select(f => f.PaymentId).FirstAsync()));
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == billingUser.Id && n.Type == "billing.payment_claim_reviewed")));
        // The account manager is told the invoice was paid (InvoicePaid event).
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == managerUser.Id && n.Type == BillingNotificationTypes.InvoicePaid)));

        // The client sees the payment and their confirmed report.
        var mine = await (await billing.GetAsync($"/api/v1/client/billing/invoices/{invoiceId}/payments")).ReadJsonAsync();
        Assert.Equal(0m, mine.Dec("balance"));
        Assert.False(mine.GetProperty("canReportPayment").GetBoolean());
        Assert.Equal("Paid", Assert.Single(mine.GetProperty("payments").EnumerateArray()).Str("status"));
        Assert.Equal("Confirmed", Assert.Single(mine.GetProperty("claims").EnumerateArray()).Str("status"));
        await (await viewer.GetAsync($"/api/v1/client/billing/invoices/{invoiceId}/payments")).ShouldFailAsync(403);
        await (await stranger.GetAsync($"/api/v1/client/billing/invoices/{invoiceId}/payments")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Rejecting_a_client_report_tells_the_client_and_changes_nothing()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync();
        var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 400m));
        var (billingUser, billing) = await api.CreateClientMemberAsync(client.Id, ClientMemberRole.Owner);
        var claim = await (await billing.PostAsJsonAsync($"/api/v1/client/billing/invoices/{invoice.GetGuid("id")}/payment-claims", new
        {
            requestId = Guid.NewGuid(), amount = 400m, method = "BankTransfer", reference = "NOT-FOUND-9", paidOn = api.Today().Iso(),
        })).ReadJsonAsync();
        var url = $"{Hub}/claims/{claim.GetGuid("id")}/reject";
        await (await admin.PostAsJsonAsync(url, new { reason = "No", concurrencyStamp = claim.GetGuid("concurrencyStamp") })).ShouldFailAsync(400);
        var body = new { reason = "No transfer with this reference on our statement", concurrencyStamp = claim.GetGuid("concurrencyStamp") };
        var rejected = await (await admin.PostAsJsonAsync(url, body)).ReadJsonAsync();
        Assert.Equal("Rejected", rejected.GetProperty("claim").Str("status"));
        Assert.True((await (await admin.PostAsJsonAsync(url, body)).ReadJsonAsync()).GetProperty("replayed").GetBoolean());
        await (await admin.PostAsJsonAsync($"{Hub}/claims/{claim.GetGuid("id")}/confirm", new { invoiceConcurrencyStamp = invoice.GetGuid("concurrencyStamp") }))
            .ShouldFailAsync(409, "payments.claim_rejected");
        Assert.Equal(400m, (await admin.GetInvoiceAsync(invoice.GetGuid("id"))).Dec("balance"));
        Assert.Equal(0, await PaymentRowsAsync(invoice.GetGuid("id")));
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == billingUser.Id && n.Body.Contains("No transfer with this reference"))));
        var listed = (await admin.HubListAsync($"&kind=PaymentClaim&invoiceId={invoice.GetGuid("id")}")).Items().Single();
        Assert.Equal("Voided", listed.Str("status"));
    }

    [Fact]
    public async Task Staff_attach_proof_to_a_payment()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync();
        var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 100m));
        var payment = (await (await admin.HubRecordAsync(invoice, 100m, "CHQ-555", method: "Cheque")).ReadJsonAsync()).GetProperty("payment");
        var upload = await admin.PostAsync($"{Hub}/invoice-payments/{payment.GetGuid("id")}/proofs", ProofForm(PdfProof(), "cheque.pdf"));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var detail = await (await admin.GetAsync($"{Hub}/records/InvoicePayment/{payment.GetGuid("id")}")).ReadJsonAsync();
        Assert.Equal("cheque.pdf", Assert.Single(detail.GetProperty("proofs").EnumerateArray()).Str("fileName"));
        Assert.True(detail.GetProperty("record").GetProperty("hasProof").GetBoolean());
    }

    [Fact]
    public async Task Send_reminder_now_is_idempotent_rate_limited_and_listed_in_the_history()
    {
        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);
        var client = await api.CreateClientAccountAsync(billingEmail: "ap@reminder-client.test");
        var invoice = await admin.IssuedInvoiceAsync(client.Id, Line("Retainer", 1, 800m));
        var (member, _) = await api.CreateClientMemberAsync(client.Id, ClientMemberRole.Billing);
        var url = $"{Hub}/invoices/{invoice.GetGuid("id")}/reminders";
        var key = Guid.NewGuid();

        var sent = await admin.PostAsJsonAsync(url, new { requestId = key });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Assert.StartsWith("manual-", (await sent.ReadJsonAsync()).Str("kind"));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(url, new { requestId = key })).StatusCode);
        await (await admin.PostAsJsonAsync(url, new { requestId = Guid.NewGuid() })).ShouldFailAsync(409, "payments.reminder_too_soon");

        var history = await (await admin.GetAsync(url)).ReadJsonAsync();
        var entry = Assert.Single(history.EnumerateArray());
        Assert.True(entry.GetProperty("manual").GetBoolean());
        Assert.NotNull(entry.Str("sentBy"));
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<Notification>().CountAsync(n => n.UserId == member.Id && n.Type == BillingNotificationTypes.InvoiceReminder)));
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<InvoiceReminder>().CountAsync(r => r.InvoiceId == invoice.GetGuid("id") && r.SentByUserId == adminUser.Id)));

        // A paid invoice gets no reminder.
        var current = await admin.GetInvoiceAsync(invoice.GetGuid("id"));
        await admin.HubMarkPaidAsync(current, "PAID-REM");
        api.Clock.Advance(TimeSpan.FromHours(2));
        admin = await api.LoginAsync(adminUser);
        await (await admin.PostAsJsonAsync(url, new { requestId = Guid.NewGuid() })).ShouldFailAsync(409, "billing.invoice_not_open");
    }
}
