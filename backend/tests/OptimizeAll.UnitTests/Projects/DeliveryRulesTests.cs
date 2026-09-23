using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Projects;

namespace OptimizeAll.UnitTests.Projects;

public sealed class DeliverableWorkflowTests
{
    [Theory]
    [InlineData(DeliverableStatus.Draft, DeliverableAction.Submit, DeliverableStatus.InternalReview)]
    [InlineData(DeliverableStatus.ChangesRequested, DeliverableAction.Submit, DeliverableStatus.InternalReview)]
    [InlineData(DeliverableStatus.InternalReview, DeliverableAction.InternalApprove, DeliverableStatus.ClientReview)]
    [InlineData(DeliverableStatus.InternalReview, DeliverableAction.InternalRequestChanges, DeliverableStatus.Draft)]
    [InlineData(DeliverableStatus.ClientReview, DeliverableAction.ClientApprove, DeliverableStatus.Approved)]
    [InlineData(DeliverableStatus.ClientReview, DeliverableAction.AutoApprove, DeliverableStatus.Approved)]
    [InlineData(DeliverableStatus.ClientReview, DeliverableAction.ClientRequestChanges, DeliverableStatus.ChangesRequested)]
    [InlineData(DeliverableStatus.Approved, DeliverableAction.Publish, DeliverableStatus.Published)]
    public void Allowed_transitions(DeliverableStatus from, DeliverableAction action, DeliverableStatus to) =>
        Assert.Equal(to, DeliverableWorkflow.Next(from, action));

    [Fact]
    public void Every_other_transition_is_rejected()
    {
        var allowed = new HashSet<(DeliverableStatus, DeliverableAction)>
        {
            (DeliverableStatus.Draft, DeliverableAction.Submit), (DeliverableStatus.ChangesRequested, DeliverableAction.Submit),
            (DeliverableStatus.InternalReview, DeliverableAction.InternalApprove), (DeliverableStatus.InternalReview, DeliverableAction.InternalRequestChanges),
            (DeliverableStatus.ClientReview, DeliverableAction.ClientApprove), (DeliverableStatus.ClientReview, DeliverableAction.AutoApprove),
            (DeliverableStatus.ClientReview, DeliverableAction.ClientRequestChanges), (DeliverableStatus.Approved, DeliverableAction.Publish),
        };
        foreach (var s in Enum.GetValues<DeliverableStatus>())
            foreach (var a in Enum.GetValues<DeliverableAction>())
                if (!allowed.Contains((s, a))) Assert.Null(DeliverableWorkflow.Next(s, a));
    }

    [Fact]
    public void Clients_never_approve_before_internal_review_and_versions_lock_after_approval()
    {
        Assert.Null(DeliverableWorkflow.Next(DeliverableStatus.Draft, DeliverableAction.ClientApprove));
        Assert.Null(DeliverableWorkflow.Next(DeliverableStatus.InternalReview, DeliverableAction.ClientApprove));
        Assert.False(DeliverableWorkflow.CanAddVersion(DeliverableStatus.ClientReview));
        Assert.False(DeliverableWorkflow.CanAddVersion(DeliverableStatus.Approved));
        Assert.True(DeliverableWorkflow.CanAddVersion(DeliverableStatus.ChangesRequested));
        Assert.False(DeliverableWorkflow.CanSubmitForInternalReview(DeliverableStatus.Draft, 0));
    }

    [Fact]
    public void Feedback_due_date_counts_business_days()
    {
        var friday = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc), DeliverableWorkflow.DueAt(friday, 3)); // Mon, Tue, Wed
        Assert.Equal(new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc), DeliverableWorkflow.DueAt(friday, 1));
    }
}

