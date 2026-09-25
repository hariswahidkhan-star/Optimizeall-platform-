using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.IntegrationTests.Campaigns;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Codes;

/// <summary>Helpers for the discount-code (affiliate sales) tests.</summary>
public sealed class CodesKit(ApiFactory api)
{
    public DateTime Now => api.Clock.GetUtcNow().UtcDateTime;

    /// <summary>Campaign managers hold codes.view, codes.manage and codes.assign.</summary>
    public Task<(TestUser User, HttpClient Client)> ManagerAsync() => api.CreateClientAsync(Role.CampaignManager);

    /// <summary>Reviewers hold sales.review.</summary>
    public Task<(TestUser User, HttpClient Client)> ReviewerAsync() => api.CreateClientAsync(Role.Reviewer);

    /// <summary>Finance holds codes.view and sales.reverse.</summary>
    public Task<(TestUser User, HttpClient Client)> FinanceAsync() => api.CreateClientAsync(Role.Finance);

    public Task<(TestUser User, HttpClient Client)> ParticipantAsync() => api.CreateClientAsync(Role.Participant);

    public static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    public static Guid Stamp(JsonElement e) => e.GetProperty("concurrencyStamp").GetGuid();

    public Dictionary<string, object?> ProgramBody(string payoutType = "PercentOfNet", decimal? flat = null, decimal? percent = 10m,
        string currency = "USD", object[]? tiers = null, decimal? daily = null, decimal? programCap = null, decimal? budget = null,
        bool activate = true, DateTime? startsAt = null, DateTime? endsAt = null, bool requireProof = false) => new()
    {
        ["name"] = "Glow " + Guid.NewGuid().ToString("N")[..6],
        ["brandName"] = "Glow Cosmetics",
        ["description"] = "Skincare brand",
        ["terms"] = "No self-purchases. Codes may not be posted on coupon sites.",
        ["storeUrl"] = "https://shop.example.com/glow",
        ["discountLabel"] = "15% off",
        ["currency"] = currency,
        ["startsAt"] = startsAt ?? Now.AddDays(-30),
        ["endsAt"] = endsAt,
        ["maxOrderAgeDays"] = 60,
        ["requireProof"] = requireProof,
        ["activate"] = activate,
        ["payout"] = new Dictionary<string, object?>
        {
            ["payoutType"] = payoutType, ["flatAmount"] = flat, ["percent"] = payoutType == "PercentOfNet" ? percent : null,
            ["tiers"] = tiers ?? Array.Empty<object>(), ["dailyCapPerPerson"] = daily, ["programCapPerPerson"] = programCap, ["budgetAmount"] = budget,
        },
    };

    public async Task<JsonElement> ProgramAsync(HttpClient manager, Dictionary<string, object?>? body = null) =>
        await (await manager.PostAsJsonAsync("/api/v1/admin/code-programs", body ?? ProgramBody())).ReadJsonAsync();

