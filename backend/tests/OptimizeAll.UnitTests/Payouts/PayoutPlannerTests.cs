using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Ledger;
using OptimizeAll.Api.Modules.Payouts;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;

namespace OptimizeAll.UnitTests.Payouts;

public sealed class PayoutPlannerTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid(), E = Guid.NewGuid(), F = Guid.NewGuid();

    private static PlannerEarning Earn(Guid user, decimal amount) => new(Guid.NewGuid(), user, amount);

    private static Dictionary<Guid, PlannerParticipant> Participants(params PlannerParticipant[] list) => list.ToDictionary(p => p.UserId);

    private static PlannerParticipant Ok(Guid id) => new(id, IsActive: true, HasActiveHold: false, HasPayoutProfile: true);

    [Fact]
    public void Nets_credits_and_clawbacks_per_participant()
    {
        var earnings = new[] { Earn(A, 30m), Earn(A, 12.5m), Earn(A, -7.25m), Earn(B, 20m) };
        var plan = PayoutPlanner.Plan(earnings, Participants(Ok(A), Ok(B)), 10m, "USD");

        Assert.Empty(plan.Exclusions);
        var a = plan.Items.Single(i => i.UserId == A);
        Assert.Equal(35.25m, a.Amount);
        Assert.Equal(3, a.EarningIds.Count);
        Assert.False(a.HeldForMissingPayoutProfile);
        Assert.Equal(20m, plan.Items.Single(i => i.UserId == B).Amount);
    }

    [Fact]
    public void Applies_exclusion_precedence_and_minimum()
    {
        var earnings = new[]
        {
            Earn(A, 50m),                // on hold
            Earn(B, 50m),                // suspended
            Earn(C, 5m), Earn(C, -9m),   // net negative → carried over
            Earn(D, 9.99m),              // below minimum 10
            Earn(E, 10m),                // exactly the minimum → paid
            Earn(F, 25m),                // no payout profile → held item
        };
        var participants = Participants(
            new PlannerParticipant(A, true, HasActiveHold: true, true),
            new PlannerParticipant(B, IsActive: false, false, true),
            Ok(C), Ok(D), Ok(E),
            new PlannerParticipant(F, true, false, HasPayoutProfile: false));

        var plan = PayoutPlanner.Plan(earnings, participants, 10m, "USD");

        Assert.Equal(PayoutExclusionReason.PayoutHold, plan.Exclusions.Single(e => e.UserId == A).Reason);
        Assert.Equal(PayoutExclusionReason.AccountInactive, plan.Exclusions.Single(e => e.UserId == B).Reason);
        var c = plan.Exclusions.Single(e => e.UserId == C);
        Assert.Equal(PayoutExclusionReason.NonPositiveBalance, c.Reason);
        Assert.Equal(-4m, c.Amount);
        Assert.Equal(2, c.EarningCount);
        Assert.Equal(PayoutExclusionReason.BelowMinimum, plan.Exclusions.Single(e => e.UserId == D).Reason);
        Assert.Equal(10m, plan.Items.Single(i => i.UserId == E).Amount);
        Assert.True(plan.Items.Single(i => i.UserId == F).HeldForMissingPayoutProfile);
        Assert.Equal(2, plan.Items.Count);
    }

    [Fact]
    public void Rounds_to_the_currency_minor_unit_and_treats_unknown_users_as_inactive()
    {
        var plan = PayoutPlanner.Plan(new[] { Earn(A, 10.005m), Earn(B, 100m) }, Participants(Ok(A)), 0m, "USD");
        Assert.Equal(10.01m, plan.Items.Single().Amount);
        Assert.Equal(PayoutExclusionReason.AccountInactive, plan.Exclusions.Single().Reason);

        var jpy = PayoutPlanner.Plan(new[] { Earn(A, 1000.5m) }, Participants(Ok(A)), 0m, "JPY");
        Assert.Equal(1001m, jpy.Items.Single().Amount);
    }

    [Fact]
    public void Zero_net_is_never_paid_even_with_a_zero_minimum()
    {
        var plan = PayoutPlanner.Plan(new[] { Earn(A, 5m), Earn(A, -5m) }, Participants(Ok(A)), 0m, "USD");
        Assert.Empty(plan.Items);
        Assert.Equal(PayoutExclusionReason.NonPositiveBalance, plan.Exclusions.Single().Reason);
    }
}

public sealed class FinanceCsvTests
{
    [Fact]
    public void Ledger_csv_row_matches_header_and_neutralises_formulas()
    {
        var entry = new EarningEntry
        {
            UserId = Guid.NewGuid(), Type = EarningType.Adjustment, Status = EarningStatus.Approved, Amount = -12.5m, Currency = "EUR",
            ExchangeRate = 1.1m, SettlementAmount = -13.75m, SettlementCurrency = "USD", Description = "=HYPERLINK(\"x\")",
            Reason = "Dispute, resolved", CreatedAt = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
        };
        var row = new LedgerRow { Entry = entry, UserEmail = "a@example.test", UserDisplayName = "Ann", CampaignTitle = null };
        var cells = LedgerCsv.Row(row).ToList();
        Assert.Equal(LedgerCsv.Header.Length, cells.Count);

        var csv = Csv.Write(LedgerCsv.Header, new[] { cells });
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Earning ID,Created at (UTC),User ID,User email", lines[0]);
        Assert.Contains("2026-09-01T10:00:00Z", lines[1]);
        Assert.Contains(",-12.5,EUR,1.1,-13.75,USD,", lines[1]);            // negative numbers stay numeric
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", lines[1]);           // formula neutralised and quoted
        Assert.Contains("\"Dispute, resolved\"", lines[1]);
    }

    [Fact]
    public void Payout_export_cells_match_the_header_and_never_contain_the_encrypted_destination()
    {
        var batch = new PayoutBatch { Reference = "PB-2026-09-27", PeriodKey = "2026-09-27", Currency = "USD" };
        var item = new PayoutItem
        {
            BatchId = batch.Id, Amount = 42.1m, Currency = "USD", EarningCount = 3, Status = PayoutItemStatus.Paid,
            DestinationHint = "PK••••1234", PaymentReference = "TX-99", PaidAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        var profile = new PayoutProfile
        {
            Method = PayoutMethod.BankTransfer, AccountHolderName = "Sara Khan", MaskedDestination = "PK••••1234",
            EncryptedDestination = "CfDJ8-secret-ciphertext",
        };
        var row = new PayoutReadModels.ExportRow(item, new PayoutUserDto(Guid.NewGuid(), "Sara", "sara@example.test", "PK"), profile);
        var cells = PayoutReadModels.ExportCells(batch, row);
        Assert.Equal(PayoutReadModels.ExportHeader.Length, cells.Length);

        var csv = Csv.Write(PayoutReadModels.ExportHeader, new[] { cells });
        Assert.Contains("PB-2026-09-27,2026-09-27," + item.Id + ",Sara,sara@example.test,PK,42.1,USD,3,Paid,BankTransfer,Sara Khan,PK••••1234,TX-99,2026-10-01T00:00:00Z", csv);
        Assert.DoesNotContain("secret", csv);
    }

    [Theory]
    [InlineData("REF-000123", "••••0123")]
    [InlineData("abc", "•••")]
    [InlineData(null, null)]
    public void Payment_references_are_masked_to_the_last_four(string? reference, string? expected) =>
        Assert.Equal(expected, FinanceGuards.MaskReference(reference));
}
