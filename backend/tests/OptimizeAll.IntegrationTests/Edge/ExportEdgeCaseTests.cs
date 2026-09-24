using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Edge;

/// <summary>
/// Bulk exports of personal data (users, CRM contacts, website inquiries) are audited like the other personal-data
/// exports (form submissions, newsletter subscribers, email lists), and are Excel-safe: UTF-8 BOM, formula cells
/// neutralized, quotes/commas/newlines and non-Latin text preserved.
/// </summary>
public sealed class ExportEdgeCaseTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task<AuditLog?> LatestAuditAsync(string action, Guid actor) =>
        await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking().Where(a => a.Action == action && a.ActorUserId == actor)
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync());

    /// <summary>The row count recorded in the audit entry (MySQL normalizes the JSON's spacing, so parse it).</summary>
    private static int Rows(AuditLog audit) => JsonDocument.Parse(audit.AfterJson!).RootElement.GetProperty("rows").GetInt32();

    private static async Task<string> CsvAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "CSV exports start with a UTF-8 BOM so Excel reads them as UTF-8");
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }

    [Fact]
    public async Task Personal_data_exports_are_audited_and_excel_safe()
    {
        var (salesUser, sales) = await api.CreateClientAsync(Role.SalesRep);
        var tag = "exp" + Guid.NewGuid().ToString("N")[..8];
        (await sales.PostAsJsonAsync("/api/v1/agency/crm/contacts", new
        {
            firstName = "=HYPERLINK(\"http://evil.example\",\"x\")", lastName = "O'Brien, \"Jr\"\nSecond line", jobTitle = "@SUM(1)", phone = "+1 555 0100",
            email = $"{tag}@export.example", tags = new[] { tag }, source = "-2+3",
        })).EnsureSuccessStatusCode();
        (await sales.PostAsJsonAsync("/api/v1/agency/crm/contacts", new { firstName = "Zoë 😀", lastName = "日本語", email = $"{tag}.2@export.example", tags = new[] { tag } }))
            .EnsureSuccessStatusCode();

        var csv = await CsvAsync(await sales.GetAsync($"/api/v1/agency/crm/contacts/export.csv?tag={tag}"));
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil.example\"\",\"\"x\"\")\"", csv);
        Assert.Contains("\"O'Brien, \"\"Jr\"\"\nSecond line\"", csv);
        Assert.Contains(",'@SUM(1),", csv);
        Assert.Contains(",'-2+3,", csv);
        Assert.Contains("Zoë 😀,日本語,", csv);
        var audit = await LatestAuditAsync("crm.contacts_exported", salesUser.Id);
        Assert.NotNull(audit);
        Assert.Equal(2, Rows(audit!));
        Assert.Contains(tag, audit.AfterJson);

        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);
        var domain = $"{tag}.example";
        await api.CreateUserAsync(email: $"a@{domain}");
        await api.CreateUserAsync(email: $"b@{domain}");
        csv = await CsvAsync(await admin.GetAsync($"/api/v1/admin/users/export.csv?search={domain}"));
        Assert.Contains($"a@{domain}", csv);
        audit = await LatestAuditAsync("admin.users_exported", adminUser.Id);
        Assert.NotNull(audit);
        Assert.Equal(2, Rows(audit!));

        await CsvAsync(await admin.GetAsync("/api/v1/agency/website/inquiries/export.csv"));
        Assert.NotNull(await LatestAuditAsync("website.inquiries_exported", adminUser.Id));

        // Without the permission there is no export (and nothing to audit).
        var (participantUser, participant) = await api.CreateClientAsync(Role.Participant);
        await (await participant.GetAsync("/api/v1/admin/users/export.csv")).ShouldFailAsync(403);
        await (await participant.GetAsync("/api/v1/agency/crm/contacts/export.csv")).ShouldFailAsync(403);
        await (await participant.GetAsync("/api/v1/agency/website/inquiries/export.csv")).ShouldFailAsync(403);
        Assert.Null(await LatestAuditAsync("admin.users_exported", participantUser.Id));
    }
}
