using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Api.Modules.Crm;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Crm;

public sealed class CrmPipelineTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task<JsonElement> StagesAsync(HttpClient client) => await (await client.GetAsync("/api/v1/agency/crm/stages")).ReadJsonAsync();

    private static Guid StageId(JsonElement stages, string name) =>
        stages.EnumerateArray().First(s => s.Str("name") == name).GetGuid("id");

    private static async Task<JsonElement> CreateDealAsync(HttpClient client, string title, decimal value = 1200m, string currency = "USD") =>
        await (await client.PostAsJsonAsync("/api/v1/agency/crm/deals", new { title, value, currency, source = "Outbound" })).ReadJsonAsync();

    [Fact]
    public async Task Moving_a_deal_to_lost_requires_a_reason_and_stale_moves_conflict()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var stages = await StagesAsync(sales);
        var names = stages.EnumerateArray().Select(s => s.Str("name")).ToList();
        Assert.Equal(new[] { "New", "Contacted", "Qualified" }, names.Take(3));
        Assert.Equal(new[] { "Proposal sent", "Negotiation", "Won", "Lost" }, names.TakeLast(4));
        var deal = await CreateDealAsync(sales, "Pipeline test");
        Assert.Equal("New", deal.Str("stageName"));
        Assert.Equal(60m, deal.Dec("weightedValue")); // 5% of 1200

        await (await sales.PostAsJsonAsync($"/api/v1/agency/crm/deals/{deal.GetGuid("id")}/move",
            new { stageId = StageId(stages, "Lost"), concurrencyStamp = deal.GetGuid("concurrencyStamp") })).ShouldFailAsync(400, "crm.lost_reason_required");

        var moved = await (await sales.PostAsJsonAsync($"/api/v1/agency/crm/deals/{deal.GetGuid("id")}/move",
            new { stageId = StageId(stages, "Qualified"), concurrencyStamp = deal.GetGuid("concurrencyStamp") })).ReadJsonAsync();
        Assert.Equal("Qualified", moved.Str("stageName"));
        // The first move changed the stamp: a second editor with the old stamp is rejected.
        await (await sales.PostAsJsonAsync($"/api/v1/agency/crm/deals/{deal.GetGuid("id")}/move",
            new { stageId = StageId(stages, "Negotiation"), concurrencyStamp = deal.GetGuid("concurrencyStamp") })).ShouldFailAsync(409, "concurrency.conflict");

        var lost = await (await sales.PostAsJsonAsync($"/api/v1/agency/crm/deals/{deal.GetGuid("id")}/move",
            new { stageId = StageId(stages, "Lost"), lostReason = "Budget frozen", concurrencyStamp = moved.GetGuid("concurrencyStamp") })).ReadJsonAsync();
        Assert.Equal("Lost", lost.Str("status"));
        Assert.Equal("Budget frozen", lost.Str("lostReason"));
        Assert.NotEqual(JsonValueKind.Null, lost.GetProperty("closedAt").ValueKind);
        var timeline = await (await sales.GetAsync($"/api/v1/agency/crm/activities?dealId={deal.GetGuid("id")}")).ReadJsonAsync();
        Assert.Contains(timeline.GetProperty("items").EnumerateArray(), a => a.Str("subject") == "Moved from Qualified to Lost");
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "crm.deal_moved" && a.Reason == "Budget frozen")));

        // Reopening clears the close.
        var reopened = await (await sales.PostAsJsonAsync($"/api/v1/agency/crm/deals/{deal.GetGuid("id")}/move",
            new { stageId = StageId(stages, "Negotiation"), concurrencyStamp = lost.GetGuid("concurrencyStamp") })).ReadJsonAsync();
        Assert.Equal("Open", reopened.Str("status"));
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("lostReason").ValueKind);

        var board = await (await sales.GetAsync("/api/v1/agency/crm/deals/board")).ReadJsonAsync();
        var column = board.GetProperty("columns").EnumerateArray().First(c => c.GetProperty("stage").Str("name") == "Negotiation");
        Assert.Contains(column.GetProperty("deals").EnumerateArray(), d => d.GetGuid("id") == deal.GetGuid("id"));
    }

    [Fact]
    public async Task Viewers_can_read_but_not_change_the_pipeline_and_other_roles_are_denied()
    {
        var (_, strategist) = await api.CreateClientAsync(Role.Strategist); // crm.view only
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var (_, designer) = await api.CreateClientAsync(Role.Designer);
        var (_, client) = await api.CreateClientAsync(Role.Client);
        var deal = await CreateDealAsync(sales, "Permission test");

        Assert.Equal(HttpStatusCode.OK, (await strategist.GetAsync("/api/v1/agency/crm/deals/board")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await strategist.GetAsync($"/api/v1/agency/crm/deals/{deal.GetGuid("id")}")).StatusCode);
        await (await strategist.PostAsJsonAsync("/api/v1/agency/crm/deals", new { title = "x", currency = "USD" })).ShouldFailAsync(403);
        await (await strategist.GetAsync("/api/v1/agency/proposals")).ShouldFailAsync(403);
        foreach (var denied in new[] { designer, client })
        {
            await (await denied.GetAsync("/api/v1/agency/crm/dashboard")).ShouldFailAsync(403);
            await (await denied.GetAsync("/api/v1/agency/crm/contacts")).ShouldFailAsync(403);
            await (await denied.GetAsync("/api/v1/agency/billing/invoices")).ShouldFailAsync(403);
            await (await denied.GetAsync("/api/v1/agency/contracts")).ShouldFailAsync(403);
        }
        // Sales reps sell but don't run contracts or finance.
        await (await sales.GetAsync("/api/v1/agency/contracts")).ShouldFailAsync(403);
        Assert.Equal(HttpStatusCode.OK, (await sales.GetAsync("/api/v1/agency/billing/invoices")).StatusCode);
        await (await sales.PostAsJsonAsync("/api/v1/agency/billing/invoices", new { })).ShouldFailAsync(403);
        Assert.Equal(HttpStatusCode.OK, (await sales.GetAsync("/api/v1/agency/proposals")).StatusCode);
    }

    [Fact]
    public async Task Pipeline_settings_validate_and_keep_stages_with_deals()
    {
        var (_, manager) = await api.CreateClientAsync(Role.AccountManager);
        var stages = await StagesAsync(manager);
        var list = stages.EnumerateArray().Select(s => new
        {
            id = (Guid?)s.GetGuid("id"), name = s.Str("name"), winProbability = s.GetProperty("winProbability").GetInt32(),
            kind = s.Str("kind"), isActive = true,
        }).ToList();
        await (await manager.PutAsJsonAsync("/api/v1/agency/crm/stages", new { stages = list.Where(s => s.kind != "Won") }))
            .ShouldFailAsync(400, "crm.invalid_pipeline");

        var deal = await CreateDealAsync(manager, "Keeps its stage");
        await (await manager.PutAsJsonAsync("/api/v1/agency/crm/stages", new { stages = list.Where(s => s.name != "New") }))
            .ShouldFailAsync(409, "crm.stage_in_use");

        var renamed = list.Select(s => s.name == "Discovery call" ? s with { name = "Discovery", winProbability = 45 } : s).ToList();
        renamed.Insert(3, new { id = (Guid?)null, name = "Audit delivered", winProbability = 35, kind = "Open", isActive = true });
        var saved = await (await manager.PutAsJsonAsync("/api/v1/agency/crm/stages", new { stages = renamed })).ReadJsonAsync();
        Assert.Equal(new[] { "New", "Contacted", "Qualified", "Audit delivered", "Discovery", "Proposal sent", "Negotiation", "Won", "Lost" },
            saved.EnumerateArray().Select(s => s.Str("name")));
        Assert.Equal(45, saved.EnumerateArray().First(s => s.Str("name") == "Discovery").GetProperty("winProbability").GetInt32());
        _ = deal;
    }

    [Fact]
    public async Task Csv_import_validates_dedupes_and_reports_each_row()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var existing = $"existing.{Guid.NewGuid():N}@importco.example";
        await (await sales.PostAsJsonAsync("/api/v1/agency/crm/contacts", new { firstName = "Existing", email = existing })).ReadJsonAsync();
        var fresh = $"new.{Guid.NewGuid():N}@importco.example";
        var csv = "first_name,last_name,email,company,company_domain,lifecycle_stage,consent,tags\r\n" +
                  $"Nina,Import,{fresh},ImportCo,importco.example,mql,yes,vip;webinar\r\n" +
                  $"Existing,Person,{existing},,,,,\r\n" +
                  "Bad,Email,not-an-email,,,,,\r\n" +
                  $"Dupe,Row,{fresh.ToUpperInvariant()},,,,,\r\n" +
                  ",,,,,,,\r\n" +
                  "Weird,Stage,weird@importco.example,,,champion,,\r\n";
        async Task<JsonElement> Import(bool dryRun)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            form.Add(file, "file", "contacts.csv");
            return await (await sales.PostAsync($"/api/v1/agency/crm/contacts/import?dryRun={dryRun}", form)).ReadJsonAsync();
        }

        var dry = await Import(dryRun: true);
        Assert.True(dry.GetProperty("dryRun").GetBoolean());
        Assert.False(await api.WithDbAsync(db => db.Set<CrmContact>().AnyAsync(c => c.NormalizedEmail == fresh.ToUpperInvariant())));

        var result = await Import(dryRun: false);
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal(1, result.GetProperty("skipped").GetInt32());
        Assert.Equal(4, result.GetProperty("failed").GetInt32());
        var rows = result.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("row").GetInt32());
        Assert.Equal("created", rows[2].Str("status"));
        Assert.Equal("skipped", rows[3].Str("status"));
        Assert.Contains("not a valid email", rows[4].GetProperty("errors")[0].GetString());
        Assert.Contains("Duplicate email", rows[5].GetProperty("errors")[0].GetString());
        Assert.Contains("needs a first_name or an email", rows[6].GetProperty("errors")[0].GetString());
        Assert.Contains("Unknown lifecycle stage", rows[7].GetProperty("errors")[0].GetString());
        var contact = await api.WithDbAsync(db => db.Set<CrmContact>().AsNoTracking().FirstAsync(c => c.NormalizedEmail == fresh.ToUpperInvariant()));
        Assert.Equal(LifecycleStage.MarketingQualifiedLead, contact.LifecycleStage);
        Assert.Equal(ConsentStatus.Subscribed, contact.ConsentStatus);
        Assert.Equal(new[] { "vip", "webinar" }, contact.Tags);
        Assert.NotNull(contact.CompanyId);

        // Tag filter + export
        var tagged = await (await sales.GetAsync("/api/v1/agency/crm/contacts?tag=vip")).ReadJsonAsync();
        Assert.Contains(tagged.GetProperty("items").EnumerateArray(), c => c.Str("email") == fresh);
        var export = await sales.GetAsync("/api/v1/agency/crm/contacts/export.csv?tag=webinar");
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        Assert.Contains(fresh, await export.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Overdue_tasks_notify_the_assignee_once()
    {
        var (salesUser, sales) = await api.CreateClientAsync(Role.SalesRep);
        var deal = await CreateDealAsync(sales, "Task deal");
        var task = await (await sales.PostAsJsonAsync("/api/v1/agency/crm/activities", new
        {
            type = "Task", subject = "Send the audit", dealId = deal.GetGuid("id"), dueAt = api.UtcNow().AddHours(1), assigneeUserId = salesUser.Id,
        })).ReadJsonAsync();
        await (await sales.PostAsJsonAsync("/api/v1/agency/crm/activities", new { type = "Task", subject = "No due date", dealId = deal.GetGuid("id") }))
            .ShouldFailAsync(400, "crm.due_required");

        var mine = await (await sales.GetAsync("/api/v1/agency/crm/tasks/mine")).ReadJsonAsync();
        Assert.Contains(mine.GetProperty("items").EnumerateArray(), t => t.GetGuid("id") == task.GetGuid("id") && !t.GetProperty("isOverdue").GetBoolean());

        api.Clock.Advance(TimeSpan.FromHours(3));
        await api.RunJobAsync<CrmTaskReminderJob>();
        api.Clock.Advance(TimeSpan.FromMinutes(10));
        await api.RunJobAsync<CrmTaskReminderJob>();
        sales = await api.LoginAsync(salesUser); // the access token expired while the clock moved
        var notices = await api.WithDbAsync(db => db.Set<Notification>().CountAsync(n => n.UserId == salesUser.Id && n.Type == BillingNotificationTypes.TaskOverdue));
        Assert.Equal(1, notices);
        mine = await (await sales.GetAsync("/api/v1/agency/crm/tasks/mine")).ReadJsonAsync();
        Assert.True(mine.GetProperty("items").EnumerateArray().First(t => t.GetGuid("id") == task.GetGuid("id")).GetProperty("isOverdue").GetBoolean());

        var done = await (await sales.PostAsJsonAsync($"/api/v1/agency/crm/activities/{task.GetGuid("id")}/complete",
            new { concurrencyStamp = task.GetGuid("concurrencyStamp") })).ReadJsonAsync();
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("completedAt").ValueKind);
    }

    [Fact]
    public async Task Contacts_and_companies_validate_dedupe_and_score()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var company = await (await sales.PostAsJsonAsync("/api/v1/agency/crm/companies", new
        {
            name = "Scored Co", domain = "https://www.scored-co.example", industry = "saas", size = "Medium", countryCode = "gb",
            customFields = "{\"employees\": 120, \"crm\": \"hubspot\"}", tags = new[] { "Enterprise" },
        })).ReadJsonAsync();
        Assert.Equal("scored-co.example", company.Str("domain"));
        await (await sales.PostAsJsonAsync("/api/v1/agency/crm/companies", new { name = "Dup", domain = "scored-co.example" }))
            .ShouldFailAsync(409, "crm.duplicate_domain");
        await (await sales.PostAsJsonAsync("/api/v1/agency/crm/companies", new { name = "Bad", customFields = "[1]" }))
            .ShouldFailAsync(400, "crm.invalid_custom_fields");

        var email = $"score.{Guid.NewGuid():N}@scored-co.example";
        var contact = await (await sales.PostAsJsonAsync("/api/v1/agency/crm/contacts", new
        {
            firstName = "Sam", lastName = "Score", email, companyId = company.GetGuid("id"), budgetRange = "10k-25k", lifecycleStage = "SalesQualifiedLead",
        })).ReadJsonAsync();
        // Fit: industry 15 + size 15 + budget 20 + country 5
        Assert.Equal(55, contact.GetProperty("score").GetInt32());
        Assert.Equal(4, contact.GetProperty("scoreBreakdown").GetArrayLength());
        await (await sales.PostAsJsonAsync("/api/v1/agency/crm/contacts", new { firstName = "Twin", email = email.ToUpperInvariant() }))
            .ShouldFailAsync(409, "crm.duplicate_email");

        // Engagement from another module re-scores the contact once per source key.
        await api.PublishAsync(new ContactEngagementRecorded(email, "email_clicked", "campaign-1:link-3", api.UtcNow()));
        await api.PublishAsync(new ContactEngagementRecorded(email, "email_clicked", "campaign-1:link-3", api.UtcNow()));
        var rescored = await (await sales.GetAsync($"/api/v1/agency/crm/contacts/{contact.GetGuid("id")}")).ReadJsonAsync();
        Assert.Equal(58, rescored.GetProperty("score").GetInt32());
        Assert.Equal(1, rescored.GetProperty("engagement").GetProperty("email_clicked").GetInt32());

        var view = await (await sales.PostAsJsonAsync("/api/v1/agency/crm/views", new
        {
            name = "Hot SQLs", entity = "contacts", filters = new Dictionary<string, string> { ["lifecycleStage"] = "SalesQualifiedLead", ["minScore"] = "50" },
        })).ReadJsonAsync();
        var views = await (await sales.GetAsync("/api/v1/agency/crm/views?entity=contacts")).ReadJsonAsync();
        Assert.Contains(views.EnumerateArray(), v => v.GetGuid("id") == view.GetGuid("id") && v.GetProperty("mine").GetBoolean());
        var dashboard = await (await sales.GetAsync("/api/v1/agency/crm/dashboard")).ReadJsonAsync();
        Assert.True(dashboard.GetProperty("pipeline").GetArrayLength() >= 6);
    }
}
