using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Audiences;

public sealed class ImportPreviewRequest
{
    [Required, MaxLength(10_000_000)] public string Csv { get; set; } = string.Empty;
}

public sealed record ImportPreview(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> SampleRows, int TotalRows,
    IReadOnlyDictionary<string, string> SuggestedMapping, IReadOnlyList<string> Targets);

public sealed class ImportRequest
{
    [Required, MaxLength(255)] public string FileName { get; set; } = "import.csv";
    [Required, MaxLength(10_000_000)] public string Csv { get; set; } = string.Empty;
    /// <summary>CSV header → target (email, phone, first_name, last_name, language, country, time_zone, tags, custom.&lt;key&gt;, ignore).</summary>
    [Required] public Dictionary<string, string> Mapping { get; set; } = new();
    public List<string>? Tags { get; set; }
    /// <summary>Must be true: the staff member confirms every contact gave consent to receive marketing from this sender.</summary>
    public bool ConfirmConsent { get; set; }
    /// <summary>How/where consent was collected (e.g. "Checkout opt-in checkbox, Shopify, 2024–2026").</summary>
    [Required, MaxLength(200)] public string ConsentSource { get; set; } = string.Empty;
    /// <summary>Also grant SMS consent for rows with a phone number (only when consent covered SMS).</summary>
    public bool GrantSmsConsent { get; set; }
}

public sealed record ImportRowError(int Row, string Message);

public sealed record ImportDto(Guid Id, Guid ListId, ImportStatus Status, string FileName, int TotalRows, int ProcessedRows, int Created, int Updated,
    int Skipped, int Failed, IReadOnlyList<ImportRowError> Errors, DateTime CreatedAt, DateTime? CompletedAt);

