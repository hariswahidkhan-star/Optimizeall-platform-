namespace OptimizeAll.Domain.Common;

/// <summary>
/// Currency helpers. Amounts are stored as DECIMAL(19,4) in the original currency and rounded to the
/// currency's minor unit only at defined points (earning creation, conversion, payout aggregation).
/// Rounding uses MidpointRounding.AwayFromZero, the convention used on bank statements and invoices.
/// </summary>
public static class Money
{
    private static readonly Dictionary<string, int> MinorUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BHD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["OMR"] = 3, ["TND"] = 3, ["IQD"] = 3, ["LYD"] = 3,
        ["JPY"] = 0, ["KRW"] = 0, ["VND"] = 0, ["CLP"] = 0, ["ISK"] = 0, ["UGX"] = 0, ["XAF"] = 0, ["XOF"] = 0,
        ["PYG"] = 0, ["RWF"] = 0, ["KMF"] = 0, ["GNF"] = 0, ["DJF"] = 0, ["VUV"] = 0, ["XPF"] = 0,
    };

    /// <summary>ISO 4217 currencies the platform accepts. Extend here when a new payout currency is enabled.</summary>
    public static readonly IReadOnlySet<string> SupportedCurrencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "EUR", "GBP", "AED", "SAR", "PKR", "INR", "CAD", "AUD", "JPY", "KWD", "BHD", "OMR", "QAR", "EGP", "TRY", "NGN", "ZAR", "BRL", "MXN",
    };

    public static int MinorUnitDigits(string currency) =>
        MinorUnits.TryGetValue(currency, out var digits) ? digits : 2;

    public static decimal Round(decimal amount, string currency) =>
        Math.Round(amount, MinorUnitDigits(currency), MidpointRounding.AwayFromZero);

    public static bool IsSupported(string? currency) =>
        currency is { Length: 3 } && SupportedCurrencies.Contains(currency);

    public static string Normalize(string currency) => currency.Trim().ToUpperInvariant();

    /// <summary>Converts and rounds to the target currency's minor unit.</summary>
    public static decimal Convert(decimal amount, decimal rate, string targetCurrency) =>
        Round(amount * rate, targetCurrency);
}
