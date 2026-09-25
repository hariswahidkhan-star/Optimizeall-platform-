using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Common.Http;

/// <summary>
/// Row caps of the CSV exports (configuration section <c>Exports</c>). An export file is built in memory, so each has a
/// cap; it never truncates silently: when more rows match than the cap, the export is refused with
/// 422 <c>export.too_large</c> (see <see cref="ExportLimit"/>) and the web app shows the message, which tells the user to
/// narrow the filters. docs/OPERATIONS.md lists the defaults.
/// </summary>
public sealed class ExportOptions
{
    public const string Section = "Exports";

    public int Ledger { get; set; } = 100_000;
    public int PaymentsHub { get; set; } = 20_000;
    public int AuditLog { get; set; } = 50_000;
    public int Users { get; set; } = 50_000;
    public int CrmContacts { get; set; } = 50_000;
    public int FormSubmissions { get; set; } = 50_000;
    public int EmailList { get; set; } = 200_000;
    public int NewsletterSubscribers { get; set; } = 100_000;
    public int Inquiries { get; set; } = 50_000;
    public int TimeEntries { get; set; } = 50_000;
}

/// <summary>Refuses an export whose matching rows exceed its cap, instead of cutting the file short without saying so.</summary>
public static class ExportLimit
{
    public const string TooLargeCode = "export.too_large";

    public static DomainException TooLarge(long matching, int max) =>
        DomainException.Unprocessable(TooLargeCode,
            $"{matching:N0} rows match these filters, but one export can hold at most {max:N0}. " +
            "Narrow the filters (for example a shorter date range) and export again.");

    /// <summary>Throws <see cref="TooLarge"/> when <paramref name="matching"/> exceeds <paramref name="max"/>.</summary>
    public static void Ensure(long matching, int max)
    {
        if (matching > max) throw TooLarge(matching, max);
    }

    /// <summary>Counts the rows <paramref name="query"/> matches and throws <see cref="TooLarge"/> above <paramref name="max"/>.</summary>
    public static async Task EnsureAsync<T>(IQueryable<T> query, int max, CancellationToken ct) =>
        Ensure(await query.CountAsync(ct), max);
}
