using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Auth;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Codes.CodesKit;

namespace OptimizeAll.IntegrationTests.Codes;

/// <summary>Discount-code programs, payout rules, codes (add / CSV import / generate), statuses and assignment.</summary>
public sealed class CodeProgramsAndCodesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly CodesKit kit = new(api);

    // ---------------------------------------------------------------- programs

    [Fact]
    public async Task Program_lifecycle_update_payout_status_and_audit()
    {
        var (_, manager) = await kit.ManagerAsync();
        var created = await manager.PostAsJsonAsync("/api/v1/admin/code-programs", kit.ProgramBody(activate: false,
            tiers: new object[] { new { thresholdSales = 5, percent = 12m, bonusAmount = 25m } }, daily: 100m, budget: 5000m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var program = await created.ReadJsonAsync();
        var id = Id(program);
        Assert.Equal("Draft", program.GetProperty("status").GetString());
        Assert.Equal(1, program.GetProperty("payoutVersion").GetInt32());
        Assert.Contains("10% of net", program.GetProperty("payoutSummary").GetString());
        Assert.Contains("budget 5,000.00 USD", program.GetProperty("payoutSummary").GetString());
        Assert.Single(program.GetProperty("tiers").EnumerateArray());

        // Metadata edit with the stamp; a stale stamp is a 409.
        var body = kit.ProgramBody();
        body["name"] = "Renamed program";
        body["concurrencyStamp"] = Stamp(program);
        var updated = await (await manager.PutAsJsonAsync($"/api/v1/admin/code-programs/{id}", body)).ReadJsonAsync();
        Assert.Equal("Renamed program", updated.GetProperty("name").GetString());
        await (await manager.PutAsJsonAsync($"/api/v1/admin/code-programs/{id}", body)).ShouldFailAsync(409, "concurrency.conflict");

        // Payout rules: confirm required, then saved with a new version and audited with the reason.
        var payout = new Dictionary<string, object?>
        {
            ["payoutType"] = "FlatPerSale", ["flatAmount"] = 7.5m, ["tiers"] = Array.Empty<object>(), ["reason"] = "Brand raised the commission",
            ["confirm"] = false, ["concurrencyStamp"] = Stamp(updated),
        };
        await (await manager.PutAsJsonAsync($"/api/v1/admin/code-programs/{id}/payout", payout)).ShouldFailAsync(400, "confirmation.required");
        payout["confirm"] = true;
        var repriced = await (await manager.PutAsJsonAsync($"/api/v1/admin/code-programs/{id}/payout", payout)).ReadJsonAsync();
        Assert.Equal(2, repriced.GetProperty("payoutVersion").GetInt32());
        Assert.Equal("FlatPerSale", repriced.GetProperty("payoutType").GetString());
        Assert.Empty(repriced.GetProperty("tiers").EnumerateArray());

        var active = await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/status",
            new { status = "Active", concurrencyStamp = Stamp(repriced) })).ReadJsonAsync();
        Assert.Equal("Active", active.GetProperty("status").GetString());
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/status",
            new { status = "Draft", concurrencyStamp = Stamp(active) })).ShouldFailAsync(400, "code_program.invalid_status");

        var actions = await api.WithDbAsync(db => db.Set<AuditLog>().Where(a => a.EntityId == id.ToString()).Select(a => new { a.Action, a.Reason }).ToListAsync());
        Assert.Contains(actions, a => a.Action == "code_program.created");
        Assert.Contains(actions, a => a.Action == "code_program.payout_changed" && a.Reason == "Brand raised the commission");
        Assert.Contains(actions, a => a.Action == "code_program.status_changed");

        // List + search.
        var list = await (await manager.GetAsync("/api/v1/admin/code-programs?search=Renamed")).ReadJsonAsync();
        Assert.Contains(list.GetProperty("items").EnumerateArray(), p => Id(p) == id);
    }

    [Fact]
    public async Task Invalid_programs_are_problem_details_and_undefined_enums_are_rejected()
    {
        var (_, manager) = await kit.ManagerAsync();
        async Task Fails(Action<Dictionary<string, object?>> change, string? code = null)
        {
            var body = kit.ProgramBody();
            change(body);
            var response = await manager.PostAsJsonAsync("/api/v1/admin/code-programs", body);
            await response.ShouldFailAsync(400, code);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(problem.TryGetProperty("traceId", out _));
        }
        await Fails(b => b["currency"] = "XYZ", "code_program.currency_unsupported");
        await Fails(b => b["endsAt"] = kit.Now.AddDays(-40), "code_program.invalid_window");
        await Fails(b => b["storeUrl"] = "javascript:alert(1)", "code_program.invalid_store_url");
        await Fails(b => ((Dictionary<string, object?>)b["payout"]!)["percent"] = 150m);
        await Fails(b => ((Dictionary<string, object?>)b["payout"]!)["payoutType"] = "Hologram");
        await Fails(b => ((Dictionary<string, object?>)b["payout"]!)["payoutType"] = 7);
        await Fails(b => b["payout"] = new Dictionary<string, object?> { ["payoutType"] = "FlatPerSale", ["percent"] = 10m, ["tiers"] = Array.Empty<object>() },
            "code_program.invalid_payout");
        await Fails(b => ((Dictionary<string, object?>)b["payout"]!)["tiers"] = new object[] { new { thresholdSales = 3 } }, "code_program.invalid_payout");
        await Fails(b => b["startsAt"] = "not-a-date");
        await (await manager.GetAsync($"/api/v1/admin/code-programs/{Guid.NewGuid()}")).ShouldFailAsync(404);
        await (await manager.GetAsync("/api/v1/admin/code-programs?status=Weird")).ShouldFailAsync(400);
    }

    [Fact]
    public async Task Permissions_are_enforced_for_every_role()
    {
        var (_, manager) = await kit.ManagerAsync();
        var program = await kit.ProgramAsync(manager);
        var id = Id(program);
        var (_, participant) = await kit.ParticipantAsync();
        var (_, reviewer) = await kit.ReviewerAsync();
        var (_, finance) = await kit.FinanceAsync();

        foreach (var client in new[] { participant, reviewer })
        {
            await (await client.GetAsync("/api/v1/admin/code-programs")).ShouldFailAsync(403);
            await (await client.GetAsync($"/api/v1/admin/code-programs/{id}/codes")).ShouldFailAsync(403);
            await (await client.PostAsJsonAsync("/api/v1/admin/code-programs", kit.ProgramBody())).ShouldFailAsync(403);
            await (await client.GetAsync($"/api/v1/admin/code-reports?programId={id}")).ShouldFailAsync(403);
        }
        await (await participant.GetAsync("/api/v1/admin/code-sales")).ShouldFailAsync(403);
        (await reviewer.GetAsync("/api/v1/admin/code-sales")).EnsureSuccessStatusCode(); // sales.review reads the queue
        // Finance reads programs (codes.view) but can't manage them or assign codes.
        (await finance.GetAsync($"/api/v1/admin/code-programs/{id}")).EnsureSuccessStatusCode();
        await (await finance.PostAsJsonAsync("/api/v1/admin/code-programs", kit.ProgramBody())).ShouldFailAsync(403);
        var code = await CodeAsync(manager, id);
        await (await PostAssignAsync(finance, Id(code), Guid.NewGuid())).ShouldFailAsync(403);
        // Anonymous: 401.
        var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/code-programs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/me/codes")).StatusCode);

        // A custom role with only codes.view can read but not write.
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var role = await (await admin.PostAsJsonAsync("/api/v1/admin/roles", new
        {
            name = "Code auditor " + Guid.NewGuid().ToString("N")[..5], description = "Reads discount codes", permissions = new[] { "codes.view" },
        })).ReadJsonAsync();
        var auditor = await api.CreateUserAsync(new[] { Role.Reviewer });
        var roleId = role.GetProperty("role").GetProperty("id").GetGuid();
        (await admin.PutAsync($"/api/v1/admin/roles/{roleId}/users/{auditor.Id}", null)).EnsureSuccessStatusCode();
        var auditorClient = await api.LoginAsync(auditor);
        (await auditorClient.GetAsync($"/api/v1/admin/code-programs/{id}")).EnsureSuccessStatusCode();
        await (await auditorClient.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes", new { code = "NOPE1" })).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Money_affecting_writes_are_denied_while_impersonating_but_reads_work()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var (managerUser, manager) = await kit.ManagerAsync();
        var program = await kit.ProgramAsync(manager);
        var code = await CodeAsync(manager, Id(program));
        var token = await Impersonating.TokenAsync(admin, managerUser.Id);
        (await Impersonating.SendAsync(admin, HttpMethod.Get, $"/api/v1/admin/code-programs/{Id(program)}", token)).EnsureSuccessStatusCode();
        await (await Impersonating.SendAsync(admin, HttpMethod.Put, $"/api/v1/admin/code-programs/{Id(program)}/payout", token, new
        {
            payoutType = "FlatPerSale", flatAmount = 99m, reason = "Should be refused", confirm = true, concurrencyStamp = Stamp(program),
        })).ShouldFailAsync(403, "auth.impersonation_forbidden_action");
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/admin/discount-codes/{Id(code)}/assign", token, new
        {
            target = "Person", userId = managerUser.Id, reason = "Should be refused",
        })).ShouldFailAsync(403, "auth.impersonation_forbidden_action");
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/admin/code-programs/{Id(program)}/codes", token, new { code = "IMP1" }))
            .ShouldFailAsync(403, "auth.impersonation_forbidden_action");
    }

    // ---------------------------------------------------------------- codes

    [Fact]
    public async Task Codes_are_unique_per_program_case_insensitively_and_validated()
    {
        var (_, manager) = await kit.ManagerAsync();
        var program = await kit.ProgramAsync(manager);
        var id = Id(program);
        var code = await CodeAsync(manager, id, "Glow-Sara");
        Assert.Equal("Available", code.GetProperty("status").GetString());
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes", new { code = "GLOW-SARA" })).ShouldFailAsync(409, "code.duplicate");
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes", new { code = "has space" })).ShouldFailAsync(400, "code.invalid");
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes",
            new { code = "WIN1", validFrom = kit.Now, validTo = kit.Now.AddDays(-1) })).ShouldFailAsync(400, "code.invalid_window");
        // The same code may exist in another program.
        var other = await kit.ProgramAsync(manager);
        await CodeAsync(manager, Id(other), "GLOW-SARA");

        var search = await (await manager.GetAsync($"/api/v1/admin/code-programs/{id}/codes?search=sara")).ReadJsonAsync();
        Assert.Equal(1, search.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Csv_import_dry_run_reports_lines_then_imports_and_refuses_duplicates()
    {
        var (_, manager) = await kit.ManagerAsync();
        var program = await kit.ProgramAsync(manager);
        var id = Id(program);
        await CodeAsync(manager, id, "EXISTING1");
        var (person, _) = await kit.ParticipantAsync();
        var csv = "Coupon Code,Expires,Note,Email\r\n" +
                  "GLOW-001,2099-12-31,first,\r\n" +
                  "\r\n" +
                  "glow-001,,dup in file,\r\n" +
                  "EXISTING1,,,\r\n" +
                  "bad code!,,,\r\n" +
                  $"GLOW-002,,,{person.Email}\r\n" +
                  "GLOW-003,not-a-date,,\r\n" +
                  "GLOW-004,,,nobody@example.test\r\n";
        var dry = await (await manager.PostAsync($"/api/v1/admin/code-programs/{id}/codes/import?dryRun=true", Csv(csv, "brand.csv"))).ReadJsonAsync();
        Assert.True(dry.GetProperty("dryRun").GetBoolean());
        Assert.Equal(7, dry.GetProperty("rows").GetInt32());
        Assert.Equal(2, dry.GetProperty("valid").GetInt32());
        Assert.Equal(0, dry.GetProperty("created").GetInt32());
        var rejected = dry.GetProperty("rejected").EnumerateArray().ToList();
        // Line numbers are spreadsheet lines (the blank line 3 still counts).
        Assert.Contains(rejected, r => r.GetProperty("row").GetInt32() == 4 && r.GetProperty("code").GetString() == "code.duplicate_in_file");
        Assert.Contains(rejected, r => r.GetProperty("row").GetInt32() == 5 && r.GetProperty("code").GetString() == "code.duplicate");
        Assert.Contains(rejected, r => r.GetProperty("row").GetInt32() == 6 && r.GetProperty("code").GetString() == "code.invalid");
        Assert.Contains(rejected, r => r.GetProperty("row").GetInt32() == 8 && r.GetProperty("code").GetString() == "csv.invalid_date");
        Assert.Contains(rejected, r => r.GetProperty("row").GetInt32() == 9 && r.GetProperty("code").GetString() == "user.not_found");
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<DiscountCode>().CountAsync(c => c.ProgramId == id)));

        var real = await (await manager.PostAsync($"/api/v1/admin/code-programs/{id}/codes/import", Csv(csv, "brand.csv"))).ReadJsonAsync();
        Assert.Equal(2, real.GetProperty("created").GetInt32());
        var codes = await api.WithDbAsync(db => db.Set<DiscountCode>().AsNoTracking().Where(c => c.ProgramId == id).ToListAsync());
        Assert.Equal(3, codes.Count);
        Assert.Equal(DiscountCodeStatus.Assigned, codes.Single(c => c.NormalizedCode == "GLOW-002").Status);
        Assert.Equal(DiscountCodeSource.Import, codes.Single(c => c.NormalizedCode == "GLOW-001").Source);
        Assert.NotNull(codes.Single(c => c.NormalizedCode == "GLOW-001").ValidTo);
        Assert.True(await api.WithDbAsync(db => db.Set<CodeImportBatch>().AnyAsync(b => b.ProgramId == id && b.Created == 2)));

        // Importing the same file again creates nothing: every code is a duplicate.
        var again = await (await manager.PostAsync($"/api/v1/admin/code-programs/{id}/codes/import", Csv(csv))).ReadJsonAsync();
        Assert.Equal(0, again.GetProperty("created").GetInt32());
        Assert.Equal(3, again.GetProperty("duplicates").GetInt32());

        await (await manager.PostAsync($"/api/v1/admin/code-programs/{id}/codes/import", Csv(""))).ShouldFailAsync(400, "csv.empty");
        await (await manager.PostAsync($"/api/v1/admin/code-programs/{id}/codes/import", Csv("code\r\n\"unterminated"))).ShouldFailAsync(400, "csv.invalid");
    }

    [Fact]
    public async Task Large_code_import_is_batched()
    {
        var (_, manager) = await kit.ManagerAsync();
        var program = await kit.ProgramAsync(manager);
        var id = Id(program);
        var csv = "code\n" + string.Join("\n", Enumerable.Range(1, 3000).Select(i => $"BULK-{i:D5}"));
        var result = await (await manager.PostAsync($"/api/v1/admin/code-programs/{id}/codes/import", Csv(csv))).ReadJsonAsync();
        Assert.Equal(3000, result.GetProperty("created").GetInt32());
        Assert.Equal(3000, await api.WithDbAsync(db => db.Set<DiscountCode>().CountAsync(c => c.ProgramId == id)));
    }

    [Fact]
    public async Task Generated_codes_follow_the_pattern_and_are_unique()
    {
        var (_, manager) = await kit.ManagerAsync();
        var program = await kit.ProgramAsync(manager);
        var id = Id(program);
        var preview = await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes/generate?dryRun=true",
            new { pattern = "GLOW-????-##", count = 50 })).ReadJsonAsync();
        Assert.Equal(0, preview.GetProperty("created").GetInt32());
        Assert.All(preview.GetProperty("sample").EnumerateArray(), c => Assert.Matches("^GLOW-[A-Z]{4}-[0-9]{2}$", c.GetString()!));
        var generated = await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes/generate",
            new { pattern = "GLOW-????-##", count = 50 })).ReadJsonAsync();
        Assert.Equal(50, generated.GetProperty("created").GetInt32());
        var codes = await api.WithDbAsync(db => db.Set<DiscountCode>().Where(c => c.ProgramId == id).Select(c => c.NormalizedCode).ToListAsync());
        Assert.Equal(50, codes.Distinct().Count());
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes/generate", new { pattern = "AB##", count = 10 }))
            .ShouldFailAsync(400, "code.invalid_pattern");
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes/generate", new { pattern = "####", count = 5000 }))
            .ShouldFailAsync(400, "code.pattern_too_small");
    }

    // ---------------------------------------------------------------- status & assignment

    [Fact]
    public async Task Pause_resume_retire_and_expired_codes()
    {
        var (_, manager) = await kit.ManagerAsync();
        var program = await kit.ProgramAsync(manager);
        var code = await CodeAsync(manager, Id(program));
        var (user, _) = await kit.ParticipantAsync();
        var paused = await (await manager.PutAsJsonAsync($"/api/v1/admin/discount-codes/{Id(code)}",
            new { status = "Paused", concurrencyStamp = Stamp(code), reason = "Brand asked" })).ReadJsonAsync();
        Assert.Equal("Paused", paused.GetProperty("status").GetString());
        await (await PostAssignAsync(manager, Id(code), user.Id)).ShouldFailAsync(409, "code.paused");
        var resumed = await (await manager.PutAsJsonAsync($"/api/v1/admin/discount-codes/{Id(code)}",
            new { status = "Available", concurrencyStamp = Stamp(paused) })).ReadJsonAsync();
        Assert.Equal("Available", resumed.GetProperty("status").GetString());
        var assigned = await AssignAsync(manager, Id(code), user.Id);
        Assert.Equal("Assigned", assigned.GetProperty("code").GetProperty("status").GetString());
        var retired = await (await manager.PutAsJsonAsync($"/api/v1/admin/discount-codes/{Id(code)}",
            new { status = "Retired", concurrencyStamp = Stamp(assigned.GetProperty("code")), reason = "Leaked on a coupon site" })).ReadJsonAsync();
        Assert.Equal("Retired", retired.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, retired.GetProperty("assignment").ValueKind);
        await (await manager.PutAsJsonAsync($"/api/v1/admin/discount-codes/{Id(code)}",
            new { status = "Available", concurrencyStamp = Stamp(retired) })).ShouldFailAsync(409, "code.retired");
        await (await manager.PutAsJsonAsync($"/api/v1/admin/discount-codes/{Id(code)}",
            new { status = "Available", concurrencyStamp = Guid.NewGuid() })).ShouldFailAsync(409);

        var expired = await CodeAsync(manager, Id(program), validFrom: kit.Now.AddDays(-10), validTo: kit.Now.AddDays(-1));
        Assert.Equal("Expired", expired.GetProperty("status").GetString());
        await (await PostAssignAsync(manager, Id(expired), user.Id)).ShouldFailAsync(409, "code.expired");
        var list = await (await manager.GetAsync($"/api/v1/admin/code-programs/{Id(program)}/codes?status=Expired")).ReadJsonAsync();
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Assign_reassign_unassign_keep_history_and_audit()
    {
        var (_, manager) = await kit.ManagerAsync();
        var program = await kit.ProgramAsync(manager);
        var code = await CodeAsync(manager, Id(program));
        var (ann, _) = await kit.ParticipantAsync();
        var (ben, _) = await kit.ParticipantAsync();
        var staff = await api.CreateUserAsync(new[] { Role.Reviewer });

        await AssignAsync(manager, Id(code), ann.Id, validFrom: kit.Now.AddDays(-10));
        await (await PostAssignAsync(manager, Id(code), ann.Id)).ShouldFailAsync(409, "code.already_assigned");
        await (await PostAssignAsync(manager, Id(code), ben.Id)).ShouldFailAsync(409, "code.already_assigned");
        await (await PostAssignAsync(manager, Id(code), staff.Id, reassign: true)).ShouldFailAsync(400, "codes.not_participant");
        await (await PostAssignAsync(manager, Id(code), Guid.NewGuid(), reassign: true)).ShouldFailAsync(404);
        await (await PostAssignAsync(manager, Id(code), ben.Id, validFrom: kit.Now, validTo: kit.Now.AddDays(-1), reassign: true))
            .ShouldFailAsync(400, "code_assignment.invalid_window");

        var moved = await AssignAsync(manager, Id(code), ben.Id, reassign: true);
        var history = moved.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal(ben.Id, history[0].GetProperty("person").GetProperty("id").GetGuid());
        Assert.True(history[0].GetProperty("isLive").GetBoolean());
        Assert.False(history[1].GetProperty("isLive").GetBoolean());
        Assert.StartsWith("Reassigned", history[1].GetProperty("endReason").GetString());

        var unassigned = await (await manager.PostAsJsonAsync($"/api/v1/admin/discount-codes/{Id(code)}/unassign",
            new { reason = "Left the program" })).ReadJsonAsync();
        Assert.Equal("Available", unassigned.GetProperty("code").GetProperty("status").GetString());
        await (await manager.PostAsJsonAsync($"/api/v1/admin/discount-codes/{Id(code)}/unassign", new { reason = "Again please" }))
            .ShouldFailAsync(409, "code.not_assigned");

        var actions = await api.WithDbAsync(db => db.Set<AuditLog>().Where(a => a.EntityId == Id(code).ToString()).Select(a => a.Action).ToListAsync());
        Assert.Contains("discount_code.assigned", actions);
        Assert.Contains("discount_code.reassigned", actions);
        Assert.Contains("discount_code.unassigned", actions);
        // The assignee was notified.
        Assert.True(await api.WithDbAsync(db => db.Set<OptimizeAll.Domain.Notifications.Notification>()
            .AnyAsync(n => n.UserId == ann.Id && n.Type == "codes.assigned")));
    }

    [Fact]
    public async Task Shared_group_codes_and_auto_assign_one_code_per_member()
    {
        var (_, manager) = await kit.ManagerAsync();
        var program = await kit.ProgramAsync(manager);
        var id = Id(program);
        var people = new List<TestUser>();
        for (var i = 0; i < 4; i++) people.Add((await kit.ParticipantAsync()).User);
        var group = await GroupAsync(manager, people.Select(p => p.Id).ToArray());

        // One shared code for the whole group.
        var shared = await CodeAsync(manager, id, "TEAM-GLOW");
        var assigned = await AssignAsync(manager, Id(shared), groupId: Id(group));
        Assert.Equal("Group", assigned.GetProperty("code").GetProperty("assignment").GetProperty("target").GetString());

        // An automatic group can't hold codes.
        var auto = await (await manager.PostAsJsonAsync("/api/v1/admin/rate-groups", new
        {
            name = "Auto " + Guid.NewGuid().ToString("N")[..5], membershipMode = "Automatic", autoTiers = new[] { "Gold" },
        })).ReadJsonAsync();
        var another = await CodeAsync(manager, id);
        await (await PostAssignAsync(manager, Id(another), groupId: Id(auto))).ShouldFailAsync(409, "codes.group_automatic");

        // Auto-assign: only three codes left for four members (one already has a personal code).
        var personal = await CodeAsync(manager, id);
        await AssignAsync(manager, Id(personal), people[0].Id);
        await CodeAsync(manager, id);
        await CodeAsync(manager, id);
        // "another" is still available too: 3 available.
        var dry = await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes/auto-assign",
            new { groupId = Id(group), reason = "Launch batch", dryRun = true })).ReadJsonAsync();
        Assert.Equal(4, dry.GetProperty("members").GetInt32());
        Assert.Equal(1, dry.GetProperty("alreadyHadCode").GetInt32());
        Assert.Equal(3, dry.GetProperty("assigned").GetInt32());
        Assert.Equal(0, await api.WithDbAsync(db => db.Set<DiscountCodeAssignment>().CountAsync(a => a.ProgramId == id && a.UserId == people[1].Id)));

        var real = await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes/auto-assign",
            new { groupId = Id(group), reason = "Launch batch" })).ReadJsonAsync();
        Assert.Equal(3, real.GetProperty("assigned").GetInt32());
        foreach (var p in people)
            Assert.Equal(1, await api.WithDbAsync(db => db.Set<DiscountCodeAssignment>()
                .CountAsync(a => a.ProgramId == id && a.UserId == p.Id && a.EndedAt == null)));
        // Everyone has one now: running it again assigns nothing.
        var again = await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes/auto-assign",
            new { groupId = Id(group), reason = "Launch batch" })).ReadJsonAsync();
        Assert.Equal(0, again.GetProperty("assigned").GetInt32());

        // Pool exhausted: a new member with no codes left is reported, not failed.
        var (late, _) = await kit.ParticipantAsync();
        await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-groups/{Id(group)}/members", new { userIds = new[] { late.Id }, note = "late" })).ReadJsonAsync();
        var exhausted = await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/codes/auto-assign",
            new { groupId = Id(group), reason = "Launch batch" })).ReadJsonAsync();
        Assert.Equal(0, exhausted.GetProperty("assigned").GetInt32());
        Assert.Contains(exhausted.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "codes.pool_exhausted");
    }

    [Fact]
    public async Task Payout_overrides_for_people_and_groups()
    {
        var (_, manager) = await kit.ManagerAsync();
        var program = await kit.ProgramAsync(manager);
        var id = Id(program);
        var (person, _) = await kit.ParticipantAsync();
        var group = await GroupAsync(manager, person.Id);
        var withPerson = await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/overrides", new
        {
            target = "Person", userId = person.Id, payoutType = "PercentOfNet", percent = 20m, reason = "Top seller deal",
        })).ReadJsonAsync();
        Assert.Single(withPerson.GetProperty("overrides").EnumerateArray());
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/overrides", new
        {
            target = "Person", userId = person.Id, payoutType = "FlatPerSale", flatAmount = 3m, reason = "Second deal",
        })).ShouldFailAsync(409, "code_override.duplicate");
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/overrides", new
        {
            target = "Group", userId = person.Id, payoutType = "FlatPerSale", flatAmount = 3m, reason = "Wrong target",
        })).ShouldFailAsync(400, "code_override.invalid_target");
        var withGroup = await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/overrides", new
        {
            target = "Group", groupId = Id(group), payoutType = "FlatPerSale", flatAmount = 4m, reason = "VIP group rate",
        })).ReadJsonAsync();
        var overrideId = withGroup.GetProperty("overrides").EnumerateArray().First(o => o.GetProperty("target").GetString() == "Person").GetProperty("id").GetGuid();
        var ended = await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{id}/overrides/{overrideId}/end",
            new { reason = "Deal finished" })).ReadJsonAsync();
        Assert.Contains(ended.GetProperty("overrides").EnumerateArray(), o => o.GetProperty("id").GetGuid() == overrideId && !o.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Fx_missing_for_settlement_refuses_the_program()
    {
        var (_, manager) = await kit.ManagerAsync();
        // No KWD→USD (or USD→KWD) rate in a fresh database: commissions could never be paid out.
        var hasRate = await api.WithDbAsync(db => db.Set<OptimizeAll.Domain.Ledger.ExchangeRate>()
            .AnyAsync(r => (r.BaseCurrency == "KWD" && r.QuoteCurrency == "USD") || (r.BaseCurrency == "USD" && r.QuoteCurrency == "KWD")));
        if (!hasRate)
            await (await manager.PostAsJsonAsync("/api/v1/admin/code-programs", kit.ProgramBody(currency: "KWD"))).ShouldFailAsync(409, "fx.rate_missing");
        await kit.AddExchangeRateAsync("KWD", "USD", 3.25m);
        var program = await kit.ProgramAsync(manager, kit.ProgramBody(currency: "KWD", payoutType: "FlatPerSale", flat: 1.2345m));
        Assert.Equal(1.235m, program.GetProperty("flatAmount").GetDecimal()); // KWD: 3 decimals
    }
}
