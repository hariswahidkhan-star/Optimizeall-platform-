using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Ads;

public sealed class AdImportInput
{
    /// <summary>google-ads, meta-ads or generic.</summary>
    [Required, MaxLength(40)] public string Template { get; set; } = "generic";
    [Required, MaxLength(255)] public string FileName { get; set; } = "import.csv";
    [Required, MaxLength(8_000_000)] public string Csv { get; set; } = string.Empty;

    /// <summary>Target field → CSV header; omit on preview to get the template's suggestion.</summary>
    public Dictionary<string, string>? Mapping { get; set; }

    /// <summary>Import the valid rows even when some rows have errors (otherwise nothing is written).</summary>
    public bool AllowPartial { get; set; }
}

public sealed record ImportTemplateDto(string Id, string Name, AdPlatform? Platform, IReadOnlyDictionary<string, string[]> HeaderAliases, string SampleUrl);

public sealed record ImportPreviewDto(
    IReadOnlyList<string> Headers, int HeaderRow, IReadOnlyDictionary<string, string> Mapping, IReadOnlyList<string> TargetFields,
    IReadOnlyList<string> RequiredFields, IReadOnlyList<ImportPreviewRowDto> Sample, int RowsTotal, int ValidRows, int ExistingRows,
    DateOnly? FromDate, DateOnly? ToDate, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed record ImportPreviewRowDto(int RowNumber, DateOnly Date, AdLevel Level, string Entity, string Currency, decimal Spend, long Impressions,
    long Clicks, decimal Conversions, decimal ConversionValue);

public sealed record ImportResultDto(Guid BatchId, int RowsTotal, int RowsImported, int RowsUpdated, int RowsSkipped, IReadOnlyList<string> Errors,
    DateOnly? FromDate, DateOnly? ToDate, string SourceLabel);

public sealed record ImportBatchDto(Guid Id, string Template, string FileName, int RowsTotal, int RowsImported, int RowsUpdated, int RowsSkipped,
    IReadOnlyList<string> Errors, DateOnly? FromDate, DateOnly? ToDate, DateTime CreatedAt);

/// <summary>CSV import wizard: templates matching the Google Ads / Meta export formats, mapping, validation and idempotent upsert.</summary>
[ApiController]
[Route("api/v1/agency/ads")]
[HasPermission(Permissions.AdsManage)]
public sealed class AdsImportController(AppDbContext db, SocialAccess access, AdMetricWriter writer, ICurrentUser currentUser, IAuditLogger audit,
    TimeProvider clock) : ControllerBase
{
    [HttpGet("import/templates")]
    public IReadOnlyList<ImportTemplateDto> Templates() => AdImportTemplates.All
        .Select(t => new ImportTemplateDto(t.Id, t.Name, t.Platform, t.HeaderAliases, $"/api/v1/agency/ads/import/templates/{t.Id}/sample.csv")).ToList();

    [HttpGet("import/templates/{id}/sample.csv")]
    public IActionResult Sample(string id)
    {
        var template = AdImportTemplates.Find(id) ?? throw DomainException.NotFound("Template");
        return File(Encoding.UTF8.GetBytes(template.SampleCsv), "text/csv; charset=utf-8", $"{template.Id}-sample.csv");
    }

    [HttpPost("accounts/{id:guid}/import/preview")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ImportPreviewDto> Preview(Guid id, AdImportInput input, CancellationToken ct)
    {
        var account = await access.OwnedAsync<AdAccount>(id, a => a.ClientAccountId, "Ad account", ct);
        var (template, rows, mapping) = Prepare(input, account);
        var parsed = AdImportParser.Parse(rows, mapping, account.Currency);
        var warnings = new List<string>();
        if (template.Platform is { } p && p != account.Platform)
            warnings.Add($"This is a {template.Name} template but the account is {account.Platform}.");
        var currencyErrors = parsed.Rows.Where(r => !string.Equals(r.Currency, account.Currency, StringComparison.OrdinalIgnoreCase))
            .Select(r => $"Row {r.RowNumber}: currency {r.Currency} does not match the account currency {account.Currency}.").Take(20).ToList();
        var valid = parsed.Rows.Where(r => string.Equals(r.Currency, account.Currency, StringComparison.OrdinalIgnoreCase)).ToList();
        var existing = 0;
        if (valid.Count > 0)
        {
            var min = valid.Min(r => r.Date);
            var max = valid.Max(r => r.Date);
            var keys = valid.Select(r => r.EntityKey).Distinct().ToList();
            var have = await db.Set<AdDailyMetric>().AsNoTracking().Where(m => m.AdAccountId == account.Id && m.Date >= min && m.Date <= max && keys.Contains(m.EntityKey))
                .Select(m => new { m.Date, m.Level, m.EntityKey }).ToListAsync(ct);
            existing = valid.Count(r => have.Any(h => h.Date == r.Date && h.Level == r.Level && h.EntityKey == r.EntityKey));
            if (existing > 0) warnings.Add($"{existing} row(s) already exist and will be updated (re-importing is safe).");
        }
        return new ImportPreviewDto(parsed.Headers, parsed.HeaderRowIndex + 1, mapping, AdImportFields.All, AdImportFields.Required,
            valid.Take(10).Select(r => new ImportPreviewRowDto(r.RowNumber, r.Date, r.Level, r.EntityName, r.Currency ?? account.Currency, r.Spend, r.Impressions,
                r.Clicks, r.Conversions, r.ConversionValue)).ToList(),
            parsed.RowsTotal, valid.Count, existing, valid.Count > 0 ? valid.Min(r => r.Date) : null, valid.Count > 0 ? valid.Max(r => r.Date) : null,
            parsed.Errors.Take(50).Concat(currencyErrors).ToList(), warnings);
    }

    [HttpPost("accounts/{id:guid}/import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ImportResultDto> Import(Guid id, AdImportInput input, CancellationToken ct)
    {
        var account = await access.OwnedAsync<AdAccount>(id, a => a.ClientAccountId, "Ad account", ct);
        if (input.Mapping is null) throw new DomainException("import.mapping_required", "Confirm the column mapping first.");
        var (template, rows, mapping) = Prepare(input, account);
        var parsed = AdImportParser.Parse(rows, mapping, account.Currency);
        var errors = parsed.Errors.ToList();
        var valid = new List<ParsedAdRow>();
        foreach (var r in parsed.Rows)
        {
            if (string.Equals(r.Currency, account.Currency, StringComparison.OrdinalIgnoreCase)) valid.Add(r);
            else errors.Add($"Row {r.RowNumber}: currency {r.Currency} does not match the account currency {account.Currency}.");
        }
        if (parsed.HeaderRowIndex < 0 || (errors.Count > 0 && !input.AllowPartial) || valid.Count == 0)
            throw new DomainException("import.invalid", errors.Count > 0 ? $"{errors.Count} problem(s) found; nothing was imported." : "No valid rows to import.",
                errors: new Dictionary<string, string[]> { ["rows"] = errors.Take(100).ToArray() });

        var batch = new AdImportBatch
        {
            ClientAccountId = account.ClientAccountId, AdAccountId = account.Id, Template = template.Id, FileName = input.FileName,
            ContentSha256 = Normalization.Sha256Hex(input.Csv), RowsTotal = parsed.RowsTotal, CreatedByUserId = currentUser.Id,
            CreatedAt = clock.GetUtcNow().UtcDateTime, FromDate = valid.Min(r => r.Date), ToDate = valid.Max(r => r.Date),
        };
        db.Set<AdImportBatch>().Add(batch); // saved inside the writer's transaction together with the rows

        var result = await writer.UpsertAsync(account, valid.Select(r => AdMetricRow.From(r, account.Currency)).ToList(), AdMetricSource.CsvImport,
            batch.Id, AdEntitySource.Imported, ct);
        batch.RowsImported = result.Inserted;
        batch.RowsUpdated = result.Updated;
        batch.RowsSkipped = parsed.RowsTotal - valid.Count;
        batch.Errors = errors.Take(100).ToList();
        audit.Record("ads.metrics.imported", nameof(AdImportBatch), batch.Id,
            after: new { account = account.Id, batch.Template, batch.RowsImported, batch.RowsUpdated, batch.RowsSkipped, batch.FromDate, batch.ToDate });
        await db.SaveChangesAsync(ct);
        return new ImportResultDto(batch.Id, batch.RowsTotal, batch.RowsImported, batch.RowsUpdated, batch.RowsSkipped, batch.Errors, batch.FromDate,
            batch.ToDate, AdMetricSources.Label(AdMetricSource.CsvImport));
    }

    [HttpGet("accounts/{id:guid}/imports")]
    public async Task<IReadOnlyList<ImportBatchDto>> Batches(Guid id, CancellationToken ct)
    {
        var account = await access.OwnedAsync<AdAccount>(id, a => a.ClientAccountId, "Ad account", ct);
        return await db.Set<AdImportBatch>().AsNoTracking().Where(b => b.AdAccountId == account.Id).OrderByDescending(b => b.CreatedAt).Take(50)
            .Select(b => new ImportBatchDto(b.Id, b.Template, b.FileName, b.RowsTotal, b.RowsImported, b.RowsUpdated, b.RowsSkipped, b.Errors, b.FromDate, b.ToDate, b.CreatedAt))
            .ToListAsync(ct);
    }

    private static (ImportTemplate Template, List<string[]> Rows, Dictionary<string, string> Mapping) Prepare(AdImportInput input, AdAccount account)
    {
        var template = AdImportTemplates.Find(input.Template) ?? throw new DomainException("import.template_unknown", "Unknown template.");
        var rows = CsvReader.Parse(input.Csv);
        if (rows.Count == 0) throw new DomainException("import.empty", "The file is empty.");
        var mapping = input.Mapping is { Count: > 0 } m
            ? m.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value.Trim())
            : Suggest(template, rows);
        return (template, rows, mapping);
    }

    /// <summary>The template's mapping for the first row (within the first 20) that contains a date header.</summary>
    private static Dictionary<string, string> Suggest(ImportTemplate template, List<string[]> rows)
    {
        foreach (var row in rows.Take(20))
        {
            var suggestion = AdImportTemplates.SuggestMapping(template, row.Select(h => h.Trim()).ToList());
            if (suggestion.ContainsKey(AdImportFields.Date)) return suggestion;
        }
        return new Dictionary<string, string>();
    }
}