public sealed class BudgetMathTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();

    [Fact]
    public void User_rate_beats_role_rate_which_beats_the_project_default()
    {
        var rates = new List<RateCard>
        {
            new(Alice, null, 150, "USD"), new(null, Role.SeoSpecialist, 999, "USD"), new(null, Role.Designer, 100, "USD"),
            new(null, Role.Designer, 80, "GBP"),
        };
        var entries = new[]
        {
            new BurnEntry(Alice, 60, true, new[] { Role.SeoSpecialist }),  // 150
            new BurnEntry(Bob, 120, true, new[] { Role.Designer }),        // 200
            new BurnEntry(Carol, 90, true, new[] { Role.ContentCreator }), // 1.5 × 50 = 75
            new BurnEntry(Carol, 30, false, new[] { Role.ContentCreator }),
        };
        var burn = BudgetMath.Compute(entries, rates, 10, 1000, "USD", 50);
        Assert.Equal(5m, burn.HoursLogged);
        Assert.Equal(4.5m, burn.BillableHours);
        Assert.Equal(425m, burn.AmountBurned);
        Assert.Equal(50m, burn.HoursBurnPercent);
        Assert.Equal(42.5m, burn.AmountBurnPercent);
        Assert.Equal(0m, burn.UnpricedHours);
    }

    [Fact]
    public void Entries_without_any_rate_are_reported_as_unpriced_and_rounding_uses_the_currency()
    {
        var burn = BudgetMath.Compute(new[] { new BurnEntry(Alice, 20, true, new[] { Role.Designer }) }, new List<RateCard>(), null, null, "JPY", null);
        Assert.Equal(0m, burn.AmountBurned);
        Assert.Equal(0.33m, burn.UnpricedHours);
        Assert.Null(burn.HoursBurnPercent);
        var yen = BudgetMath.Compute(new[] { new BurnEntry(Alice, 20, true, new[] { Role.Designer }) }, new List<RateCard>(), null, 1000, "JPY", 1000);
        Assert.Equal(333m, yen.AmountBurned);
    }

    [Theory]
    [InlineData("2026-09-23", "2026-09-21")] // Wednesday → Monday
    [InlineData("2026-09-21", "2026-09-21")]
    [InlineData("2026-09-27", "2026-09-21")] // Sunday belongs to the week that started Monday
    public void Week_starts_on_monday(string day, string monday) =>
        Assert.Equal(DateOnly.Parse(monday), BudgetMath.WeekStart(DateOnly.Parse(day)));
}

public sealed class ClientHealthCalculatorTests
{
    private static HealthSignals Healthy() => new(0, 0, 0, 1, 4.8, 3, 9, Array.Empty<HealthReason>());

    [Fact]
    public void A_healthy_client_is_green_with_no_reasons()
    {
        var r = ClientHealthCalculator.Compute(Healthy());
        Assert.Equal(HealthLevel.Green, r.Level);
        Assert.Equal(100, r.Score);
        Assert.Empty(r.Reasons);
    }

    [Fact]
    public void Overdue_work_and_stale_approvals_turn_it_red_with_reasons()
    {
        var r = ClientHealthCalculator.Compute(Healthy() with { OverdueTasks = 6, PendingApprovals = 2, OldestPendingApprovalDays = 8.2 });
        Assert.Equal(HealthLevel.Red, r.Level);
        Assert.Contains(r.Reasons, x => x.Code == "overdue_tasks" && x.Message == "6 overdue tasks");
        Assert.Contains(r.Reasons, x => x.Code == "approvals_stale" && x.Message.Contains("oldest 8 days"));
        Assert.Equal(HealthLevel.Red, r.Reasons[0].Level);
    }

    [Fact]
    public void Amber_signals_and_external_reasons()
    {
        var r = ClientHealthCalculator.Compute(Healthy() with
        {
            OverdueTasks = 1, DaysSinceLastActivity = 15, AverageCsat = 3.2, LatestNps = 6,
            External = new[] { new HealthReason("invoice_overdue", HealthLevel.Amber, "Invoice INV-1 is 10 days overdue", 10) },
        });
        Assert.Equal(HealthLevel.Red, r.Level); // penalties add up below 50
        Assert.Equal(new[] { "csat_mixed", "invoice_overdue", "nps_detractor", "overdue_tasks", "quiet" }, r.Reasons.Select(x => x.Code).OrderBy(c => c));
        var mild = ClientHealthCalculator.Compute(Healthy() with { OverdueTasks = 1 });
        Assert.Equal(HealthLevel.Amber, mild.Level);
        Assert.Equal("1 overdue task", mild.Reasons.Single().Message);
    }

    [Fact]
    public void Long_inactivity_is_red()
    {
        var r = ClientHealthCalculator.Compute(Healthy() with { DaysSinceLastActivity = 40 });
        Assert.Contains(r.Reasons, x => x.Code == "inactive" && x.Level == HealthLevel.Red);
        Assert.Equal(HealthLevel.Red, r.Level);
    }
}

public sealed class DeliveryFileValidatorTests
{
    [Fact]
    public void Identifies_allowed_types_by_magic_bytes_only()
    {
        Assert.Equal("application/pdf", DeliveryFileValidator.Identify("%PDF-1.7\n..."u8)?.ContentType);
        var mp4 = new byte[32];
        "\0\0\0 ftypmp42"u8.ToArray().CopyTo(mp4, 0);
        Assert.Equal("video/mp4", DeliveryFileValidator.Identify(mp4)?.ContentType);
        var mov = new byte[32];
        "\0\0\0\u0014ftypqt  "u8.ToArray().CopyTo(mov, 0);
        Assert.Null(DeliveryFileValidator.Identify(mov));
        Assert.Null(DeliveryFileValidator.Identify("<html><script>"u8));
        Assert.Null(DeliveryFileValidator.Identify("GIF89a......"u8));
        Assert.Null(DeliveryFileValidator.Identify("PK\u0003\u0004 docx"u8));
        Assert.Equal(".png", DeliveryFileValidator.Identify(Api.Modules.Seed.DemoPng.Creative(10, 10, 1))?.Extension);
    }
}
