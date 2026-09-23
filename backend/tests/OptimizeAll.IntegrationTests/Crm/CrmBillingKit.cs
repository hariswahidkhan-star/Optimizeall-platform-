using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Crm;

/// <summary>Arrange/act helpers for CRM, proposal and billing tests.</summary>
public static class CrmBillingKit
{
    public static DateTime UtcNow(this ApiFactory api) => api.Clock.GetUtcNow().UtcDateTime;

    public static DateOnly Today(this ApiFactory api) => DateOnly.FromDateTime(api.UtcNow());

    public static string Iso(this DateOnly d) => d.ToString("yyyy-MM-dd");

    public static Guid GetGuid(this JsonElement e, string property) => e.GetProperty(property).GetGuid();

    public static decimal Dec(this JsonElement e, string property) => e.GetProperty(property).GetDecimal();

    public static string Str(this JsonElement e, string property) => e.GetProperty(property).GetString()!;

    public static async Task<ClientAccount> CreateClientAccountAsync(this ApiFactory api, string? name = null, string currency = "USD", string country = "US",
        string? billingEmail = null)
    {
        name ??= "Client " + Guid.NewGuid().ToString("N")[..8];
        var client = new ClientAccount
        {
            Name = name, Slug = name.ToLowerInvariant().Replace(' ', '-') + "-" + Guid.NewGuid().ToString("N")[..6], Currency = currency, CountryCode = country,
            Status = ClientAccountStatus.Active, BillingEmail = billingEmail,
        };
        await api.WithDbAsync(async db =>
        {
            db.Set<ClientAccount>().Add(client);
            await db.SaveChangesAsync();
        });
        return client;
    }

    /// <summary>Creates a Client-role user who is a member of the organization with the given duty, and signs them in.</summary>
    public static async Task<(TestUser User, HttpClient Client)> CreateClientMemberAsync(this ApiFactory api, Guid clientAccountId, ClientMemberRole role)
    {
        var (user, client) = await api.CreateClientAsync(Role.Client);
        await api.WithDbAsync(async db =>
        {
            db.Set<ClientMember>().Add(new ClientMember { ClientAccountId = clientAccountId, UserId = user.Id, Role = role, AddedAt = api.UtcNow() });
            await db.SaveChangesAsync();
        });
        return (user, client);
    }

    public static Task PublishAsync<TEvent>(this ApiFactory api, TEvent e) where TEvent : IDomainEvent =>
        api.Services.GetRequiredService<IEventPublisher>().PublishAsync(e);

    public static object Line(string description, decimal quantity, decimal unitPrice, string recurrence = "OneTime", string discountType = "None",
        decimal discountValue = 0, Guid? taxRateId = null, string? serviceSlug = null) =>
        new { description, quantity, unitPrice, recurrence, discountType, discountValue, taxRateId, serviceSlug };

    public static async Task<JsonElement> CreateDraftInvoiceAsync(this HttpClient client, Guid clientAccountId, params object[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/v1/agency/billing/invoices", new
        {
            clientAccountId, lines = lines.Length == 0 ? new[] { Line("Monthly retainer", 1, 1000m) } : lines,
        });
        return await response.ReadJsonAsync();
    }

    public static async Task<JsonElement> IssueInvoiceAsync(this HttpClient client, JsonElement invoice) =>
        await (await client.PostAsJsonAsync($"/api/v1/agency/billing/invoices/{invoice.GetGuid("id")}/issue",
            new { concurrencyStamp = invoice.GetGuid("concurrencyStamp") })).ReadJsonAsync();

    public static async Task<JsonElement> IssuedInvoiceAsync(this HttpClient client, Guid clientAccountId, params object[] lines) =>
        await client.IssueInvoiceAsync(await client.CreateDraftInvoiceAsync(clientAccountId, lines));

    public static Task<HttpResponseMessage> RecordPaymentAsync(this HttpClient client, Guid invoiceId, Guid stamp, decimal amount, string reference,
        Guid? requestId = null, DateOnly? paidOn = null) =>
        client.PostAsJsonAsync($"/api/v1/agency/billing/invoices/{invoiceId}/payments", new
        {
            requestId = requestId ?? Guid.NewGuid(), amount, method = "BankTransfer", reference, paidOn = (paidOn ?? DateOnly.FromDateTime(DateTime.UtcNow)).Iso(),
            concurrencyStamp = stamp,
        });

    public static async Task<JsonElement> GetInvoiceAsync(this HttpClient client, Guid id) =>
        await (await client.GetAsync($"/api/v1/agency/billing/invoices/{id}")).ReadJsonAsync();

    /// <summary>A host on the same database with a counting <see cref="InvoicePaid"/> handler.</summary>
    public static WebApplicationFactory<Program> WithInvoicePaidCounter(this ApiFactory api) =>
        api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IEventHandler<InvoicePaid>, InvoicePaidCounter>()));

    public static async Task<HttpClient> LoginAsync(this WebApplicationFactory<Program> factory, TestUser user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }
}

/// <summary>Counts <see cref="InvoicePaid"/> events per invoice (static: handlers are created per scope).</summary>
public sealed class InvoicePaidCounter : IEventHandler<InvoicePaid>
{
    public static readonly ConcurrentDictionary<Guid, int> Counts = new();

    public Task HandleAsync(InvoicePaid e, CancellationToken ct)
    {
        Counts.AddOrUpdate(e.InvoiceId, 1, (_, n) => n + 1);
        return Task.CompletedTask;
    }
}
