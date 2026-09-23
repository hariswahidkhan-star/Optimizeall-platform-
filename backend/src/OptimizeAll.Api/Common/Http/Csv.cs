using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace OptimizeAll.Api.Common.Http;

/// <summary>RFC 4180 CSV writer with spreadsheet formula-injection protection.</summary>
public static class Csv
{
    public static string Write(IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', header.Select(Escape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(',', row.Select(v => Escape(Format(v)))));
        return sb.ToString();
    }

    public static FileContentResult File(string fileName, IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Write(header, rows))).ToArray();
        return new FileContentResult(bytes, "text/csv; charset=utf-8") { FileDownloadName = fileName };
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    public static string Escape(string value)
    {
        // Prevent =, +, -, @, tab and CR from being interpreted as formulas by spreadsheet apps,
        // unless the value is a plain number (e.g. negative amounts).
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]) &&
            !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
