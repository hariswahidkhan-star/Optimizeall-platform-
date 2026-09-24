using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Social;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Rates;

public interface IRateGroupsService
{
    Task<PagedResult<RateGroupListItemDto>> ListAsync(RateGroupQuery query, CancellationToken ct);
    Task<RateGroupDto> GetAsync(Guid id, CancellationToken ct);
    Task<RateGroupDto> CreateAsync(CreateRateGroupRequest request, CancellationToken ct);
    Task<RateGroupDto> UpdateAsync(Guid id, UpdateRateGroupRequest request, CancellationToken ct);
    Task<RateGroupDto> ArchiveAsync(Guid id, ArchiveRateGroupRequest request, CancellationToken ct);
    Task<PagedResult<RateGroupMemberDto>> MembersAsync(Guid id, RateGroupMembersQuery query, CancellationToken ct);
    Task<BulkMembersResultDto> AddMembersAsync(Guid id, AddRateGroupMembersRequest request, CancellationToken ct);
    Task<BulkMembersResultDto> RemoveMembersAsync(Guid id, RemoveRateGroupMembersRequest request, CancellationToken ct);
    Task<CsvImportResultDto> ImportAsync(Guid id, IFormFile file, bool dryRun, string? note, CancellationToken ct);
    Task<(string FileName, IReadOnlyList<string> Header, IReadOnlyList<object?[]> Rows)> ExportAsync(Guid id, CancellationToken ct);
    Task<PagedResult<RateGroupMemberEventDto>> HistoryAsync(Guid id, PageQuery query, CancellationToken ct);
}

