using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Projects;
using OptimizeAll.IntegrationTests.Clients;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Projects;

public sealed class DeliveryOperationsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private DateOnly Today => DateOnly.FromDateTime(api.Clock.GetUtcNow().UtcDateTime);

    [Fact]
    public async Task One_running_timer_per_user_manual_entries_and_timesheet_approval()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var designer = await api.StaffAsync(Role.Designer);

        var started = await (await designer.Client.PostAsJsonAsync("/api/v1/agency/time/timer/start", new { projectId, note = "Banners" })).ReadJsonAsync();
        Assert.True(started.GetProperty("isRunning").GetBoolean());
        await (await designer.Client.PostAsJsonAsync("/api/v1/agency/time/timer/start", new { projectId })).ShouldFailAsync(409, "time.timer_running");
        // Even a race can't create a second running timer.
        var race = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => designer.Client.PostAsJsonAsync("/api/v1/agency/time/timer/start", new { projectId })));
        Assert.All(race, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<TimeEntry>().CountAsync(e => e.RunningUserId == designer.User.Id)));

        api.Clock.Advance(TimeSpan.FromMinutes(5));
        var stopped = await (await designer.Client.PostAsJsonAsync("/api/v1/agency/time/timer/stop", new { })).ReadJsonAsync();
        Assert.False(stopped.GetProperty("isRunning").GetBoolean());
        Assert.Equal(5, stopped.GetProperty("minutes").GetInt32());
        await (await designer.Client.PostAsJsonAsync("/api/v1/agency/time/timer/stop", new { })).ShouldFailAsync(409, "time.no_timer");

        var manual = await (await designer.Client.PostAsJsonAsync("/api/v1/agency/time/entries", new
        {
            projectId, date = Today, minutes = 90, billable = false, note = "Internal review",
        })).ReadJsonAsync();
        Assert.Equal(90, manual.GetProperty("minutes").GetInt32());

        // Submit the week; entries lock; a manager approves (not your own).
        var week = await (await designer.Client.PostAsync($"/api/v1/agency/time/timesheets/submit?date={Today:yyyy-MM-dd}", null)).ReadJsonAsync();
        Assert.Equal("Submitted", week.GetProperty("status").GetString());
        Assert.Equal(95, week.GetProperty("totalMinutes").GetInt32());
        await (await designer.Client.PostAsJsonAsync("/api/v1/agency/time/entries", new { projectId, date = Today, minutes = 30 }))
            .ShouldFailAsync(409, "time.week_locked");
        var sheetId = week.GetProperty("id").GetGuid();
        await (await designer.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/approve", new { concurrencyStamp = week.GetProperty("concurrencyStamp").GetGuid() }))
            .ShouldFailAsync(403);
        var pending = await (await am.Client.GetAsync("/api/v1/agency/time/timesheets/pending")).ReadJsonAsync();
        Assert.Contains(pending.EnumerateArray(), p => p.GetProperty("id").GetGuid() == sheetId);
        var approved = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/approve",
            new { concurrencyStamp = week.GetProperty("concurrencyStamp").GetGuid() })).ReadJsonAsync();
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/reject",
            new { comment = "late", concurrencyStamp = week.GetProperty("concurrencyStamp").GetGuid() })).ShouldFailAsync(409);

        // Utilization and CSV export.
        var util = await (await am.Client.GetAsync($"/api/v1/agency/time/utilization?from={Today.AddDays(-6):yyyy-MM-dd}&to={Today:yyyy-MM-dd}")).ReadJsonAsync();
        var row = util.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("userId").GetGuid() == designer.User.Id);
        Assert.Equal(95, row.GetProperty("totalMinutes").GetInt32());
        Assert.Equal(5, row.GetProperty("billableMinutes").GetInt32());
        var csv = await am.Client.GetAsync($"/api/v1/agency/time/entries/export.csv?projectId={projectId}&from={Today.AddDays(-6):yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        csv.EnsureSuccessStatusCode();
        Assert.Contains("Internal review", await csv.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admins_cannot_approve_their_own_timesheet_either()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var admin = await api.StaffAsync(Role.Admin);
        (await admin.Client.PostAsJsonAsync("/api/v1/agency/time/entries", new { projectId, date = Today, minutes = 60 })).EnsureSuccessStatusCode();
        var week = await (await admin.Client.PostAsync($"/api/v1/agency/time/timesheets/submit?date={Today:yyyy-MM-dd}", null)).ReadJsonAsync();
        var sheetId = week.GetProperty("id").GetGuid();
        var stamp = week.GetProperty("concurrencyStamp").GetGuid();
        await (await admin.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/approve", new { concurrencyStamp = stamp }))
            .ShouldFailAsync(403, "time.own_timesheet");
        // Another manager can.
        var approved = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/approve", new { concurrencyStamp = stamp })).ReadJsonAsync();
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Entries_racing_a_week_submission_never_land_in_the_submitted_week()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var designer = await api.StaffAsync(Role.Designer);
        for (var round = 1; round <= 6; round++)
        {
            var day = Today.AddDays(-7 * round);
            (await designer.Client.PostAsJsonAsync("/api/v1/agency/time/entries", new { projectId, date = day, minutes = 10 })).EnsureSuccessStatusCode();
            var writes = Enumerable.Range(0, 12)
                .Select(_ => designer.Client.PostAsJsonAsync("/api/v1/agency/time/entries", new { projectId, date = day, minutes = 1 })).ToList();
            var submit = designer.Client.PostAsync($"/api/v1/agency/time/timesheets/submit?date={day:yyyy-MM-dd}", null);
            await Task.WhenAll(writes.Append(submit));
            Assert.All(writes, w => Assert.Contains(w.Result.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
            submit.Result.EnsureSuccessStatusCode();
            var week = BudgetMath.WeekStart(day);
            var (total, logged) = await api.WithDbAsync(async db => (
                await db.Set<Timesheet>().Where(t => t.UserId == designer.User.Id && t.WeekStart == week).Select(t => t.TotalMinutes).SingleAsync(),
                await db.Set<TimeEntry>().Where(e => e.UserId == designer.User.Id && e.Date >= week && e.Date <= week.AddDays(6)).SumAsync(e => e.Minutes)));
            // The submitted total is exactly what is in the (now locked) week.
            Assert.Equal(logged, total);
        }
    }

    [Fact]
    public async Task Budget_burn_uses_user_then_role_then_project_rates()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var project = await (await am.Client.PostAsJsonAsync("/api/v1/agency/projects", new
        {
            clientId = org, name = "Burn", type = "RetainerMonth", budgetHours = 10, budgetAmount = 1000, defaultHourlyRate = 50,
        })).ReadJsonAsync();
        var projectId = project.GetProperty("id").GetGuid();
        var designer = await api.StaffAsync(Role.Designer);
        var seo = await api.StaffAsync(Role.SeoSpecialist);
        var content = await api.StaffAsync(Role.ContentCreator);
        (await am.Client.PutAsJsonAsync("/api/v1/agency/time/rates", new { role = "Designer", rate = 100, currency = "USD" })).EnsureSuccessStatusCode();
        (await am.Client.PutAsJsonAsync("/api/v1/agency/time/rates", new { userId = seo.User.Id, rate = 150, currency = "USD" })).EnsureSuccessStatusCode();
        (await am.Client.PutAsJsonAsync("/api/v1/agency/time/rates", new { role = "SeoSpecialist", rate = 999, currency = "USD" })).EnsureSuccessStatusCode();

        async Task Log(HttpClient c, int minutes, bool billable) =>
            (await c.PostAsJsonAsync("/api/v1/agency/time/entries", new { projectId, date = Today, minutes, billable })).EnsureSuccessStatusCode();
        await Log(designer.Client, 120, true);  // 2h × 100 (role) = 200
        await Log(seo.Client, 60, true);        // 1h × 150 (user beats role) = 150
        await Log(content.Client, 90, true);    // 1.5h × 50 (project default) = 75
        await Log(content.Client, 30, false);   // non-billable: hours only

        var budget = await (await am.Client.GetAsync($"/api/v1/agency/projects/{projectId}/budget")).ReadJsonAsync();
        Assert.Equal(5m, budget.GetProperty("hoursLogged").GetDecimal());
        Assert.Equal(4.5m, budget.GetProperty("billableHours").GetDecimal());
        Assert.Equal(425m, budget.GetProperty("amountBurned").GetDecimal());
        Assert.Equal(50m, budget.GetProperty("hoursBurnPercent").GetDecimal());
        Assert.Equal(42.5m, budget.GetProperty("amountBurnPercent").GetDecimal());
        Assert.Equal("USD", budget.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Recurring_task_and_monthly_report_jobs_are_idempotent()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client, "Retainer Co");
        var project = await am.Client.CreateProjectAsync(org, "social-monthly-content", "Social retainer");
        var projectId = project.GetProperty("id").GetGuid();
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{projectId}/recurring-tasks", new { title = "Monthly analytics check", dayOfMonth = 1, assigneeUserId = am.User.Id }))
            .EnsureSuccessStatusCode();

        await Task.WhenAll(api.RunJobAsync<RecurringTaskJob>(), api.RunJobAsync<RecurringTaskJob>());
        await api.RunJobAsync<RecurringTaskJob>();
        var generated = await api.WithDbAsync(db => db.Set<ProjectTask>().Where(t => t.ProjectId == projectId && t.RecurrenceKey != null).ToListAsync());
        var rules = await api.WithDbAsync(db => db.Set<RecurringTaskRule>().CountAsync(r => r.ProjectId == projectId && r.DayOfMonth <= Today.Day));
        Assert.Equal(rules, generated.Count);
        Assert.Equal(generated.Count, generated.Select(t => t.RecurrenceKey).Distinct().Count());
        Assert.Contains(generated, t => t.Title.StartsWith("Monthly analytics check", StringComparison.Ordinal) && t.Labels.Contains("recurring"));

        await api.RunJobAsync<MonthlyReportDraftJob>();
        await api.RunJobAsync<MonthlyReportDraftJob>();
        var drafts = await api.WithDbAsync(db => db.Set<ClientReport>().Where(r => r.ClientAccountId == org).ToListAsync());
        var draft = Assert.Single(drafts);
        Assert.Equal(ReportStatus.Draft, draft.Status);
        var expected = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        Assert.Equal(expected, draft.PeriodStart);
        // Channel sections without a registered provider are left manual and say so.
        var seo = draft.Sections.Single(s => s.Key == "seo");
        Assert.Contains("No data source", seo.ProviderNote);
        Assert.Empty(seo.Kpis);
        var delivery = draft.Sections.Single(s => s.Key == "delivery");
        Assert.All(delivery.Kpis, k => Assert.Equal(KpiMeasurement.Measured, k.Measurement));
    }

    [Fact]
    public async Task Reports_carry_sources_and_measurement_labels_and_publishing_notifies_the_client()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var viewer = await api.ClientUserAsync(org, ClientMemberRole.Viewer);
        var report = await (await am.Client.PostAsJsonAsync("/api/v1/agency/reports", new { clientId = org, period = "2026-07" })).ReadJsonAsync();
        var id = report.GetProperty("id").GetGuid();
        var sections = JsonSerializer.Deserialize<List<Domain.Projects.ReportSection>>(report.GetProperty("sections").GetRawText(), ApiFactory.Json)!;

        // A KPI without a source is rejected.
        var missingSource = sections.Select(s => s.Key == "seo"
            ? s with { Kpis = new() { new("sessions", "Organic sessions", 1200, null, null, "", KpiMeasurement.Manual) } } : s).ToList();
        await (await am.Client.PutAsJsonAsync($"/api/v1/agency/reports/{id}", new { title = "July", sections = missingSource, concurrencyStamp = report.GetProperty("concurrencyStamp").GetGuid() }))
            .ShouldFailAsync(400, "report.kpi_source_required");
        var edited = sections.Select(s => s.Key switch
        {
            "seo" => s with { Kpis = new() { new("sessions", "Organic sessions", 1200, null, 1000, "Google Analytics 4 export", KpiMeasurement.Manual) } },
            "summary" => s with { Body = "A strong month." },
            _ => s,
        }).ToList();
        var saved = await (await am.Client.PutAsJsonAsync($"/api/v1/agency/reports/{id}", new { title = "July report", sections = edited, concurrencyStamp = report.GetProperty("concurrencyStamp").GetGuid() }))
            .ReadJsonAsync();

        // Not visible to the client until published.
        await (await viewer.Client.GetAsync($"/api/v1/client/orgs/{org}/reports/{id}")).ShouldFailAsync(404);
        var published = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/reports/{id}/publish", new { concurrencyStamp = saved.GetProperty("concurrencyStamp").GetGuid() }))
            .ReadJsonAsync();
        Assert.Equal("Published", published.GetProperty("status").GetString());
        var notification = await api.WithDbAsync(db => db.Set<Notification>().SingleAsync(n => n.UserId == viewer.User.Id && n.Type == DeliveryNotificationTypes.ReportPublished));
        Assert.True(await api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d => d.NotificationId == notification.Id && d.Channel == NotificationChannel.Email)));
        var clientReport = await (await viewer.Client.GetAsync($"/api/v1/client/orgs/{org}/reports/{id}")).ReadJsonAsync();
        var kpi = clientReport.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "seo").GetProperty("kpis")[0];
        Assert.Equal("Manual", kpi.GetProperty("measurement").GetString());
        Assert.Equal("Google Analytics 4 export", kpi.GetProperty("source").GetString());
        await (await am.Client.PutAsJsonAsync($"/api/v1/agency/reports/{id}", new { title = "Edited title", sections = edited, concurrencyStamp = published.GetProperty("concurrencyStamp").GetGuid() }))
            .ShouldFailAsync(409, "report.published");
    }

    [Fact]
    public async Task Published_reports_hide_empty_sections_and_staff_notes_from_the_client()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var viewer = await api.ClientUserAsync(org, ClientMemberRole.Viewer);
        var report = await (await am.Client.PostAsJsonAsync("/api/v1/agency/reports", new { clientId = org, period = "2026-06" })).ReadJsonAsync();
        var id = report.GetProperty("id").GetGuid();
        var staffSections = report.GetProperty("sections").EnumerateArray().ToList();
        Assert.Contains(staffSections, s => s.GetProperty("providerNote").GetString()?.Contains("No data source") == true);
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/reports/{id}/publish", new { concurrencyStamp = report.GetProperty("concurrencyStamp").GetGuid() }))
            .EnsureSuccessStatusCode();

        var clientReport = await (await viewer.Client.GetAsync($"/api/v1/client/orgs/{org}/reports/{id}")).ReadJsonAsync();
        var raw = clientReport.GetRawText();
        Assert.DoesNotContain("No data source", raw);
        Assert.DoesNotContain("<!--", raw);
        Assert.DoesNotContain("\\u003C!--", raw);
        var sections = clientReport.GetProperty("sections").EnumerateArray().ToList();
        Assert.True(sections.Count < staffSections.Count);
        Assert.All(sections, s => Assert.True(s.GetProperty("kpis").GetArrayLength() > 0 || s.GetProperty("body").ValueKind == JsonValueKind.String));
        Assert.All(sections, s => Assert.Equal(JsonValueKind.Null, s.GetProperty("providerNote").ValueKind));
    }

    [Fact]
    public async Task Mentions_notify_staff_and_kanban_moves_respect_dependencies()
    {
        var am = await api.StaffAsync();
        var content = await api.StaffAsync(Role.ContentCreator);
        var designer = await api.StaffAsync(Role.Designer);
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var first = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{projectId}/tasks", new { title = "Write copy", assigneeUserIds = new[] { content.User.Id } })).ReadJsonAsync();
        var firstId = first.GetProperty("task").GetProperty("id").GetGuid();
        var second = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{projectId}/tasks", new
        {
            title = "Design banner", blockedByTaskIds = new[] { firstId }, checklist = new[] { "Sizes", "Export" },
        })).ReadJsonAsync();
        var secondId = second.GetProperty("task").GetProperty("id").GetGuid();
        Assert.True(second.GetProperty("task").GetProperty("isBlocked").GetBoolean());
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == content.User.Id && n.Type == DeliveryNotificationTypes.TaskAssigned)));

        // @mention: the mentioned user gets one notification (in-app + email); a participant can't be mentioned.
        var (participant, _) = await api.CreateClientAsync();
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/tasks/{secondId}/comments", new { body = "hi", mentionUserIds = new[] { participant.Id } }))
            .ShouldFailAsync(400, "task.invalid_mention");
        var commented = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/tasks/{secondId}/comments", new
        {
            body = "@Designer please start once copy lands", mentionUserIds = new[] { designer.User.Id, designer.User.Id },
        })).ReadJsonAsync();
        Assert.Single(commented.GetProperty("comments").EnumerateArray());
        var mention = await api.WithDbAsync(db => db.Set<Notification>().Where(n => n.UserId == designer.User.Id && n.Type == DeliveryNotificationTypes.TaskMention).ToListAsync());
        Assert.Single(mention);
        Assert.True(await api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d => d.NotificationId == mention[0].Id && d.Channel == NotificationChannel.Email)));

        // Blocked task can't be Done; moving the blocker first unblocks it. A stale stamp is a 409.
        var stamp = second.GetProperty("task").GetProperty("concurrencyStamp").GetGuid();
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/tasks/{secondId}/move", new { status = "Done", concurrencyStamp = stamp }))
            .ShouldFailAsync(409, "task.blocked");
        var moved = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/tasks/{firstId}/move", new
        {
            status = "Done", concurrencyStamp = first.GetProperty("task").GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal("Done", moved.GetProperty("status").GetString());
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/tasks/{firstId}/move", new
        {
            status = "Todo", concurrencyStamp = first.GetProperty("task").GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(409, "concurrency.conflict");
        var inReview = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/tasks/{secondId}/move", new { status = "InReview", concurrencyStamp = stamp })).ReadJsonAsync();
        Assert.False(inReview.GetProperty("isBlocked").GetBoolean());

        // Circular dependencies are rejected.
        var detail = await (await am.Client.GetAsync($"/api/v1/agency/tasks/{firstId}")).ReadJsonAsync();
        await (await am.Client.PutAsJsonAsync($"/api/v1/agency/tasks/{firstId}", new
        {
            title = "Write copy", status = "Done", blockedByTaskIds = new[] { secondId }, concurrencyStamp = detail.GetProperty("task").GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(400, "task.dependency_cycle");

        var mine = await (await content.Client.GetAsync("/api/v1/agency/tasks/mine?filter=done")).ReadJsonAsync();
        Assert.Contains(mine.EnumerateArray(), t => t.GetProperty("id").GetGuid() == firstId);
    }

    [Fact]
    public async Task Messages_read_receipts_briefs_meetings_and_dashboards()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{org}/team", new { userId = am.User.Id, serviceRole = "Strategist" })).EnsureSuccessStatusCode();
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var owner = await api.ClientUserAsync(org, ClientMemberRole.Owner);
        var viewer = await api.ClientUserAsync(org, ClientMemberRole.Viewer);

        // Client starts a thread → team notified; staff reads → read receipt.
        var thread = await (await owner.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/threads", new { subject = "Launch date", body = "Can we move it?" })).ReadJsonAsync();
        var threadId = thread.GetProperty("id").GetGuid();
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == am.User.Id && n.Type == DeliveryNotificationTypes.Message)));
        var staffList = await (await am.Client.GetAsync($"/api/v1/agency/clients/{org}/threads")).ReadJsonAsync();
        Assert.Equal(1, staffList[0].GetProperty("unreadCount").GetInt32());
        (await am.Client.GetAsync($"/api/v1/agency/clients/{org}/threads/{threadId}")).EnsureSuccessStatusCode();
        var reply = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{org}/threads/{threadId}/messages", new { body = "Yes — Tuesday." })).ReadJsonAsync();
        var ownerView = await (await owner.Client.GetAsync($"/api/v1/client/orgs/{org}/threads/{threadId}")).ReadJsonAsync();
        var messages = ownerView.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.NotEmpty(messages[0].GetProperty("readBy").EnumerateArray());
        Assert.Equal("", messages[1].GetProperty("author").GetProperty("email").GetString()); // staff emails aren't exposed to clients
        Assert.True(reply.GetProperty("messages").GetArrayLength() == 2);

        // Briefs: Viewer can't submit; Owner can; staff converts into tasks and deliverables.
        var templates = await (await owner.Client.GetAsync($"/api/v1/client/orgs/{org}/brief-templates")).ReadJsonAsync();
        Assert.Contains(templates.EnumerateArray(), t => t.GetProperty("key").GetString() == "social-content");
        await (await viewer.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/briefs", new { templateKey = "social-content", title = "Viewer brief", answers = new { } }))
            .ShouldFailAsync(403);
        await (await owner.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/briefs", new { templateKey = "social-content", title = "Spring", answers = new { goal = "More" } }))
            .ShouldFailAsync(400, "brief.invalid_answers");
        var brief = await (await owner.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/briefs", new
        {
            templateKey = "social-content", title = "Spring promo",
            answers = new { goal = "Sign-ups", audience = "Runners", key_message = "Spring into it", channels = "Instagram", format = "Reels / short video" },
        })).ReadJsonAsync();
        var briefId = brief.GetProperty("id").GetGuid();
        var converted = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/briefs/{briefId}/convert", new
        {
            projectId, tasks = new[] { new { title = "Storyboard reels", assigneeUserId = am.User.Id } },
            deliverables = new[] { new { title = "Spring reels", type = "Video" } }, concurrencyStamp = brief.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal("Converted", converted.GetProperty("status").GetString());
        Assert.True(await api.WithDbAsync(db => db.Set<ProjectTask>().AnyAsync(t => t.BriefId == briefId && t.Title == "Storyboard reels")));

        // Meeting action item → task (once).
        var start = api.Clock.GetUtcNow().UtcDateTime.AddHours(1);
        var meeting = await (await am.Client.PostAsJsonAsync("/api/v1/agency/meetings", new
        {
            clientId = org, title = "Monthly review", kind = "MonthlyReview", startsAt = start, attendeeUserIds = new[] { am.User.Id, owner.User.Id },
            actionItems = new[] { new { text = "Send Q3 plan", assigneeUserId = am.User.Id } },
        })).ReadJsonAsync();
        var itemId = meeting.GetProperty("actionItems")[0].GetProperty("id").GetGuid();
        var withTask = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/meetings/{meeting.GetProperty("id").GetGuid()}/action-items/{itemId}/convert", new { projectId }))
            .ReadJsonAsync();
        Assert.NotEqual(JsonValueKind.Null, withTask.GetProperty("actionItems")[0].GetProperty("taskId").ValueKind);
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/meetings/{meeting.GetProperty("id").GetGuid()}/action-items/{itemId}/convert", new { projectId }))
            .ShouldFailAsync(409, "meeting.already_converted");

        // Dashboards.
        var dashboard = await (await am.Client.GetAsync("/api/v1/agency/dashboard")).ReadJsonAsync();
        Assert.NotEqual(JsonValueKind.Null, dashboard.GetProperty("accountManager").ValueKind);
        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("admin").ValueKind);
        var designer = await api.StaffAsync(Role.Designer);
        var designerDash = await (await designer.Client.GetAsync("/api/v1/agency/dashboard")).ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, designerDash.GetProperty("accountManager").ValueKind);
        var home = await (await owner.Client.GetAsync($"/api/v1/client/orgs/{org}/home")).ReadJsonAsync();
        Assert.True(home.GetProperty("canApprove").GetBoolean());
        Assert.Contains(home.GetProperty("team").EnumerateArray(), t => t.GetProperty("userId").GetGuid() == am.User.Id);
        Assert.Single(home.GetProperty("upcomingMeetings").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, home.GetProperty("upcomingMeetings")[0].GetProperty("notes").ValueKind);
        var viewerHome = await (await viewer.Client.GetAsync($"/api/v1/client/orgs/{org}/home")).ReadJsonAsync();
        Assert.False(viewerHome.GetProperty("canApprove").GetBoolean());
    }

    [Fact]
    public async Task Dashboard_counts_the_whole_review_queue_not_just_the_listed_ten()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        for (var i = 0; i < 12; i++)
        {
            var id = (await am.Client.CreateDeliverableAsync(projectId, $"Review load {i}")).GetProperty("deliverable").GetProperty("id").GetGuid();
            await am.Client.AddLinkVersionAsync(id);
            (await am.Client.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/submit", new { version = 1 })).EnsureSuccessStatusCode();
        }
        var total = (await (await am.Client.GetAsync("/api/v1/agency/deliverables?view=review&pageSize=1")).ReadJsonAsync()).GetProperty("total").GetInt32();
        Assert.True(total >= 12);
        var dashboard = await (await am.Client.GetAsync("/api/v1/agency/dashboard")).ReadJsonAsync();
        Assert.Equal(10, dashboard.GetProperty("reviewQueue").GetArrayLength());
        Assert.Equal(total, dashboard.GetProperty("reviewQueueTotal").GetInt32());
    }

    [Fact]
    public async Task Client_portal_projects_show_only_client_visible_tasks()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{projectId}/tasks", new { title = "Visible milestone task", clientVisible = true, status = "Done" }))
            .EnsureSuccessStatusCode();
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{projectId}/tasks", new { title = "Internal QA", clientVisible = false })).EnsureSuccessStatusCode();
        var viewer = await api.ClientUserAsync(org, ClientMemberRole.Viewer);
        var detail = await (await viewer.Client.GetAsync($"/api/v1/client/orgs/{org}/projects/{projectId}")).ReadJsonAsync();
        var tasks = detail.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Single(tasks);
        Assert.Equal("Visible milestone task", tasks[0].GetProperty("title").GetString());
        Assert.Equal(100, detail.GetProperty("project").GetProperty("progressPercent").GetInt32());
        Assert.DoesNotContain("Internal QA", detail.GetRawText());
    }

    [Fact]
    public async Task Demo_seed_is_idempotent_and_creates_the_canonical_accounts()
    {
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OptimizeAll.Infrastructure.Persistence.AppDbContext>();
            var seeder = scope.ServiceProvider.GetRequiredService<DeliveryDemoSeeder>();
            await seeder.SeedAsync(db, CancellationToken.None);
            await seeder.SeedAsync(db, CancellationToken.None);
        }
        var slugs = await api.WithDbAsync(db => db.Set<ClientAccount>().Select(c => c.Slug).ToListAsync());
        foreach (var slug in new[] { "nimbus-fitness", "wanderly-travel", "aurora-skincare", "karachi-eats" })
            Assert.Single(slugs, s => s == slug);
        var nimbus = await api.WithDbAsync(db => db.Set<ClientAccount>().SingleAsync(c => c.Slug == "nimbus-fitness"));
        Assert.Equal("USD", nimbus.Currency);
        Assert.Equal(ClientAccountStatus.Onboarding, await api.WithDbAsync(db => db.Set<ClientAccount>().Where(c => c.Slug == "karachi-eats").Select(c => c.Status).SingleAsync()));
        var statuses = await api.WithDbAsync(db => db.Set<Deliverable>().Where(d => d.ClientAccountId == nimbus.Id).Select(d => d.Status).Distinct().ToListAsync());
        Assert.Equal(Enum.GetValues<DeliverableStatus>().OrderBy(s => s), statuses.OrderBy(s => s));
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<User>().CountAsync(u => u.NormalizedEmail == "OWNER@NIMBUS.DEMO.OPTIMIZEALL.APP")));
        Assert.Equal(2, await api.WithDbAsync(db => db.Set<ClientMember>().CountAsync(m => m.ClientAccountId == nimbus.Id && (m.Role == ClientMemberRole.Owner || m.Role == ClientMemberRole.Approver))));

        // The demo client owner can sign in and see their home.
        var owner = await api.LoginAsync(new TestUser(Guid.Empty, "owner@nimbus.demo.optimizeall.app", "Demo#2026!pass"));
        var home = await (await owner.GetAsync($"/api/v1/client/orgs/{nimbus.Id}/home")).ReadJsonAsync();
        Assert.NotEmpty(home.GetProperty("awaitingApproval").EnumerateArray());
        Assert.NotEqual(JsonValueKind.Null, home.GetProperty("latestReport").ValueKind);
    }
}
