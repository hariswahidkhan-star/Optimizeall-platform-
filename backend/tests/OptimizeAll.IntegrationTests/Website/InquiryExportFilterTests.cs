using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Website;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Website;

/// <summary>
/// The inquiries CSV export holds exactly what the inbox lists for the same filters (search, type, status, assignee, UTM
/// source, dates and service), not every inquiry of a type/status; the newsletter export honours the search too.
/// </summary>
public sealed class InquiryExportFilterTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static async Task<List<string>> CsvEmailsAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(',');
        var email = Array.IndexOf(header, "email");
        return lines.Skip(1).Select(l => l.Split(',')[email]).Order().ToList();
    }

    [Fact]
    public async Task The_inquiry_export_applies_every_list_filter_including_search()
    {
        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var now = DateTime.UtcNow;
        WebsiteInquiry Inquiry(string name, InquiryType type, Guid? assignee = null, string? utm = null, string[]? services = null) => new()
        {
            Type = type, Name = $"{name} {tag}", Email = $"{name.ToLowerInvariant()}-{tag}@inq.example", Company = $"Co{tag}", ConsentVersion = "v1",
            ConsentAt = now, AssignedToUserId = assignee, UtmSource = utm, ServiceSlugs = services?.ToList() ?? new List<string>(),
        };
        await api.WithDbAsync(async db =>
        {
            db.Set<WebsiteInquiry>().AddRange(
                Inquiry("Alpha", InquiryType.Contact, adminUser.Id, $"google{tag}", new[] { "seo" }),
                Inquiry("Beta", InquiryType.Contact, null, $"meta{tag}"),
                Inquiry("Gamma", InquiryType.Quote, adminUser.Id, $"google{tag}", new[] { "seo", "ppc" }));
            await db.SaveChangesAsync();
        });

        async Task Same(string query, params string[] expected)
        {
            var exported = await CsvEmailsAsync(await admin.GetAsync($"/api/v1/agency/website/inquiries/export.csv?{query}"));
            var listed = (await (await admin.GetAsync($"/api/v1/agency/website/inquiries?{query}&pageSize=200")).ReadJsonAsync())
                .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("email").GetString()!).Order().ToList();
            var want = expected.Select(n => $"{n}-{tag}@inq.example").Order().ToList();
            Assert.Equal(want, listed);
            Assert.Equal(want, exported);
        }

        await Same($"search={tag}", "alpha", "beta", "gamma");
        await Same($"search=alpha-{tag}", "alpha");
        await Same($"search={tag}&type=Contact", "alpha", "beta");
        await Same($"search={tag}&assignedTo=me", "alpha", "gamma");
        await Same($"search={tag}&assignedTo=unassigned", "beta");
        await Same($"utmSource=google{tag}", "alpha", "gamma");
        await Same($"search={tag}&service=ppc", "gamma");
        await Same($"search={tag}&from={now.AddDays(1):yyyy-MM-dd}");

        // The audit entry records the filters the export used.
        var audit = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == "website.inquiries_exported" && a.ActorUserId == adminUser.Id).OrderByDescending(a => a.Id).FirstAsync());
        var after = JsonDocument.Parse(audit.AfterJson!).RootElement;
        Assert.Equal(0, after.GetProperty("rows").GetInt32());
        Assert.Equal(tag, after.GetProperty("search").GetString());
    }
}
