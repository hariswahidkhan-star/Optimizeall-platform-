using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.UnitTests.Billing;

public sealed class PricingTests
{
    private static PriceLineInput Line(decimal qty, decimal price, DiscountType dt = DiscountType.None, decimal dv = 0, decimal tax = 0,
        bool inclusive = false, Recurrence recurrence = Recurrence.OneTime) => new(qty, price, dt, dv, tax, inclusive, recurrence, tax > 0 ? "VAT" : null);

    [Fact]
    public void Percent_discount_then_exclusive_tax()
    {
        var a = Pricing.Compute(Line(3, 99.99m, DiscountType.Percent, 10, 20), "USD");
        Assert.Equal(299.97m, a.Gross);
        Assert.Equal(30.00m, a.Discount);       // 29.997 → 30.00
        Assert.Equal(269.97m, a.Subtotal);
        Assert.Equal(53.99m, a.Tax);            // 53.994 → 53.99
        Assert.Equal(323.96m, a.Total);
    }

    [Fact]
    public void Amount_discount_and_inclusive_tax_split_exactly()
    {
        var a = Pricing.Compute(Line(1, 120m, DiscountType.Amount, 20, 20, inclusive: true), "GBP");
        Assert.Equal(100m, a.Subtotal + a.Tax);
        Assert.Equal(83.33m, a.Subtotal);
        Assert.Equal(16.67m, a.Tax);
        Assert.Equal(100m, a.Total);
    }

    [Theory]
    [InlineData("JPY", 3, 1234.5, 10, 3704, 370, 4074)]      // 0 decimals: 3703.5 → 3704 (away from zero)
    [InlineData("KWD", 3, 12.3455, 5, 37.037, 1.852, 38.889)] // 3 decimals: 37.0365 → 37.037
    [InlineData("USD", 1, 0.005, 0, 0.01, 0, 0.01)]
    public void Rounds_to_the_currency_minor_unit(string currency, decimal qty, decimal price, decimal tax, decimal subtotal, decimal taxAmount, decimal total)
    {
        var a = Pricing.Compute(Line(qty, price, tax: tax), currency);
        Assert.Equal(subtotal, a.Subtotal);
        Assert.Equal(taxAmount, a.Tax);
        Assert.Equal(total, a.Total);
    }

    [Fact]
    public void Totals_sum_rounded_lines_and_break_down_taxes()
    {
        var totals = Pricing.Totals(new[]
        {
            Line(1, 1000m, DiscountType.Percent, 15, 5),
            Line(2, 250m, tax: 5),
            Line(1, 400m),
        }, "AED");
        Assert.Equal(1900m, totals.GrossTotal);
        Assert.Equal(150m, totals.DiscountTotal);
        Assert.Equal(1750m, totals.Subtotal);
        Assert.Equal(67.5m, totals.TaxTotal);
        Assert.Equal(1817.5m, totals.Total);
        var tax = Assert.Single(totals.Taxes);
        Assert.Equal(1350m, tax.TaxableAmount);
        Assert.Equal(67.5m, tax.TaxAmount);
    }

    [Fact]
    public void Recurring_split_first_invoice_and_mrr()
    {
        var r = Pricing.Recurring(new[]
        {
            Line(1, 3000m, DiscountType.Percent, 10, recurrence: Recurrence.OneTime),
            Line(1, 2500m, recurrence: Recurrence.Monthly),
            Line(1, 900m, recurrence: Recurrence.Quarterly),
            Line(1, 1200m, recurrence: Recurrence.Annually),
        }, "USD");
        Assert.Equal(2700m, r.OneTimeTotal);
        Assert.Equal(2500m, r.MonthlyTotal);
        Assert.Equal(2900m, r.MonthlyRecurringValue); // 2500 + 900/3 + 1200/12
        Assert.Equal(7300m, r.FirstInvoiceTotal);      // every line once
        Assert.Equal(2700m + 30000m + 3600m + 1200m, r.FirstYearValue);
    }

