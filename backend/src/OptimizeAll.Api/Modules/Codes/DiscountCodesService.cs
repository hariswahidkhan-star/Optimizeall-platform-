using System.Data;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Codes;

public interface IDiscountCodesService
{
    Task<PagedResult<DiscountCodeDto>> ListAsync(Guid programId, DiscountCodeQuery query, CancellationToken ct);
    Task<DiscountCodeDetailDto> GetAsync(Guid codeId, CancellationToken ct);
    Task<DiscountCodeDto> AddAsync(Guid programId, AddCodeRequest request, CancellationToken ct);
    Task<CodeImportResultDto> ImportAsync(Guid programId, IFormFile file, bool dryRun, CancellationToken ct);
    Task<CodeImportResultDto> GenerateAsync(Guid programId, GenerateCodesRequest request, bool dryRun, CancellationToken ct);
    Task<DiscountCodeDto> UpdateAsync(Guid codeId, UpdateCodeRequest request, CancellationToken ct);
    Task<DiscountCodeDetailDto> AssignAsync(Guid codeId, AssignCodeRequest request, CancellationToken ct);
    Task<DiscountCodeDetailDto> UnassignAsync(Guid codeId, UnassignCodeRequest request, CancellationToken ct);
    Task<AutoAssignResultDto> AutoAssignAsync(Guid programId, AutoAssignRequest request, CancellationToken ct);
    Task<PagedResult<CodeAssignmentDto>> AssignmentsAsync(Guid programId, PageQuery query, CancellationToken ct);
}

