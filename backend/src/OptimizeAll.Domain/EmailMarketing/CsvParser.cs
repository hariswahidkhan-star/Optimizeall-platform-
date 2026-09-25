using System.Text;

namespace OptimizeAll.Domain.EmailMarketing;

/// <summary>RFC 4180 CSV reader (quoted fields, escaped quotes, embedded new lines, CRLF/LF, optional BOM, comma or semicolon).</summary>
public static class CsvParser
{
    public const int MaxColumns = 100;
    public const int MaxFieldLength = 5000;

    /// <summary>Detects the delimiter from the header line (comma, semicolon or tab).</summary>
    public static char DetectDelimiter(string content)
    {
        var firstLine = content.Split('\n', 2)[0];
        var counts = new[] { ',', ';', '\t' }.Select(d => (d, firstLine.Count(c => c == d))).OrderByDescending(x => x.Item2).First();
        return counts.Item2 == 0 ? ',' : counts.d;
    }

    /// <summary>Parses all records. Throws <see cref="FormatException"/> for an unterminated quote or too many columns.</summary>
    public static List<string[]> Parse(string content, char? delimiter = null) =>
        ParseRecords(content, delimiter).Select(r => r.Fields).ToList();

    /// <summary>
    /// Like <see cref="Parse"/>, with the 1-based line each record starts on (blank lines are skipped but still counted), so
    /// import reports can point at the line a spreadsheet shows.
    /// </summary>
    public static List<CsvRecord> ParseRecords(string content, char? delimiter = null)
    {
        if (content.Length > 0 && content[0] == '﻿') content = content[1..];
        var d = delimiter ?? DetectDelimiter(content);
        var rows = new List<CsvRecord>();
        var fields = new List<string>();
        var recordLine = 1;
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        var line = 1;
        while (i < content.Length)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false;
                    i++;
                    continue;
                }
                if (c == '\n') line++;
                field.Append(c);
                i++;
            }
            else if (c == '"' && field.Length == 0) { inQuotes = true; i++; }
            else if (c == d) { AddField(); i++; }
            else if (c == '\r') { i++; }
            else if (c == '\n')
            {
                AddField();
                EndRow();
                line++;
                recordLine = line;
                i++;
            }
            else { field.Append(c); i++; }

            if (field.Length > MaxFieldLength) throw new FormatException($"A field on line {line} is longer than {MaxFieldLength} characters.");
        }
        if (inQuotes) throw new FormatException($"Unterminated quoted field starting before line {line}.");
        if (field.Length > 0 || fields.Count > 0) { AddField(); EndRow(); }
        return rows;

        void AddField()
        {
            fields.Add(field.ToString());
            field.Clear();
            if (fields.Count > MaxColumns) throw new FormatException($"Line {line} has more than {MaxColumns} columns.");
        }

        void EndRow()
        {
            // Skip completely blank lines.
            if (!(fields.Count == 1 && fields[0].Length == 0)) rows.Add(new CsvRecord(recordLine, fields.ToArray()));
            fields.Clear();
        }
    }
}

/// <summary>A parsed CSV record and the (1-based) line it starts on.</summary>
public sealed record CsvRecord(int Line, string[] Fields);
