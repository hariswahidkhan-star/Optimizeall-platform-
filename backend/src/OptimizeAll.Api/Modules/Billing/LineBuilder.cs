using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

/// <summary>
/// Turns line requests into priced lines: validates every value, snapshots the referenced tax rate (the client never sends
/// a tax percentage) and computes the amounts with <see cref="Pricing"/>. Shared by proposals, contracts and invoices.
/// </summary>
public sealed class LineBuilder(AppDbContext db)
{
    public sealed record Built<T>(List<T> Lines, DocumentTotals Totals, RecurringTotals Recurring) where T : PricedLine;

    public async Task<Built<T>> BuildAsync<T>(IReadOnlyList<PriceLineRequest> requests, string currency, Func<T> factory,
        CancellationToken ct, bool allowRecurrence = false) where T : PricedLine
    {
        currency = NormalizeCurrency(currency);
        if (requests.Count == 0)
            throw new DomainException("billing.no_lines", "Add at least one line.", errors: Errors("lines", "Add at least one line."));
        if (requests.Count > Pricing.MaxLines)
            throw new DomainException("billing.too_many_lines", $"A document can have at most {Pricing.MaxLines} lines.");

        var rateIds = requests.Where(r => r.TaxRateId.HasValue).Select(r => r.TaxRateId!.Value).Distinct().ToList();
        var rates = rateIds.Count == 0
            ? new Dictionary<Guid, TaxRate>()
            : await db.Set<TaxRate>().AsNoTracking().Where(t => rateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);

        var lines = new List<T>(requests.Count);
        var inputs = new List<PriceLineInput>(requests.Count);
        for (var i = 0; i < requests.Count; i++)
        {
            var r = requests[i];
            var field = $"lines[{i}]";
            var description = (r.Description ?? string.Empty).Trim();
            if (description.Length is 0 or > 500)
                throw new DomainException("billing.invalid_line", $"Line {i + 1} needs a description (up to 500 characters).",
                    errors: Errors(field, "Enter a description."));
            TaxRate? rate = null;
            if (r.TaxRateId is { } rateId)
            {
                if (!rates.TryGetValue(rateId, out rate) || !rate.IsActive)
                    throw new DomainException("billing.invalid_tax_rate", $"Line {i + 1} uses a tax rate that doesn't exist or is inactive.",
                        errors: Errors(field, "Choose an active tax rate."));
            }
            var recurrence = allowRecurrence ? r.Recurrence : Recurrence.OneTime;
            var input = new PriceLineInput(r.Quantity, r.UnitPrice, r.DiscountType, r.DiscountValue, rate?.RatePercent ?? 0m,
                rate?.Inclusive ?? false, recurrence, rate?.Name);
            Pricing.Validate(input, currency, field);
            var amounts = Pricing.Compute(input, currency);

            var line = factory();
            line.Position = i + 1;
            line.Description = description;
            line.ServiceSlug = string.IsNullOrWhiteSpace(r.ServiceSlug) ? null : r.ServiceSlug.Trim().ToLowerInvariant();
            line.Quantity = r.Quantity;
            line.UnitPrice = r.UnitPrice;
            line.DiscountType = r.DiscountType;
            line.DiscountValue = r.DiscountType == DiscountType.None ? 0 : r.DiscountValue;
            line.TaxRateId = rate?.Id;
            line.TaxName = rate?.Name;
            line.TaxPercent = rate?.RatePercent ?? 0m;
            line.TaxInclusive = rate?.Inclusive ?? false;
            line.ApplyAmounts(amounts);
            if (line is ProposalLine proposalLine)
            {
                proposalLine.Recurrence = recurrence;
                proposalLine.PackageSlug = string.IsNullOrWhiteSpace(r.PackageSlug) ? null : r.PackageSlug.Trim().ToLowerInvariant();
            }
            lines.Add(line);
            inputs.Add(input);
        }
        return new Built<T>(lines, Pricing.Totals(inputs, currency), Pricing.Recurring(inputs, currency));
    }

    public static string NormalizeCurrency(string? currency)
    {
        var c = Money.Normalize(currency ?? string.Empty);
        if (!Money.IsSupported(c))
            throw new DomainException("billing.currency_unsupported", $"The currency '{currency}' is not supported.",
                errors: Errors("currency", "Choose a supported currency."));
        return c;
    }

    public static PriceLineDto ToDto(PricedLine l) => new(
        l.Id, l.Position, l.Description, l.ServiceSlug, (l as ProposalLine)?.PackageSlug, l.Quantity, l.UnitPrice, l.DiscountType,
        l.DiscountValue, l.TaxRateId, l.TaxName, l.TaxPercent, l.TaxInclusive, (l as ProposalLine)?.Recurrence ?? Recurrence.OneTime,
        l.DiscountAmount, l.Subtotal, l.TaxAmount, l.Total);

    /// <summary>Totals of stored lines (recomputed from their snapshots; equals what was stored at creation).</summary>
    public static DocumentTotals TotalsOf(IEnumerable<PricedLine> lines, string currency) =>
        Pricing.Totals(lines.OrderBy(l => l.Position).Select(l => l.ToInput((l as ProposalLine)?.Recurrence ?? Recurrence.OneTime)).ToList(), currency);

    public static TotalsDto ToDto(DocumentTotals t) => new(t.Currency, t.GrossTotal, t.DiscountTotal, t.Subtotal, t.TaxTotal, t.Total, t.Taxes);

    public static RecurringTotalsDto ToDto(RecurringTotals r) =>
        new(r.OneTimeTotal, r.MonthlyTotal, r.QuarterlyTotal, r.AnnualTotal, r.MonthlyRecurringValue, r.FirstInvoiceTotal, r.FirstYearValue);

    /// <summary>Copies a priced line (e.g. contract line → invoice line) keeping the tax snapshot.</summary>
    public static T Copy<T>(PricedLine source, Func<T> factory, string currency) where T : PricedLine
    {
        var line = factory();
        line.Position = source.Position;
        line.Description = source.Description;
        line.ServiceSlug = source.ServiceSlug;
        line.Quantity = source.Quantity;
        line.UnitPrice = source.UnitPrice;
        line.DiscountType = source.DiscountType;
        line.DiscountValue = source.DiscountValue;
        line.TaxRateId = source.TaxRateId;
        line.TaxName = source.TaxName;
        line.TaxPercent = source.TaxPercent;
        line.TaxInclusive = source.TaxInclusive;
        line.ApplyAmounts(Pricing.Compute(line.ToInput(), currency));
        return line;
    }

    public static void ApplyTotals(Invoice invoice, DocumentTotals totals)
    {
        invoice.GrossTotal = totals.GrossTotal;
        invoice.DiscountTotal = totals.DiscountTotal;
        invoice.Subtotal = totals.Subtotal;
        invoice.TaxTotal = totals.TaxTotal;
        invoice.Total = totals.Total;
        invoice.RecalculateBalance(invoice.Currency);
    }

    public static IReadOnlyDictionary<string, string[]> Errors(string field, string message) =>
        new Dictionary<string, string[]> { [field] = new[] { message } };
}
