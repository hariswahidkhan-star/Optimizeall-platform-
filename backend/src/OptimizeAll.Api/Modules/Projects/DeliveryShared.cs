using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

/// <summary>
/// Web-app paths for delivery notifications and emails (agency portal /agency, client portal /client). Kept in the module
/// (instead of <c>Common/Notifications/AppLinks</c>, owned by another workstream); the patterns are listed in
/// <c>frontend/src/features/agency/shared/deliveryLinks.fixture.json</c>, which a unit test keeps in sync with this class
/// and a frontend test resolves against the real portal routes.
/// </summary>
public static class DeliveryLinks
{
    public const string AgencyHome = "/agency";
    public static string AgencyClient(Guid clientId) => $"/agency/clients/{clientId}";
    public static string AgencyProject(Guid projectId) => $"/agency/projects/{projectId}";
    public static string AgencyTask(Guid projectId, Guid taskId) => $"/agency/projects/{projectId}?task={taskId}";
    public static string AgencyDeliverable(Guid deliverableId) => $"/agency/deliverables/{deliverableId}";
    public static string AgencyThread(Guid clientId, Guid threadId) => $"/agency/clients/{clientId}?tab=messages&thread={threadId}";
    public static string AgencyReport(Guid reportId) => $"/agency/reports/{reportId}";
    public static string AgencyBrief(Guid clientId, Guid briefId) => $"/agency/clients/{clientId}?tab=briefs&brief={briefId}";
    public const string AgencyTimesheets = "/agency/time";

    public const string ClientHome = "/client";
    public static string ClientDeliverable(Guid clientId, Guid deliverableId) => $"/client/approvals/{deliverableId}?org={clientId}";
    public static string ClientReport(Guid clientId, Guid reportId) => $"/client/reports/{reportId}?org={clientId}";
    public static string ClientThread(Guid clientId, Guid threadId) => $"/client/messages?org={clientId}&thread={threadId}";
    public static string ClientProject(Guid clientId, Guid projectId) => $"/client/projects/{projectId}?org={clientId}";
}

/// <summary>Notification types of the delivery modules (in-app + optional email through the outbox).</summary>
public static class DeliveryNotificationTypes
{
    public const string ClientInvited = "clients.invited";
    public const string TaskAssigned = "projects.task_assigned";
    public const string TaskMention = "projects.mention";
    public const string TaskComment = "projects.task_comment";
    public const string DeliverableInternalReview = "deliverables.internal_review";
    public const string DeliverableAwaitingClient = "deliverables.awaiting_client";
    public const string DeliverableDecision = "deliverables.client_decision";
    public const string DeliverableReminder = "deliverables.sla_reminder";
    public const string TimesheetDecision = "time.timesheet_decision";
    public const string ReportPublished = "reports.published";
    public const string Message = "messages.new";
    public const string BriefSubmitted = "briefs.submitted";
}

/// <summary>Small shared queries for DTOs (names, avatars) and idempotency keys.</summary>
public sealed class DeliveryLookup(AppDbContext db, IDatabaseDialect dialect, TimeProvider clock)
{
    public sealed record Person(Guid Id, string DisplayName, string Email);

