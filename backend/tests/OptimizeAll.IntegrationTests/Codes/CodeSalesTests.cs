using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Codes.CodesKit;

namespace OptimizeAll.IntegrationTests.Codes;

/// <summary>Participants' code sales: my codes, reporting, validation, edits, tenancy, review, ledger, caps, FX, refunds.</summary>
public sealed class CodeSalesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly CodesKit kit = new(api);

    // ---------------------------------------------------------------- my codes & tenancy

    [Fact]
    public async Task Participants_see_only_their_own_and_their_groups_codes()
    {
        var (program, codeId, user, participant, manager) = await kit.AssignedAsync();
        var shared = await CodeAsync(manager, Id(program), "SQUAD-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant());
        var group = await GroupAsync(manager, user.Id);
        await AssignAsync(manager, Id(shared), groupId: Id(group), validFrom: kit.Now.AddDays(-5));
        var otherCode = await CodeAsync(manager, Id(program));
        var (other, otherClient) = await kit.ParticipantAsync();
        await AssignAsync(manager, Id(otherCode), other.Id, validFrom: kit.Now.AddDays(-1));

        var mine = (await (await participant.GetAsync("/api/v1/me/codes")).ReadJsonAsync()).EnumerateArray().ToList();
        Assert.Equal(2, mine.Count);
        var personal = mine.Single(c => c.GetProperty("codeId").GetGuid() == codeId);
        Assert.False(personal.GetProperty("shared").GetBoolean());
        Assert.True(personal.GetProperty("isActive").GetBoolean());
        Assert.Equal("10% of net", personal.GetProperty("yourRate").GetString());
        Assert.Contains("code=", personal.GetProperty("shareUrl").GetString());
        Assert.StartsWith("https://shop.example.com/glow?", personal.GetProperty("shareUrl").GetString());
        Assert.True(mine.Single(c => c.GetProperty("codeId").GetGuid() == Id(shared)).GetProperty("shared").GetBoolean());
        Assert.DoesNotContain(mine, c => c.GetProperty("codeId").GetGuid() == Id(otherCode));

        // Someone else's code is a 404 (never 403) and so is their sale.
        await (await kit.SubmitAsync(participant, Id(otherCode))).ShouldFailAsync(404);
        var theirs = await kit.SubmitOkAsync(otherClient, Id(otherCode));
        await (await participant.GetAsync($"/api/v1/me/code-sales/{Id(theirs)}")).ShouldFailAsync(404);
        await (await participant.PostAsJsonAsync($"/api/v1/me/code-sales/{Id(theirs)}/withdraw", new { })).ShouldFailAsync(404);
        var list = await (await participant.GetAsync("/api/v1/me/code-sales")).ReadJsonAsync();
        Assert.Equal(0, list.GetProperty("total").GetInt32());
        // Draft programs are invisible to participants.
        var draft = await kit.ProgramAsync(manager, kit.ProgramBody(activate: false));
        var draftCode = await CodeAsync(manager, Id(draft));
        await AssignAsync(manager, Id(draftCode), user.Id);
        Assert.Equal(2, (await (await participant.GetAsync("/api/v1/me/codes")).ReadJsonAsync()).GetArrayLength());
        await (await kit.SubmitAsync(participant, Id(draftCode))).ShouldFailAsync(404);
    }

    // ---------------------------------------------------------------- reporting a sale

    [Fact]
    public async Task Participant_reports_a_sale_with_proof_edits_and_withdraws_it()
    {
        var (program, codeId, _, participant, manager) = await kit.AssignedAsync(kit.ProgramBody(requireProof: true));
        await (await kit.SubmitAsync(participant, codeId)).ShouldFailAsync(400, "code_sale.proof_required");
        var response = await kit.SubmitAsync(participant, codeId, "A-1001", net: 80m, discount: 20m, proof: true);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sale = await response.ReadJsonAsync();
        Assert.Equal("Pending", sale.GetProperty("status").GetString());
        Assert.Equal(8m, sale.GetProperty("estimatedCommission").GetDecimal());
        Assert.True(sale.GetProperty("canEdit").GetBoolean());
        var proofUrl = sale.GetProperty("proofUrl").GetString()!;

        // The proof is private: its owner and reviewers can read it, another participant can't.
        (await participant.GetAsync(proofUrl)).EnsureSuccessStatusCode();
        var (_, reviewer) = await kit.ReviewerAsync();
        (await reviewer.GetAsync(proofUrl)).EnsureSuccessStatusCode();
        var (_, stranger) = await kit.ParticipantAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(proofUrl)).StatusCode);

        // Edit while pending (stale stamp → 409).
        var edited = await (await participant.PutAsync($"/api/v1/me/code-sales/{Id(sale)}",
            SaleForm(codeId, "A-1001", kit.Now.AddHours(-3), 120m, stamp: Stamp(sale)))).ReadJsonAsync();
        Assert.Equal(120m, edited.GetProperty("netAmount").GetDecimal());
        Assert.Equal(12m, edited.GetProperty("estimatedCommission").GetDecimal());
        await (await participant.PutAsync($"/api/v1/me/code-sales/{Id(sale)}", SaleForm(codeId, "A-1001", kit.Now.AddHours(-3), 90m, stamp: Stamp(sale))))
            .ShouldFailAsync(409, "concurrency.conflict");

        var withdrawn = await (await participant.PostAsJsonAsync($"/api/v1/me/code-sales/{Id(sale)}/withdraw", new { reason = "Duplicate" })).ReadJsonAsync();
        Assert.Equal("Withdrawn", withdrawn.GetProperty("status").GetString());
        await (await participant.PostAsJsonAsync($"/api/v1/me/code-sales/{Id(sale)}/withdraw", new { })).ShouldFailAsync(409, "code_sale.not_withdrawable");
        // The order is free again after a withdrawal.
        (await kit.SubmitAsync(participant, codeId, "A-1001", proof: true)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Order_date_code_validity_and_program_rules_are_enforced()
    {
        var body = kit.ProgramBody(startsAt: kit.Now.AddDays(-40));
        var (program, codeId, _, participant, manager) = await kit.AssignedAsync(body, assignedFrom: kit.Now.AddDays(-10));
        // Future, too old, before the assignment.
        await (await kit.SubmitAsync(participant, codeId, orderDate: kit.Now.AddHours(2))).ShouldFailAsync(400, "code_sale.order_in_future");
        await (await kit.SubmitAsync(participant, codeId, orderDate: kit.Now.AddDays(-61))).ShouldFailAsync(400, "code_sale.order_too_old");
        await (await kit.SubmitAsync(participant, codeId, orderDate: kit.Now.AddDays(-12))).ShouldFailAsync(409, "code_sale.before_assignment");
        await (await kit.SubmitAsync(participant, codeId, net: 0m)).ShouldFailAsync(400);
        await (await kit.SubmitAsync(participant, codeId, currency: "XYZ")).ShouldFailAsync(400, "code_sale.currency_unsupported");

        // A code used after its expiry (assigned while valid, then its end date was set).
        var (user2, participant2) = await kit.ParticipantAsync();
        var expiring = await CodeAsync(manager, Id(program));
        var assigned = await AssignAsync(manager, Id(expiring), user2.Id, validFrom: kit.Now.AddDays(-9));
        await (await manager.PutAsJsonAsync($"/api/v1/admin/discount-codes/{Id(expiring)}",
            new { validTo = kit.Now.AddDays(-2), concurrencyStamp = Stamp(assigned.GetProperty("code")), reason = "Brand ended it" })).ReadJsonAsync();
        await (await kit.SubmitAsync(participant2, Id(expiring), orderDate: kit.Now.AddDays(-1))).ShouldFailAsync(409, "code_sale.code_expired");
        (await kit.SubmitAsync(participant2, Id(expiring), orderDate: kit.Now.AddDays(-3))).EnsureSuccessStatusCode();

        // A paused program refuses new sales.
        var current = await (await manager.GetAsync($"/api/v1/admin/code-programs/{Id(program)}")).ReadJsonAsync();
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{Id(program)}/status", new { status = "Paused", concurrencyStamp = Stamp(current) }))
            .ReadJsonAsync();
        await (await kit.SubmitAsync(participant, codeId)).ShouldFailAsync(409, "code_program.paused");
    }

    [Fact]
    public async Task Duplicate_orders_are_refused_and_the_first_group_member_wins()
    {
        var (program, codeId, user, participant, manager) = await kit.AssignedAsync();
        await kit.SubmitOkAsync(participant, codeId, "DUP-1");
        await (await kit.SubmitAsync(participant, codeId, "dup-1 ")).ShouldFailAsync(409, "code_sale.duplicate_order");

        var (ann, annClient) = await kit.ParticipantAsync();
        var (ben, benClient) = await kit.ParticipantAsync();
        var group = await GroupAsync(manager, ann.Id, ben.Id);
        var shared = await CodeAsync(manager, Id(program));
        await AssignAsync(manager, Id(shared), groupId: Id(group), validFrom: kit.Now.AddDays(-3));
        var first = await kit.SubmitOkAsync(annClient, Id(shared), "SHARED-9");
        var second = await kit.SubmitAsync(benClient, Id(shared), "SHARED-9");
        await second.ShouldFailAsync(409, "code_sale.duplicate_order");
        Assert.Contains("another member of your group", await second.Content.ReadAsStringAsync());
        var sale = await (await manager.GetAsync($"/api/v1/admin/code-sales/{Id(first)}")).ReadJsonAsync();
        Assert.Equal(ann.Id, sale.GetProperty("person").GetProperty("id").GetGuid());
        Assert.Equal(Id(group), sale.GetProperty("group").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Concurrent_reports_of_one_order_create_a_single_sale()
    {
        var (program, _, _, _, manager) = await kit.AssignedAsync();
        var members = new List<(TestUser User, HttpClient Client)>();
        for (var i = 0; i < 6; i++) members.Add(await kit.ParticipantAsync());
        var group = await GroupAsync(manager, members.Select(m => m.User.Id).ToArray());
        var shared = await CodeAsync(manager, Id(program));
        await AssignAsync(manager, Id(shared), groupId: Id(group), validFrom: kit.Now.AddDays(-3));
        var responses = await Task.WhenAll(members.Select(m => kit.SubmitAsync(m.Client, Id(shared), "RACE-1")));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<CodeSale>().CountAsync(s => s.ProgramId == Id(program) && s.NormalizedOrderReference == "RACE-1")));
    }

    // ---------------------------------------------------------------- review & ledger

    [Fact]
    public async Task Approval_writes_one_idempotent_ledger_commission_with_its_source()
    {
        var (program, codeId, user, participant, manager) = await kit.AssignedAsync();
        var sale = await kit.SubmitOkAsync(participant, codeId, "LEDGER-1", net: 250m);
        var (_, reviewer) = await kit.ReviewerAsync();
        var queue = await (await reviewer.GetAsync($"/api/v1/admin/code-sales?status=Pending&programId={Id(program)}")).ReadJsonAsync();
        Assert.Contains(queue.GetProperty("items").EnumerateArray(), s => Id(s) == Id(sale) && s.GetProperty("canDecide").GetBoolean());

        var stamp = await SaleStampAsync(reviewer, Id(sale));
        var approved = await (await DecideAsync(reviewer, Id(sale), stamp: stamp)).ReadJsonAsync();
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal(25m, approved.GetProperty("commissionAmount").GetDecimal());
        Assert.Contains("Program rate", approved.GetProperty("payoutSourceLabel").GetString());
        // Deciding again: already decided (stale stamp or not).
        await (await DecideAsync(reviewer, Id(sale), stamp: stamp)).ShouldFailAsync(409, "code_sale.already_decided");

        var entry = Assert.Single(await kit.EarningsAsync(Id(sale)));
        Assert.Equal(EarningType.SaleCommission, entry.Type);
        Assert.Equal(EarningStatus.Approved, entry.Status);
        Assert.Equal(25m, entry.Amount);
        Assert.Equal(RateSourceLevel.CodeProgramRules, entry.RateSource);
        Assert.Equal(Id(program), entry.CodeProgramId);
        Assert.Equal(user.Id, entry.UserId);
        Assert.Equal($"codesale:{Id(sale):N}:commission", entry.IdempotencyKey);
        Assert.NotNull(entry.AvailableAt);

        // The participant sees it in their sale and code stats; the notification links to the sale.
        var mine = await (await participant.GetAsync($"/api/v1/me/code-sales/{Id(sale)}")).ReadJsonAsync();
        Assert.Equal(25m, mine.GetProperty("commissionAmount").GetDecimal());
        Assert.False(mine.GetProperty("canEdit").GetBoolean());
        await (await participant.PutAsync($"/api/v1/me/code-sales/{Id(sale)}", SaleForm(codeId, "LEDGER-1", kit.Now.AddHours(-1), 1m, stamp: Stamp(mine))))
            .ShouldFailAsync(409, "code_sale.not_editable");
        var codes = (await (await participant.GetAsync("/api/v1/me/codes")).ReadJsonAsync()).EnumerateArray().Single();
        Assert.Equal(25m, codes.GetProperty("stats").GetProperty("commissionApproved").GetDecimal());
        Assert.True(await api.WithDbAsync(db => db.Set<OptimizeAll.Domain.Notifications.Notification>()
            .AnyAsync(n => n.UserId == user.Id && n.Type == "code_sale.decision" && n.LinkUrl == $"/app/codes/sales/{Id(sale)}")));
        // Ledger UI shows the source.
        var (_, finance) = await kit.FinanceAsync();
        var ledger = await (await finance.GetAsync($"/api/v1/finance/ledger?userId={user.Id}")).ReadJsonAsync();
        Assert.Contains(ledger.GetProperty("items").EnumerateArray(), e => e.GetProperty("type").GetString() == "SaleCommission");
    }

    [Fact]
    public async Task Reviewers_cannot_decide_their_own_or_self_entered_sales()
    {
        var (_, manager) = await kit.ManagerAsync();
        var program = await kit.ProgramAsync(manager);
        // A reviewer who is also a participant.
        var both = await api.CreateUserAsync(new[] { Role.Participant, Role.Reviewer });
        var bothClient = await api.LoginAsync(both);
        var code = await CodeAsync(manager, Id(program));
        await AssignAsync(manager, Id(code), both.Id, validFrom: kit.Now.AddDays(-2));
        var own = await kit.SubmitOkAsync(bothClient, Id(code));
        await (await DecideAsync(bothClient, Id(own))).ShouldFailAsync(403, "code_sale.self_review");
        var detail = await (await bothClient.GetAsync($"/api/v1/admin/code-sales/{Id(own)}")).ReadJsonAsync();
        Assert.False(detail.GetProperty("canDecide").GetBoolean());

        // Four eyes: an admin who entered a sale can't approve it; another reviewer can.
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var (person, _) = await kit.ParticipantAsync();
        var code2 = await CodeAsync(manager, Id(program), "FOUR-EYES-" + Guid.NewGuid().ToString("N")[..4]);
        await AssignAsync(manager, Id(code2), person.Id, validFrom: kit.Now.AddDays(-5));
        var entered = await (await admin.PostAsJsonAsync($"/api/v1/admin/code-programs/{Id(program)}/sales", new
        {
            code = code2.GetProperty("code").GetString(), orderReference = "PHONE-77", orderDate = kit.Now.AddDays(-1), netAmount = 40m, currency = "USD",
            reason = "Brand emailed the order",
        })).ReadJsonAsync();
        Assert.Equal("Admin", entered.GetProperty("source").GetString());
        Assert.Equal(person.Id, entered.GetProperty("person").GetProperty("id").GetGuid());
        await (await DecideAsync(admin, Id(entered))).ShouldFailAsync(403, "code_sale.four_eyes");
        var (_, reviewer) = await kit.ReviewerAsync();
        await ApproveAsync(reviewer, Id(entered));
        // Reject needs a reason.
        var another = await (await admin.PostAsJsonAsync($"/api/v1/admin/code-programs/{Id(program)}/sales", new
        {
            code = code2.GetProperty("code").GetString(), orderReference = "PHONE-78", orderDate = kit.Now.AddDays(-1), netAmount = 40m, currency = "USD",
            reason = "Brand emailed the order",
        })).ReadJsonAsync();
        await (await reviewer.PostAsJsonAsync($"/api/v1/admin/code-sales/{Id(another)}/decision",
            new { decision = "Reject", concurrencyStamp = Stamp(another) })).ShouldFailAsync(400, "code_sale.reason_required");
        await (await reviewer.PostAsJsonAsync($"/api/v1/admin/code-sales/{Id(another)}/decision",
            new { decision = "Maybe", concurrencyStamp = Stamp(another) })).ShouldFailAsync(400);
    }

    [Fact]
    public async Task Request_info_reject_and_resubmit()
    {
        var (_, codeId, _, participant, _) = await kit.AssignedAsync();
        var sale = await kit.SubmitOkAsync(participant, codeId, "INFO-1");
        var (_, reviewer) = await kit.ReviewerAsync();
        var queried = await (await DecideAsync(reviewer, Id(sale), "RequestInfo", "Please attach the order confirmation")).ReadJsonAsync();
        Assert.Equal("NeedsInfo", queried.GetProperty("status").GetString());
        var mine = await (await participant.GetAsync($"/api/v1/me/code-sales/{Id(sale)}")).ReadJsonAsync();
        Assert.Equal("Please attach the order confirmation", mine.GetProperty("decisionReason").GetString());
        var resubmitted = await (await participant.PutAsync($"/api/v1/me/code-sales/{Id(sale)}",
            SaleForm(codeId, "INFO-1", kit.Now.AddHours(-2), 100m, proof: true, stamp: Stamp(mine)))).ReadJsonAsync();
        Assert.Equal("Pending", resubmitted.GetProperty("status").GetString());
        Assert.Contains(resubmitted.GetProperty("events").EnumerateArray(), e => e.GetProperty("action").GetString() == "resubmitted");

        var rejected = await (await DecideAsync(reviewer, Id(sale), "Reject", "The brand has no such order")).ReadJsonAsync();
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Empty(await kit.EarningsAsync(Id(sale)));
        // A rejected order may be reported again (e.g. by the right person).
        (await kit.SubmitAsync(participant, codeId, "INFO-1")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Concurrent_approvals_record_a_single_commission()
    {
        var (_, codeId, _, participant, _) = await kit.AssignedAsync();
        var sale = await kit.SubmitOkAsync(participant, codeId);
        var reviewers = new List<HttpClient>();
        for (var i = 0; i < 4; i++) reviewers.Add((await kit.ReviewerAsync()).Client);
        var stamp = await SaleStampAsync(reviewers[0], Id(sale));
        var results = await Task.WhenAll(reviewers.Select(r => DecideAsync(r, Id(sale), stamp: stamp)));
        Assert.Equal(1, results.Count(r => r.IsSuccessStatusCode));
        Assert.All(results.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Single(await kit.EarningsAsync(Id(sale)));
    }

    [Fact]
    public async Task Overrides_tiers_caps_and_budget_price_approvals()
    {
        var body = kit.ProgramBody(payoutType: "FlatPerSale", flat: 5m,
            tiers: new object[] { new { thresholdSales = 2, flatAmount = 8m, bonusAmount = 20m } }, daily: 40m, budget: 60m);
        var (program, codeId, user, participant, manager) = await kit.AssignedAsync(body);
        var (reviewerUser, reviewer) = await kit.ReviewerAsync();

        var s1 = await kit.SubmitOkAsync(participant, codeId);
        Assert.Equal(5m, (await ApproveAsync(reviewer, Id(s1))).GetProperty("commissionAmount").GetDecimal());
        // 2nd approved sale: still the program rate (tier applies after 2) + the 20 bonus for reaching 2.
        var s2 = await kit.SubmitOkAsync(participant, codeId);
        var a2 = await ApproveAsync(reviewer, Id(s2));
        Assert.Equal(25m, a2.GetProperty("commissionAmount").GetDecimal());
        var bonus = (await kit.EarningsAsync(Id(s2))).Single(e => e.Type == EarningType.SaleTierBonus);
        Assert.Equal(20m, bonus.Amount);
        Assert.Equal($"codetier:{Id(program):N}:{user.Id:N}:2", bonus.IdempotencyKey);
        // 3rd: tier rate 8, but the daily cap (40) leaves 10 → 8 fits.
        var s3 = await kit.SubmitOkAsync(participant, codeId);
        var a3 = await ApproveAsync(reviewer, Id(s3));
        Assert.Equal(8m, a3.GetProperty("commissionAmount").GetDecimal());
        Assert.Equal(RateSourceLevel.CodeProgramTier, (await kit.EarningsAsync(Id(s3))).Single().RateSource);
        // 4th: daily cap leaves 2 of 8.
        var s4 = await kit.SubmitOkAsync(participant, codeId);
        var a4 = await ApproveAsync(reviewer, Id(s4));
        Assert.Equal(2m, a4.GetProperty("commissionAmount").GetDecimal());
        Assert.Contains("daily_cap", a4.GetProperty("appliedCaps").EnumerateArray().Select(c => c.GetString()));

        // A group override replaces the rate (source recorded with the group); the budget (60) is now nearly spent (40 used).
        var group = await GroupAsync(manager, user.Id);
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{Id(program)}/overrides", new
        {
            target = "Group", groupId = Id(group), payoutType = "FlatPerSale", flatAmount = 30m, reason = "VIP group",
        })).ReadJsonAsync();
        api.Clock.Advance(TimeSpan.FromDays(1)); // a new day: the daily cap resets, the budget doesn't
        participant = await api.LoginAsync(user); // access tokens are short-lived
        reviewer = await api.LoginAsync(reviewerUser);
        var s5 = await kit.SubmitOkAsync(participant, codeId);
        var a5 = await ApproveAsync(reviewer, Id(s5));
        Assert.Equal(20m, a5.GetProperty("commissionAmount").GetDecimal());
        Assert.Contains("program_budget", a5.GetProperty("appliedCaps").EnumerateArray().Select(c => c.GetString()));
        var e5 = (await kit.EarningsAsync(Id(s5))).Single();
        Assert.Equal(RateSourceLevel.CodeGroupOverride, e5.RateSource);
        Assert.Equal(Id(group), e5.RateGroupId);
        // Budget exhausted: approved with no commission (never a zero ledger row).
        var s6 = await kit.SubmitOkAsync(participant, codeId);
        var a6 = await ApproveAsync(reviewer, Id(s6));
        Assert.Equal("Approved", a6.GetProperty("status").GetString());
        Assert.Equal(0m, a6.GetProperty("commissionAmount").GetDecimal());
        Assert.Empty(await kit.EarningsAsync(Id(s6)));
        // A personal override wins over the group's.
        (_, manager) = await kit.ManagerAsync();
        await (await manager.PostAsJsonAsync($"/api/v1/admin/code-programs/{Id(program)}/overrides", new
        {
            target = "Person", userId = user.Id, payoutType = "PercentOfNet", percent = 50m, reason = "Personal deal",
        })).ReadJsonAsync();
        var codes = (await (await participant.GetAsync("/api/v1/me/codes")).ReadJsonAsync()).EnumerateArray().Single();
        Assert.Equal("50% of net", codes.GetProperty("yourRate").GetString());
    }

    [Fact]
    public async Task Foreign_currency_orders_convert_at_the_order_date_and_missing_rates_are_refused()
    {
        var (_, codeId, _, participant, _) = await kit.AssignedAsync(kit.ProgramBody(currency: "USD", percent: 10m));
        // No SEK support at all; no GBP rate yet.
        await (await kit.SubmitAsync(participant, codeId, currency: "GBP")).ShouldFailAsync(409, "fx.rate_missing");
        await kit.AddExchangeRateAsync("GBP", "USD", 1.25m);
        var sale = await kit.SubmitOkAsync(participant, codeId, currency: "GBP", net: 80.555m);
        Assert.Equal(80.56m, sale.GetProperty("netAmount").GetDecimal());
        var (_, reviewer) = await kit.ReviewerAsync();
        var approved = await ApproveAsync(reviewer, Id(sale));
        Assert.Equal(100.70m, approved.GetProperty("programNetAmount").GetDecimal()); // 80.56 × 1.25
        Assert.Equal(10.07m, approved.GetProperty("commissionAmount").GetDecimal());

        // JPY orders into a JPY program: whole yen.
        await kit.AddExchangeRateAsync("JPY", "USD", 0.0067m);
        var (_, jpyCode, _, jpyParticipant, _) = await kit.AssignedAsync(kit.ProgramBody(currency: "JPY", percent: 7.5m));
        var jpy = await kit.SubmitOkAsync(jpyParticipant, jpyCode, currency: "JPY", net: 12_345.4m, discount: 0m);
        Assert.Equal(12_345m, jpy.GetProperty("netAmount").GetDecimal());
        var jpyApproved = await ApproveAsync(reviewer, Id(jpy));
        Assert.Equal(926m, jpyApproved.GetProperty("commissionAmount").GetDecimal()); // 925.875 → 926
    }

    [Fact]
    public async Task Suspended_participants_get_no_commission_and_test_accounts_are_flagged()
    {
        var (_, codeId, user, participant, _) = await kit.AssignedAsync();
        var sale = await kit.SubmitOkAsync(participant, codeId);
        await api.WithDbAsync(async db =>
        {
            var u = await db.Set<User>().FirstAsync(x => x.Id == user.Id);
            u.Status = UserStatus.Suspended;
            u.IsTestAccount = true;
            await db.SaveChangesAsync();
        });
        var (_, reviewer) = await kit.ReviewerAsync();
        var detail = await (await reviewer.GetAsync($"/api/v1/admin/code-sales/{Id(sale)}")).ReadJsonAsync();
        Assert.True(detail.GetProperty("isTestAccount").GetBoolean());
        Assert.Equal("Suspended", detail.GetProperty("userStatus").GetString());
        await (await DecideAsync(reviewer, Id(sale))).ShouldFailAsync(409, "participant.not_active");
        Assert.Empty(await kit.EarningsAsync(Id(sale)));
    }

    // ---------------------------------------------------------------- refunds

    [Fact]
    public async Task Refunds_reverse_unpaid_commissions_and_claw_back_paid_ones()
    {
        var (program, codeId, user, participant, _) = await kit.AssignedAsync();
        var (_, reviewer) = await kit.ReviewerAsync();
        var (_, finance) = await kit.FinanceAsync();

        // Unpaid: cancelled with a zero-sum reversal leg.
        var unpaid = await kit.SubmitOkAsync(participant, codeId, "REF-1");
        await ApproveAsync(reviewer, Id(unpaid));
        await (await reviewer.PostAsJsonAsync($"/api/v1/admin/code-sales/{Id(unpaid)}/refund", new { reason = "Returned", confirm = true })).ShouldFailAsync(403);
        await (await finance.PostAsJsonAsync($"/api/v1/admin/code-sales/{Id(unpaid)}/refund", new { reason = "Returned", confirm = false }))
            .ShouldFailAsync(400, "confirmation.required");
        var refunded = await (await finance.PostAsJsonAsync($"/api/v1/admin/code-sales/{Id(unpaid)}/refund",
            new { reason = "Customer returned the order", confirm = true })).ReadJsonAsync();
        Assert.Equal("Refunded", refunded.GetProperty("status").GetString());
        var legs = await kit.EarningsAsync(Id(unpaid));
        Assert.Equal(EarningStatus.Reversed, legs.Single(e => e.Type == EarningType.SaleCommission).Status);
        var reversal = legs.Single(e => e.Type == EarningType.Reversal);
        Assert.Equal(-10m, reversal.Amount);
        Assert.Equal(EarningStatus.Reversed, reversal.Status);
        Assert.Equal(RateSourceLevel.CodeProgramRules, reversal.RateSource); // copied onto the reversal leg
        Assert.Equal(Id(unpaid), reversal.CodeSaleId);
        await (await finance.PostAsJsonAsync($"/api/v1/admin/code-sales/{Id(unpaid)}/refund", new { reason = "Again", confirm = true }))
            .ShouldFailAsync(409, "code_sale.not_refundable");

        // Paid: a negative Approved entry netted against the next payout (clawback).
        var paid = await kit.SubmitOkAsync(participant, codeId, "REF-2");
        await ApproveAsync(reviewer, Id(paid));
        await api.WithDbAsync(async db =>
        {
            var e = await db.Set<EarningEntry>().FirstAsync(x => x.CodeSaleId == Id(paid) && x.Type == EarningType.SaleCommission);
            e.Status = EarningStatus.Paid;
            e.PaidAt = kit.Now;
            await db.SaveChangesAsync();
        });
        await (await finance.PostAsJsonAsync($"/api/v1/admin/code-sales/{Id(paid)}/refund", new { reason = "Chargeback", confirm = true })).ReadJsonAsync();
        var paidLegs = await kit.EarningsAsync(Id(paid));
        Assert.Equal(EarningStatus.Paid, paidLegs.Single(e => e.Type == EarningType.SaleCommission).Status);
        var clawback = paidLegs.Single(e => e.Type == EarningType.Reversal);
        Assert.Equal(EarningStatus.Approved, clawback.Status);
        Assert.Equal(-10m, clawback.Amount);

        // Pending sale refunded → cancelled, no earnings; it keeps the order (can't be claimed again).
        var pending = await kit.SubmitOkAsync(participant, codeId, "REF-3");
        var cancelled = await (await finance.PostAsJsonAsync($"/api/v1/admin/code-sales/{Id(pending)}/refund",
            new { reason = "Order cancelled", confirm = true })).ReadJsonAsync();
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        await (await kit.SubmitAsync(participant, codeId, "REF-3")).ShouldFailAsync(409, "code_sale.duplicate_order");
    }

    // ---------------------------------------------------------------- brand report import & bulk approval

    [Fact]
    public async Task Brand_report_import_matches_flags_creates_and_refunds()
    {
        var (program, codeId, user, participant, manager) = await kit.AssignedAsync();
        var code = (await (await manager.GetAsync($"/api/v1/admin/discount-codes/{codeId}")).ReadJsonAsync()).GetProperty("code").GetProperty("code").GetString()!;
        var (_, reviewer) = await kit.ReviewerAsync();
        var matched = await kit.SubmitOkAsync(participant, codeId, "BR-1", orderDate: kit.Now.AddDays(-2), net: 100m);
        var mismatched = await kit.SubmitOkAsync(participant, codeId, "BR-2", orderDate: kit.Now.AddDays(-2), net: 100m);
        var toRefund = await kit.SubmitOkAsync(participant, codeId, "BR-3", orderDate: kit.Now.AddDays(-2), net: 50m);
        await ApproveAsync(reviewer, Id(toRefund));
        var d = kit.Now.AddDays(-2).ToString("yyyy-MM-dd");
        var csv = "Order ID,Coupon,Total,Order Date,Status\n" +
                  $"BR-1,{code},100.00,{d},completed\n" +
                  $"BR-2,{code},180.00,{d},completed\n" +
                  $"BR-3,{code},50.00,{d},refunded\n" +
                  $"BR-4,{code.ToLowerInvariant()},75.50,{d},paid\n" +
                  $"BR-5,NOPE,10,{d},completed\n" +
                  $"BR-6,{code},abc,{d},completed\n" +
                  $"BR-7,{code},10,{d},weird\n" +
                  $"BR-1,{code},100.00,{d},completed\n";
        var dry = await (await manager.PostAsync($"/api/v1/admin/code-programs/{Id(program)}/sales/import?dryRun=true", Csv(csv, "brand-report.csv"))).ReadJsonAsync();
        Assert.Equal(1, dry.GetProperty("matched").GetInt32());
        Assert.Equal(1, dry.GetProperty("mismatched").GetInt32());
        Assert.Equal(1, dry.GetProperty("created").GetInt32());
        Assert.Equal(1, dry.GetProperty("refunded").GetInt32());
        Assert.Equal(4, dry.GetProperty("rejected").GetInt32());
        Assert.Contains(dry.GetProperty("issues").EnumerateArray(), i => i.GetProperty("row").GetInt32() == 9 && i.GetProperty("message").GetString()!.Contains("earlier"));
        Assert.Equal(EarningStatus.Approved, (await kit.EarningsAsync(Id(toRefund))).Single().Status); // dry run changed nothing

        var result = await (await manager.PostAsync($"/api/v1/admin/code-programs/{Id(program)}/sales/import", Csv(csv, "brand-report.csv"))).ReadJsonAsync();
        Assert.False(result.GetProperty("dryRun").GetBoolean());
        var sales = await api.WithDbAsync(db => db.Set<CodeSale>().AsNoTracking().Where(s => s.ProgramId == Id(program)).ToListAsync());
        Assert.Equal(CodeSaleVerification.Matched, sales.Single(s => s.Id == Id(matched)).Verification);
        var flagged = sales.Single(s => s.Id == Id(mismatched));
        Assert.Equal(CodeSaleVerification.Mismatch, flagged.Verification);
        Assert.Contains("amount", flagged.VerificationNote);
        Assert.Equal(CodeSaleStatus.Refunded, sales.Single(s => s.Id == Id(toRefund)).Status);
        Assert.Contains(await kit.EarningsAsync(Id(toRefund)), e => e.Type == EarningType.Reversal);
        var created = sales.Single(s => s.NormalizedOrderReference == "BR-4");
        Assert.Equal(CodeSaleSource.Import, created.Source);
        Assert.Equal(CodeSaleVerification.ReportedByBrand, created.Verification);
        Assert.Equal(user.Id, created.UserId);
        Assert.Equal(75.5m, created.NetAmount);

        // Importing again changes nothing.
        var again = await (await manager.PostAsync($"/api/v1/admin/code-programs/{Id(program)}/sales/import", Csv(csv))).ReadJsonAsync();
        Assert.Equal(0, again.GetProperty("matched").GetInt32() + again.GetProperty("created").GetInt32() + again.GetProperty("refunded").GetInt32());

        // The importer can't approve the sale their import created (four eyes); bulk approval takes matched sales only.
        await (await DecideAsync(manager, created.Id)).ShouldFailAsync(403);
        var bulk = await (await reviewer.PostAsJsonAsync("/api/v1/admin/code-sales/bulk-approve",
            new { saleIds = new[] { Id(matched), Id(mismatched), created.Id }, reason = "Verified by the brand report" })).ReadJsonAsync();
        Assert.Equal(1, bulk.GetProperty("approved").GetInt32());
        Assert.Equal(2, bulk.GetProperty("skipped").GetInt32());
        Assert.Single(await kit.EarningsAsync(Id(matched)));

        // Missing columns are a 400.
        await (await manager.PostAsync($"/api/v1/admin/code-programs/{Id(program)}/sales/import", Csv("order,code\nX,Y\n"))).ShouldFailAsync(400, "csv.missing_column");
    }

    // ---------------------------------------------------------------- reports & export

    [Fact]
    public async Task Reports_by_person_code_group_and_program_with_csv_export()
    {
        var (program, codeId, user, participant, manager) = await kit.AssignedAsync();
        var group = await GroupAsync(manager, user.Id);
        var (_, reviewer) = await kit.ReviewerAsync();
        var a = await kit.SubmitOkAsync(participant, codeId, "REP-1", net: 100m, discount: 10m);
        await kit.SubmitOkAsync(participant, codeId, "REP-2", net: 50m, discount: 5m);
        await ApproveAsync(reviewer, Id(a));

        var byPerson = await (await manager.GetAsync($"/api/v1/admin/code-reports?programId={Id(program)}&groupBy=Person")).ReadJsonAsync();
        var row = byPerson.GetProperty("rows").EnumerateArray().Single();
        Assert.Equal(user.Id, row.GetProperty("id").GetGuid());
        Assert.Equal(2, row.GetProperty("uses").GetInt32());
        Assert.Equal(1, row.GetProperty("approved").GetInt32());
        Assert.Equal(165m, row.GetProperty("grossSales").GetDecimal());
        Assert.Equal(15m, row.GetProperty("discountGiven").GetDecimal());
        Assert.Equal(10m, row.GetProperty("commissionApproved").GetDecimal());
        Assert.Equal(5m, row.GetProperty("commissionPending").GetDecimal());
        Assert.Equal("USD", byPerson.GetProperty("currency").GetString());

        var byGroup = await (await manager.GetAsync($"/api/v1/admin/code-reports?programId={Id(program)}&groupBy=Group")).ReadJsonAsync();
        Assert.Equal(Id(group), byGroup.GetProperty("rows").EnumerateArray().Single().GetProperty("id").GetGuid());
        var byCode = await (await manager.GetAsync($"/api/v1/admin/code-reports?programId={Id(program)}&groupBy=Code")).ReadJsonAsync();
        Assert.Equal(codeId, byCode.GetProperty("rows").EnumerateArray().Single().GetProperty("id").GetGuid());
        var byProgram = await (await manager.GetAsync("/api/v1/admin/code-reports?groupBy=Program")).ReadJsonAsync();
        Assert.Contains(byProgram.GetProperty("rows").EnumerateArray(), r => r.GetProperty("id").GetGuid() == Id(program));
        await (await manager.GetAsync("/api/v1/admin/code-reports?groupBy=Person")).ShouldFailAsync(400, "code_report.program_required");
        await (await manager.GetAsync($"/api/v1/admin/code-reports?programId={Id(program)}&groupBy=Nope")).ShouldFailAsync(400);

        var csv = await manager.GetAsync($"/api/v1/admin/code-reports/export.csv?programId={Id(program)}&groupBy=Person");
        csv.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.Contains("grossSales", text);
        Assert.Contains("Total", text);
        var sales = await manager.GetAsync($"/api/v1/admin/code-sales/export.csv?programId={Id(program)}");
        Assert.Contains("REP-2", await sales.Content.ReadAsStringAsync());

        // Participants only see their own stats (my codes), never the report.
        await (await participant.GetAsync($"/api/v1/admin/code-reports?programId={Id(program)}")).ShouldFailAsync(403);
        var mine = (await (await participant.GetAsync("/api/v1/me/codes")).ReadJsonAsync()).EnumerateArray().Single();
        Assert.Equal(2, mine.GetProperty("stats").GetProperty("sales").GetInt32());
        Assert.Equal(165m, mine.GetProperty("stats").GetProperty("grossSales").GetDecimal());
    }

    [Fact]
    public async Task Sale_list_filters_and_search_escape_like_wildcards()
    {
        var (program, codeId, user, participant, manager) = await kit.AssignedAsync();
        await kit.SubmitOkAsync(participant, codeId, "WILD_1");
        await kit.SubmitOkAsync(participant, codeId, "WILDX1");
        var found = await (await manager.GetAsync($"/api/v1/admin/code-sales?programId={Id(program)}&search={Uri.EscapeDataString("WILD_")}")).ReadJsonAsync();
        Assert.Equal(1, found.GetProperty("total").GetInt32());
        var byUser = await (await manager.GetAsync($"/api/v1/admin/code-sales?userId={user.Id}&status=Pending")).ReadJsonAsync();
        Assert.Equal(2, byUser.GetProperty("total").GetInt32());
        await (await manager.GetAsync("/api/v1/admin/code-sales?status=Bogus")).ShouldFailAsync(400);
        await (await manager.GetAsync($"/api/v1/admin/code-sales/{Guid.NewGuid()}")).ShouldFailAsync(404);
    }
}
