using System.Text;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Crm;

/// <summary>Minimal RFC 4180 CSV reader (quoted fields, escaped quotes, CRLF/LF, UTF-8 BOM).</summary>
public static class CsvReader
{
    public static List<List<string>> Parse(string text, int maxRows)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"' when field.Length == 0: inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                    row = new List<string>();
                    if (rows.Count > maxRows + 1)
                        throw new DomainException("crm.import_too_large", $"Import at most {maxRows} rows per file.");
                    break;
                default: field.Append(c); break;
            }
        }
        if (inQuotes) throw new DomainException("crm.import_invalid_csv", "The file has an unterminated quoted field.");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
        }
        if (rows.Count > maxRows + 1) throw new DomainException("crm.import_too_large", $"Import at most {maxRows} rows per file.");
        return rows;
    }
}

/// <summary>
/// CSV import/export of contacts. Import validates every row, dedupes by normalized email (within the file and against
/// existing contacts), links or creates companies by domain/name, and reports a result per row. <c>dryRun</c> validates
/// without saving; <c>updateExisting</c> fills blank fields of existing contacts instead of skipping them.
/// </summary>
public sealed class ContactImportService(AppDbContext db, IDatabaseDialect dialect, IAuditLogger audit, LeadScoringService scoring, TimeProvider clock)
{
    public const int MaxRows = 5000;
    public const long MaxBytes = 2 * 1024 * 1024;

    public static readonly string[] Columns =
    {
        "first_name", "last_name", "email", "phone", "job_title", "company", "company_domain", "lifecycle_stage", "consent", "source", "tags",
    };