    public async Task<Dictionary<Guid, Person>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (set.Count == 0) return new Dictionary<Guid, Person>();
        return await db.Set<User>().AsNoTracking().Where(u => set.Contains(u.Id))
            .Select(u => new Person(u.Id, u.DisplayName, u.Email)).ToDictionaryAsync(p => p.Id, ct);
    }

    public Task<Dictionary<Guid, Person>> PeopleAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        PeopleAsync(ids.Select(i => (Guid?)i), ct);

    /// <summary>Client users of an organization holding one of the duties (Owner always included).</summary>
    public async Task<List<Guid>> ClientUsersAsync(Guid clientId, CancellationToken ct, params ClientMemberRole[] duties) =>
        await db.Set<ClientMember>().AsNoTracking()
            .Where(m => m.ClientAccountId == clientId && (m.Role == ClientMemberRole.Owner || duties.Contains(m.Role)))
            .Select(m => m.UserId).ToListAsync(ct);

    /// <summary>The account team of a client (assigned staff, plus the account manager).</summary>
    public async Task<List<Guid>> ClientTeamAsync(Guid clientId, CancellationToken ct)
    {
        var team = await db.Set<ClientTeamAssignment>().AsNoTracking().Where(a => a.ClientAccountId == clientId)
            .Select(a => a.UserId).ToListAsync(ct);
        var am = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == clientId).Select(c => c.AccountManagerUserId).FirstOrDefaultAsync(ct);
        if (am is { } a) team.Add(a);
        return team.Distinct().ToList();
    }

    /// <summary>
    /// Claims an idempotency key (insert-if-absent). Returns false when the key was already claimed, so a job re-run or a
    /// concurrent instance never sends the same reminder twice. Saves immediately in its own statement.
    /// </summary>
    public async Task<bool> TryClaimAsync(string key, CancellationToken ct)
    {
        if (await db.Set<DeliveryDispatchKey>().AnyAsync(k => k.Key == key, ct)) return false;
        var row = new DeliveryDispatchKey { Key = key, CreatedAt = clock.GetUtcNow().UtcDateTime };
        db.Set<DeliveryDispatchKey>().Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.Entry(row).State = EntityState.Detached;
            return false;
        }
    }
}

/// <summary>Validation helpers shared by the delivery services.</summary>
internal static class DeliveryRules
{
    public static readonly string[] ServiceLines = { "seo", "social", "ads", "email", "content", "web", "design", "strategy", "analytics" };

    public static DomainException Invalid(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = new[] { message } });

    public static void EnsureStamp(IConcurrencyStamped entity, Guid? expected, AppDbContext db)
    {
        if (expected is null) return;
        if (entity.ConcurrencyStamp != expected.Value)
            throw DomainException.Conflict("concurrency.conflict", "This record was changed by someone else. Reload and try again.");
        db.Entry(entity).Property(nameof(IConcurrencyStamped.ConcurrencyStamp)).OriginalValue = expected.Value;
    }

    public static List<string> CleanList(IEnumerable<string>? values, int maxItems, int maxLength, string field)
    {
        var list = (values ?? Array.Empty<string>()).Select(v => v?.Trim() ?? string.Empty).Where(v => v.Length > 0).Distinct().ToList();
        if (list.Count > maxItems) throw Invalid("validation.too_many", field, $"At most {maxItems} entries.");
        if (list.Any(v => v.Length > maxLength)) throw Invalid("validation.too_long", field, $"Each entry must be at most {maxLength} characters.");
        return list;
    }

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        string.IsNullOrEmpty(uri.UserInfo);

    public static bool IsStaffUser(IEnumerable<Role> roles) => roles.Any(r => r is not (Role.Participant or Role.Client));
}

/// <summary>Staff users who may be assigned to delivery work.</summary>
public static class StaffDirectory
{
    /// <summary>The built-in roles of delivery staff (for display); membership is decided by <see cref="DeliveryPermissions"/>.</summary>
    public static readonly Role[] DeliveryRoles =
    {
        Role.Admin, Role.AccountManager, Role.Strategist, Role.ContentCreator, Role.Designer, Role.SeoSpecialist,
        Role.AdsSpecialist, Role.SocialMediaManager, Role.SalesRep,
    };

    /// <summary>
    /// Delivery staff = holders of <c>projects.view</c> (the delivery team) or <c>crm.manage</c> (sales), through built-in
    /// or custom roles — for the built-in roles exactly <see cref="DeliveryRoles"/>.
    /// </summary>
    public static readonly string[] DeliveryPermissions = { Permissions.ProjectsView, Permissions.CrmManage };

    /// <summary>Active delivery staff (built-in or custom roles), composable and untracked.</summary>
    public static async Task<IQueryable<User>> DeliveryStaffAsync(AppDbContext db, CancellationToken ct) =>
        (await new PermissionDirectory(db).UsersWithAnyPermissionAsync(DeliveryPermissions, ct)).Where(u => u.Status == UserStatus.Active);

    /// <summary>Ids from <paramref name="ids"/> that are active staff users (anything else is rejected by callers).</summary>
    public static async Task<HashSet<Guid>> ValidStaffAsync(AppDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new HashSet<Guid>();
        var active = await (await DeliveryStaffAsync(db, ct)).Where(u => list.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        return active.ToHashSet();
    }
}
