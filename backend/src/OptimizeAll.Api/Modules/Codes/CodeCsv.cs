using System.Globalization;
using System.Text;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;

namespace OptimizeAll.Api.Modules.Codes;

/// <summary>CSV reading for code and sales-report imports: size limits, header aliases, line numbers, tolerant values.</summary>
internal static class CodeCsv
{
    public sealed record Table(IReadOnlyDictionary<string, int> Columns, IReadOnlyList<CsvRecord> Rows, bool HasHeader)
    {
        public string? Get(CsvRecord row, string column) =>
            Columns.TryGetValue(column, out var i) && i < row.Fields.Length && !string.IsNullOrWhiteSpace(row.Fields[i]) ? row.Fields[i].Trim() : null;
    }

    public static async Task<string> ReadAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) throw new DomainException("csv.empty", "The file is empty.");
        if (file.Length > CodeLimits.MaxCsvBytes) throw new DomainException("csv.too_large", "The file is larger than 10 MB; split it.");
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>Parses the file and maps header names (case/space/underscore-insensitive) through <paramref name="aliases"/>.</summary>
    public static Table Parse(string content, IReadOnlyDictionary<string, string> aliases, string requiredColumn, bool firstColumnFallback, int maxRows)
    {
        List<CsvRecord> records;
        try
        {
            records = CsvParser.ParseRecords(content);
        }
        catch (FormatException ex)
        {
            throw new DomainException("csv.invalid", ex.Message);
        }
        records = records.Where(r => r.Fields.Any(f => !string.IsNullOrWhiteSpace(f))).ToList();
        if (records.Count == 0) throw new DomainException("csv.empty", "The file has no rows.");

        var columns = new Dictionary<string, int>();
        var header = records[0].Fields;
        for (var i = 0; i < header.Length; i++)
        {
            var key = Key(header[i]);
            if (aliases.TryGetValue(key, out var column) && !columns.ContainsKey(column)) columns[column] = i;
        }
        var hasHeader = columns.Count > 0;
        if (!columns.ContainsKey(requiredColumn))
        {
            if (!firstColumnFallback || hasHeader)
                throw new DomainException("csv.missing_column", $"The file needs a '{requiredColumn}' column (header row).");
            columns[requiredColumn] = 0;
        }
        var rows = records.Skip(hasHeader ? 1 : 0).ToList();
        if (rows.Count == 0) throw new DomainException("csv.empty", "The file has a header but no rows.");
        if (rows.Count > maxRows) throw new DomainException("csv.too_many_rows", $"A file can have at most {maxRows:N0} rows; split it.");
        return new Table(columns, rows, hasHeader);
    }

    public static string Key(string header) =>
        new string(header.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c)).ToArray());

    public static bool TryDate(string? value, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
            return false;
        utc = d.UtcDateTime;
        return true;
    }

    /// <summary>Invariant decimal; tolerates a currency symbol/code and thousands separators ("$1,234.50", "1234.50 USD").</summary>
    public static bool TryAmount(string? value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var cleaned = new string(value.Where(c => char.IsDigit(c) || c is '.' or '-').ToArray());
        return cleaned.Length > 0 && decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}
