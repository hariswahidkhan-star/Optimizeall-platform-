using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Billing;

public enum DiscountType
{
    None,
    Percent,
    Amount,
}

/// <summary>How often a proposal/contract line is charged. One-time lines are billed once (on acceptance).</summary>
public enum Recurrence
{
    OneTime,
    Monthly,
    Quarterly,
    Annually,
}

/// <summary>How often a contract (retainer) is invoiced.</summary>
public enum BillingFrequency
{
    Monthly,
    Quarterly,
    Annually,
}

/// <summary>A priced line as entered (proposal, contract or invoice line). Amounts are per billing period.</summary>
public sealed record PriceLineInput(
    decimal Quantity,
    decimal UnitPrice,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal TaxPercent,
    bool TaxInclusive,
    Recurrence Recurrence = Recurrence.OneTime,
    string? TaxName = null);

/// <summary>
/// Computed amounts of one line, each rounded to the currency's minor unit.
/// <list type="bullet">
/// <item><c>Gross</c> = quantity × unit price.</item>
/// <item><c>Discount</c> = percent of gross or a fixed amount (never more than gross).</item>
/// <item><c>Subtotal</c> = the taxable amount excluding tax (net of discount).</item>
/// <item><c>Tax</c>; <c>Total</c> = Subtotal + Tax. For tax-inclusive prices the discounted price already contains the tax.</item>
/// </list>
/// </summary>
public sealed record PriceLineAmounts(decimal Gross, decimal Discount, decimal Subtotal, decimal Tax, decimal Total);

public sealed record TaxBreakdown(string Name, decimal RatePercent, bool Inclusive, decimal TaxableAmount, decimal TaxAmount);

/// <summary>Totals of a document (proposal version, invoice). Every value is already rounded to the currency.</summary>
public sealed record DocumentTotals(
    string Currency,
    decimal GrossTotal,
    decimal DiscountTotal,
    decimal Subtotal,
    decimal TaxTotal,
    decimal Total,
    IReadOnlyList<TaxBreakdown> Taxes);

/// <summary>Proposal-specific totals: what is charged once, what recurs, and what the first invoice will be.</summary>
public sealed record RecurringTotals(
    decimal OneTimeTotal,
    decimal MonthlyTotal,
    decimal QuarterlyTotal,
    decimal AnnualTotal,
    decimal MonthlyRecurringValue,
    decimal FirstInvoiceTotal,
    decimal FirstYearValue);

/// <summary>
/// The single money calculator for proposals, contracts and invoices (the UI never computes money). Rounds each line
/// to the currency's minor unit (JPY 0, KWD 3 decimals, most currencies 2) with <see cref="Money.Round"/>; document
/// totals are sums of rounded line amounts, so the printed lines always add up to the printed totals.
/// </summary>
public static class Pricing
{
    public const decimal MaxQuantity = 1_000_000m;
    public const decimal MaxUnitPrice = 1_000_000_000m;
    public const int MaxLines = 200;