    public async Task<ImportResultDto> ImportAsync(Stream stream, bool dryRun, bool updateExisting, CancellationToken ct)
    {
        string text;
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = await reader.ReadToEndAsync(ct);
        var rows = CsvReader.Parse(text, MaxRows);
        if (rows.Count == 0) throw new DomainException("crm.import_empty", "The file is empty.");
        var header = rows[0].Select(h => h.Trim().ToLowerInvariant().Replace(' ', '_')).ToList();
        if (!header.Contains("email") && !header.Contains("first_name"))
            throw new DomainException("crm.import_invalid_header", $"The first row must be a header with at least first_name or email. Columns: {string.Join(", ", Columns)}.");
        string? Cell(List<string> row, string column)
        {
            var index = header.IndexOf(column);
            return index >= 0 && index < row.Count && !string.IsNullOrWhiteSpace(row[index]) ? row[index].Trim() : null;
        }

        var results = new List<ImportRowResultDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var emails = rows.Skip(1).Select(r => Cell(r, "email")).Where(e => e is not null).Select(e => Normalization.Email(e!)).Distinct().ToList();
        var existing = new Dictionary<string, CrmContact>();
        foreach (var chunk in emails.Chunk(500))
            foreach (var c in await db.Set<CrmContact>().Where(c => c.NormalizedEmail != null && chunk.Contains(c.NormalizedEmail)).ToListAsync(ct))
                existing[c.NormalizedEmail!] = c;
        var companiesByDomain = new Dictionary<string, CrmCompany>(StringComparer.Ordinal);
        var companiesByName = new Dictionary<string, CrmCompany>(StringComparer.OrdinalIgnoreCase);
        var touched = new List<CrmContact>();
        int created = 0, updated = 0, skipped = 0, failed = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 1;
            var errors = new List<string>();
            var email = Cell(row, "email");
            var first = Cell(row, "first_name");
            var last = Cell(row, "last_name");
            if (email is not null && !CrmNormalization.IsValidEmail(email)) errors.Add($"'{Short(email)}' is not a valid email address.");
            if (first is null && email is null) errors.Add("Each row needs a first_name or an email.");
            if (first is { Length: > 100 } || last is { Length: > 100 }) errors.Add("Names can be at most 100 characters.");
            var lifecycle = LifecycleStage.Lead;
            if (Cell(row, "lifecycle_stage") is { } ls && !TryLifecycle(ls, out lifecycle)) errors.Add($"Unknown lifecycle stage '{Short(ls)}'.");
            var consent = ConsentStatus.Unknown;
            if (Cell(row, "consent") is { } cs && !TryConsent(cs, out consent)) errors.Add($"Unknown consent value '{Short(cs)}' (use subscribed, unsubscribed, not_given).");
            var domainText = Cell(row, "company_domain");
            var domain = CrmNormalization.Domain(domainText);
            if (domainText is not null && domain is null) errors.Add($"'{Short(domainText)}' is not a valid domain.");
            var normalized = email is null ? null : Normalization.Email(email);
            if (normalized is not null && !seen.Add(normalized)) errors.Add("Duplicate email earlier in this file.");
            if (errors.Count > 0)
            {
                failed++;
                results.Add(new ImportRowResultDto(rowNumber, "error", email, null, errors));
                continue;
            }

            CrmCompany? company = null;
            var companyName = CrmService.Trim(Cell(row, "company"), 200);
            if (domain is not null || companyName is not null)
                company = await ResolveCompanyAsync(domain, companyName, companiesByDomain, companiesByName, dryRun, ct);

            var tags = CrmNormalization.Tags((Cell(row, "tags") ?? string.Empty).Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries));
            if (normalized is not null && existing.TryGetValue(normalized, out var match))
            {
                if (!updateExisting)
                {
                    skipped++;
                    results.Add(new ImportRowResultDto(rowNumber, "skipped", email, match.Id, new[] { "A contact with this email already exists." }));
                    continue;
                }
                match.LastName ??= CrmService.Trim(last, 100);
                match.Phone ??= CrmService.Trim(Cell(row, "phone"), 40);
                match.JobTitle ??= CrmService.Trim(Cell(row, "job_title"), 120);
                match.CompanyId ??= company?.Id;
                match.Source ??= CrmService.Trim(Cell(row, "source"), 100);
                if (consent != ConsentStatus.Unknown && consent != match.ConsentStatus)
                {
                    match.ConsentStatus = consent;
                    match.ConsentChangedAt = clock.GetUtcNow().UtcDateTime;
                }
                match.Tags = CrmNormalization.Tags(match.Tags.Concat(tags));
                match.TagIndex = CrmService.TagIndex(match.Tags);
                touched.Add(match);
                updated++;
                results.Add(new ImportRowResultDto(rowNumber, "updated", email, match.Id, Array.Empty<string>()));
                continue;
            }
            var contact = new CrmContact
            {
                FirstName = CrmService.Trim(first, 100) ?? email!.Split('@')[0],
                LastName = CrmService.Trim(last, 100),
                Email = email,
                NormalizedEmail = normalized,
                Phone = CrmService.Trim(Cell(row, "phone"), 40),
                JobTitle = CrmService.Trim(Cell(row, "job_title"), 120),
                CompanyId = company?.Id,
                LifecycleStage = lifecycle,
                ConsentStatus = consent,
                ConsentChangedAt = consent == ConsentStatus.Unknown ? null : clock.GetUtcNow().UtcDateTime,
                Source = CrmService.Trim(Cell(row, "source"), 100) ?? "csv-import",
                Tags = tags,
                TagIndex = CrmService.TagIndex(tags),
            };
            if (!dryRun) db.Set<CrmContact>().Add(contact);
            if (normalized is not null) existing[normalized] = contact;
            touched.Add(contact);
            created++;
            results.Add(new ImportRowResultDto(rowNumber, "created", email, dryRun ? null : contact.Id, Array.Empty<string>()));
        }

        if (!dryRun && (created > 0 || updated > 0))
        {
            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            audit.Record("crm.contacts_imported", nameof(CrmContact), "import",
                after: new { Rows = rows.Count - 1, Created = created, Updated = updated, Skipped = skipped, Failed = failed });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                throw DomainException.Conflict("crm.import_conflict", "Some contacts or companies were created by someone else during the import. Run the import again.");
            }
            await tx.CommitAsync(ct);
            foreach (var c in touched) await scoring.RecomputeAsync(c.Id, ct);
        }
        return new ImportResultDto(dryRun, rows.Count - 1, created, updated, skipped, failed, results);
    }

    private async Task<CrmCompany> ResolveCompanyAsync(string? domain, string? name, Dictionary<string, CrmCompany> byDomain,
        Dictionary<string, CrmCompany> byName, bool dryRun, CancellationToken ct)
    {
        if (domain is not null)
        {
            if (byDomain.TryGetValue(domain, out var cached)) return cached;
            var found = await db.Set<CrmCompany>().FirstOrDefaultAsync(c => c.Domain == domain, ct);
            if (found is not null) return byDomain[domain] = found;
        }
        if (name is not null)
        {
            if (byName.TryGetValue(name, out var cached)) return cached;
            var found = await db.Set<CrmCompany>().FirstOrDefaultAsync(c => c.Name == name && (domain == null || c.Domain == null), ct);
            if (found is not null)
            {
                if (domain is not null && found.Domain is null) found.Domain = domain;
                if (domain is not null) byDomain[domain] = found;
                return byName[name] = found;
            }
        }
        var company = new CrmCompany { Name = name ?? domain!, Domain = domain };
        if (!dryRun) db.Set<CrmCompany>().Add(company);
        if (domain is not null) byDomain[domain] = company;
        if (name is not null) byName[name] = company;
        return company;
    }

    private static bool TryLifecycle(string value, out LifecycleStage stage)
    {
        var v = value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        (bool, LifecycleStage) r = v switch
        {
            "mql" => (true, LifecycleStage.MarketingQualifiedLead),
            "sql" => (true, LifecycleStage.SalesQualifiedLead),
            _ => Enum.TryParse<LifecycleStage>(v, ignoreCase: true, out var parsed) && !int.TryParse(v, out _) ? (true, parsed) : (false, LifecycleStage.Lead),
        };
        stage = r.Item2;
        return r.Item1;
    }

    private static bool TryConsent(string value, out ConsentStatus consent)
    {
        var v = value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        (bool, ConsentStatus) r = v switch
        {
            "yes" or "true" or "optedin" => (true, ConsentStatus.Subscribed),
            "no" or "false" or "optedout" => (true, ConsentStatus.Unsubscribed),
            _ => Enum.TryParse<ConsentStatus>(v, ignoreCase: true, out var parsed) && !int.TryParse(v, out _) ? (true, parsed) : (false, ConsentStatus.Unknown),
        };
        consent = r.Item2;
        return r.Item1;
    }

    private static string Short(string s) => s.Length <= 60 ? s : s[..60] + "…";
}