    public static async Task<JsonElement> CodeAsync(HttpClient manager, Guid programId, string? code = null, DateTime? validFrom = null, DateTime? validTo = null) =>
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{programId}/codes",
            new { code = code ?? "C" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), validFrom, validTo })).ReadJsonAsync();

    public static Task<HttpResponseMessage> PostAssignAsync(HttpClient manager, Guid codeId, Guid? userId = null, Guid? groupId = null,
        DateTime? validFrom = null, DateTime? validTo = null, bool reassign = false) =>
        manager.PostAsJsonAsync($"/api/v1/admin/discount-codes/{codeId}/assign", new
        {
            target = userId is null ? "Group" : "Person", userId, groupId, validFrom, validTo, reassign, reason = "Assigned in test",
        });

    public static async Task<JsonElement> AssignAsync(HttpClient manager, Guid codeId, Guid? userId = null, Guid? groupId = null,
        DateTime? validFrom = null, DateTime? validTo = null, bool reassign = false) =>
        await (await PostAssignAsync(manager, codeId, userId, groupId, validFrom, validTo, reassign)).ReadJsonAsync();

    public static async Task<JsonElement> GroupAsync(HttpClient manager, params Guid[] members)
    {
        var group = await (await manager.PostAsJsonAsync("/api/v1/admin/rate-groups", new
        {
            name = "Codes group " + Guid.NewGuid().ToString("N")[..6], priority = 10, membershipMode = "Manual",
        })).ReadJsonAsync();
        if (members.Length > 0)
            await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-groups/{Id(group)}/members", new { userIds = members, note = "test" })).ReadJsonAsync();
        return group;
    }

    public static MultipartFormDataContent SaleForm(Guid codeId, string orderRef, DateTime orderDate, decimal net, string currency = "USD",
        decimal? discount = null, bool proof = false, string? note = null, Guid? stamp = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(codeId.ToString()), "codeId" },
            { new StringContent(orderRef), "orderReference" },
            { new StringContent(orderDate.ToString("O")), "orderDate" },
            { new StringContent(net.ToString(System.Globalization.CultureInfo.InvariantCulture)), "netAmount" },
            { new StringContent(currency), "currency" },
        };
        if (discount is { } d) form.Add(new StringContent(d.ToString(System.Globalization.CultureInfo.InvariantCulture)), "discountAmount");
        if (note is not null) form.Add(new StringContent(note), "productNote");
        if (stamp is { } s) form.Add(new StringContent(s.ToString()), "concurrencyStamp");
        if (proof)
        {
            var file = new ByteArrayContent(CampaignTestKit.Png());
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "proof", "receipt.png");
        }
        return form;
    }

    public Task<HttpResponseMessage> SubmitAsync(HttpClient participant, Guid codeId, string? orderRef = null, DateTime? orderDate = null,
        decimal net = 100m, string currency = "USD", decimal? discount = 15m, bool proof = false) =>
        participant.PostAsync("/api/v1/me/code-sales",
            SaleForm(codeId, orderRef ?? "ORD-" + Guid.NewGuid().ToString("N")[..10], orderDate ?? Now.AddHours(-2), net, currency, discount, proof));

    public async Task<JsonElement> SubmitOkAsync(HttpClient participant, Guid codeId, string? orderRef = null, DateTime? orderDate = null,
        decimal net = 100m, string currency = "USD", decimal? discount = 15m) =>
        await (await SubmitAsync(participant, codeId, orderRef, orderDate, net, currency, discount)).ReadJsonAsync();

    public static async Task<Guid> SaleStampAsync(HttpClient staff, Guid saleId) =>
        Stamp(await (await staff.GetAsync($"/api/v1/admin/code-sales/{saleId}")).ReadJsonAsync());

    public static async Task<HttpResponseMessage> DecideAsync(HttpClient reviewer, Guid saleId, string decision = "Approve", string? reason = null,
        Guid? stamp = null) =>
        await reviewer.PostAsJsonAsync($"/api/v1/admin/code-sales/{saleId}/decision", new
        {
            decision, reason = reason ?? (decision == "Approve" ? null : "Order not found in the brand's records"),
            concurrencyStamp = stamp ?? await SaleStampAsync(reviewer, saleId),
        });

    public static async Task<JsonElement> ApproveAsync(HttpClient reviewer, Guid saleId) => await (await DecideAsync(reviewer, saleId)).ReadJsonAsync();

    public Task<List<EarningEntry>> EarningsAsync(Guid saleId) =>
        api.WithDbAsync(db => db.Set<EarningEntry>().AsNoTracking().Where(e => e.CodeSaleId == saleId).OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToListAsync());

    public async Task AddExchangeRateAsync(string from, string to, decimal rate, DateTime? effectiveAt = null) =>
        await api.WithDbAsync(async db =>
        {
            db.Add(new ExchangeRate
            {
                BaseCurrency = from, QuoteCurrency = to, Rate = rate, EffectiveAt = effectiveAt ?? Now.AddDays(-90), Source = "test", CreatedAt = Now,
            });
            await db.SaveChangesAsync();
        });

    public static MultipartFormDataContent Csv(string content, string name = "file.csv")
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", name } };
    }

    /// <summary>A program with one code personally assigned to a new participant.</summary>
    public async Task<(JsonElement Program, Guid CodeId, TestUser User, HttpClient Participant, HttpClient Manager)> AssignedAsync(
        Dictionary<string, object?>? body = null, DateTime? assignedFrom = null)
    {
        var (_, manager) = await ManagerAsync();
        var program = await ProgramAsync(manager, body);
        var code = await CodeAsync(manager, Id(program));
        var (user, participant) = await ParticipantAsync();
        await AssignAsync(manager, Id(code), user.Id, validFrom: assignedFrom ?? Now.AddDays(-20));
        return (program, Id(code), user, participant, manager);
    }
}
