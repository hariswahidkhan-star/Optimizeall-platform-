using OptimizeAll.Domain.Billing;

namespace OptimizeAll.UnitTests.Billing;

public sealed class ContractRenewalTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    // Monthly renewal of a month-end contract stays on month ends (no drift to the 28th after February).
    [InlineData("2026-01-31", 1, "2026-02-01", "2026-02-28")]
    [InlineData("2026-01-31", 1, "2026-03-01", "2026-03-31")]
    [InlineData("2026-01-31", 1, "2026-05-01", "2026-05-31")]
    [InlineData("2027-01-31", 1, "2027-12-01", "2027-12-31")]
    // Leap years: a February contract renewed for a year ends on Feb 29 in a leap year and Feb 28 otherwise.
    [InlineData("2027-02-28", 12, "2027-03-01", "2028-02-29")]
    [InlineData("2027-02-28", 12, "2028-03-01", "2029-02-28")]
    [InlineData("2028-02-29", 12, "2028-03-01", "2029-02-28")]
    // Mid-month ends renew to the same day.
    [InlineData("2026-03-14", 3, "2026-03-15", "2026-06-14")]
    [InlineData("2026-03-14", 3, "2026-09-20", "2026-12-14")]
    // Already covered: unchanged.
    [InlineData("2026-03-31", 1, "2026-03-31", "2026-03-31")]
    public void Renewal_is_anchored_on_the_original_end(string end, int term, string mustCover, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), BillingPeriods.RenewedEnd(DateOnly.Parse(end), term, DateOnly.Parse(mustCover)));

    [Fact]
    public void Renewed_end_matches_the_billing_periods_of_a_contract_starting_the_day_after()
    {
        // A contract Jan 1–Jan 31 renewed monthly bills Feb 1–Feb 28, Mar 1–Mar 31, ...: the renewed end must be the end
        // of the period being billed.
        var end = D(2026, 1, 31);
        for (var i = 1; i <= 26; i++)
        {
            var periodStart = BillingPeriods.PeriodStart(D(2026, 1, 1), BillingFrequency.Monthly, i);
            Assert.Equal(BillingPeriods.PeriodEnd(D(2026, 1, 1), BillingFrequency.Monthly, i), BillingPeriods.RenewedEnd(end, 1, periodStart));
        }
    }

    [Fact]
    public void A_non_positive_term_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BillingPeriods.RenewedEnd(D(2026, 1, 31), 0, D(2026, 3, 1)));
}
