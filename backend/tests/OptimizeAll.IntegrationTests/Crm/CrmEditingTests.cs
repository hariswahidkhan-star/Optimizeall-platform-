using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Crm.CrmBillingKit;

namespace OptimizeAll.IntegrationTests.Crm;

/// <summary>Archive/restore, bulk actions, saved-view editing, option lists, proposal delete/duplicate and templates.</summary>
public sealed class CrmEditingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Crm = "/api/v1/agency/crm";

    private static async Task<JsonElement> ContactAsync(HttpClient client, string? email = null) =>
        await (await client.PostAsJsonAsync($"{Crm}/contacts", new
        {
            firstName = "Arch", lastName = "Ive", email = email ?? $"arch.{Guid.NewGuid():N}@example.test",
        })).ReadJsonAsync();

    private static bool ListContains(JsonElement page, Guid id) =>
        page.GetProperty("items").EnumerateArray().Any(i => i.GetGuid("id") == id);

    [Fact]
    public async Task Contacts_archive_and_restore_with_read_only_archive_and_concurrency()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var contact = await ContactAsync(sales);
        var id = contact.GetGuid("id");

        var archived = await (await sales.PostAsJsonAsync($"{Crm}/contacts/{id}/archive", new { concurrencyStamp = contact.GetGuid("concurrencyStamp") }))
            .ReadJsonAsync();
        Assert.NotEqual(JsonValueKind.Null, archived.GetProperty("archivedAt").ValueKind);

        // Hidden from the default list, shown in the archived view.
        Assert.False(ListContains(await (await sales.GetAsync($"{Crm}/contacts?search=Arch&pageSize=200")).ReadJsonAsync(), id));
        Assert.True(ListContains(await (await sales.GetAsync($"{Crm}/contacts?archived=true&pageSize=200")).ReadJsonAsync(), id));

        // The old stamp is stale; archived records are read-only until restored.
        await (await sales.PostAsJsonAsync($"{Crm}/contacts/{id}/restore", new { concurrencyStamp = contact.GetGuid("concurrencyStamp") }))
            .ShouldFailAsync(409, "concurrency.conflict");
        await (await sales.PutAsJsonAsync($"{Crm}/contacts/{id}", new
        {
            firstName = "Changed", lifecycleStage = "Lead", consentStatus = "Unknown", concurrencyStamp = archived.GetGuid("concurrencyStamp"),
        })).ShouldFailAsync(409, "crm.archived");
        await (await sales.PostAsJsonAsync($"{Crm}/contacts/{id}/archive", new { concurrencyStamp = archived.GetGuid("concurrencyStamp") }))
            .ShouldFailAsync(409, "crm.already_archived");

        var restored = await (await sales.PostAsJsonAsync($"{Crm}/contacts/{id}/restore", new { concurrencyStamp = archived.GetGuid("concurrencyStamp") }))
            .ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, restored.GetProperty("archivedAt").ValueKind);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "crm.contact_archived" && a.EntityId == id.ToString())));
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "crm.contact_restored" && a.EntityId == id.ToString())));

        // Validation, permissions and unknown ids.
        await (await sales.PostAsJsonAsync($"{Crm}/contacts/{id}/archive", new { })).ShouldFailAsync(400);
        var (_, strategist) = await api.CreateClientAsync(Role.Strategist); // crm.view only
        await (await strategist.PostAsJsonAsync($"{Crm}/contacts/{id}/archive", new { concurrencyStamp = restored.GetGuid("concurrencyStamp") }))
            .ShouldFailAsync(403);
        await (await sales.PostAsJsonAsync($"{Crm}/contacts/{Guid.NewGuid()}/archive", new { concurrencyStamp = Guid.NewGuid() })).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Archived_deals_leave_the_board_and_client_companies_cannot_be_archived()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var deal = await (await sales.PostAsJsonAsync($"{Crm}/deals", new { title = "Archive me", value = 500m, currency = "USD", source = "Outbound" }))
            .ReadJsonAsync();
        var dealId = deal.GetGuid("id");
        await (await sales.PostAsJsonAsync($"{Crm}/deals/{dealId}/archive", new { concurrencyStamp = deal.GetGuid("concurrencyStamp") })).ReadJsonAsync();
        var board = await (await sales.GetAsync($"{Crm}/deals/board")).ReadJsonAsync();
        Assert.DoesNotContain(board.GetProperty("columns").EnumerateArray().SelectMany(c => c.GetProperty("deals").EnumerateArray()),
            d => d.GetGuid("id") == dealId);
        var archivedDeal = await (await sales.GetAsync($"{Crm}/deals/{dealId}")).ReadJsonAsync();
        var stages = await (await sales.GetAsync($"{Crm}/stages")).ReadJsonAsync();
        await (await sales.PostAsJsonAsync($"{Crm}/deals/{dealId}/move", new
        {
            stageId = stages.EnumerateArray().First(s => s.Str("kind") == "Open").GetGuid("id"), concurrencyStamp = archivedDeal.GetGuid("concurrencyStamp"),
        })).ShouldFailAsync(409, "crm.archived");

        var client = await api.CreateClientAccountAsync();
        var company = await (await sales.PostAsJsonAsync($"{Crm}/companies", new { name = "Client Co " + Guid.NewGuid().ToString("N")[..6] })).ReadJsonAsync();
        await api.WithDbAsync(async db =>
        {
            var row = await db.Set<Domain.Crm.CrmCompany>().FirstAsync(c => c.Id == company.GetGuid("id"));
            row.ClientAccountId = client.Id;
            await db.SaveChangesAsync();
        });
        var fresh = await (await sales.GetAsync($"{Crm}/companies/{company.GetGuid("id")}")).ReadJsonAsync();
        await (await sales.PostAsJsonAsync($"{Crm}/companies/{company.GetGuid("id")}/archive", new { concurrencyStamp = fresh.GetGuid("concurrencyStamp") }))
            .ShouldFailAsync(409, "crm.company_is_client");
    }

    [Fact]
    public async Task Bulk_actions_update_many_records_skip_non_applicable_ones_and_validate_input()
    {
        var (salesUser, sales) = await api.CreateClientAsync(Role.SalesRep);
        var a = await ContactAsync(sales);
        var b = await ContactAsync(sales);
        var ids = new[] { a.GetGuid("id"), b.GetGuid("id"), Guid.NewGuid() };

        var owner = await (await sales.PostAsJsonAsync($"{Crm}/contacts/bulk", new { ids, action = "assignOwner", ownerUserId = salesUser.Id })).ReadJsonAsync();
        Assert.Equal(3, owner.GetProperty("requested").GetInt32());
        Assert.Equal(2, owner.GetProperty("updated").GetInt32());
        Assert.Equal(1, owner.GetProperty("notFound").GetInt32());

        var tagged = await (await sales.PostAsJsonAsync($"{Crm}/contacts/bulk", new { ids, action = "addTag", tag = "VIP" })).ReadJsonAsync();
        Assert.Equal(2, tagged.GetProperty("updated").GetInt32());
        var byTag = await (await sales.GetAsync($"{Crm}/contacts?tag=vip&pageSize=200")).ReadJsonAsync();
        Assert.True(ListContains(byTag, a.GetGuid("id")) && ListContains(byTag, b.GetGuid("id")));

        var staged = await (await sales.PostAsJsonAsync($"{Crm}/contacts/bulk", new { ids, action = "setLifecycle", lifecycleStage = "SalesQualifiedLead" }))
            .ReadJsonAsync();
        Assert.Equal(2, staged.GetProperty("updated").GetInt32());
        var archived = await (await sales.PostAsJsonAsync($"{Crm}/contacts/bulk", new { ids, action = "archive" })).ReadJsonAsync();
        Assert.Equal(2, archived.GetProperty("updated").GetInt32());
        // Archived contacts are skipped by other actions and restored in bulk.
        var skipped = await (await sales.PostAsJsonAsync($"{Crm}/contacts/bulk", new { ids, action = "removeTag", tag = "vip" })).ReadJsonAsync();
        Assert.Equal(0, skipped.GetProperty("updated").GetInt32());
        Assert.Equal(2, (await (await sales.PostAsJsonAsync($"{Crm}/contacts/bulk", new { ids, action = "restore" })).ReadJsonAsync())
            .GetProperty("updated").GetInt32());
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(x => x.Action == "crm.contacts_bulk_archive")));

        await (await sales.PostAsJsonAsync($"{Crm}/contacts/bulk", new { ids, action = "setLifecycle" })).ShouldFailAsync(400, "crm.invalid_bulk");
        await (await sales.PostAsJsonAsync($"{Crm}/contacts/bulk", new { ids, action = "addTag", tag = "  " })).ShouldFailAsync(400, "crm.invalid_bulk");
        await (await sales.PostAsJsonAsync($"{Crm}/contacts/bulk", new { ids, action = "explode" })).ShouldFailAsync(400);
        await (await sales.PostAsJsonAsync($"{Crm}/contacts/bulk", new { ids = Array.Empty<Guid>(), action = "archive" })).ShouldFailAsync(400);
        await (await sales.PostAsJsonAsync($"{Crm}/deals/bulk", new { ids, action = "addTag", tag = "x" })).ShouldFailAsync(400, "crm.invalid_bulk");
        var (_, strategist) = await api.CreateClientAsync(Role.Strategist);
        await (await strategist.PostAsJsonAsync($"{Crm}/contacts/bulk", new { ids, action = "archive" })).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Saved_views_can_be_renamed_shared_and_refiltered_by_their_owner_only()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var view = await (await sales.PostAsJsonAsync($"{Crm}/views", new { name = "Mine", entity = "contacts", filters = new { tag = "vip" }, shared = false }))
            .ReadJsonAsync();
        var updated = await (await sales.PutAsJsonAsync($"{Crm}/views/{view.GetGuid("id")}", new
        {
            name = "Hot leads", shared = true, filters = new { lifecycleStage = "SalesQualifiedLead" },
        })).ReadJsonAsync();
        Assert.Equal("Hot leads", updated.Str("name"));
        Assert.True(updated.GetProperty("shared").GetBoolean());
        Assert.Equal("SalesQualifiedLead", updated.GetProperty("filters").GetProperty("lifecycleStage").GetString());

        var (_, other) = await api.CreateClientAsync(Role.SalesRep);
        var visible = await (await other.GetAsync($"{Crm}/views?entity=contacts")).ReadJsonAsync();
        Assert.Contains(visible.EnumerateArray(), v => v.GetGuid("id") == view.GetGuid("id") && !v.GetProperty("mine").GetBoolean());
        await (await other.PutAsJsonAsync($"{Crm}/views/{view.GetGuid("id")}", new { name = "Taken", shared = false })).ShouldFailAsync(404);
        await (await sales.PutAsJsonAsync($"{Crm}/views/{view.GetGuid("id")}", new { name = "", shared = false })).ShouldFailAsync(400);
    }

    [Fact]
    public async Task Crm_option_lists_are_editable_versioned_and_validated()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var options = await (await sales.GetAsync($"{Crm}/options")).ReadJsonAsync();
        Assert.Contains(options.GetProperty("lostReasons").EnumerateArray(), r => r.GetString() == "Budget");

        var saved = await (await sales.PutAsJsonAsync($"{Crm}/options", new
        {
            lostReasons = new[] { "Budget", " budget ", "Went with in-house team", "" },
            budgetRanges = new[] { "<2k", "2k-5k" },
            industries = new[] { "Dentistry" },
            version = options.Str("version"),
        })).ReadJsonAsync();
        Assert.Equal(new[] { "Budget", "Went with in-house team" }, saved.GetProperty("lostReasons").EnumerateArray().Select(r => r.GetString()));
        Assert.NotEqual(options.Str("version"), saved.Str("version"));

        // Saving over someone else's change is refused.
        await (await sales.PutAsJsonAsync($"{Crm}/options", new
        {
            lostReasons = new[] { "Budget" }, budgetRanges = Array.Empty<string>(), industries = Array.Empty<string>(), version = options.Str("version"),
        })).ShouldFailAsync(409, "concurrency.conflict");
        await (await sales.PutAsJsonAsync($"{Crm}/options", new
        {
            lostReasons = Array.Empty<string>(), budgetRanges = Array.Empty<string>(), industries = Array.Empty<string>(), version = saved.Str("version"),
        })).ShouldFailAsync(400, "crm.invalid_options");
        var (_, strategist) = await api.CreateClientAsync(Role.Strategist);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await strategist.GetAsync($"{Crm}/options")).StatusCode);
        await (await strategist.PutAsJsonAsync($"{Crm}/options", new
        {
            lostReasons = new[] { "x" }, budgetRanges = Array.Empty<string>(), industries = Array.Empty<string>(), version = saved.Str("version"),
        })).ShouldFailAsync(403);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "crm.options_updated")));
    }

    private object ProposalBody(string title = "Retainer") => new
    {
        title, validUntil = api.Today().AddDays(14).Iso(), recipientEmail = "buyer@example.test",
        lines = new[] { Line("SEO retainer", 1, 1500m, "Monthly", serviceSlug: "seo") },
    };

    [Fact]
    public async Task Unsent_proposals_can_be_deleted_sent_ones_only_withdrawn_and_any_can_be_duplicated()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var draft = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", ProposalBody("Draft one"))).ReadJsonAsync();
        var draftId = draft.GetGuid("id");

        await (await sales.DeleteAsync($"/api/v1/agency/proposals/{draftId}?concurrencyStamp={Guid.NewGuid()}")).ShouldFailAsync(409, "concurrency.conflict");
        var (_, designer) = await api.CreateClientAsync(Role.Designer);
        await (await designer.DeleteAsync($"/api/v1/agency/proposals/{draftId}?concurrencyStamp={draft.GetGuid("concurrencyStamp")}")).ShouldFailAsync(403);
        Assert.Equal(System.Net.HttpStatusCode.NoContent,
            (await sales.DeleteAsync($"/api/v1/agency/proposals/{draftId}?concurrencyStamp={draft.GetGuid("concurrencyStamp")}")).StatusCode);
        await (await sales.GetAsync($"/api/v1/agency/proposals/{draftId}")).ShouldFailAsync(404);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "crm.proposal_deleted" && a.EntityId == draftId.ToString())));

        var sentDraft = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", ProposalBody("Sent one"))).ReadJsonAsync();
        var sent = (await (await sales.PostAsJsonAsync($"/api/v1/agency/proposals/{sentDraft.GetGuid("id")}/send",
            new { concurrencyStamp = sentDraft.GetGuid("concurrencyStamp"), email = false })).ReadJsonAsync()).GetProperty("proposal");
        await (await sales.DeleteAsync($"/api/v1/agency/proposals/{sent.GetGuid("id")}?concurrencyStamp={sent.GetGuid("concurrencyStamp")}"))
            .ShouldFailAsync(409, "proposal.not_deletable");

        var copy = await (await sales.PostAsync($"/api/v1/agency/proposals/{sent.GetGuid("id")}/duplicate", null)).ReadJsonAsync();
        Assert.NotEqual(sent.GetGuid("id"), copy.GetGuid("id"));
        Assert.NotEqual(sent.Str("number"), copy.Str("number"));
        Assert.Equal("Draft", copy.Str("status"));
        Assert.Equal("Copy of Sent one", copy.Str("title"));
        Assert.Equal(1, copy.GetProperty("version").GetProperty("lines").GetArrayLength());
        await (await sales.PostAsync($"/api/v1/agency/proposals/{Guid.NewGuid()}/duplicate", null)).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Proposal_templates_crud_with_validation_uniqueness_and_concurrency()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var name = "Template " + Guid.NewGuid().ToString("N")[..6];
        var body = new
        {
            name, currency = "USD", validForDays = 21, scope = "SEO and content", sortOrder = 5, isActive = true,
            lines = new[] { Line("Monthly SEO", 1, 1200m, "Monthly", serviceSlug: "seo") },
        };
        var created = await (await sales.PostAsJsonAsync("/api/v1/agency/proposal-templates", body)).ReadJsonAsync();
        Assert.Equal(21, created.GetProperty("validForDays").GetInt32());
        Assert.Equal("Monthly", created.GetProperty("lines")[0].GetProperty("recurrence").GetString());
        await (await sales.PostAsJsonAsync("/api/v1/agency/proposal-templates", body)).ShouldFailAsync(409, "crm.duplicate_template");
        await (await sales.PostAsJsonAsync("/api/v1/agency/proposal-templates", new
        {
            name = "Bad lines " + Guid.NewGuid().ToString("N")[..6], currency = "USD", validForDays = 30, isActive = true,
            lines = new[] { Line("Negative", 1, -5m) },
        })).ShouldFailAsync(400);

        var id = created.GetGuid("id");
        var updated = await (await sales.PutAsJsonAsync($"/api/v1/agency/proposal-templates/{id}", new
        {
            name, currency = "USD", validForDays = 30, isActive = false, sortOrder = 5, lines = Array.Empty<object>(),
            concurrencyStamp = created.GetGuid("concurrencyStamp"),
        })).ReadJsonAsync();
        Assert.False(updated.GetProperty("isActive").GetBoolean());
        await (await sales.PutAsJsonAsync($"/api/v1/agency/proposal-templates/{id}", new
        {
            name, currency = "USD", validForDays = 30, isActive = true, lines = Array.Empty<object>(), concurrencyStamp = created.GetGuid("concurrencyStamp"),
        })).ShouldFailAsync(409, "concurrency.conflict");

        var active = await (await sales.GetAsync("/api/v1/agency/proposal-templates")).ReadJsonAsync();
        Assert.DoesNotContain(active.EnumerateArray(), t => t.GetGuid("id") == id);
        var all = await (await sales.GetAsync("/api/v1/agency/proposal-templates?includeInactive=true")).ReadJsonAsync();
        Assert.Contains(all.EnumerateArray(), t => t.GetGuid("id") == id);

        var (_, strategist) = await api.CreateClientAsync(Role.Strategist); // no proposals.manage
        await (await strategist.GetAsync("/api/v1/agency/proposal-templates")).ShouldFailAsync(403);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await sales.DeleteAsync($"/api/v1/agency/proposal-templates/{id}")).StatusCode);
        await (await sales.DeleteAsync($"/api/v1/agency/proposal-templates/{id}")).ShouldFailAsync(404);
    }
}
