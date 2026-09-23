using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Domain.Support;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Support;

public sealed class SupportService(
    AppDbContext db,
    IAuditLogger audit,
    INotificationService notifications,
    TimeProvider clock,
    ILogger<SupportService> logger)
{
    public const string StaffAuthorName = "Optimize All Support";
    private const string ReferenceAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>"SUP-" + 6 random upper-case alphanumerics.</summary>
    public static string NewReference() =>
        "SUP-" + new string(Enumerable.Range(0, 6).Select(_ => ReferenceAlphabet[RandomNumberGenerator.GetInt32(ReferenceAlphabet.Length)]).ToArray());

    public static bool IsOpen(TicketStatus status) => status is not (TicketStatus.Resolved or TicketStatus.Closed);

    // ---------- Participant ----------

    public async Task<PagedResult<TicketSummaryDto>> ListMineAsync(Guid userId, MyTicketQuery query, CancellationToken ct)
    {
        var q = db.Set<SupportTicket>().AsNoTracking().Where(t => t.UserId == userId);
        if (query.Status is { } status) q = q.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(t => EF.Functions.Like(t.Subject, p) || EF.Functions.Like(t.Reference, p));
        }
        return await q.OrderByDescending(t => t.UpdatedAt)
            .Select(t => new TicketSummaryDto(t.Id, t.Reference, t.Subject, t.Category, t.Status, t.Priority, t.CreatedAt, t.UpdatedAt))
            .ToPagedAsync(query, ct);
    }

    public async Task<ParticipantTicketDto> CreateAsync(Guid userId, CreateTicketRequest request, CancellationToken ct)
    {
        var category = request.Category!.Value;
        if (!Enum.IsDefined(category))
            throw FieldRules.FieldError("support.invalid_category", "category", "Choose a category.");
        if (request.SubmissionId is { } submissionId &&
            !await db.Set<Submission>().AnyAsync(s => s.Id == submissionId && s.UserId == userId, ct))
            throw FieldRules.FieldError("support.invalid_submission", "submissionId", "That submission wasn't found in your account.");
        if (request.PayoutItemId is { } payoutItemId &&
            !await db.Set<PayoutItem>().AnyAsync(p => p.Id == payoutItemId && p.UserId == userId, ct))
            throw FieldRules.FieldError("support.invalid_payout_item", "payoutItemId", "That payout wasn't found in your account.");

        for (var attempt = 1; ; attempt++)
        {
            var ticket = new SupportTicket
            {
                Reference = NewReference(),
                UserId = userId,
                Subject = request.Subject.Trim(),
                Category = category,
                Priority = category == TicketCategory.Dispute ? TicketPriority.High : TicketPriority.Normal,
                Status = TicketStatus.Open,
                SubmissionId = request.SubmissionId,
                PayoutItemId = request.PayoutItemId,
            };
            ticket.Messages.Add(new SupportMessage
            {
                TicketId = ticket.Id, AuthorUserId = userId, Body = request.Body.Trim(), CreatedAt = Now,
            });
            db.Set<SupportTicket>().Add(ticket);
            try
            {
                await db.SaveChangesAsync(ct);
                return await ParticipantViewAsync(userId, ticket.Id, ct);
            }
            catch (DbUpdateException ex) when (Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex) && attempt < 5)
            {
                // Reference collision (36^6 space): detach and retry with a new reference.
                logger.LogInformation("Support reference collision, retrying (attempt {Attempt})", attempt);
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<ParticipantTicketDto> GetMineAsync(Guid userId, Guid id, CancellationToken ct) =>
        await ParticipantViewAsync(userId, id, ct);

    public async Task<ParticipantTicketDto> ReplyAsParticipantAsync(Guid userId, Guid id, TicketMessageRequest request, CancellationToken ct)
    {
        var ticket = await db.Set<SupportTicket>().FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct)
                     ?? throw DomainException.NotFound("SupportTicket");
        if (ticket.Status == TicketStatus.Closed)
            throw DomainException.Conflict("support.ticket_closed", "This ticket is closed. Open a new ticket if you still need help.");

        db.Set<SupportMessage>().Add(new SupportMessage { TicketId = ticket.Id, AuthorUserId = userId, Body = request.Body.Trim(), CreatedAt = Now });
        // A participant reply always puts the ball back in the staff's court (reopening a resolved ticket).
        ticket.Status = TicketStatus.AwaitingStaff;
        ticket.ResolvedAt = null;
        ConcurrencyGuard.Touch(db, ticket);
        await db.SaveChangesAsync(ct);
        return await ParticipantViewAsync(userId, id, ct);
    }

    public async Task<ParticipantTicketDto> CloseAsParticipantAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var ticket = await db.Set<SupportTicket>().FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct)
                     ?? throw DomainException.NotFound("SupportTicket");
        if (ticket.Status != TicketStatus.Closed)
        {
            var before = ticket.Status;
            ticket.Status = TicketStatus.Closed;
            ticket.ResolvedAt ??= Now;
            audit.Record("support.ticket_closed_by_participant", nameof(SupportTicket), ticket.Id,
                new { status = before }, new { status = ticket.Status });
            await db.SaveChangesAsync(ct);
        }
        return await ParticipantViewAsync(userId, id, ct);
    }

    private async Task<ParticipantTicketDto> ParticipantViewAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var t = await db.Set<SupportTicket>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct)
                ?? throw DomainException.NotFound("SupportTicket");
        var me = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstAsync(ct);

        // Internal notes are filtered in the query: they never leave the database for a participant request.
        var messages = await db.Set<SupportMessage>().AsNoTracking()
            .Where(m => m.TicketId == id && !m.IsInternalNote)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.Body, m.AuthorUserId, m.CreatedAt })
            .ToListAsync(ct);

        return new ParticipantTicketDto(t.Id, t.Reference, t.Subject, t.Category, t.Status, t.Priority, t.SubmissionId, t.PayoutItemId,
            t.CreatedAt, t.UpdatedAt, t.ResolvedAt, t.Status != TicketStatus.Closed,
            messages.Select(m => new ParticipantMessageDto(m.Id, m.Body, m.AuthorUserId != userId,
                m.AuthorUserId == userId ? me : StaffAuthorName, m.CreatedAt)).ToList());
    }

    // ---------- Staff ----------

    public async Task<PagedResult<StaffTicketSummaryDto>> ListAllAsync(Guid staffId, StaffTicketQuery query, CancellationToken ct)
    {
        var q = from t in db.Set<SupportTicket>().AsNoTracking()
                join u in db.Set<User>().AsNoTracking() on t.UserId equals u.Id
                join a in db.Set<User>().AsNoTracking() on t.AssignedToUserId equals a.Id into assignees
                from a in assignees.DefaultIfEmpty()
                select new { t, u, a };
        if (query.Status is { } status) q = q.Where(x => x.t.Status == status);
        if (query.Priority is { } priority) q = q.Where(x => x.t.Priority == priority);
        if (query.Category is { } category) q = q.Where(x => x.t.Category == category);
        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
        {
            var value = query.AssignedTo.Trim();
            if (value.Equals("unassigned", StringComparison.OrdinalIgnoreCase)) q = q.Where(x => x.t.AssignedToUserId == null);
            else if (value.Equals("me", StringComparison.OrdinalIgnoreCase)) q = q.Where(x => x.t.AssignedToUserId == staffId);
            else if (Guid.TryParse(value, out var assignee)) q = q.Where(x => x.t.AssignedToUserId == assignee);
            else throw FieldRules.FieldError("support.invalid_assignee_filter", "assignedTo", "Use a user id, \"me\" or \"unassigned\".");
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(x => EF.Functions.Like(x.t.Subject, p) || EF.Functions.Like(x.t.Reference, p) || EF.Functions.Like(x.u.Email, p));
        }
        q = query.Desc ? q.OrderByDescending(x => x.t.UpdatedAt) : q.OrderBy(x => x.t.UpdatedAt);

        return await q.Select(x => new StaffTicketSummaryDto(x.t.Id, x.t.Reference, x.t.Subject, x.t.Category, x.t.Status, x.t.Priority,
                new TicketPersonDto(x.u.Id, x.u.DisplayName, x.u.Email),
                x.a == null ? null : new TicketPersonDto(x.a.Id, x.a.DisplayName, x.a.Email),
                x.t.CreatedAt, x.t.UpdatedAt, x.t.ConcurrencyStamp))
            .ToPagedAsync(query, ct);
    }

    public async Task<StaffTicketDto> GetForStaffAsync(Guid id, CancellationToken ct)
    {
        var t = await db.Set<SupportTicket>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw DomainException.NotFound("SupportTicket");
        var requester = await db.Set<User>().AsNoTracking().FirstAsync(u => u.Id == t.UserId, ct);
        var ticketCounts = new
        {
            Total = await db.Set<SupportTicket>().CountAsync(x => x.UserId == t.UserId, ct),
            Open = await db.Set<SupportTicket>().CountAsync(x => x.UserId == t.UserId &&
                x.Status != TicketStatus.Resolved && x.Status != TicketStatus.Closed, ct),
        };

        TicketPersonDto? assignee = null;
        if (t.AssignedToUserId is { } assigneeId)
            assignee = await db.Set<User>().AsNoTracking().Where(u => u.Id == assigneeId)
                .Select(u => new TicketPersonDto(u.Id, u.DisplayName, u.Email)).FirstOrDefaultAsync(ct);

        var messages = await (from m in db.Set<SupportMessage>().AsNoTracking()
                              where m.TicketId == id
                              join u in db.Set<User>().AsNoTracking() on m.AuthorUserId equals u.Id into authors
                              from u in authors.DefaultIfEmpty()
                              orderby m.CreatedAt, m.Id
                              select new StaffMessageDto(m.Id, m.Body, m.IsInternalNote, m.AuthorUserId != t.UserId, m.AuthorUserId,
                                  u == null ? "Unknown" : u.DisplayName, m.CreatedAt))
            .ToListAsync(ct);

        return new StaffTicketDto(t.Id, t.Reference, t.Subject, t.Category, t.Status, t.Priority, t.SubmissionId, t.PayoutItemId,
            t.CreatedAt, t.UpdatedAt, t.ResolvedAt,
            new RequesterSummaryDto(requester.Id, requester.Email, requester.DisplayName, requester.CountryCode, requester.Status,
                requester.Tier, requester.CreatedAt, ticketCounts.Open, ticketCounts.Total),
            assignee, messages, t.ConcurrencyStamp);
    }

    public async Task<StaffTicketDto> ReplyAsStaffAsync(Guid staffId, Guid id, StaffMessageRequest request, CancellationToken ct)
    {
        var ticket = await db.Set<SupportTicket>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound("SupportTicket");
        if (!request.IsInternalNote && ticket.Status == TicketStatus.Closed)
            throw DomainException.Conflict("support.ticket_closed", "This ticket is closed; reopen it before replying to the participant.");

        db.Set<SupportMessage>().Add(new SupportMessage
        {
            TicketId = ticket.Id, AuthorUserId = staffId, Body = request.Body.Trim(), IsInternalNote = request.IsInternalNote, CreatedAt = Now,
        });

        if (request.IsInternalNote)
        {
            audit.Record("support.internal_note_added", nameof(SupportTicket), ticket.Id);
        }
        else
        {
            var before = ticket.Status;
            ticket.Status = TicketStatus.AwaitingParticipant;
            ticket.ResolvedAt = null;
            audit.Record("support.staff_replied", nameof(SupportTicket), ticket.Id, new { status = before }, new { status = ticket.Status });
            await notifications.StageAsync(new NotificationRequest(
                ticket.UserId,
                NotificationTypes.SupportReply,
                $"New reply on your support ticket {ticket.Reference}",
                $"Our support team replied to \"{ticket.Subject}\". Open the ticket to read the reply and respond.",
                $"/app/support/{ticket.Id}",
                new[] { NotificationChannel.InApp, NotificationChannel.Email }), ct);
        }
        ConcurrencyGuard.Touch(db, ticket);
        await db.SaveChangesAsync(ct);
        return await GetForStaffAsync(id, ct);
    }

    public async Task<StaffTicketDto> UpdateAsync(Guid id, UpdateTicketRequest request, CancellationToken ct)
    {
        var status = request.Status!.Value;
        var priority = request.Priority!.Value;
        if (!Enum.IsDefined(status)) throw FieldRules.FieldError("support.invalid_status", "status", "Unknown status.");
        if (!Enum.IsDefined(priority)) throw FieldRules.FieldError("support.invalid_priority", "priority", "Unknown priority.");

        var ticket = await db.Set<SupportTicket>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound("SupportTicket");
        ConcurrencyGuard.Apply(db, ticket, request.ConcurrencyStamp!.Value);

        if (request.AssignedToUserId is { } assigneeId && assigneeId != ticket.AssignedToUserId)
        {
            var assignee = await db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == assigneeId, ct);
            if (assignee is null || assignee.Status != UserStatus.Active ||
                !RolePermissions.For(assignee.Roles.Select(r => r.Role)).Contains(Permissions.SupportManage))
                throw FieldRules.FieldError("support.invalid_assignee", "assignedToUserId", "Tickets can only be assigned to active support staff.");
        }

        var before = new { ticket.Status, ticket.Priority, ticket.AssignedToUserId };
        ticket.Status = status;
        ticket.Priority = priority;
        ticket.AssignedToUserId = request.AssignedToUserId;
        ticket.ResolvedAt = status is TicketStatus.Resolved or TicketStatus.Closed ? ticket.ResolvedAt ?? Now : null;
        audit.Record("support.ticket_updated", nameof(SupportTicket), ticket.Id, before,
            new { ticket.Status, ticket.Priority, ticket.AssignedToUserId });
        await db.SaveChangesAsync(ct);
        return await GetForStaffAsync(id, ct);
    }
}