    public static void Validate(PriceLineInput line, string currency, string field = "lines")
    {
        if (line.Quantity <= 0 || line.Quantity > MaxQuantity || decimal.Round(line.Quantity, 4) != line.Quantity)
            throw new DomainException("billing.invalid_quantity",
                $"Quantity must be greater than 0 and at most {MaxQuantity:0} (up to 4 decimals).",
                errors: Field(field, "Quantity must be greater than 0 (up to 4 decimals)."));
        if (line.UnitPrice < 0 || line.UnitPrice > MaxUnitPrice || decimal.Round(line.UnitPrice, 4) != line.UnitPrice)
            throw new DomainException("billing.invalid_price",
                "Unit price must be between 0 and 1,000,000,000 (up to 4 decimals).",
                errors: Field(field, "Unit price must be 0 or more."));
        if (line.TaxPercent < 0 || line.TaxPercent > 100 || decimal.Round(line.TaxPercent, 4) != line.TaxPercent)
            throw new DomainException("billing.invalid_tax", "Tax rate must be between 0 and 100%.",
                errors: Field(field, "Tax rate must be between 0 and 100%."));
        switch (line.DiscountType)
        {
            case DiscountType.None when line.DiscountValue != 0:
                throw new DomainException("billing.invalid_discount", "Choose a discount type or leave the discount at 0.",
                    errors: Field(field, "Choose a discount type."));
            case DiscountType.Percent when line.DiscountValue < 0 || line.DiscountValue > 100:
                throw new DomainException("billing.invalid_discount", "A percentage discount must be between 0 and 100.",
                    errors: Field(field, "A percentage discount must be between 0 and 100."));
            case DiscountType.Amount when line.DiscountValue < 0:
                throw new DomainException("billing.invalid_discount", "A discount amount can't be negative.",
                    errors: Field(field, "A discount amount can't be negative."));
        }
        var gross = Money.Round(line.Quantity * line.UnitPrice, currency);
        if (line.DiscountType == DiscountType.Amount && Money.Round(line.DiscountValue, currency) > gross)
            throw new DomainException("billing.invalid_discount", "A discount can't be larger than the line amount.",
                errors: Field(field, "A discount can't be larger than the line amount."));
    }

    public static PriceLineAmounts Compute(PriceLineInput line, string currency)
    {
        Validate(line, currency);
        var gross = Money.Round(line.Quantity * line.UnitPrice, currency);
        var discount = line.DiscountType switch
        {
            DiscountType.Percent => Money.Round(gross * line.DiscountValue / 100m, currency),
            DiscountType.Amount => Money.Round(line.DiscountValue, currency),
            _ => 0m,
        };
        if (discount > gross) discount = gross;
        var net = gross - discount;

        decimal subtotal, tax;
        if (line.TaxPercent == 0)
        {
            subtotal = net;
            tax = 0m;
        }
        else if (line.TaxInclusive)
        {
            // The price already includes tax: split it so subtotal + tax equals the price exactly.
            subtotal = Money.Round(net * 100m / (100m + line.TaxPercent), currency);
            tax = net - subtotal;
        }
        else
        {
            subtotal = net;
            tax = Money.Round(net * line.TaxPercent / 100m, currency);
        }
        return new PriceLineAmounts(gross, discount, subtotal, tax, subtotal + tax);
    }