    [Theory]
    [InlineData(0, 10, DiscountType.None, 0, 0, "billing.invalid_quantity")]
    [InlineData(1, -1, DiscountType.None, 0, 0, "billing.invalid_price")]
    [InlineData(1, 10, DiscountType.Percent, 101, 0, "billing.invalid_discount")]
    [InlineData(1, 10, DiscountType.Amount, 11, 0, "billing.invalid_discount")]
    [InlineData(1, 10, DiscountType.None, 5, 0, "billing.invalid_discount")]
    [InlineData(1, 10, DiscountType.None, 0, 101, "billing.invalid_tax")]
    public void Rejects_invalid_lines(decimal qty, decimal price, DiscountType dt, decimal dv, decimal tax, string code)
    {
        var ex = Assert.Throws<DomainException>(() => Pricing.Compute(Line(qty, price, dt, dv, tax), "USD"));
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void Unsupported_currency_is_rejected() =>
        Assert.Equal("billing.currency_unsupported", Assert.Throws<DomainException>(() => Pricing.Totals(new[] { Line(1, 1) }, "XYZ")).Code);

    [Fact]
    public void Periods_are_anchored_on_the_contract_start()
    {
        var start = new DateOnly(2026, 1, 31);
        Assert.Equal(new DateOnly(2026, 2, 28), BillingPeriods.PeriodStart(start, BillingFrequency.Monthly, 1));
        Assert.Equal(new DateOnly(2026, 3, 31), BillingPeriods.PeriodStart(start, BillingFrequency.Monthly, 2));
        Assert.Equal(new DateOnly(2026, 3, 30), BillingPeriods.PeriodEnd(start, BillingFrequency.Monthly, 1));
        Assert.Equal(new DateOnly(2026, 7, 31), BillingPeriods.PeriodStart(start, BillingFrequency.Quarterly, 2));
        Assert.Equal(new DateOnly(2027, 1, 30), BillingPeriods.PeriodEnd(start, BillingFrequency.Annually, 0));
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal("contract:11111111-2222-3333-4444-555555555555:2026-02-28", BillingPeriods.InvoiceKey(id, new DateOnly(2026, 2, 28)));
    }

    [Theory]
    [InlineData(0, AgingBucket.Current)]
    [InlineData(-5, AgingBucket.Current)]
    [InlineData(1, AgingBucket.Days1To30)]
    [InlineData(30, AgingBucket.Days1To30)]
    [InlineData(31, AgingBucket.Days31To60)]
    [InlineData(60, AgingBucket.Days31To60)]
    [InlineData(61, AgingBucket.Days61To90)]
    [InlineData(90, AgingBucket.Days61To90)]
    [InlineData(91, AgingBucket.Over90)]
    public void Aging_buckets_by_days_past_due(int daysPastDue, AgingBucket expected)
    {
        var asOf = new DateOnly(2026, 9, 23);
        Assert.Equal(expected, Aging.BucketFor(asOf.AddDays(-daysPastDue), asOf));
    }

    [Fact]
    public void Document_numbers_are_padded() => Assert.Equal("OA-2026-0007", DocumentNumbers.Format("OA", 2026, 7));

    [Fact]
    public void Reminder_schedule_picks_the_latest_reached_offset()
    {
        var due = new DateOnly(2026, 9, 20);
        var offsets = new[] { -3, 0, 7, 14 };
        Assert.Null(InvoiceOverdueJob.ApplicableOffset(due, due.AddDays(-4), offsets));
        Assert.Equal(-3, InvoiceOverdueJob.ApplicableOffset(due, due.AddDays(-3), offsets));
        Assert.Equal(0, InvoiceOverdueJob.ApplicableOffset(due, due, offsets));
        Assert.Equal(7, InvoiceOverdueJob.ApplicableOffset(due, due.AddDays(10), offsets));
        Assert.Equal(14, InvoiceOverdueJob.ApplicableOffset(due, due.AddDays(40), offsets));
        Assert.Equal("before-3", InvoiceOverdueJob.ReminderKind(-3));
        Assert.Equal("due", InvoiceOverdueJob.ReminderKind(0));
        Assert.Equal("after-14", InvoiceOverdueJob.ReminderKind(14));
    }

    [Fact]
    public void Settlement_status_follows_balance_and_due_date()
    {
        var today = new DateOnly(2026, 9, 23);
        var invoice = new Invoice { Currency = "USD", Total = 100m, DueDate = today.AddDays(5) };
        invoice.AmountPaid = 40m;
        invoice.RecalculateBalance("USD");
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.SettlementStatus(today));
        invoice.DueDate = today.AddDays(-1);
        Assert.Equal(InvoiceStatus.Overdue, invoice.SettlementStatus(today));
        invoice.AmountCredited = 60m;
        invoice.RecalculateBalance("USD");
        Assert.Equal(InvoiceStatus.Paid, invoice.SettlementStatus(today));
    }

    [Fact]
    public void Billing_settings_validation_rejects_bad_values()
    {
        var ex = Assert.Throws<DomainException>(() => BillingSettingsService.Validate(new BillingSettings
        {
            InvoicePrefix = "O A", PaymentTermsDays = 400, DefaultCurrency = "ZZZ", ReminderOffsetsDays = new() { 3, 3 },
        }));
        Assert.Equal("billing.invalid_settings", ex.Code);
        Assert.Contains("invoicePrefix", ex.Errors!.Keys);
        Assert.Contains("paymentTermsDays", ex.Errors!.Keys);
        Assert.Contains("defaultCurrency", ex.Errors!.Keys);
        Assert.Contains("reminderOffsetsDays", ex.Errors!.Keys);
    }

    [Fact]
    public void Public_link_tokens_are_well_formed_and_hashed()
    {
        Assert.False(PublicLinkTokens.IsWellFormed("short"));
        Assert.False(PublicLinkTokens.IsWellFormed(new string('a', 42) + "/"));
        Assert.True(PublicLinkTokens.IsWellFormed(new string('a', 43)));
        Assert.Equal(64, PublicLinkTokens.Hash("x").Length);
    }
}