public sealed class RateGroupsService(
    AppDbContext db,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IRateAssignmentsService assignments,
    TimeProvider clock) : IRateGroupsService
{
    public const string GroupsLock = "rates:groups";
    private const int Chunk = 500;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(60);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public static string GroupLockName(Guid id) => $"rates:group:{id:N}";

    public static string? DescribeRule(RateGroup g)
    {
        if (g.MembershipMode != RateGroupMembershipMode.Automatic) return null;
        var parts = new List<string>();
        if (g.AutoTiers.Count > 0) parts.Add($"tier {string.Join(" or ", g.AutoTiers)}");
        var kind = g.AutoRequireVerified ? "verified followers" : "followers";
        if (g.AutoMinFollowers is { } min && g.AutoMaxFollowers is { } max) parts.Add($"{min:N0}–{max - 1:N0} {kind}");
        else if (g.AutoMinFollowers is { } mn) parts.Add($"≥ {mn:N0} {kind}");
        else if (g.AutoMaxFollowers is { } mx) parts.Add($"< {mx:N0} {kind}");
        return parts.Count == 0 ? "every participant" : string.Join(", ", parts) + " on the post's platform";
    }

    public async Task<PagedResult<RateGroupListItemDto>> ListAsync(RateGroupQuery query, CancellationToken ct)
    {
        var q = db.Set<RateGroup>().AsNoTracking();
        if (!query.IncludeArchived) q = q.Where(g => g.ArchivedAt == null);
        if (query.Mode is { } mode) q = q.Where(g => g.MembershipMode == mode);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(g => EF.Functions.Like(g.Name, like, "\\"));
        }
        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(g => g.Priority).ThenBy(g => g.Name).ThenBy(g => g.Id).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var ids = page.Select(g => g.Id).ToList();
        var counts = await db.Set<RateGroupMember>().AsNoTracking().Where(m => ids.Contains(m.GroupId))
            .GroupBy(m => m.GroupId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var now = Now;
        var cards = await (from a in db.Set<RateAssignment>().AsNoTracking()
                           join c in db.Set<RateCard>() on a.RateCardId equals c.Id
                           where a.GroupId != null && ids.Contains(a.GroupId.Value) && a.EndedAt == null && (a.ValidTo == null || a.ValidTo > now)
                           select new { GroupId = a.GroupId!.Value, c.Id, c.Name, c.Kind, c.Status, c.Currency, c.CurrentVersion }).ToListAsync(ct);
        var items = new List<RateGroupListItemDto>();
        foreach (var g in page)
        {
            int? count = g.MembershipMode == RateGroupMembershipMode.Manual ? counts.GetValueOrDefault(g.Id) : await AutoMembers(g).CountAsync(ct);
            items.Add(new RateGroupListItemDto(g.Id, g.Name, g.Description, g.Priority, g.MembershipMode, DescribeRule(g), count,
                cards.Where(c => c.GroupId == g.Id).Select(c => new RateCardRefDto(c.Id, c.Name, c.Kind, c.Status, c.Currency, c.CurrentVersion))
                    .DistinctBy(c => c.Id).ToList(),
                g.ArchivedAt, g.UpdatedAt));
        }
        return new PagedResult<RateGroupListItemDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<RateGroupDto> GetAsync(Guid id, CancellationToken ct)
    {
        var g = await db.Set<RateGroup>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        int? count = g.MembershipMode == RateGroupMembershipMode.Manual
            ? await db.Set<RateGroupMember>().CountAsync(m => m.GroupId == id, ct)
            : await AutoMembers(g).CountAsync(ct);
        var list = await db.Set<RateAssignment>().AsNoTracking().Where(a => a.GroupId == id).OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct);
        return new RateGroupDto(g.Id, g.Name, g.Description, g.Priority, g.MembershipMode, g.AutoTiers, g.AutoMinFollowers, g.AutoMaxFollowers,
            g.AutoRequireVerified, DescribeRule(g), count, g.CreatedAt, g.UpdatedAt, g.ArchivedAt, g.ArchiveReason, g.ConcurrencyStamp,
            await assignments.ToDtosAsync(list, ct));
    }

    public async Task<RateGroupDto> CreateAsync(CreateRateGroupRequest request, CancellationToken ct)
    {
        var group = new RateGroup { CreatedByUserId = currentUser.Id };
        Apply(group, request);
        await using (await db.Dialect().AcquireNamedLockAsync(db, GroupsLock, LockTimeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await EnsureNameFreeAsync(group.Name, null, ct);
            db.Set<RateGroup>().Add(group);
            audit.Record("rate_group.created", nameof(RateGroup), group.Id, after: Snapshot(group));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(group.Id, ct);
    }

    public async Task<RateGroupDto> UpdateAsync(Guid id, UpdateRateGroupRequest request, CancellationToken ct)
    {
        await using (await db.Dialect().AcquireNamedLockAsync(db, GroupsLock, LockTimeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, "rate_groups", id, ct);
            var group = await db.Set<RateGroup>().FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw NotFound();
            ConcurrencyGuard.Apply(db, group, request.ConcurrencyStamp!.Value);
            if (group.ArchivedAt is not null) throw Archived();
            if (group.MembershipMode == RateGroupMembershipMode.Manual && request.MembershipMode == RateGroupMembershipMode.Automatic &&
                await db.Set<RateGroupMember>().AnyAsync(m => m.GroupId == id, ct))
                throw DomainException.Conflict("rate_group.has_members",
                    "Remove the group's members before switching it to automatic membership.");
            var before = Snapshot(group);
            if (!string.Equals(group.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                await EnsureNameFreeAsync(request.Name, id, ct);
            Apply(group, request);
            audit.Record("rate_group.updated", nameof(RateGroup), id, before, Snapshot(group), string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim());
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<RateGroupDto> ArchiveAsync(Guid id, ArchiveRateGroupRequest request, CancellationToken ct)
    {
        var now = Now;
        var reason = request.Reason.Trim();
        await using (await db.Dialect().AcquireNamedLockAsync(db, GroupLockName(id), LockTimeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, "rate_groups", id, ct);
            var group = await db.Set<RateGroup>().FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw NotFound();
            ConcurrencyGuard.Apply(db, group, request.ConcurrencyStamp!.Value);
            if (group.ArchivedAt is not null) throw Archived();
            var members = await db.Set<RateGroupMember>().Where(m => m.GroupId == id).Select(m => m.UserId).ToListAsync(ct);
            var live = await db.Set<RateAssignment>()
                .Where(a => a.GroupId == id && a.EndedAt == null && (a.ValidTo == null || a.ValidTo > now)).ToListAsync(ct);
            if ((members.Count > 0 || live.Count > 0) && !request.Force)
                throw DomainException.Conflict("rate_group.in_use",
                    $"This group has {members.Count} member{(members.Count == 1 ? "" : "s")} and {live.Count} active assignment{(live.Count == 1 ? "" : "s")}. " +
                    "Remove them first, or archive with \"force\": true to remove the members and end the assignments (submissions already made keep their price).");
            foreach (var a in live)
            {
                a.EndedAt = now;
                a.EndedByUserId = currentUser.Id;
                a.EndReason = $"Group archived: {reason}".Length > 500 ? $"Group archived: {reason}"[..500] : $"Group archived: {reason}";
            }
            foreach (var chunk in members.Chunk(Chunk))
            {
                var ids = chunk.ToList();
                await db.Set<RateGroupMember>().Where(m => m.GroupId == id && ids.Contains(m.UserId)).ExecuteDeleteAsync(ct);
                db.Set<RateGroupMemberEvent>().AddRange(ids.Select(u => Event(id, u, RateGroupMemberAction.Removed, now, "group_archived", reason)));
            }
            group.ArchivedAt = now;
            group.ArchivedByUserId = currentUser.Id;
            group.ArchiveReason = reason;
            audit.Record("rate_group.archived", nameof(RateGroup), id, new { Archived = false },
                new { Archived = true, RemovedMembers = members.Count, EndedAssignments = live.Count }, reason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<PagedResult<RateGroupMemberDto>> MembersAsync(Guid id, RateGroupMembersQuery query, CancellationToken ct)
    {
        var g = await db.Set<RateGroup>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var like = string.IsNullOrWhiteSpace(query.Search) ? null : PagingExtensions.LikePattern(query.Search);
        if (g.MembershipMode == RateGroupMembershipMode.Automatic)
        {
            var users = AutoMembers(g);
            if (like is not null) users = users.Where(u => EF.Functions.Like(u.DisplayName, like, "\\") || EF.Functions.Like(u.Email, like, "\\"));
            var total = await users.CountAsync(ct);
            var page = await users.OrderBy(u => u.DisplayName).ThenBy(u => u.Id).Skip(query.Skip).Take(query.PageSize)
                .Select(u => new { u.Id, u.DisplayName, u.Email, u.CountryCode, u.Tier, u.Status, u.IsTestAccount }).ToListAsync(ct);
            var ids = page.Select(u => u.Id).ToList();
            var followers = await db.Set<SocialAccount>().AsNoTracking()
                .Where(a => ids.Contains(a.UserId) && a.IsActive && (!g.AutoRequireVerified || a.VerificationStatus == SocialAccountVerificationStatus.Verified))
                .GroupBy(a => a.UserId).Select(x => new { x.Key, Max = x.Max(a => a.FollowerCount) }).ToDictionaryAsync(x => x.Key, x => x.Max, ct);
            return new PagedResult<RateGroupMemberDto>(page.Select(u => new RateGroupMemberDto(u.Id, u.DisplayName, u.Email, u.CountryCode,
                u.Tier, u.Status, u.IsTestAccount, null, null, null, followers.TryGetValue(u.Id, out var f) ? f : null)).ToList(), total, query.Page, query.PageSize);
        }

        var q = from m in db.Set<RateGroupMember>().AsNoTracking()
                join u in db.Set<User>() on m.UserId equals u.Id
                where m.GroupId == id
                select new { m, u };
        if (like is not null) q = q.Where(x => EF.Functions.Like(x.u.DisplayName, like, "\\") || EF.Functions.Like(x.u.Email, like, "\\"));
        var count = await db.Set<RateGroupMember>().CountAsync(m => m.GroupId == id, ct);
        if (like is not null) count = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.u.DisplayName).ThenBy(x => x.u.Id).Skip(query.Skip).Take(query.PageSize)
            .Select(x => new { x.u.Id, x.u.DisplayName, x.u.Email, x.u.CountryCode, x.u.Tier, x.u.Status, x.u.IsTestAccount, x.m.AddedAt, x.m.AddedByUserId, x.m.Note })
            .ToListAsync(ct);
        var adders = rows.Where(r => r.AddedByUserId.HasValue).Select(r => r.AddedByUserId!.Value).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => adders.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return new PagedResult<RateGroupMemberDto>(rows.Select(r => new RateGroupMemberDto(r.Id, r.DisplayName, r.Email, r.CountryCode, r.Tier,
            r.Status, r.IsTestAccount, r.AddedAt, r.AddedByUserId is { } a ? new UserRefDto(a, names.GetValueOrDefault(a, "Unknown user")) : null,
            r.Note, null)).ToList(), count, query.Page, query.PageSize);
    }

    public async Task<BulkMembersResultDto> AddMembersAsync(Guid id, AddRateGroupMembersRequest request, CancellationToken ct)
    {
        var (added, unchanged, rejected, warnings) = await AddCoreAsync(id, request.UserIds.Select((u, i) => (Row: (int?)null, Value: u.ToString(), UserId: u)).ToList(),
            request.Note, "bulk", ct);
        return new BulkMembersResultDto(request.UserIds.Count, added, unchanged, 0, rejected, warnings);
    }

    /// <summary>
    /// Adds people in chunks inside one transaction (group lock held): existing members are left alone, unknown,
    /// deactivated and non-participant accounts are rejected, suspended and test accounts are added with a warning.
    /// </summary>
    private async Task<(int Added, int Unchanged, List<BulkIssueDto> Rejected, List<BulkIssueDto> Warnings)> AddCoreAsync(
        Guid id, IReadOnlyList<(int? Row, string Value, Guid UserId)> rows, string? note, string source, CancellationToken ct)
    {
        var rejected = new List<BulkIssueDto>();
        var warnings = new List<BulkIssueDto>();
        var added = 0;
        var unchanged = 0;
        var now = Now;
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        await using (await db.Dialect().AcquireNamedLockAsync(db, GroupLockName(id), LockTimeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, "rate_groups", id, ct);
            var group = await db.Set<RateGroup>().FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw NotFound();
            if (group.ArchivedAt is not null) throw Archived();
            if (group.MembershipMode == RateGroupMembershipMode.Automatic)
                throw DomainException.Conflict("rate_group.automatic", "Members of an automatic group come from its rule; they can't be added by hand.");

            var seen = new HashSet<Guid>();
            var unique = new List<(int? Row, string Value, Guid UserId)>();
            foreach (var r in rows)
            {
                if (seen.Add(r.UserId)) unique.Add(r);
                else warnings.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "duplicate", "Listed more than once; added once."));
            }

            foreach (var chunk in unique.Chunk(Chunk))
            {
                var ids = chunk.Select(c => c.UserId).ToList();
                var users = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id))
                    .Select(u => new { u.Id, u.Status, u.IsTestAccount, IsParticipant = u.Roles.Any(r => r.Role == Role.Participant) })
                    .ToDictionaryAsync(u => u.Id, ct);
                var existing = (await db.Set<RateGroupMember>().AsNoTracking().Where(m => m.GroupId == id && ids.Contains(m.UserId))
                    .Select(m => m.UserId).ToListAsync(ct)).ToHashSet();
                foreach (var r in chunk)
                {
                    if (!users.TryGetValue(r.UserId, out var u))
                    {
                        rejected.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "user.not_found", "No account with this id."));
                        continue;
                    }
                    if (!u.IsParticipant)
                    {
                        rejected.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "rates.not_participant", "Not a participant account."));
                        continue;
                    }
                    if (u.Status == UserStatus.Deactivated)
                    {
                        rejected.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "rates.user_deactivated", "The account is deactivated."));
                        continue;
                    }
                    if (existing.Contains(r.UserId))
                    {
                        unchanged++;
                        continue;
                    }
                    if (u.Status == UserStatus.Suspended)
                        warnings.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "user.suspended", "Added, but the account is suspended (it can't submit until reactivated)."));
                    if (u.IsTestAccount)
                        warnings.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "user.test_account", "Added; test accounts are never paid."));
                    db.Set<RateGroupMember>().Add(new RateGroupMember { GroupId = id, UserId = r.UserId, AddedAt = now, AddedByUserId = currentUser.Id, Note = note });
                    db.Set<RateGroupMemberEvent>().Add(Event(id, r.UserId, RateGroupMemberAction.Added, now, source, note));
                    added++;
                }
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }

            if (added > 0)
            {
                group = await db.Set<RateGroup>().FirstAsync(g => g.Id == id, ct);
                ConcurrencyGuard.Touch(db, group);
                audit.Record("rate_group.members_added", nameof(RateGroup), id,
                    after: new { Added = added, Unchanged = unchanged, Rejected = rejected.Count, Source = source }, reason: note);
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        return (added, unchanged, rejected, warnings);
    }

    public async Task<BulkMembersResultDto> RemoveMembersAsync(Guid id, RemoveRateGroupMembersRequest request, CancellationToken ct)
    {
        var now = Now;
        var reason = request.Reason.Trim();
        var removed = 0;
        var ids = request.UserIds.Distinct().ToList();
        await using (await db.Dialect().AcquireNamedLockAsync(db, GroupLockName(id), LockTimeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, "rate_groups", id, ct);
            var group = await db.Set<RateGroup>().FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw NotFound();
            if (group.MembershipMode == RateGroupMembershipMode.Automatic)
                throw DomainException.Conflict("rate_group.automatic", "Members of an automatic group come from its rule; change the rule instead.");
            foreach (var chunk in ids.Chunk(Chunk))
            {
                var list = chunk.ToList();
                var present = await db.Set<RateGroupMember>().AsNoTracking().Where(m => m.GroupId == id && list.Contains(m.UserId))
                    .Select(m => m.UserId).ToListAsync(ct);
                if (present.Count == 0) continue;
                await db.Set<RateGroupMember>().Where(m => m.GroupId == id && present.Contains(m.UserId)).ExecuteDeleteAsync(ct);
                db.Set<RateGroupMemberEvent>().AddRange(present.Select(u => Event(id, u, RateGroupMemberAction.Removed, now, "bulk", reason)));
                removed += present.Count;
            }
            if (removed > 0)
            {
                ConcurrencyGuard.Touch(db, group);
                audit.Record("rate_group.members_removed", nameof(RateGroup), id, after: new { Removed = removed, Requested = ids.Count }, reason: reason);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return new BulkMembersResultDto(request.UserIds.Count, 0, ids.Count - removed, removed, Array.Empty<BulkIssueDto>(), Array.Empty<BulkIssueDto>());
    }

    public async Task<CsvImportResultDto> ImportAsync(Guid id, IFormFile file, bool dryRun, string? note, CancellationToken ct)
    {
        if (file.Length == 0) throw new DomainException("csv.empty", "The file is empty.");
        if (file.Length > RateGroupLimits.MaxCsvBytes) throw new DomainException("csv.too_large", "The file is larger than 2 MB.");
        var group = await db.Set<RateGroup>().AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw NotFound();
        if (group.ArchivedAt is not null) throw Archived();
        if (group.MembershipMode == RateGroupMembershipMode.Automatic)
            throw DomainException.Conflict("rate_group.automatic", "Members of an automatic group come from its rule; they can't be imported.");

        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            content = await reader.ReadToEndAsync(ct);
        var table = CsvParser.Parse(content).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (table.Count == 0) throw new DomainException("csv.empty", "The file has no rows.");

        // Header: a column named email / userId (any case); otherwise the first column, detected per row.
        var header = table[0].Select(h => h.Trim().ToLowerInvariant().Replace("_", "").Replace(" ", "")).ToArray();
        var col = Array.FindIndex(header, h => h is "email" or "emailaddress" or "userid" or "id");
        var hasHeader = col >= 0 || header.Any(h => h is "name" or "displayname" or "note");
        if (col < 0) col = 0;
        var dataRows = table.Skip(hasHeader ? 1 : 0).Select((r, i) => (Row: i + (hasHeader ? 2 : 1), Value: col < r.Length ? r[col].Trim() : string.Empty)).ToList();
        if (dataRows.Count > RateGroupLimits.MaxBulk)
            throw new DomainException("csv.too_many_rows", $"A file can add at most {RateGroupLimits.MaxBulk:N0} people; split it.");

        var rejected = new List<BulkIssueDto>();
        var resolved = new List<(int? Row, string Value, Guid UserId)>();
        var emails = dataRows.Where(r => !Guid.TryParse(r.Value, out _) && r.Value.Contains('@')).Select(r => Normalization.Email(r.Value)).Distinct().ToList();
        var byEmail = new Dictionary<string, Guid>();
        foreach (var chunk in emails.Chunk(Chunk))
        {
            var list = chunk.ToList();
            foreach (var u in await db.Set<User>().AsNoTracking().Where(u => list.Contains(u.NormalizedEmail)).Select(u => new { u.NormalizedEmail, u.Id }).ToListAsync(ct))
                byEmail[u.NormalizedEmail] = u.Id;
        }
        foreach (var r in dataRows)
        {
            if (string.IsNullOrEmpty(r.Value)) rejected.Add(new BulkIssueDto(r.Row, r.Value, null, "csv.blank", "The row has no email or user id."));
            else if (Guid.TryParse(r.Value, out var gid)) resolved.Add((r.Row, r.Value, gid));
            else if (!r.Value.Contains('@')) rejected.Add(new BulkIssueDto(r.Row, r.Value, null, "csv.invalid_value", "Not an email address or user id."));
            else if (byEmail.TryGetValue(Normalization.Email(r.Value), out var uid)) resolved.Add((r.Row, r.Value, uid));
            else rejected.Add(new BulkIssueDto(r.Row, r.Value, null, "user.not_found", "No account uses this email."));
        }

        if (dryRun)
        {
            // Validate everything the real import would, without writing: same classification, no transaction.
            var warnings = new List<BulkIssueDto>();
            var seen = new HashSet<Guid>();
            var already = 0;
            var valid = 0;
            foreach (var chunk in resolved.Chunk(Chunk))
            {
                var ids = chunk.Select(c => c.UserId).ToList();
                var users = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id))
                    .Select(u => new { u.Id, u.Status, u.IsTestAccount, IsParticipant = u.Roles.Any(x => x.Role == Role.Participant) }).ToDictionaryAsync(u => u.Id, ct);
                var existing = (await db.Set<RateGroupMember>().AsNoTracking().Where(m => m.GroupId == id && ids.Contains(m.UserId)).Select(m => m.UserId).ToListAsync(ct)).ToHashSet();
                foreach (var r in chunk)
                {
                    if (!seen.Add(r.UserId)) { warnings.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "duplicate", "Listed more than once; added once.")); continue; }
                    if (!users.TryGetValue(r.UserId, out var u)) { rejected.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "user.not_found", "No account with this id.")); continue; }
                    if (!u.IsParticipant) { rejected.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "rates.not_participant", "Not a participant account.")); continue; }
                    if (u.Status == UserStatus.Deactivated) { rejected.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "rates.user_deactivated", "The account is deactivated.")); continue; }
                    if (existing.Contains(r.UserId)) { already++; continue; }
                    if (u.Status == UserStatus.Suspended) warnings.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "user.suspended", "Will be added, but the account is suspended."));
                    if (u.IsTestAccount) warnings.Add(new BulkIssueDto(r.Row, r.Value, r.UserId, "user.test_account", "Will be added; test accounts are never paid."));
                    valid++;
                }
            }
            return new CsvImportResultDto(true, dataRows.Count, valid, 0, already, rejected.OrderBy(r => r.Row).ToList(), warnings);
        }

        var (added, unchanged, moreRejected, warn) = await AddCoreAsync(id, resolved, note ?? $"CSV import ({file.FileName})", "csv", ct);
        rejected.AddRange(moreRejected);
        return new CsvImportResultDto(false, dataRows.Count, added, added, unchanged, rejected.OrderBy(r => r.Row).ToList(), warn);
    }

    public async Task<(string FileName, IReadOnlyList<string> Header, IReadOnlyList<object?[]> Rows)> ExportAsync(Guid id, CancellationToken ct)
    {
        var g = await db.Set<RateGroup>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var header = new[] { "userId", "email", "displayName", "country", "tier", "status", "addedAt", "note" };
        List<object?[]> rows;
        if (g.MembershipMode == RateGroupMembershipMode.Automatic)
            rows = (await AutoMembers(g).OrderBy(u => u.DisplayName).Take(100_000)
                    .Select(u => new { u.Id, u.Email, u.DisplayName, u.CountryCode, u.Tier, u.Status }).ToListAsync(ct))
                .Select(u => new object?[] { u.Id, u.Email, u.DisplayName, u.CountryCode, u.Tier.ToString(), u.Status.ToString(), null, "automatic" }).ToList();
        else
            rows = (await (from m in db.Set<RateGroupMember>().AsNoTracking()
                           join u in db.Set<User>() on m.UserId equals u.Id
                           where m.GroupId == id
                           orderby u.DisplayName
                           select new { u.Id, u.Email, u.DisplayName, u.CountryCode, u.Tier, u.Status, m.AddedAt, m.Note }).ToListAsync(ct))
                .Select(u => new object?[] { u.Id, u.Email, u.DisplayName, u.CountryCode, u.Tier.ToString(), u.Status.ToString(), u.AddedAt, u.Note }).ToList();
        var slug = new string(g.Name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return ($"rate-group-{(slug.Length == 0 ? "members" : slug)}-{Now:yyyyMMdd}.csv", header, rows);
    }

    public async Task<PagedResult<RateGroupMemberEventDto>> HistoryAsync(Guid id, PageQuery query, CancellationToken ct)
    {
        if (!await db.Set<RateGroup>().AnyAsync(g => g.Id == id, ct)) throw NotFound();
        var q = db.Set<RateGroupMemberEvent>().AsNoTracking().Where(e => e.GroupId == id);
        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var ids = page.SelectMany(e => new[] { (Guid?)e.UserId, e.ActorUserId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return new PagedResult<RateGroupMemberEventDto>(page.Select(e => new RateGroupMemberEventDto(e.UserId, names.GetValueOrDefault(e.UserId, "Unknown user"),
            e.Action, e.At, e.ActorUserId is { } a ? new UserRefDto(a, names.GetValueOrDefault(a, "Unknown user")) : null, e.Source, e.Reason)).ToList(),
            total, query.Page, query.PageSize);
    }

    /// <summary>Participants matching an automatic group's rule on at least one platform (for lists and counts).</summary>
    private IQueryable<User> AutoMembers(RateGroup g)
    {
        var users = db.Set<User>().AsNoTracking()
            .Where(u => u.Status != UserStatus.Deactivated && u.Roles.Any(r => r.Role == Role.Participant));
        if (g.AutoTiers.Count > 0)
        {
            var tiers = g.AutoTiers.ToList();
            users = users.Where(u => tiers.Contains(u.Tier));
        }
        if (g.AutoMinFollowers is not null || g.AutoMaxFollowers is not null)
        {
            var min = g.AutoMinFollowers;
            var max = g.AutoMaxFollowers;
            var verified = g.AutoRequireVerified;
            users = users.Where(u => db.Set<SocialAccount>().Any(a => a.UserId == u.Id && a.IsActive &&
                (!verified || a.VerificationStatus == SocialAccountVerificationStatus.Verified) &&
                (min == null || a.FollowerCount >= min) && (max == null || a.FollowerCount < max)));
        }
        return users;
    }

    private static void Apply(RateGroup group, RateGroupInput input)
    {
        if (input.AutoMinFollowers is { } min && input.AutoMaxFollowers is { } max && max <= min)
            throw new DomainException("rate_group.invalid_rule", "The follower maximum must be greater than the minimum.",
                errors: new Dictionary<string, string[]> { ["autoMaxFollowers"] = new[] { "Must be greater than the minimum." } });
        group.Name = input.Name.Trim();
        group.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        group.Priority = input.Priority;
        group.MembershipMode = input.MembershipMode;
        var auto = input.MembershipMode == RateGroupMembershipMode.Automatic;
        group.AutoTiers = auto ? (input.AutoTiers ?? new List<ParticipantTier>()).Distinct().OrderBy(t => t).ToList() : new List<ParticipantTier>();
        group.AutoMinFollowers = auto ? input.AutoMinFollowers : null;
        group.AutoMaxFollowers = auto ? input.AutoMaxFollowers : null;
        group.AutoRequireVerified = !auto || input.AutoRequireVerified;
        if (auto && group.AutoTiers.Count == 0 && group.AutoMinFollowers is null && group.AutoMaxFollowers is null)
            throw new DomainException("rate_group.invalid_rule", "An automatic group needs a tier or follower rule.",
                errors: new Dictionary<string, string[]> { ["autoTiers"] = new[] { "Choose tiers or follower bounds." } });
    }

    private static object Snapshot(RateGroup g) => new
    {
        g.Name, g.Description, g.Priority, MembershipMode = g.MembershipMode.ToString(),
        AutoTiers = g.AutoTiers.Select(t => t.ToString()), g.AutoMinFollowers, g.AutoMaxFollowers, g.AutoRequireVerified,
    };

    private RateGroupMemberEvent Event(Guid groupId, Guid userId, RateGroupMemberAction action, DateTime at, string source, string? reason) => new()
    {
        GroupId = groupId, UserId = userId, Action = action, At = at, ActorUserId = currentUser.Id, Source = source,
        Reason = reason is { Length: > 500 } ? reason[..500] : reason,
    };

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.Trim().ToLower();
        if (await db.Set<RateGroup>().AnyAsync(g => g.ArchivedAt == null && g.Name.ToLower() == lower && (exceptId == null || g.Id != exceptId), ct))
            throw new DomainException("rate_group.name_taken", $"A rate group named '{name.Trim()}' already exists.", DomainErrorKind.Conflict,
                new Dictionary<string, string[]> { ["name"] = new[] { "This name is already used by another group." } });
    }

    private static DomainException NotFound() => DomainException.NotFound("RateGroup");

    private static DomainException Archived() => DomainException.Conflict("rate_group.archived", "This group is archived.");
}
