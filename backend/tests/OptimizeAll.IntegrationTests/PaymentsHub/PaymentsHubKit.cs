using System.Net.Http.Json;
using System.Text.Json;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Crm.CrmBillingKit;

namespace OptimizeAll.IntegrationTests.PaymentsHub;

/// <summary>HTTP helpers for the Payments hub (<c>/api/v1/admin/payments</c>).</summary>
public static class PaymentsHubKit
{
    public const string Hub = "/api/v1/admin/payments";

    public static Task<HttpResponseMessage> HubRecordAsync(this HttpClient client, JsonElement invoice, decimal amount, string reference,
        Guid? requestId = null, string method = "BankTransfer", DateOnly? paidOn = null) =>
        client.PostAsJsonAsync($"{Hub}/invoices/{invoice.GetGuid("id")}/payments", new
        {
            requestId = requestId ?? Guid.NewGuid(), amount, method, reference,
            paidOn = (paidOn ?? DateOnly.FromDateTime(DateTime.UtcNow)).Iso(), concurrencyStamp = invoice.GetGuid("concurrencyStamp"),
        });

    public static Task<HttpResponseMessage> HubMarkPaidAsync(this HttpClient client, JsonElement invoice, string reference, Guid? requestId = null) =>
        client.PostAsJsonAsync($"{Hub}/invoices/{invoice.GetGuid("id")}/mark-paid", new
        {
            requestId = requestId ?? Guid.NewGuid(), method = "Cheque", reference, paidOn = DateOnly.FromDateTime(DateTime.UtcNow).Iso(),
            expectedBalance = invoice.Dec("balance"), concurrencyStamp = invoice.GetGuid("concurrencyStamp"),
        });

    public static Task<HttpResponseMessage> HubReverseAsync(this HttpClient client, JsonElement payment, string kind, Guid? requestId = null,
        string reason = "Recorded on the wrong invoice") =>
        client.PostAsJsonAsync($"{Hub}/invoice-payments/{payment.GetGuid("id")}/reverse", new
        {
            requestId = requestId ?? Guid.NewGuid(), kind, reason, confirm = true, concurrencyStamp = payment.GetGuid("concurrencyStamp"),
        });

    public static async Task<JsonElement> HubListAsync(this HttpClient client, string query = "") =>
        await (await client.GetAsync($"{Hub}?pageSize=200{query}")).ReadJsonAsync();

    public static IEnumerable<JsonElement> Items(this JsonElement page) => page.GetProperty("items").EnumerateArray();

    public static IEnumerable<string> Actions(this JsonElement record) =>
        record.GetProperty("actions").EnumerateArray().Select(a => a.GetString()!);

    /// <summary>The smallest valid PDF header the upload validator recognizes (magic bytes).</summary>
    public static ByteArrayContent PdfProof() =>
        new(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n"))
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf") },
        };

    public static MultipartFormDataContent ProofForm(HttpContent file, string fileName = "slip.pdf")
    {
        var form = new MultipartFormDataContent();
        form.Add(file, "file", fileName);
        return form;
    }
}
