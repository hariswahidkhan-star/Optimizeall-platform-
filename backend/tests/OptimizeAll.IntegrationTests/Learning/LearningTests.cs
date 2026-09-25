using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.Learning;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Learning;
using OptimizeAll.IntegrationTests.Auth;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Learning;

/// <summary>Helpers shared by the Learning tests (the sample pack "platform-getting-started" is seeded by Baseline).</summary>
internal static class LearningHelpers
{
    public const string Slug = "platform-getting-started";

    public static CoursePack Pack => CoursePackLibrary.All.Single(f => f.Pack?.Slug == Slug).Pack!;

    public static async Task<List<string>> LessonSlugsAsync(HttpClient client)
    {
        var course = await (await client.GetAsync($"/api/v1/public/learning/courses/{Slug}")).ReadJsonAsync();
        return course.GetProperty("modules").EnumerateArray()
            .SelectMany(m => m.GetProperty("lessons").EnumerateArray().Select(l => l.GetProperty("slug").GetString()!)).ToList();
    }

    public static async Task EnrolAndCompleteAsync(HttpClient client)
    {
        (await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/enrol", null)).EnsureSuccessStatusCode();
        foreach (var lesson in await LessonSlugsAsync(client))
            (await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/lessons/{lesson}/complete", null)).EnsureSuccessStatusCode();
    }

    /// <summary>Shown positions of the correct options of an attempt question (matched by option text against the pack).</summary>
    public static int[] CorrectPositions(JsonElement question)
    {
        var q = Pack.FinalExam!.Pool!.Single(p => p.Id == question.GetProperty("id").GetString());
        var correctTexts = q.Correct!.Select(i => q.Options![i]).ToHashSet();
        return question.GetProperty("options").EnumerateArray().Select((o, i) => (o.GetString(), i))
            .Where(x => correctTexts.Contains(x.Item1!)).Select(x => x.i).ToArray();
    }

    public static int[] WrongPositions(JsonElement question)
    {
        var correct = CorrectPositions(question);
        return new[] { Enumerable.Range(0, question.GetProperty("options").GetArrayLength()).First(i => !correct.Contains(i)) };
    }

    public static async Task<JsonElement> StartAsync(HttpClient client)
    {
        var response = await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/exam/attempts", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    public static async Task<JsonElement> SubmitAsync(HttpClient client, JsonElement attempt, bool pass)
    {
        var answers = attempt.GetProperty("questions").EnumerateArray()
            .Select(q => new { questionId = q.GetProperty("id").GetString(), selected = pass ? CorrectPositions(q) : WrongPositions(q) }).ToList();
        var response = await client.PostAsJsonAsync($"/api/v1/me/learning/attempts/{attempt.GetProperty("id").GetString()}/submit", new { answers });
        return await response.ReadJsonAsync();
    }
}

public sealed class LearningTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Slug = LearningHelpers.Slug;

    [Fact]
    public async Task The_catalog_upsert_seeded_the_sample_pack_and_is_idempotent()
    {
        var (courses, versions) = await api.WithDbAsync(async db => (
            await db.Set<Course>().CountAsync(c => c.Slug == Slug),
            await db.Set<CourseVersion>().CountAsync(v => db.Set<Course>().Any(c => c.Id == v.CourseId && c.Slug == Slug))));
        Assert.Equal(1, courses);
        Assert.Equal(1, versions);

        // Running the upsert again (same pack version) changes nothing.
        var changes = await api.WithDbAsync(async db =>
        {
            using var scope = api.Services.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<LearningCatalogSeeder>();
            return await seeder.UpsertAsync(scope.ServiceProvider.GetRequiredService<OptimizeAll.Infrastructure.Persistence.AppDbContext>(),
                CoursePackLibrary.All, CancellationToken.None);
        });
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task A_higher_pack_version_is_published_unless_staff_published_an_edit()
    {
        var pack = LearningHelpers.Pack.Clone();
        pack.Slug = "upsert-" + Guid.NewGuid().ToString("N")[..8];
        async Task<int> Upsert(CoursePack p)
        {
            using var scope = api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OptimizeAll.Infrastructure.Persistence.AppDbContext>();
            return await scope.ServiceProvider.GetRequiredService<LearningCatalogSeeder>()
                .UpsertAsync(db, new[] { new PackFile(p.Slug + ".json", p.ToJson(), p, null) }, CancellationToken.None);
        }
        Assert.Equal(1, await Upsert(pack));
        pack.Version = 2;
        pack.Title = "Updated title";
        Assert.Equal(1, await Upsert(pack));
        var course = await api.WithDbAsync(db => db.Set<Course>().AsNoTracking().SingleAsync(c => c.Slug == pack.Slug));
        Assert.Equal("Updated title", course.Title);

        // Staff publish an edited version; pack v3 is then stored but not published.
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var edited = pack.Clone();
        edited.Title = "Staff title";
        var detail = await (await admin.PostAsJsonAsync($"/api/v1/admin/learning/courses/{course.Id}/versions",
            new { document = JsonDocument.Parse(edited.ToJson()).RootElement, publish = true, concurrencyStamp = course.ConcurrencyStamp })).ReadJsonAsync();
        Assert.Equal(3, detail.GetProperty("summary").GetProperty("publishedVersionNumber").GetInt32());
        pack.Version = 3;
        pack.Title = "Pack title v3";
        Assert.Equal(1, await Upsert(pack));
        course = await api.WithDbAsync(db => db.Set<Course>().AsNoTracking().SingleAsync(c => c.Slug == pack.Slug));
        Assert.Equal("Staff title", course.Title);
        var row = await (await admin.GetAsync($"/api/v1/admin/learning/courses/{course.Id}")).ReadJsonAsync();
        Assert.True(row.GetProperty("summary").GetProperty("packUpdateAvailable").GetBoolean());
    }

    [Fact]
    public async Task Anonymous_visitors_read_the_catalog_courses_lessons_and_badges_but_never_exam_answers()
    {
        var anon = api.CreateClient();
        var catalog = await (await anon.GetAsync("/api/v1/public/learning/courses?category=Platform")).ReadJsonAsync();
        Assert.Contains(catalog.GetProperty("items").EnumerateArray(), c => c.GetProperty("slug").GetString() == Slug);
        var categories = await (await anon.GetAsync("/api/v1/public/learning/categories")).ReadJsonAsync();
        Assert.Contains(categories.EnumerateArray(), c => c.GetProperty("category").GetString() == "Platform");

        var courseResponse = await anon.GetAsync($"/api/v1/public/learning/courses/{Slug}");
        var courseText = await courseResponse.Content.ReadAsStringAsync();
        var course = JsonDocument.Parse(courseText).RootElement;
        Assert.Equal("Getting started on Optimize All", course.GetProperty("card").GetProperty("title").GetString());
        // schema.org Course with a free offer and a course instance.
        var ld = course.GetProperty("jsonLd").EnumerateArray().First(j => j.GetProperty("@type").GetString() == "Course");
        Assert.True(ld.GetProperty("isAccessibleForFree").GetBoolean());
        Assert.Equal(0, ld.GetProperty("offers").GetProperty("price").GetInt32());
        Assert.Equal("Online", ld.GetProperty("hasCourseInstance").GetProperty("courseMode").GetString());
        // No exam question text or explanation is exposed.
        foreach (var q in LearningHelpers.Pack.FinalExam!.Pool!)
            Assert.DoesNotContain(q.Explanation, courseText);

        var lessons = await LearningHelpers.LessonSlugsAsync(anon);
        var lesson = await (await anon.GetAsync($"/api/v1/public/learning/courses/{Slug}/lessons/{lessons[1]}")).ReadJsonAsync();
        Assert.Equal("Video", lesson.GetProperty("type").GetString());
        Assert.True(lesson.GetProperty("video").GetProperty("src").ValueKind == JsonValueKind.Null);
        Assert.NotEmpty(lesson.GetProperty("knowledgeCheck").EnumerateArray());
        Assert.Equal(lessons[0], lesson.GetProperty("previous").GetProperty("slug").GetString());

        var badge = await anon.GetAsync($"/api/v1/public/learning/courses/{Slug}/badge.svg");
        Assert.Equal(HttpStatusCode.OK, badge.StatusCode);
        Assert.Equal("image/svg+xml", badge.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<svg", await badge.Content.ReadAsStringAsync());

        await (await anon.GetAsync("/api/v1/public/learning/courses/no-such-course")).ShouldFailAsync(404);
        await (await anon.GetAsync($"/api/v1/public/learning/courses/{Slug}/lessons/no-such-lesson")).ShouldFailAsync(404);
        // Participant endpoints need an account.
        await (await anon.PostAsync($"/api/v1/me/learning/courses/{Slug}/enrol", null)).ShouldFailAsync(401);

        var sitemap = await (await anon.GetAsync("/api/v1/public/sitemap.xml")).Content.ReadAsStringAsync();
        Assert.Contains($"/learn/{Slug}</loc>", sitemap);
        Assert.Contains($"/learn/{Slug}/{lessons[0]}</loc>", sitemap);
    }

    [Fact]
    public async Task Enrolment_progress_resume_and_knowledge_checks_are_saved_per_learner()
    {
        var (_, client) = await api.CreateClientAsync();
        var created = await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/enrol", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/enrol", null)).StatusCode);

        var lessons = await LearningHelpers.LessonSlugsAsync(client);
        (await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/lessons/{lessons[0]}/complete", null)).EnsureSuccessStatusCode();
        var progress = await (await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/lessons/{lessons[2]}/start", null)).ReadJsonAsync();
        Assert.Equal(1, progress.GetProperty("completedLessonCount").GetInt32());
        Assert.Equal(lessons[2], progress.GetProperty("resumeLessonSlug").GetString());
        Assert.False(progress.GetProperty("examUnlocked").GetBoolean());

        var check = LearningHelpers.Pack.AllLessons.First().Lesson.KnowledgeCheck![0];
        var answer = await (await client.PostAsJsonAsync($"/api/v1/me/learning/courses/{Slug}/lessons/{lessons[0]}/checks/0",
            new { selected = check.Correct })).ReadJsonAsync();
        Assert.True(answer.GetProperty("isCorrect").GetBoolean());
        await (await client.PostAsJsonAsync($"/api/v1/me/learning/courses/{Slug}/lessons/{lessons[0]}/checks/0", new { selected = new[] { 9 } }))
            .ShouldFailAsync(400, "learning.invalid_answer");
        await (await client.PostAsJsonAsync($"/api/v1/me/learning/courses/{Slug}/lessons/{lessons[0]}/checks/99", new { selected = new[] { 0 } }))
            .ShouldFailAsync(404);

        var lesson = await (await client.GetAsync($"/api/v1/me/learning/courses/{Slug}/lessons/{lessons[0]}")).ReadJsonAsync();
        Assert.True(lesson.GetProperty("completed").GetBoolean());
        Assert.Single(lesson.GetProperty("answers").EnumerateArray());

        var dashboard = await (await client.GetAsync("/api/v1/me/learning")).ReadJsonAsync();
        Assert.Equal(Slug, dashboard.GetProperty("continue").GetProperty("course").GetProperty("slug").GetString());
        Assert.Equal(1, dashboard.GetProperty("stats").GetProperty("inProgress").GetInt32());

        // Progress needs an enrolment.
        var (_, other) = await api.CreateClientAsync();
        await (await other.PostAsync($"/api/v1/me/learning/courses/{Slug}/lessons/{lessons[0]}/complete", null)).ShouldFailAsync(409, "learning.not_enrolled");
        // Staff without the participant portal cannot use it.
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        await (await reviewer.GetAsync("/api/v1/me/learning")).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Exam_flow_fail_retake_pass_certificate_verification_pdf_badges_and_linkedin()
    {
        var (user, client) = await api.CreateClientAsync();
        (await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/enrol", null)).EnsureSuccessStatusCode();
        await (await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/exam/attempts", null)).ShouldFailAsync(409, "learning.lessons_incomplete");
        await LearningHelpers.EnrolAndCompleteAsync(client);

        var attempt = await LearningHelpers.StartAsync(client);
        var attemptText = attempt.GetRawText();
        // Answers never leave the server before submission.
        Assert.All(attempt.GetProperty("questions").EnumerateArray(), q => Assert.Equal(JsonValueKind.Null, q.GetProperty("review").ValueKind));
        foreach (var q in LearningHelpers.Pack.FinalExam!.Pool!) Assert.DoesNotContain(q.Explanation, attemptText);
        Assert.Equal(10, attempt.GetProperty("questions").GetArrayLength());
        Assert.Equal(10, attempt.GetProperty("questions").EnumerateArray().Select(q => q.GetProperty("id").GetString()).Distinct().Count());
        Assert.InRange(attempt.GetProperty("secondsRemaining").GetInt32(), 890, 900);

        // One attempt at a time.
        await (await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/exam/attempts", null)).ShouldFailAsync(409, "learning.attempt_in_progress");
        var attemptId = attempt.GetProperty("id").GetString();
        var first = attempt.GetProperty("questions")[0];
        await (await client.PutAsJsonAsync($"/api/v1/me/learning/attempts/{attemptId}/answers", new { questionId = "not-in-draw", selected = new[] { 0 } }))
            .ShouldFailAsync(400, "learning.unknown_question");
        await (await client.PutAsJsonAsync($"/api/v1/me/learning/attempts/{attemptId}/answers",
            new { questionId = first.GetProperty("id").GetString(), selected = new[] { 0, 0 } })).ShouldFailAsync(400, "learning.invalid_answer");
        var saved = await (await client.PutAsJsonAsync($"/api/v1/me/learning/attempts/{attemptId}/answers",
            new { questionId = first.GetProperty("id").GetString(), selected = new[] { 1 } })).ReadJsonAsync();
        Assert.Equal(1, saved.GetProperty("questions")[0].GetProperty("selected")[0].GetInt32());

        // Another learner cannot see or answer this attempt.
        var (_, stranger) = await api.CreateClientAsync();
        await (await stranger.GetAsync($"/api/v1/me/learning/attempts/{attemptId}")).ShouldFailAsync(404);
        await (await stranger.PostAsJsonAsync($"/api/v1/me/learning/attempts/{attemptId}/submit", new { })).ShouldFailAsync(404);

        // Fail, with per-question review and explanations.
        var failed = await LearningHelpers.SubmitAsync(client, attempt, pass: false);
        Assert.Equal("Submitted", failed.GetProperty("status").GetString());
        Assert.False(failed.GetProperty("result").GetProperty("passed").GetBoolean());
        Assert.Equal(0, failed.GetProperty("result").GetProperty("score").GetInt32());
        Assert.All(failed.GetProperty("questions").EnumerateArray(), q =>
        {
            Assert.False(q.GetProperty("review").GetProperty("isCorrect").GetBoolean());
            Assert.False(string.IsNullOrEmpty(q.GetProperty("review").GetProperty("explanation").GetString()));
        });
        // Submitting again is idempotent.
        var again = await (await client.PostAsJsonAsync($"/api/v1/me/learning/attempts/{attemptId}/submit", new { })).ReadJsonAsync();
        Assert.Equal(0, again.GetProperty("result").GetProperty("score").GetInt32());
        await (await client.PutAsJsonAsync($"/api/v1/me/learning/attempts/{attemptId}/answers",
            new { questionId = first.GetProperty("id").GetString(), selected = new[] { 1 } })).ShouldFailAsync(409, "learning.attempt_closed");

        // Retake and pass: the certificate is issued.
        var retake = await LearningHelpers.StartAsync(client);
        var passed = await LearningHelpers.SubmitAsync(client, retake, pass: true);
        Assert.True(passed.GetProperty("result").GetProperty("passed").GetBoolean());
        Assert.Equal(100, passed.GetProperty("result").GetProperty("score").GetInt32());
        var certificateId = passed.GetProperty("result").GetProperty("certificateId").GetString()!;
        await (await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/exam/attempts", null)).ShouldFailAsync(409, "learning.already_certified");

        var mine = await (await client.GetAsync($"/api/v1/me/learning/certificates/{certificateId}")).ReadJsonAsync();
        var code = mine.GetProperty("verificationCode").GetString()!;
        Assert.Matches("^OA-[A-Z2-9]{4}-[A-Z2-9]{4}$", code);
        var links = mine.GetProperty("links");
        var addToProfile = new Uri(links.GetProperty("linkedInAddToProfileUrl").GetString()!);
        Assert.Equal("www.linkedin.com", addToProfile.Host);
        Assert.Equal("/profile/add", addToProfile.AbsolutePath);
        var query = System.Web.HttpUtility.ParseQueryString(addToProfile.Query);
        Assert.Equal("CERTIFICATION_NAME", query["startTask"]);
        Assert.Equal("Optimize All Certified Creator", query["name"]);
        // organizationName by default; organizationId once the LinkedIn company page id is configured (another test sets it).
        Assert.True(query["organizationName"] == "Optimize All Academy" ^ query["organizationId"] == "12345678", addToProfile.Query);
        Assert.Equal(code, query["certId"]);
        Assert.Equal($"http://app.test/verify/certificates/{certificateId}", query["certUrl"]);
        Assert.Equal(DateTime.UtcNow.Year.ToString(), query["issueYear"]);
        Assert.StartsWith("https://www.linkedin.com/sharing/share-offsite/?url=http%3A%2F%2Fapp.test%2Fverify%2Fcertificates%2F",
            links.GetProperty("linkedInShareUrl").GetString());
        await (await stranger.GetAsync($"/api/v1/me/learning/certificates/{certificateId}")).ShouldFailAsync(404);

        // Public verification (by id and by code), PDF, SVG, server-rendered page and Open Badges 2.0.
        var anon = api.CreateClient();
        var verification = await (await anon.GetAsync($"/api/v1/public/learning/certificates/{certificateId}")).ReadJsonAsync();
        Assert.Equal("valid", verification.GetProperty("status").GetString());
        Assert.Equal(code, (await (await anon.GetAsync($"/api/v1/public/learning/certificates/verify?code={code.ToLowerInvariant()}")).ReadJsonAsync())
            .GetProperty("verificationCode").GetString());
        var pdf = await anon.GetAsync($"/api/v1/public/learning/certificates/{certificateId}/certificate.pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 10_000, $"PDF is only {bytes.Length} bytes");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        var svg = await (await anon.GetAsync($"/api/v1/public/learning/certificates/{certificateId}/certificate.svg")).Content.ReadAsStringAsync();
        Assert.Contains(code, svg);
        var page = await anon.GetAsync($"/api/v1/public/learning/certificates/{certificateId}/page");
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("<meta property=\"og:title\"", html);
        Assert.Contains("Valid certificate", html);

        var assertion = await (await anon.GetAsync($"/api/v1/public/learning/openbadges/assertions/{certificateId}")).ReadJsonAsync();
        Assert.Equal("Assertion", assertion.GetProperty("type").GetString());
        Assert.Equal("hosted", assertion.GetProperty("verification").GetProperty("type").GetString());
        Assert.Equal(OptimizeAll.Api.Modules.Learning.Certificates.OpenBadges.HashIdentity(user.Email, assertion.GetProperty("recipient").GetProperty("salt").GetString()!),
            assertion.GetProperty("recipient").GetProperty("identity").GetString());
        Assert.DoesNotContain(user.Email, assertion.GetRawText());
        var badgeClass = await (await anon.GetAsync(assertion.GetProperty("badge").GetString()!.Replace("http://app.test", ""))).ReadJsonAsync();
        Assert.Equal("BadgeClass", badgeClass.GetProperty("type").GetString());
        var issuer = await (await anon.GetAsync("/api/v1/public/learning/openbadges/issuer")).ReadJsonAsync();
        Assert.Equal(badgeClass.GetProperty("issuer").GetString(), issuer.GetProperty("id").GetString());

        // Revocation by an admin (audited, reason and confirmation required, stale stamps refused).
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var list = await (await admin.GetAsync($"/api/v1/admin/learning/certificates?search={code}")).ReadJsonAsync();
        var stamp = list.GetProperty("items")[0].GetProperty("concurrencyStamp").GetString();
        await (await admin.PostAsJsonAsync($"/api/v1/admin/learning/certificates/{certificateId}/revoke", new { reason = "Exam misconduct", confirm = false, concurrencyStamp = stamp }))
            .ShouldFailAsync(400, "admin.confirmation_required");
        await (await admin.PostAsJsonAsync($"/api/v1/admin/learning/certificates/{certificateId}/revoke", new { reason = "Exam misconduct", confirm = true, concurrencyStamp = Guid.NewGuid() }))
            .ShouldFailAsync(409, "concurrency.conflict");
        var revoked = await (await admin.PostAsJsonAsync($"/api/v1/admin/learning/certificates/{certificateId}/revoke",
            new { reason = "Exam misconduct", confirm = true, concurrencyStamp = stamp })).ReadJsonAsync();
        Assert.False(revoked.GetProperty("revokedAt").ValueKind == JsonValueKind.Null);
        await (await admin.PostAsJsonAsync($"/api/v1/admin/learning/certificates/{certificateId}/revoke",
            new { reason = "Again", confirm = true, concurrencyStamp = revoked.GetProperty("concurrencyStamp").GetString() })).ShouldFailAsync(409, "learning.certificate_revoked");
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "learning.certificate_revoked" && a.EntityId == certificateId)));

        var after = await (await anon.GetAsync($"/api/v1/public/learning/certificates/{certificateId}")).ReadJsonAsync();
        Assert.Equal("revoked", after.GetProperty("status").GetString());
        var gone = await anon.GetAsync($"/api/v1/public/learning/openbadges/assertions/{certificateId}");
        Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
        var stub = JsonDocument.Parse(await gone.Content.ReadAsStringAsync()).RootElement;
        Assert.True(stub.GetProperty("revoked").GetBoolean());
        Assert.Equal("learning.certificate_revoked", stub.GetProperty("code").GetString());
        Assert.Contains("Revoked", await (await anon.GetAsync($"/api/v1/public/learning/certificates/{certificateId}/page")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_server_enforces_the_time_limit_and_the_daily_attempt_budget()
    {
        var (user, client) = await api.CreateClientAsync();
        await LearningHelpers.EnrolAndCompleteAsync(client);
        var attempt = await LearningHelpers.StartAsync(client);
        var attemptId = attempt.GetProperty("id").GetString();
        var first = attempt.GetProperty("questions")[0];
        (await client.PutAsJsonAsync($"/api/v1/me/learning/attempts/{attemptId}/answers",
            new { questionId = first.GetProperty("id").GetString(), selected = LearningHelpers.CorrectPositions(first) })).EnsureSuccessStatusCode();

        // Move the attempt's deadline into the past (instead of moving the shared test clock).
        await api.WithDbAsync(db => db.Set<ExamAttempt>().Where(a => a.Id == Guid.Parse(attemptId!))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeadlineAt, a => a.StartedAt.AddMinutes(-1))));
        await (await client.PutAsJsonAsync($"/api/v1/me/learning/attempts/{attemptId}/answers",
            new { questionId = first.GetProperty("id").GetString(), selected = new[] { 0 } })).ShouldFailAsync(409, "learning.time_expired");
        // Late answers are ignored: only the saved answer counts.
        var graded = await LearningHelpers.SubmitAsync(client, attempt, pass: true);
        Assert.Equal("Expired", graded.GetProperty("status").GetString());
        Assert.Equal(1, graded.GetProperty("result").GetProperty("correctCount").GetInt32());

        // Two more attempts, then the 24-hour budget (3) is used up.
        for (var i = 0; i < 2; i++) await LearningHelpers.SubmitAsync(client, await LearningHelpers.StartAsync(client), pass: false);
        await (await client.PostAsync($"/api/v1/me/learning/courses/{Slug}/exam/attempts", null)).ShouldFailAsync(409, "learning.attempt_limit");
        var overview = await (await client.GetAsync($"/api/v1/me/learning/courses/{Slug}/exam")).ReadJsonAsync();
        Assert.Equal(0, overview.GetProperty("attemptsRemaining").GetInt32());
        Assert.Equal("learning.attempt_limit", overview.GetProperty("blockedReason").GetString());
        Assert.NotEqual(JsonValueKind.Null, overview.GetProperty("nextAttemptAt").ValueKind);
        Assert.Equal(3, await api.WithDbAsync(db => db.Set<ExamAttempt>().CountAsync(a => a.UserId == user.Id)));
    }

    [Fact]
    public async Task Writes_that_act_as_the_learner_and_certificate_admin_are_denied_while_impersonating()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var participant = await api.CreateUserAsync();
        var token = await Impersonating.TokenAsync(admin, participant.Id);
        (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/me/learning", token)).EnsureSuccessStatusCode();
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/me/learning/courses/{Slug}/enrol", token))
            .ShouldFailAsync(403, "auth.impersonation_forbidden_action");
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/me/learning/courses/{Slug}/exam/attempts", token))
            .ShouldFailAsync(403, "auth.impersonation_forbidden_action");
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/me/learning/attempts/{Guid.NewGuid()}/submit", token, new { }))
            .ShouldFailAsync(403, "auth.impersonation_forbidden_action");
    }

    [Fact]
    public async Task Learning_admin_permissions_authoring_publishing_analytics_and_export()
    {
        var (_, participant) = await api.CreateClientAsync();
        await (await participant.GetAsync("/api/v1/admin/learning/courses")).ShouldFailAsync(403);
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        (await manager.GetAsync("/api/v1/admin/learning/courses")).EnsureSuccessStatusCode();
        // Campaign managers author courses but cannot issue or revoke certificates.
        await (await manager.PostAsJsonAsync("/api/v1/admin/learning/certificates",
            new { userId = Guid.NewGuid(), courseId = Guid.NewGuid(), reason = "Because", confirm = true })).ShouldFailAsync(403);

        // Validation uses the pack contract (identical MCQ rules).
        var bad = LearningHelpers.Pack.Clone();
        bad.Slug = "admin-" + Guid.NewGuid().ToString("N")[..8];
        bad.FinalExam!.Pool![0].Correct = new List<int> { 0, 1 };
        bad.FinalExam.Pool[1].Options!.Add("All of the above");
        var report = await (await manager.PostAsJsonAsync("/api/v1/admin/learning/courses/validate",
            new { document = JsonDocument.Parse(bad.ToJson()).RootElement })).ReadJsonAsync();
        Assert.False(report.GetProperty("valid").GetBoolean());
        var issues = report.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("path").GetString()).ToList();
        Assert.Contains("finalExam.pool[0].correct", issues);
        Assert.Contains("finalExam.pool[1].options[4]", issues);
        await (await manager.PostAsJsonAsync("/api/v1/admin/learning/courses", new { document = JsonDocument.Parse(bad.ToJson()).RootElement }))
            .ShouldFailAsync(400, "learning.invalid_course");

        // Create (draft) → not public → publish → public.
        var good = LearningHelpers.Pack.Clone();
        good.Slug = bad.Slug;
        good.Title = "Staff-authored course";
        var created = await manager.PostAsJsonAsync("/api/v1/admin/learning/courses", new { document = JsonDocument.Parse(good.ToJson()).RootElement });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var detail = await created.ReadJsonAsync();
        var courseId = detail.GetProperty("summary").GetProperty("id").GetString();
        await (await manager.PostAsJsonAsync("/api/v1/admin/learning/courses", new { document = JsonDocument.Parse(good.ToJson()).RootElement }))
            .ShouldFailAsync(409, "learning.slug_taken");
        var anon = api.CreateClient();
        await (await anon.GetAsync($"/api/v1/public/learning/courses/{good.Slug}")).ShouldFailAsync(404);
        var versionId = detail.GetProperty("versions")[0].GetProperty("id").GetString();
        var stamp = detail.GetProperty("summary").GetProperty("concurrencyStamp").GetString();
        var published = await (await manager.PostAsJsonAsync($"/api/v1/admin/learning/courses/{courseId}/publish", new { versionId, concurrencyStamp = stamp })).ReadJsonAsync();
        Assert.Equal("Published", published.GetProperty("summary").GetProperty("status").GetString());
        (await anon.GetAsync($"/api/v1/public/learning/courses/{good.Slug}")).EnsureSuccessStatusCode();
        // A stale stamp is refused; the slug cannot change.
        await (await manager.PostAsJsonAsync($"/api/v1/admin/learning/courses/{courseId}/unpublish", new { concurrencyStamp = stamp })).ShouldFailAsync(409);
        var renamed = good.Clone();
        renamed.Slug = "other-slug";
        await (await manager.PostAsJsonAsync($"/api/v1/admin/learning/courses/{courseId}/versions",
            new { document = JsonDocument.Parse(renamed.ToJson()).RootElement, concurrencyStamp = published.GetProperty("summary").GetProperty("concurrencyStamp").GetString() }))
            .ShouldFailAsync(400, "learning.slug_immutable");

        // Lesson video: a new version with the video fields.
        var video = await (await manager.PutAsJsonAsync($"/api/v1/admin/learning/courses/{courseId}/lessons/submitting-posts-that-pass-review/video",
            new { src = "https://videos.example.com/lesson.mp4", poster = (string?)null, captions = (string?)null, publish = true,
                concurrencyStamp = published.GetProperty("summary").GetProperty("concurrencyStamp").GetString() })).ReadJsonAsync();
        Assert.Equal(2, video.GetProperty("summary").GetProperty("publishedVersionNumber").GetInt32());
        var lesson = await (await anon.GetAsync($"/api/v1/public/learning/courses/{good.Slug}/lessons/submitting-posts-that-pass-review")).ReadJsonAsync();
        Assert.Equal("https://videos.example.com/lesson.mp4", lesson.GetProperty("video").GetProperty("src").GetString());
        await (await manager.PutAsJsonAsync($"/api/v1/admin/learning/courses/{courseId}/lessons/campaigns-and-eligibility/video",
            new { src = "https://videos.example.com/x.mp4", concurrencyStamp = video.GetProperty("summary").GetProperty("concurrencyStamp").GetString() }))
            .ShouldFailAsync(409, "learning.not_a_video_lesson");

        // Analytics and export over a learner who passes.
        var (_, learner) = await api.CreateClientAsync();
        await LearningHelpers.EnrolAndCompleteAsync(learner);
        await LearningHelpers.SubmitAsync(learner, await LearningHelpers.StartAsync(learner), pass: true);
        var starter = await api.WithDbAsync(db => db.Set<Course>().Where(c => c.Slug == Slug).Select(c => c.Id).SingleAsync());
        var questions = await (await manager.GetAsync($"/api/v1/admin/learning/courses/{starter}/questions")).ReadJsonAsync();
        Assert.Equal(15, questions.GetArrayLength());
        Assert.Contains(questions.EnumerateArray(), q => q.GetProperty("answered").GetInt32() > 0 && q.GetProperty("percentCorrect").GetInt32() == 100);
        var learners = await (await manager.GetAsync($"/api/v1/admin/learning/courses/{starter}/learners?passed=true")).ReadJsonAsync();
        Assert.True(learners.GetProperty("total").GetInt32() >= 1);
        var csv = await manager.GetAsync($"/api/v1/admin/learning/courses/{starter}/results.csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("email,name,enrolled_at", await csv.Content.ReadAsStringAsync());
        var list = await (await manager.GetAsync("/api/v1/admin/learning/courses?category=Platform")).ReadJsonAsync();
        var row = list.GetProperty("items").EnumerateArray().Single(c => c.GetProperty("slug").GetString() == Slug);
        Assert.True(row.GetProperty("certificates").GetInt32() >= 1);
    }

    [Fact]
    public async Task Admins_issue_certificates_manually_and_the_issuer_setting_changes_linkedin_links()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var learner = await api.CreateUserAsync();
        var courseId = await api.WithDbAsync(db => db.Set<Course>().Where(c => c.Slug == Slug).Select(c => c.Id).SingleAsync());
        await (await admin.PutAsJsonAsync("/api/v1/admin/settings/learning.linkedInOrganizationId",
            new { value = "12345678", confirm = true, reason = "Company page created" })).EnsureSuccessAsync();
        var issued = await admin.PostAsJsonAsync("/api/v1/admin/learning/certificates",
            new { userId = learner.Id, courseId, reason = "Completed the in-person workshop", confirm = true });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var cert = await issued.ReadJsonAsync();
        Assert.True(cert.GetProperty("manual").GetBoolean());
        await (await admin.PostAsJsonAsync("/api/v1/admin/learning/certificates",
            new { userId = learner.Id, courseId, reason = "Twice", confirm = true })).ShouldFailAsync(409, "learning.already_certified");
        var client = await api.LoginAsync(learner);
        var mine = await (await client.GetAsync("/api/v1/me/learning/certificates")).ReadJsonAsync();
        var url = mine[0].GetProperty("links").GetProperty("linkedInAddToProfileUrl").GetString()!;
        Assert.Contains("organizationId=12345678", url);
        Assert.DoesNotContain("organizationName=", url);
    }
}

internal static class LearningHttpExtensions
{
    public static async Task EnsureSuccessAsync(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