/// <summary>
/// CSV imports: column mapping, per-row validation and errors, de-duplication (within the file and against existing
/// contacts), suppression and prior-unsubscribe protection, and a mandatory consent attestation. Files up to
/// <see cref="InlineRows"/> rows are processed in the request; larger files are processed by <see cref="SubscriberImportJob"/>
/// in resumable chunks.
/// </summary>
public sealed class ImportService(
    AppDbContext db, ICurrentUser currentUser, IAuditLogger audit, EmailAccess access, AudienceService audiences, TimeProvider clock)
{
    public const int InlineRows = 500;
    public const int ChunkRows = 500;
    public const int MaxRows = 100_000;
    public const int MaxErrors = 500;

    public static readonly string[] Targets =
    {
        "email", "phone", "first_name", "last_name", "language", "country", "time_zone", "tags", "ignore",
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ImportPreview> PreviewAsync(Guid listId, ImportPreviewRequest r, CancellationToken ct)
    {
        await audiences.LoadListAsync(listId, ct);
        var rows = Parse(r.Csv);
        if (rows.Count == 0) throw new DomainException("email.import_empty", "The file is empty.");
        var headers = rows[0].Select(h => h.Trim()).ToList();
        var suggested = new Dictionary<string, string>();
        foreach (var h in headers) suggested[h] = Suggest(h);
        return new ImportPreview(headers, rows.Skip(1).Take(5).Select(x => (IReadOnlyList<string>)x.ToList()).ToList(), rows.Count - 1, suggested, Targets);
    }

    public async Task<ImportDto> StartAsync(Guid listId, ImportRequest r, CancellationToken ct)
    {
        var list = await audiences.LoadListAsync(listId, ct);
        if (list.IsArchived) throw new DomainException("email.list_archived", "The list is archived.");
        if (!r.ConfirmConsent)
            throw new DomainException("email.import_consent_required",
                "Confirm that every contact in the file gave consent to receive marketing from this sender.");
        if (string.IsNullOrWhiteSpace(r.ConsentSource))
            throw new DomainException("email.import_consent_required", "Describe how and where consent was collected.");
        var rows = Parse(r.Csv);
        if (rows.Count < 2) throw new DomainException("email.import_empty", "The file has no data rows.");
        if (rows.Count - 1 > MaxRows) throw new DomainException("email.import_too_large", $"Import at most {MaxRows:N0} rows at a time.");
        ValidateMapping(rows[0], r.Mapping);
        var tags = AudienceService.NormalizeTags(r.Tags);

        var import = new SubscriberImport
        {
            ClientAccountId = list.ClientAccountId,
            ListId = list.Id,
            FileName = Text.Truncate(Path.GetFileName(r.FileName), 255),
            CsvContent = r.Csv,
            MappingJson = JsonSerializer.Serialize(r.Mapping, Json),
            TagsJson = JsonSerializer.Serialize(tags, Json),
            ConsentAttestation = "I confirm every contact in this file gave consent to receive marketing from this sender, and the consent can be evidenced.",
            ConsentSource = r.ConsentSource.Trim(),
            GrantSmsConsent = r.GrantSmsConsent,
            AttestedByUserId = currentUser.Id,
            TotalRows = rows.Count - 1,
        };
        db.Set<SubscriberImport>().Add(import);
        audit.Record("email.import.started", nameof(SubscriberImport), import.Id,
            after: new { list = list.Id, import.FileName, import.TotalRows, import.ConsentSource, r.GrantSmsConsent, mapping = r.Mapping });
        await db.SaveChangesAsync(ct);

        if (import.TotalRows <= InlineRows) await ProcessAsync(import.Id, int.MaxValue, ct);
        return await GetAsync(import.Id, ct);
    }

    public async Task<ImportDto> GetAsync(Guid id, CancellationToken ct)
    {
        var import = await access.LoadAsync(db.Set<SubscriberImport>().AsNoTracking().Where(i => i.Id == id), i => i.ClientAccountId, "Import", ct);
        return ToDto(import);
    }

    public async Task<IReadOnlyList<ImportDto>> ForListAsync(Guid listId, CancellationToken ct)
    {
        await audiences.LoadListAsync(listId, ct);
        var imports = await db.Set<SubscriberImport>().AsNoTracking().Where(i => i.ListId == listId).OrderByDescending(i => i.CreatedAt).Take(20).ToListAsync(ct);
        return imports.Select(ToDto).ToList();
    }

    private static ImportDto ToDto(SubscriberImport i) => new(i.Id, i.ListId, i.Status, i.FileName, i.TotalRows, i.ProcessedRows, i.Created, i.Updated,
        i.Skipped, i.Failed, JsonSerializer.Deserialize<List<ImportRowError>>(i.ErrorsJson, Json) ?? new(), i.CreatedAt, i.CompletedAt);

    /// <summary>
    /// Processes up to <paramref name="maxRows"/> rows of an import. Claimed with a conditional update (lease), so two
    /// workers never process the same import; progress is checkpointed after each chunk, and re-processing a row is
    /// harmless because contacts are upserted by email.
    /// </summary>
    public async Task<bool> ProcessAsync(Guid importId, int maxRows, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var lease = now.AddMinutes(10);
        var claimed = await db.Set<SubscriberImport>()
            .Where(i => i.Id == importId && (i.Status == ImportStatus.Pending || i.Status == ImportStatus.Processing) && (i.LockedUntil == null || i.LockedUntil < now))
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, ImportStatus.Processing).SetProperty(i => i.LockedUntil, lease)
                .SetProperty(i => i.StartedAt, i => i.StartedAt ?? now), ct);
        if (claimed != 1) return false;

        var import = await db.Set<SubscriberImport>().AsNoTracking().FirstAsync(i => i.Id == importId, ct);
        var list = await db.Set<EmailList>().AsNoTracking().FirstAsync(l => l.Id == import.ListId, ct);
        var records = ParseRecords(import.CsvContent ?? string.Empty);
        var rows = records.Select(r => r.Fields).ToList();
        var header = rows.Count > 0 ? rows[0].Select(h => h.Trim()).ToArray() : Array.Empty<string>();
        var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(import.MappingJson, Json) ?? new();
        var tags = JsonSerializer.Deserialize<List<string>>(import.TagsJson, Json) ?? new();
        var errors = JsonSerializer.Deserialize<List<ImportRowError>>(import.ErrorsJson, Json) ?? new();
        var columns = header.Select(h => mapping.TryGetValue(h, out var t) ? t : "ignore").ToArray();

        // Addresses seen earlier in the file (for de-duplication across chunks).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i <= import.ProcessedRows && i < rows.Count; i++)
            if (Key(rows[i], columns) is { } k) seen.Add(k);

        int created = import.Created, updated = import.Updated, skipped = import.Skipped, failed = import.Failed, processed = import.ProcessedRows;
        var start = import.ProcessedRows + 1;
        var end = (int)Math.Min(rows.Count - 1L, (long)import.ProcessedRows + Math.Min(maxRows, int.MaxValue - 1));
        for (var index = start; index <= end; index++)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await ImportRowAsync(list, import, rows[index], columns, tags, seen, ct);
            switch (outcome.Result)
            {
                case RowResult.Created: created++; break;
                case RowResult.Updated: updated++; break;
                case RowResult.Skipped: skipped++; break;
                default: failed++; break;
            }
            // The line of the file (as the person sees it in their editor), not the record index: blank lines are skipped
            // and quoted values can span lines.
            if (outcome.Message is not null && errors.Count < MaxErrors) errors.Add(new ImportRowError(records[index].Line, outcome.Message));
            processed = index;
            db.ChangeTracker.Clear();
            if ((index - start + 1) % 100 == 0 || index == end)
                await Checkpoint(importId, processed, created, updated, skipped, failed, errors, done: false, ct);
        }

        var finished = processed >= rows.Count - 1;
        await Checkpoint(importId, processed, created, updated, skipped, failed, errors, finished, ct);
        if (finished)
        {
            audit.RecordSystem("email.import.completed", nameof(SubscriberImport), importId, after: new { created, updated, skipped, failed });
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    private async Task Checkpoint(Guid id, int processed, int created, int updated, int skipped, int failed, List<ImportRowError> errors, bool done, CancellationToken ct)
    {
        var errorsJson = JsonSerializer.Serialize(errors, Json);
        var now = clock.GetUtcNow().UtcDateTime;
        var lease = now.AddMinutes(10);
        await db.Set<SubscriberImport>().Where(i => i.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(i => i.ProcessedRows, processed).SetProperty(i => i.Created, created).SetProperty(i => i.Updated, updated)
            .SetProperty(i => i.Skipped, skipped).SetProperty(i => i.Failed, failed).SetProperty(i => i.ErrorsJson, errorsJson)
            .SetProperty(i => i.Status, done ? ImportStatus.Completed : ImportStatus.Processing)
            .SetProperty(i => i.CompletedAt, done ? now : null)
            .SetProperty(i => i.CsvContent, i => done ? null : i.CsvContent)
            .SetProperty(i => i.LockedUntil, done ? null : lease), ct);
    }

    private enum RowResult { Created, Updated, Skipped, Failed }

    private sealed record RowOutcome(RowResult Result, string? Message = null);

    private async Task<RowOutcome> ImportRowAsync(EmailList list, SubscriberImport import, string[] row, string[] columns, List<string> fileTags,
        HashSet<string> seen, CancellationToken ct)
    {
        string? Cell(string target)
        {
            var idx = Array.IndexOf(columns, target);
            return idx >= 0 && idx < row.Length && !string.IsNullOrWhiteSpace(row[idx]) ? row[idx].Trim() : null;
        }

        var rawEmail = Cell("email");
        var rawPhone = Cell("phone");
        var email = rawEmail is null ? null : ContactRules.NormalizeEmail(rawEmail);
        var phone = rawPhone is null ? null : ContactRules.NormalizePhone(rawPhone);
        if (rawEmail is not null && email is null) return new(RowResult.Failed, $"Invalid email address '{Text.Truncate(rawEmail, 80)}'.");
        if (rawPhone is not null && phone is null) return new(RowResult.Failed, $"Invalid phone number '{Text.Truncate(rawPhone, 40)}' (use +countrycode…).");
        if (email is null && phone is null) return new(RowResult.Failed, "No email address or phone number.");
        var dedupKey = email ?? phone!;
        if (!seen.Add(dedupKey)) return new(RowResult.Skipped, $"Duplicate of an earlier row ({dedupKey}).");

        var scope = Workspace.Key(list.ClientAccountId);
        if (email is not null && await db.Set<Suppression>().AsNoTracking().Where(x => x.ScopeKey == scope && x.Channel == MessageChannel.Email && x.Value == email)
                .Select(x => (SuppressionReason?)x.Reason).FirstOrDefaultAsync(ct) is { } reason)
            return new(RowResult.Skipped, $"{email} is on the suppression list ({reason}); not imported.");

        var existing = email is not null
            ? await db.Set<Subscriber>().AsNoTracking().FirstOrDefaultAsync(s => s.ScopeKey == scope && s.NormalizedEmail == email, ct)
            : await db.Set<Subscriber>().AsNoTracking().FirstOrDefaultAsync(s => s.ScopeKey == scope && s.Phone == phone, ct);
        if (existing is not null && (existing.Status is not SubscriberStatus.Subscribed || existing.EmailConsent == ConsentStatus.Withdrawn))
            return new(RowResult.Skipped, $"{dedupKey} previously unsubscribed, bounced or complained; an import cannot re-subscribe them.");
        // Unsubscribing from this one list (preference center topic, list-level unsubscribe) is an opt-out too: only the
        // contact can undo it (preference center or a confirmed sign-up), never a file.
        if (existing is not null && await db.Set<ListMembership>().AsNoTracking()
                .AnyAsync(m => m.ListId == list.Id && m.SubscriberId == existing.Id && m.Status == MembershipStatus.Unsubscribed, ct))
            return new(RowResult.Skipped, $"{dedupKey} previously unsubscribed from this list; an import cannot re-subscribe them.");

        var errors = new List<string>();
        var country = Cell("country")?.ToUpperInvariant();
        if (country is not null && !ContactRules.IsValidCountry(country)) { errors.Add($"country '{country}' ignored"); country = null; }
        var language = Cell("language");
        if (language is not null && !ContactRules.IsValidLanguage(language)) { errors.Add($"language '{language}' ignored"); language = null; }
        var tz = Cell("time_zone");
        if (tz is not null && !SendTiming.IsValidZone(tz)) { errors.Add($"time zone '{tz}' ignored"); tz = null; }
        var fields = new Dictionary<string, string?>();
        for (var i = 0; i < columns.Length && i < row.Length; i++)
        {
            if (!columns[i].StartsWith("custom.", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(row[i])) continue;
            var value = row[i].Trim();
            if (value.Length > ContactRules.MaxCustomValueLength) { errors.Add($"{columns[i]} too long, ignored"); continue; }
            fields[columns[i]["custom.".Length..]] = value;
        }
        var rowTags = new List<string>(fileTags);
        if (Cell("tags") is { } tagCell)
            foreach (var t in tagCell.Split(new[] { ';', '|', ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (ContactRules.NormalizeTag(t) is { } tag && !rowTags.Contains(tag)) rowTags.Add(tag);

        var input = new ContactInput(email, phone, Text.Clean(Cell("first_name"), 100), Text.Clean(Cell("last_name"), 100), language, country, tz,
            fields, rowTags, "import");
        var consentNote = $"Import {import.FileName}: {import.ConsentSource}";
        var emailConsent = email is null ? null : new ConsentGrant(ConsentStatus.Granted, "import", null, "import-attestation", import.AttestedByUserId, consentNote);
        // A number that texted STOP (or is otherwise SMS-suppressed) can only opt back in itself (START); an import never re-grants it.
        var smsSuppressed = phone is not null && import.GrantSmsConsent &&
                            await db.Set<Suppression>().AsNoTracking().AnyAsync(x => x.ScopeKey == scope && x.Channel == MessageChannel.Sms && x.Value == phone, ct);
        if (smsSuppressed) errors.Add($"{phone} opted out of SMS; SMS consent not recorded");
        var smsConsent = phone is not null && import.GrantSmsConsent && !smsSuppressed ? new ConsentGrant(ConsentStatus.Granted, "import", null, "import-attestation", import.AttestedByUserId, consentNote) : null;
        var (subscriber, created) = await audiences.UpsertContactAsync(list.ClientAccountId, input, emailConsent, smsConsent, allowResubscribe: false, ct);
        await audiences.SubscribeAsync(list, subscriber, "import", requireConfirmation: false, null, ct);
        return new(created ? RowResult.Created : RowResult.Updated, errors.Count == 0 ? null : string.Join("; ", errors));
    }

    private static string? Key(string[] row, string[] columns)
    {
        var e = Array.IndexOf(columns, "email");
        if (e >= 0 && e < row.Length && ContactRules.NormalizeEmail(row[e]) is { } email) return email;
        var p = Array.IndexOf(columns, "phone");
        return p >= 0 && p < row.Length ? ContactRules.NormalizePhone(row[p]) : null;
    }

    private static List<string[]> Parse(string csv) => ParseRecords(csv).Select(r => r.Fields).ToList();

    private static List<CsvRecord> ParseRecords(string csv)
    {
        try { return CsvParser.ParseRecords(csv); }
        catch (FormatException ex) { throw new DomainException("email.import_invalid_csv", ex.Message); }
    }

    private static void ValidateMapping(string[] header, Dictionary<string, string> mapping)
    {
        var errors = new List<string>();
        var headers = header.Select(h => h.Trim()).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>();
        foreach (var (column, target) in mapping)
        {
            if (!headers.Contains(column)) errors.Add($"Column '{Text.Truncate(column, 60)}' is not in the file.");
            var valid = Targets.Contains(target) || (target.StartsWith("custom.", StringComparison.Ordinal) && ContactRules.IsValidFieldKey(target["custom.".Length..]));
            if (!valid) errors.Add($"'{Text.Truncate(target, 60)}' is not a valid target field.");
            if (target != "ignore" && !used.Add(target)) errors.Add($"'{target}' is mapped from more than one column.");
        }
        if (!used.Contains("email") && !used.Contains("phone")) errors.Add("Map a column to email or phone.");
        if (errors.Count > 0) throw EmailProblem.Invalid("email.import_mapping_invalid", "The column mapping is invalid.", errors, "mapping");
    }

    public static string Suggest(string header)
    {
        var h = header.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        return h switch
        {
            "email" or "e_mail" or "email_address" or "mail" => "email",
            "phone" or "mobile" or "phone_number" or "mobile_number" or "cell" or "msisdn" => "phone",
            "first_name" or "firstname" or "first" or "given_name" or "name" => "first_name",
            "last_name" or "lastname" or "last" or "surname" or "family_name" => "last_name",
            "language" or "lang" or "locale" => "language",
            "country" or "country_code" => "country",
            "time_zone" or "timezone" or "tz" => "time_zone",
            "tags" or "tag" or "labels" => "tags",
            _ => ContactRules.IsValidFieldKey(h) ? "custom." + h : "ignore",
        };
    }
}

/// <summary>Processes large CSV imports in chunks (resumable; each run claims one import with a lease).</summary>
public sealed class SubscriberImportJob(AppDbContext db, ImportService imports, TimeProvider clock) : IJob
{
    public string Name => nameof(SubscriberImportJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.Set<SubscriberImport>().AsNoTracking()
            .Where(i => (i.Status == ImportStatus.Pending || i.Status == ImportStatus.Processing) && (i.LockedUntil == null || i.LockedUntil < now))
            .OrderBy(i => i.CreatedAt).Select(i => i.Id).Take(5).ToListAsync(ct);
        var processed = 0;
        foreach (var id in due)
            if (await imports.ProcessAsync(id, ImportService.ChunkRows * 4, ct)) processed++;
        return $"Processed chunks of {processed} of {due.Count} pending imports.";
    }
}