    public static DocumentTotals Totals(IReadOnlyList<PriceLineInput> lines, string currency)
    {
        currency = Money.Normalize(currency);
        if (!Money.IsSupported(currency))
            throw new DomainException("billing.currency_unsupported", $"The currency {currency} is not supported.");
        if (lines.Count > MaxLines)
            throw new DomainException("billing.too_many_lines", $"A document can have at most {MaxLines} lines.");

        decimal gross = 0, discount = 0, subtotal = 0, tax = 0, total = 0;
        var taxes = new Dictionary<(string, decimal, bool), (decimal Taxable, decimal Tax)>();
        foreach (var line in lines)
        {
            var a = Compute(line, currency);
            gross += a.Gross;
            discount += a.Discount;
            subtotal += a.Subtotal;
            tax += a.Tax;
            total += a.Total;
            if (line.TaxPercent > 0)
            {
                var key = (string.IsNullOrWhiteSpace(line.TaxName) ? $"Tax {line.TaxPercent:0.##}%" : line.TaxName.Trim(), line.TaxPercent, line.TaxInclusive);
                taxes[key] = taxes.TryGetValue(key, out var t) ? (t.Taxable + a.Subtotal, t.Tax + a.Tax) : (a.Subtotal, a.Tax);
            }
        }
        return new DocumentTotals(currency, gross, discount, subtotal, tax, total,
            taxes.Select(kv => new TaxBreakdown(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value.Taxable, kv.Value.Tax))
                .OrderBy(t => t.RatePercent).ThenBy(t => t.Name, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// One-time vs recurring split of a proposal. Each recurring line is priced per period, so the first invoice charges
    /// every line once (one-time lines plus the first period of each recurring line).
    /// </summary>
    public static RecurringTotals Recurring(IReadOnlyList<PriceLineInput> lines, string currency)
    {
        decimal oneTime = 0, monthly = 0, quarterly = 0, annual = 0;
        foreach (var line in lines)
        {
            var total = Compute(line, currency).Total;
            switch (line.Recurrence)
            {
                case Recurrence.Monthly: monthly += total; break;
                case Recurrence.Quarterly: quarterly += total; break;
                case Recurrence.Annually: annual += total; break;
                default: oneTime += total; break;
            }
        }
        var mrr = Money.Round(monthly + quarterly / 3m + annual / 12m, currency);
        return new RecurringTotals(oneTime, monthly, quarterly, annual, mrr,
            oneTime + monthly + quarterly + annual,
            oneTime + monthly * 12 + quarterly * 4 + annual);
    }

    /// <summary>Monthly equivalent of a per-period amount (MRR contribution), rounded to the currency.</summary>
    public static decimal MonthlyEquivalent(decimal perPeriod, Recurrence recurrence, string currency) => recurrence switch
    {
        Recurrence.Monthly => Money.Round(perPeriod, currency),
        Recurrence.Quarterly => Money.Round(perPeriod / 3m, currency),
        Recurrence.Annually => Money.Round(perPeriod / 12m, currency),
        _ => 0m,
    };

    public static int MonthsPer(BillingFrequency frequency) => frequency switch
    {
        BillingFrequency.Quarterly => 3,
        BillingFrequency.Annually => 12,
        _ => 1,
    };

    public static Recurrence ToRecurrence(BillingFrequency frequency) => frequency switch
    {
        BillingFrequency.Quarterly => Recurrence.Quarterly,
        BillingFrequency.Annually => Recurrence.Annually,
        _ => Recurrence.Monthly,
    };

    private static IReadOnlyDictionary<string, string[]> Field(string field, string message) =>
        new Dictionary<string, string[]> { [field] = new[] { message } };
}

/// <summary>Billing period arithmetic, anchored on the contract start so periods never drift (Jan 31 → Feb 28 → Mar 31).</summary>
public static class BillingPeriods
{
    public static DateOnly PeriodStart(DateOnly contractStart, BillingFrequency frequency, int index) =>
        contractStart.AddMonths(index * Pricing.MonthsPer(frequency));

    public static DateOnly PeriodEnd(DateOnly contractStart, BillingFrequency frequency, int index) =>
        PeriodStart(contractStart, frequency, index + 1).AddDays(-1);

    /// <summary>Idempotency key of the invoice for one contract period (unique in the invoices table).</summary>
    public static string InvoiceKey(Guid contractId, DateOnly periodStart) => $"contract:{contractId}:{periodStart:yyyy-MM-dd}";
}

public enum AgingBucket
{
    Current,
    Days1To30,
    Days31To60,
    Days61To90,
    Over90,
}

/// <summary>Accounts-receivable aging: days past the due date on the as-of date.</summary>
public static class Aging
{
    public static AgingBucket BucketFor(DateOnly dueDate, DateOnly asOf)
    {
        var daysPastDue = asOf.DayNumber - dueDate.DayNumber;
        return daysPastDue switch
        {
            <= 0 => AgingBucket.Current,
            <= 30 => AgingBucket.Days1To30,
            <= 60 => AgingBucket.Days31To60,
            <= 90 => AgingBucket.Days61To90,
            _ => AgingBucket.Over90,
        };
    }
}

/// <summary>Document number formatting (e.g. OA-2026-0001).</summary>
public static class DocumentNumbers
{
    public static string Format(string prefix, int year, long sequence, int padding = 4) =>
        $"{prefix}-{year}-{sequence.ToString().PadLeft(Math.Clamp(padding, 1, 10), '0')}";
}