/// <summary>Codes of a program: add, CSV import, generate, pause/retire, assign to people or rate groups (docs/DISCOUNT_CODES.md).</summary>
public sealed class DiscountCodesService(
    AppDbContext db,
    IAuditLogger audit,
    ICurrentUser currentUser,
    INotificationService notifications,
    TimeProvider clock) : IDiscountCodesService
{
    private const int Chunk = 500;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static readonly Dictionary<string, string> CodeColumns = new()
    {
        ["code"] = "code", ["discountcode"] = "code", ["couponcode"] = "code", ["promocode"] = "code", ["coupon"] = "code", ["voucher"] = "code",
        ["validfrom"] = "validFrom", ["startsat"] = "validFrom", ["start"] = "validFrom",
        ["validto"] = "validTo", ["expires"] = "validTo", ["expiresat"] = "validTo", ["expiry"] = "validTo", ["end"] = "validTo",
        ["note"] = "note", ["notes"] = "note", ["description"] = "note",
        ["email"] = "email", ["assignto"] = "email", ["assignee"] = "email",
    };

    // ------------------------------------------------------------------ reads

    public async Task<PagedResult<DiscountCodeDto>> ListAsync(Guid programId, DiscountCodeQuery query, CancellationToken ct)
    {
        var program = await LoadProgramAsync(programId, ct);
        var now = Now;
        var q = db.Set<DiscountCode>().AsNoTracking().Where(c => c.ProgramId == programId);
        var ends = program.EndsAt;
        switch (query.Status)
        {
            case DiscountCodeStatus.Expired:
                q = q.Where(c => c.Status != DiscountCodeStatus.Retired && ((c.ValidTo != null && c.ValidTo < now) || (c.ValidTo == null && ends != null && ends < now)));
                break;
            case DiscountCodeStatus.Retired:
                q = q.Where(c => c.Status == DiscountCodeStatus.Retired);
                break;
            case { } s:
                q = q.Where(c => c.Status == s && !((c.ValidTo != null && c.ValidTo < now) || (c.ValidTo == null && ends != null && ends < now)));
                break;
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search.ToUpperInvariant());
            q = q.Where(c => EF.Functions.Like(c.NormalizedCode, like, "\\"));
        }
        if (query.UserId is { } uid)
            q = q.Where(c => db.Set<DiscountCodeAssignment>().Any(a => a.CodeId == c.Id && a.UserId == uid && a.EndedAt == null));
        if (query.GroupId is { } gid)
            q = q.Where(c => db.Set<DiscountCodeAssignment>().Any(a => a.CodeId == c.Id && a.GroupId == gid && a.EndedAt == null));
        var total = await q.CountAsync(ct);
        var page = await q.OrderBy(c => c.NormalizedCode).ThenBy(c => c.Id).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<DiscountCodeDto>(await ToDtosAsync(page, program, ct), total, query.Page, query.PageSize);
    }

    public async Task<DiscountCodeDetailDto> GetAsync(Guid codeId, CancellationToken ct)
    {
        var code = await db.Set<DiscountCode>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == codeId, ct) ?? throw NotFound();
        var program = await LoadProgramAsync(code.ProgramId, ct);
        var history = await db.Set<DiscountCodeAssignment>().AsNoTracking().Where(a => a.CodeId == codeId)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(200).ToListAsync(ct);
        return new DiscountCodeDetailDto((await ToDtosAsync(new[] { code }, program, ct))[0],
            new ProgramRefDto(program.Id, program.Name, program.BrandName, program.Currency), await AssignmentDtosAsync(history, ct));
    }

    public async Task<PagedResult<CodeAssignmentDto>> AssignmentsAsync(Guid programId, PageQuery query, CancellationToken ct)
    {
        await LoadProgramAsync(programId, ct);
        var q = db.Set<DiscountCodeAssignment>().AsNoTracking().Where(a => a.ProgramId == programId);
        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<CodeAssignmentDto>(await AssignmentDtosAsync(page, ct), total, query.Page, query.PageSize);
    }

    // ------------------------------------------------------------------ add / import / generate

    public async Task<DiscountCodeDto> AddAsync(Guid programId, AddCodeRequest request, CancellationToken ct)
    {
        var raw = request.Code.Trim();
        if (!CodePattern.IsValidCode(raw))
            throw new DomainException("code.invalid", "Codes use letters, digits, '-' and '_' (2–64 characters).",
                errors: new Dictionary<string, string[]> { ["code"] = new[] { "Letters, digits, '-' and '_' only." } });
        ValidateWindow(request.ValidFrom, request.ValidTo);
        DiscountCode code;
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(programId), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, programId, ct);
            var program = await LoadProgramAsync(programId, ct);
            if (program.Status == CodeProgramStatus.Archived) throw CodeProgramsService.Archived();
            var normalized = DiscountCode.Normalize(raw);
            if (await db.Set<DiscountCode>().AnyAsync(c => c.ProgramId == programId && c.NormalizedCode == normalized, ct))
                throw Duplicate(raw);
            code = new DiscountCode
            {
                ProgramId = programId, Code = raw, NormalizedCode = normalized, Source = DiscountCodeSource.Manual,
                ValidFrom = Utc(request.ValidFrom), ValidTo = Utc(request.ValidTo), Note = CodeQueries.Trimmed(request.Note), CreatedByUserId = currentUser.Id,
            };
            db.Set<DiscountCode>().Add(code);
            audit.Record("discount_code.created", nameof(DiscountCode), code.Id, after: new { programId, code.Code, code.ValidFrom, code.ValidTo, Source = "Manual" });
            await SaveUniqueAsync(() => Duplicate(raw), ct);
            await tx.CommitAsync(ct);
        }
        return (await GetAsync(code.Id, ct)).Code;
    }

    private sealed record ParsedCode(int Row, string Code, string Normalized, DateTime? ValidFrom, DateTime? ValidTo, string? Note, string? Email, Guid? UserId);

    public async Task<CodeImportResultDto> ImportAsync(Guid programId, IFormFile file, bool dryRun, CancellationToken ct)
    {
        var program = await LoadProgramAsync(programId, ct);
        if (program.Status == CodeProgramStatus.Archived) throw CodeProgramsService.Archived();
        var table = CodeCsv.Parse(await CodeCsv.ReadAsync(file, ct), CodeColumns, "code", firstColumnFallback: true, CodeLimits.MaxCodeRows);

        var rejected = new List<CodeIssueDto>();
        var warnings = new List<CodeIssueDto>();
        var parsed = new List<ParsedCode>();
        var seen = new HashSet<string>();
        foreach (var row in table.Rows)
        {
            var raw = table.Get(row, "code");
            if (raw is null) { rejected.Add(new CodeIssueDto(row.Line, null, "csv.blank", "The row has no code.")); continue; }
            if (!CodePattern.IsValidCode(raw)) { rejected.Add(new CodeIssueDto(row.Line, raw, "code.invalid", "Letters, digits, '-' and '_' only (2–64 characters).")); continue; }
            var normalized = DiscountCode.Normalize(raw);
            if (!seen.Add(normalized)) { rejected.Add(new CodeIssueDto(row.Line, raw, "code.duplicate_in_file", "This code appears earlier in the file.")); continue; }
            DateTime? from = null, to = null;
            if (table.Get(row, "validFrom") is { } f)
            {
                if (!CodeCsv.TryDate(f, out var d)) { rejected.Add(new CodeIssueDto(row.Line, raw, "csv.invalid_date", $"'{f}' is not a date (use YYYY-MM-DD).")); continue; }
                from = d;
            }
            if (table.Get(row, "validTo") is { } t)
            {
                if (!CodeCsv.TryDate(t, out var d)) { rejected.Add(new CodeIssueDto(row.Line, raw, "csv.invalid_date", $"'{t}' is not a date (use YYYY-MM-DD).")); continue; }
                to = d.TimeOfDay == TimeSpan.Zero ? d.AddDays(1).AddTicks(-10) : d; // a plain date means "through that day"
            }
            if (from is { } a && to is { } b && b <= a) { rejected.Add(new CodeIssueDto(row.Line, raw, "code.invalid_window", "The end is before the start.")); continue; }
            parsed.Add(new ParsedCode(row.Line, raw, normalized, from, to, table.Get(row, "note") is { } n ? CodeQueries.Fit(n, 300) : null, table.Get(row, "email"), null));
        }

        // Codes already in the program are refused (duplicates are never overwritten).
        var existing = new HashSet<string>();
        foreach (var chunk in parsed.Select(p => p.Normalized).Chunk(Chunk))
        {
            var list = chunk.ToList();
            existing.UnionWith(await db.Set<DiscountCode>().AsNoTracking().Where(c => c.ProgramId == programId && list.Contains(c.NormalizedCode))
                .Select(c => c.NormalizedCode).ToListAsync(ct));
        }
        var duplicates = 0;
        foreach (var p in parsed.Where(p => existing.Contains(p.Normalized)).ToList())
        {
            rejected.Add(new CodeIssueDto(p.Row, p.Code, "code.duplicate", "The program already has this code."));
            parsed.Remove(p);
            duplicates++;
        }

        // Optional "email" column: assign the code to that participant.
        var emails = parsed.Where(p => p.Email is not null).Select(p => Normalization.Email(p.Email!)).Distinct().ToList();
        var people = new Dictionary<string, (Guid Id, bool IsParticipant, UserStatus Status, bool IsTest)>();
        foreach (var chunk in emails.Chunk(Chunk))
        {
            var list = chunk.ToList();
            foreach (var u in await db.Set<User>().AsNoTracking().Where(u => list.Contains(u.NormalizedEmail))
                         .Select(u => new { u.NormalizedEmail, u.Id, IsParticipant = u.Roles.Any(r => r.Role == Role.Participant), u.Status, u.IsTestAccount })
                         .ToListAsync(ct))
                people[u.NormalizedEmail] = (u.Id, u.IsParticipant, u.Status, u.IsTestAccount);
        }
        for (var i = 0; i < parsed.Count; i++)
        {
            var p = parsed[i];
            if (p.Email is null) continue;
            if (!people.TryGetValue(Normalization.Email(p.Email), out var person))
            {
                rejected.Add(new CodeIssueDto(p.Row, p.Code, "user.not_found", $"No account uses {p.Email}."));
                parsed.RemoveAt(i--);
                continue;
            }
            if (!person.IsParticipant || person.Status == UserStatus.Deactivated)
            {
                rejected.Add(new CodeIssueDto(p.Row, p.Code, "codes.not_participant", $"{p.Email} is not an active participant."));
                parsed.RemoveAt(i--);
                continue;
            }
            if (person.Status == UserStatus.Suspended) warnings.Add(new CodeIssueDto(p.Row, p.Code, "user.suspended", $"{p.Email} is suspended (can't report sales until reactivated)."));
            if (person.IsTest) warnings.Add(new CodeIssueDto(p.Row, p.Code, "user.test_account", $"{p.Email} is a test account (never paid)."));
            parsed[i] = p with { UserId = person.Id };
        }

        var sample = parsed.Take(5).Select(p => p.Code).ToList();
        var ordered = rejected.OrderBy(r => r.Row).ToList();
        if (dryRun || parsed.Count == 0)
            return new CodeImportResultDto(dryRun, table.Rows.Count, parsed.Count, 0, duplicates, ordered, warnings, sample);

        var created = await InsertCodesAsync(programId, parsed, DiscountCodeSource.Import, file.FileName, table.Rows.Count, ordered.Count, ct);
        return new CodeImportResultDto(false, table.Rows.Count, parsed.Count, created, duplicates, ordered, warnings, sample);
    }

    public async Task<CodeImportResultDto> GenerateAsync(Guid programId, GenerateCodesRequest request, bool dryRun, CancellationToken ct)
    {
        var pattern = request.Pattern.Trim();
        if (!CodePattern.IsValidPattern(pattern))
            throw new DomainException("code.invalid_pattern", "Use letters, digits, '-', '_' and at least four placeholders: # digit, ? letter, * either.",
                errors: new Dictionary<string, string[]> { ["pattern"] = new[] { "At least four of #, ? or *." } });
        if (CodePattern.Capacity(pattern) < request.Count * 20d)
            throw new DomainException("code.pattern_too_small", "The pattern can't produce that many unique codes; add placeholders.",
                errors: new Dictionary<string, string[]> { ["pattern"] = new[] { "Too few combinations for this count." } });
        ValidateWindow(request.ValidFrom, request.ValidTo);
        var program = await LoadProgramAsync(programId, ct);
        if (program.Status == CodeProgramStatus.Archived) throw CodeProgramsService.Archived();

        var codes = new HashSet<string>();
        for (var attempt = 0; codes.Count < request.Count && attempt < 10; attempt++)
        {
            var batch = new HashSet<string>();
            while (batch.Count + codes.Count < request.Count) batch.Add(CodePattern.Generate(pattern));
            batch.ExceptWith(codes);
            foreach (var chunk in batch.Chunk(Chunk))
            {
                var list = chunk.ToList();
                var taken = await db.Set<DiscountCode>().AsNoTracking().Where(c => c.ProgramId == programId && list.Contains(c.NormalizedCode))
                    .Select(c => c.NormalizedCode).ToListAsync(ct);
                codes.UnionWith(list.Except(taken));
            }
        }
        if (codes.Count < request.Count)
            throw DomainException.Conflict("code.pattern_exhausted", "Could not generate enough unique codes with this pattern.");
        var parsed = codes.Take(request.Count)
            .Select((c, i) => new ParsedCode(i + 1, c, c, Utc(request.ValidFrom), Utc(request.ValidTo), CodeQueries.Trimmed(request.Note), null, null)).ToList();
        var sample = parsed.Take(5).Select(p => p.Code).ToList();
        if (dryRun) return new CodeImportResultDto(true, request.Count, parsed.Count, 0, 0, Array.Empty<CodeIssueDto>(), Array.Empty<CodeIssueDto>(), sample);
        var created = await InsertCodesAsync(programId, parsed, DiscountCodeSource.Generated, $"pattern {pattern}", request.Count, 0, ct);
        return new CodeImportResultDto(false, request.Count, parsed.Count, created, 0, Array.Empty<CodeIssueDto>(), Array.Empty<CodeIssueDto>(), sample);
    }

    /// <summary>Inserts codes (and optional personal assignments) in batches inside one transaction under the program lock.</summary>
    private async Task<int> InsertCodesAsync(Guid programId, IReadOnlyList<ParsedCode> rows, DiscountCodeSource source, string fileName, int totalRows,
        int rejected, CancellationToken ct)
    {
        var now = Now;
        var me = currentUser.Id;
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(programId), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, programId, ct);
            var program = await LoadProgramAsync(programId, ct);
            if (program.Status == CodeProgramStatus.Archived) throw CodeProgramsService.Archived();
            var batch = new CodeImportBatch
            {
                ProgramId = programId, Kind = CodeImportKind.Codes, FileName = CodeQueries.Fit(fileName, 200), Rows = totalRows, Created = rows.Count,
                Rejected = rejected, CreatedAt = now, CreatedByUserId = me,
            };
            db.Set<CodeImportBatch>().Add(batch);
            await db.SaveChangesAsync(ct);
            var assigned = 0;
            foreach (var chunk in rows.Chunk(Chunk))
            {
                // Re-checked under the lock: a code added since the dry run is a duplicate.
                var list = chunk.Select(c => c.Normalized).ToList();
                if (await db.Set<DiscountCode>().AnyAsync(c => c.ProgramId == programId && list.Contains(c.NormalizedCode), ct))
                    throw DomainException.Conflict("code.duplicate", "Some of these codes were added meanwhile. Run the import again to see which.");
                foreach (var r in chunk)
                {
                    var code = new DiscountCode
                    {
                        ProgramId = programId, Code = r.Code, NormalizedCode = r.Normalized, Source = source, ValidFrom = r.ValidFrom, ValidTo = r.ValidTo,
                        Note = r.Note, ImportBatchId = batch.Id, CreatedByUserId = me,
                        Status = r.UserId is null ? DiscountCodeStatus.Available : DiscountCodeStatus.Assigned,
                    };
                    db.Set<DiscountCode>().Add(code);
                    if (r.UserId is { } uid)
                    {
                        db.Set<DiscountCodeAssignment>().Add(new DiscountCodeAssignment
                        {
                            CodeId = code.Id, ProgramId = programId, Target = CodeAssignmentTarget.Person, UserId = uid,
                            ValidFrom = r.ValidFrom is { } vf && vf > program.StartsAt ? vf : program.StartsAt, ValidTo = r.ValidTo,
                            Reason = $"Imported with the code ({CodeQueries.Fit(fileName, 100)})", CreatedAt = now, CreatedByUserId = me,
                        });
                        assigned++;
                    }
                }
                await SaveUniqueAsync(() => DomainException.Conflict("code.duplicate", "A code in this file already exists in the program."), ct);
                db.ChangeTracker.Clear();
            }
            audit.Record(source == DiscountCodeSource.Generated ? "discount_code.generated" : "discount_code.imported", nameof(CodeProgram), programId,
                after: new { BatchId = batch.Id, Created = rows.Count, Assigned = assigned, Rejected = rejected, File = batch.FileName });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return rows.Count;
    }

    // ------------------------------------------------------------------ status

    public async Task<DiscountCodeDto> UpdateAsync(Guid codeId, UpdateCodeRequest request, CancellationToken ct)
    {
        var programId = await ProgramOfAsync(codeId, ct);
        ValidateWindow(request.ValidFrom, request.ValidTo);
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(programId), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, programId, ct);
            var code = await db.Set<DiscountCode>().FirstAsync(c => c.Id == codeId, ct);
            ConcurrencyGuard.Apply(db, code, request.ConcurrencyStamp!.Value);
            if (code.Status == DiscountCodeStatus.Retired) throw DomainException.Conflict("code.retired", "This code is retired.");
            var before = new { Status = code.Status.ToString(), code.ValidFrom, code.ValidTo, code.Note };
            var live = await db.Set<DiscountCodeAssignment>().FirstOrDefaultAsync(a => a.CodeId == codeId && a.EndedAt == null, ct);
            var now = Now;
            switch (request.Status)
            {
                case null:
                    break;
                case DiscountCodeStatus.Paused:
                    code.Status = DiscountCodeStatus.Paused;
                    break;
                case DiscountCodeStatus.Available or DiscountCodeStatus.Assigned:
                    code.Status = live is not null ? DiscountCodeStatus.Assigned : DiscountCodeStatus.Available;
                    break;
                case DiscountCodeStatus.Retired:
                    code.Status = DiscountCodeStatus.Retired;
                    if (live is not null)
                    {
                        live.EndedAt = now;
                        live.EndedByUserId = currentUser.Id;
                        live.EndReason = CodeQueries.Fit($"Code retired: {CodeQueries.Trimmed(request.Reason) ?? "no reason given"}", 500);
                    }
                    break;
                default:
                    throw new DomainException("code.invalid_status", "Expired is derived from the end date; set Available, Paused or Retired.");
            }
            code.ValidFrom = Utc(request.ValidFrom);
            code.ValidTo = Utc(request.ValidTo);
            code.Note = CodeQueries.Trimmed(request.Note);
            audit.Record("discount_code.updated", nameof(DiscountCode), codeId, before,
                new { Status = code.Status.ToString(), code.ValidFrom, code.ValidTo, code.Note }, CodeQueries.Trimmed(request.Reason));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return (await GetAsync(codeId, ct)).Code;
    }

    // ------------------------------------------------------------------ assignment

    public async Task<DiscountCodeDetailDto> AssignAsync(Guid codeId, AssignCodeRequest request, CancellationToken ct)
    {
        var target = request.Target!.Value;
        if ((target == CodeAssignmentTarget.Person) != (request.UserId is not null) || (target == CodeAssignmentTarget.Group) != (request.GroupId is not null))
            throw new DomainException("code_assignment.invalid_target", "A personal code needs userId; a shared (group) code needs groupId.");
        var programId = await ProgramOfAsync(codeId, ct);
        var now = Now;
        var from = Utc(request.ValidFrom) ?? now;
        var to = Utc(request.ValidTo);
        if (to is { } t && t <= from)
            throw new DomainException("code_assignment.invalid_window", "The end must be after the start.",
                errors: new Dictionary<string, string[]> { ["validTo"] = new[] { "Must be after the start." } });
        string? groupName = null;
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(programId), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, programId, ct);
            var program = await LoadProgramAsync(programId, ct);
            if (program.Status == CodeProgramStatus.Archived) throw CodeProgramsService.Archived();
            var code = await db.Set<DiscountCode>().FirstAsync(c => c.Id == codeId, ct);
            EnsureAssignable(code, program, now);
            if (request.UserId is { } uid) await CodeTargets.EnsureParticipantAsync(db, uid, ct);
            if (request.GroupId is { } gid) groupName = (await CodeTargets.EnsureManualGroupAsync(db, gid, ct)).Name;

            var history = await db.Set<DiscountCodeAssignment>().Where(a => a.CodeId == codeId).ToListAsync(ct);
            var live = history.FirstOrDefault(a => a.EndedAt == null);
            if (live is not null)
            {
                if (live.Target == target && live.UserId == request.UserId && live.GroupId == request.GroupId)
                    throw DomainException.Conflict("code.already_assigned", "The code is already assigned to them.");
                if (!request.Reassign)
                    throw DomainException.Conflict("code.already_assigned", "The code is assigned to someone else. Send \"reassign\": true to move it (history is kept).");
                if (from < live.ValidFrom)
                    throw new DomainException("code_assignment.invalid_window", "A reassignment can't start before the current assignment did.");
                live.EndedAt = from > now ? from : now;
                if (from < live.EndedAt) from = live.EndedAt.Value; // orders before the move stay attributed to the previous holder
                live.EndedByUserId = currentUser.Id;
                live.EndReason = CodeQueries.Fit($"Reassigned: {request.Reason.Trim()}", 500);
            }
            // Order attribution needs non-overlapping windows.
            if (history.Where(a => a != live).Any(a => Overlaps(a.ValidFrom, a.EffectiveTo, from, to)))
                throw DomainException.Conflict("code_assignment.overlap", "Another assignment of this code covers part of that period.");

            var assignment = new DiscountCodeAssignment
            {
                CodeId = codeId, ProgramId = programId, Target = target, UserId = request.UserId, GroupId = request.GroupId, ValidFrom = from, ValidTo = to,
                Reason = request.Reason.Trim(), CreatedAt = now, CreatedByUserId = currentUser.Id,
            };
            db.Set<DiscountCodeAssignment>().Add(assignment);
            if (code.Status == DiscountCodeStatus.Available) code.Status = DiscountCodeStatus.Assigned;
            ConcurrencyGuard.Touch(db, code);
            audit.Record(live is null ? "discount_code.assigned" : "discount_code.reassigned", nameof(DiscountCode), codeId,
                live is null ? null : new { Target = live.Target.ToString(), live.UserId, live.GroupId },
                new { Target = target.ToString(), request.UserId, request.GroupId, GroupName = groupName, ValidFrom = from, ValidTo = to }, assignment.Reason);
            if (request.UserId is { } person && program.Status != CodeProgramStatus.Draft)
                await notifications.StageAsync(new NotificationRequest(person, NotificationTypes.CodeAssigned, "New discount code",
                    $"{program.BrandName} code {code.Code} is now yours. Share it and report the sales made with it.", AppLinks.MyCodes), ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(codeId, ct);
    }

    public async Task<DiscountCodeDetailDto> UnassignAsync(Guid codeId, UnassignCodeRequest request, CancellationToken ct)
    {
        var programId = await ProgramOfAsync(codeId, ct);
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(programId), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, programId, ct);
            var code = await db.Set<DiscountCode>().FirstAsync(c => c.Id == codeId, ct);
            var live = await db.Set<DiscountCodeAssignment>().FirstOrDefaultAsync(a => a.CodeId == codeId && a.EndedAt == null, ct)
                       ?? throw DomainException.Conflict("code.not_assigned", "The code isn't assigned.");
            live.EndedAt = Now > live.ValidFrom ? Now : live.ValidFrom;
            live.EndedByUserId = currentUser.Id;
            live.EndReason = request.Reason.Trim();
            if (code.Status == DiscountCodeStatus.Assigned) code.Status = DiscountCodeStatus.Available;
            ConcurrencyGuard.Touch(db, code);
            audit.Record("discount_code.unassigned", nameof(DiscountCode), codeId, new { Target = live.Target.ToString(), live.UserId, live.GroupId },
                new { Assigned = false }, live.EndReason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(codeId, ct);
    }

    public async Task<AutoAssignResultDto> AutoAssignAsync(Guid programId, AutoAssignRequest request, CancellationToken ct)
    {
        var now = Now;
        var from = Utc(request.ValidFrom) ?? now;
        var to = Utc(request.ValidTo);
        if (to is { } t && t <= from)
            throw new DomainException("code_assignment.invalid_window", "The end must be after the start.");
        var issues = new List<CodeIssueDto>();
        var created = new List<DiscountCodeAssignment>();
        int members, already, skipped, available;
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(programId), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, programId, ct);
            var program = await LoadProgramAsync(programId, ct);
            if (program.Status == CodeProgramStatus.Archived) throw CodeProgramsService.Archived();
            var rateGroup = await CodeTargets.EnsureManualGroupAsync(db, request.GroupId!.Value, ct);
            var people = await (from m in db.Set<RateGroupMember>().AsNoTracking()
                                join u in db.Set<User>() on m.UserId equals u.Id
                                where m.GroupId == rateGroup.Id
                                orderby u.DisplayName, u.Id
                                select new { u.Id, u.Email, u.Status, u.IsTestAccount }).ToListAsync(ct);
            members = people.Count;
            var ids = people.Select(p => p.Id).ToList();
            var holders = new HashSet<Guid>();
            foreach (var chunk in ids.Chunk(Chunk))
            {
                var list = chunk.ToList();
                holders.UnionWith(await db.Set<DiscountCodeAssignment>().AsNoTracking()
                    .Where(a => a.ProgramId == programId && a.Target == CodeAssignmentTarget.Person && a.EndedAt == null && list.Contains(a.UserId!.Value))
                    .Select(a => a.UserId!.Value).ToListAsync(ct));
            }
            already = people.Count(p => holders.Contains(p.Id));
            var candidates = people.Where(p => !holders.Contains(p.Id)).ToList();
            var ends = program.EndsAt;
            var pool = await db.Set<DiscountCode>().Where(c => c.ProgramId == programId && c.Status == DiscountCodeStatus.Available &&
                                                               !((c.ValidTo != null && c.ValidTo < now) || (c.ValidTo == null && ends != null && ends < now)))
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).Take(candidates.Count).ToListAsync(ct);
            available = await db.Set<DiscountCode>().CountAsync(c => c.ProgramId == programId && c.Status == DiscountCodeStatus.Available &&
                                                                     !((c.ValidTo != null && c.ValidTo < now) || (c.ValidTo == null && ends != null && ends < now)), ct);
            skipped = 0;
            var next = 0;
            foreach (var p in candidates)
            {
                if (p.Status == UserStatus.Deactivated) { skipped++; issues.Add(new CodeIssueDto(null, p.Email, "codes.user_deactivated", "Deactivated account skipped.")); continue; }
                if (next >= pool.Count) { skipped++; issues.Add(new CodeIssueDto(null, p.Email, "codes.pool_exhausted", "No available code left; add or import more codes.")); continue; }
                if (p.Status == UserStatus.Suspended) issues.Add(new CodeIssueDto(null, p.Email, "user.suspended", "Assigned, but the account is suspended."));
                if (p.IsTestAccount) issues.Add(new CodeIssueDto(null, p.Email, "user.test_account", "Assigned; test accounts are never paid."));
                var code = pool[next++];
                var a = new DiscountCodeAssignment
                {
                    CodeId = code.Id, ProgramId = programId, Target = CodeAssignmentTarget.Person, UserId = p.Id, ValidFrom = from, ValidTo = to,
                    Reason = request.Reason.Trim(), CreatedAt = now, CreatedByUserId = currentUser.Id,
                };
                created.Add(a);
                if (!request.DryRun)
                {
                    db.Set<DiscountCodeAssignment>().Add(a);
                    code.Status = DiscountCodeStatus.Assigned;
                    if (program.Status != CodeProgramStatus.Draft)
                        await notifications.StageAsync(new NotificationRequest(p.Id, NotificationTypes.CodeAssigned, "New discount code",
                            $"{program.BrandName} code {code.Code} is now yours. Share it and report the sales made with it.", AppLinks.MyCodes), ct);
                }
            }
            if (!request.DryRun && created.Count > 0)
            {
                audit.Record("discount_code.auto_assigned", nameof(CodeProgram), programId,
                    after: new { GroupId = rateGroup.Id, GroupName = rateGroup.Name, Assigned = created.Count, AlreadyHadCode = already, Skipped = skipped }, reason: request.Reason.Trim());
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
        }
        var dtos = await AssignmentDtosAsync(created.Take(200).ToList(), ct);
        return new AutoAssignResultDto(request.DryRun, members, created.Count, already, skipped, available, issues, dtos);
    }

    // ------------------------------------------------------------------ helpers

    private static bool Overlaps(DateTime aFrom, DateTime? aTo, DateTime bFrom, DateTime? bTo) =>
        (aTo is null || bFrom < aTo) && (bTo is null || aFrom < bTo);

    private static void EnsureAssignable(DiscountCode code, CodeProgram program, DateTime now)
    {
        switch (code.EffectiveStatus(now, program.EndsAt))
        {
            case DiscountCodeStatus.Retired: throw DomainException.Conflict("code.retired", "This code is retired.");
            case DiscountCodeStatus.Paused: throw DomainException.Conflict("code.paused", "This code is paused; resume it before assigning it.");
            case DiscountCodeStatus.Expired: throw DomainException.Conflict("code.expired", "This code has expired.");
        }
    }

    private static void ValidateWindow(DateTime? from, DateTime? to)
    {
        if (from is { } f && to is { } t && t <= f)
            throw new DomainException("code.invalid_window", "The end must be after the start.",
                errors: new Dictionary<string, string[]> { ["validTo"] = new[] { "Must be after the start." } });
    }

    private static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v.ToUniversalTime(), DateTimeKind.Utc) : null;

    private async Task SaveUniqueAsync(Func<DomainException> onDuplicate, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (db.Dialect().IsUniqueViolation(ex))
        {
            throw onDuplicate();
        }
    }

    private async Task<CodeProgram> LoadProgramAsync(Guid id, CancellationToken ct) =>
        await db.Set<CodeProgram>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("CodeProgram");

    private async Task<Guid> ProgramOfAsync(Guid codeId, CancellationToken ct) =>
        await db.Set<DiscountCode>().AsNoTracking().Where(c => c.Id == codeId).Select(c => (Guid?)c.ProgramId).FirstOrDefaultAsync(ct) ?? throw NotFound();

    private async Task<List<DiscountCodeDto>> ToDtosAsync(IReadOnlyList<DiscountCode> codes, CodeProgram program, CancellationToken ct)
    {
        var ids = codes.Select(c => c.Id).ToList();
        var live = await db.Set<DiscountCodeAssignment>().AsNoTracking().Where(a => ids.Contains(a.CodeId) && a.EndedAt == null).ToListAsync(ct);
        var liveDtos = (await AssignmentDtosAsync(live, ct)).ToDictionary(a => a.CodeId);
        var sales = await db.Set<CodeSale>().AsNoTracking().Where(s => ids.Contains(s.CodeId) && s.ActiveOrderKey != null)
            .GroupBy(s => s.CodeId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var now = Now;
        return codes.Select(c => new DiscountCodeDto(c.Id, c.ProgramId, c.Code, c.EffectiveStatus(now, program.EndsAt), c.Source, c.ValidFrom, c.ValidTo,
            c.Note, liveDtos.GetValueOrDefault(c.Id), sales.GetValueOrDefault(c.Id), c.CreatedAt, c.ConcurrencyStamp)).ToList();
    }

    private async Task<List<CodeAssignmentDto>> AssignmentDtosAsync(IReadOnlyList<DiscountCodeAssignment> list, CancellationToken ct)
    {
        var names = await db.UserNamesAsync(list.Select(a => a.UserId).Concat(list.Select(a => (Guid?)a.CreatedByUserId)), ct);
        var groups = await db.GroupNamesAsync(list.Select(a => a.GroupId), ct);
        var codeIds = list.Select(a => a.CodeId).Distinct().ToList();
        var codes = await db.Set<DiscountCode>().AsNoTracking().Where(c => codeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
        var now = Now;
        return list.Select(a => new CodeAssignmentDto(a.Id, a.CodeId, codes.GetValueOrDefault(a.CodeId, "?"), a.Target, names.Ref(a.UserId),
            a.GroupId is { } g ? new NamedRefDto(g, groups.GetValueOrDefault(g, "Unknown group")) : null, a.ValidFrom, a.ValidTo, a.EndedAt, a.EndReason,
            a.Reason, a.CreatedAt, names.Ref(a.CreatedByUserId), a.IsLive(now))).ToList();
    }

    private static DomainException Duplicate(string code) =>
        new("code.duplicate", $"The program already has the code {code}.", DomainErrorKind.Conflict,
            new Dictionary<string, string[]> { ["code"] = new[] { "Already in this program." } });

    private static DomainException NotFound() => DomainException.NotFound("DiscountCode");
}
