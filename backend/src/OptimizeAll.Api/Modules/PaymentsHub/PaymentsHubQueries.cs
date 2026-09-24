using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.PaymentsHub;

/// <summary>
/// Read model of the Payments hub: one list over four sources — invoice payments, open invoice balances, client "I've
/// paid" claims (incoming, <c>billing.view</c>, client-scoped) and payout items (outgoing, <c>payouts.view</c>).
/// Each source is filtered and ordered in the database by (date, id); the page is merged from the first
/// <c>skip + take</c> rows of every source, so paging is exact without loading whole tables. Nothing here writes.
/// </summary>
public sealed class PaymentsHubQueries(
    AppDbContext db,
    ICurrentUser currentUser,
    IClientScope scope,
    BillingReports reports,
    IPayoutScheduleProvider schedules,
    TimeProvider clock)
{
    /// <summary>Hard cap of the CSV export.</summary>
    public const int MaxExportRows = 20_000;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public bool CanIncoming => currentUser.HasPermission(Permissions.BillingView);
    public bool CanOutgoing => currentUser.HasPermission(Permissions.PayoutsView);

    public void RequireAny()
    {
        if (!CanIncoming && !CanOutgoing)
            throw DomainException.Forbidden("auth.forbidden", "You do not have permission to perform this action.");
    }

    // ------------------------------------------------------------------ List

    /// <summary>Sort key of a record (a class with settable members so EF can order and page the projection).</summary>
    private sealed class Key
    {
        public PaymentRecordKind Kind { get; init; }
        public Guid Id { get; init; }
        public DateOnly Date { get; init; }
    }

    public async Task<PagedResult<PaymentRecordDto>> ListAsync(PaymentHubQuery query, CancellationToken ct)
    {
        RequireAny();
        var sources = await SourcesAsync(query, ct);
        var need = PagingExtensions.EndFor(query.Page, query.PageSize); // rows up to the end of the page, capped (no negative LIMIT)
        var total = 0;
        var candidates = new List<Key>();
        foreach (var source in sources)
        {
            total += await source.CountAsync(ct);
            candidates.AddRange(await Order(source, query.Desc).Take(need).ToListAsync(ct));
        }
        var page = Sort(candidates, query.Desc).Skip(query.Skip).Take(query.PageSize).ToList();
        return new PagedResult<PaymentRecordDto>(await HydrateAsync(page, ct), total, query.Page, query.PageSize);
    }

    /// <summary>Every matching record (for CSV), up to <see cref="MaxExportRows"/>.</summary>
    public async Task<IReadOnlyList<PaymentRecordDto>> ExportAsync(PaymentHubQuery query, CancellationToken ct)
    {
        RequireAny();
        var keys = new List<Key>();
        foreach (var source in await SourcesAsync(query, ct))
            keys.AddRange(await Order(source, query.Desc).Take(MaxExportRows).ToListAsync(ct));
        var rows = new List<PaymentRecordDto>();
        foreach (var chunk in Sort(keys, query.Desc).Take(MaxExportRows).Chunk(500))
            rows.AddRange(await HydrateAsync(chunk, ct));
        return rows;
    }

    private static IQueryable<Key> Order(IQueryable<Key> q, bool desc) =>
        desc ? q.OrderByDescending(k => k.Date).ThenByDescending(k => k.Id) : q.OrderBy(k => k.Date).ThenBy(k => k.Id);

    /// <summary>
    /// The same total order the database applies within a source: date, then id. Ids are compared as their canonical text
    /// (the database compares the stored GUID text; case differs by provider but hex digits sort below letters either way).
    /// </summary>
    private static IEnumerable<Key> Sort(IEnumerable<Key> keys, bool desc)
    {
        var ordered = keys.OrderBy(k => k.Date).ThenBy(k => k.Id.ToString("D").ToUpperInvariant(), StringComparer.Ordinal);
        return desc ? ordered.Reverse() : ordered;
    }

    private async Task<List<IQueryable<Key>>> SourcesAsync(PaymentHubQuery q, CancellationToken ct)
    {
        var list = new List<IQueryable<Key>>();
        var wantIncoming = CanIncoming && q.Direction is null or PaymentDirection.Incoming && q.UserId is null && q.BatchId is null;
        var wantOutgoing = CanOutgoing && q.Direction is null or PaymentDirection.Outgoing && q.ClientAccountId is null && q.InvoiceId is null &&
                           q.Method is null && q.OverdueOnly != true;
        bool Kind(PaymentRecordKind k) => q.Kind is null || q.Kind == k;
        var currency = string.IsNullOrWhiteSpace(q.Currency) ? null : q.Currency.Trim().ToUpperInvariant();
        var pattern = string.IsNullOrWhiteSpace(q.Search) ? null : PagingExtensions.LikePattern(q.Search);
        var today = Today;

        if (wantIncoming)
        {
            var clientsByName = pattern is null ? null : db.Set<ClientAccount>().Where(c => EF.Functions.Like(c.Name, pattern, "\\")).Select(c => c.Id);
            var invoicesByNumber = pattern is null ? null : db.Set<Invoice>().Where(i => EF.Functions.Like(i.Number!, pattern, "\\")).Select(i => i.Id);

            if (Kind(PaymentRecordKind.InvoicePayment) && q.OverdueOnly != true &&
                q.Status is null or PaymentHubStatus.Paid or PaymentHubStatus.Refunded or PaymentHubStatus.Voided)
            {
                var p = (await scope.ApplyAsync(db.Set<Payment>().AsNoTracking(), x => x.ClientAccountId, ct)).Where(x => x.ReversalOfPaymentId == null);
                p = q.Status switch
                {
                    PaymentHubStatus.Paid => p.Where(x => x.ReversedAt == null),
                    PaymentHubStatus.Refunded => p.Where(x => x.ReversedAt != null && x.ReversalKind == PaymentReversalKind.Refund),
                    PaymentHubStatus.Voided => p.Where(x => x.ReversedAt != null && x.ReversalKind != PaymentReversalKind.Refund),
                    _ => p,
                };
                if (currency is not null) p = p.Where(x => x.Currency == currency);
                if (q.Method is { } method) p = p.Where(x => x.Method == method);
                if (q.ClientAccountId is { } c) p = p.Where(x => x.ClientAccountId == c);
                if (q.InvoiceId is { } inv) p = p.Where(x => x.InvoiceId == inv);
                if (q.From is { } from) p = p.Where(x => x.PaidOn >= from);
                if (q.To is { } to) p = p.Where(x => x.PaidOn <= to);
                if (pattern is not null)
                    p = p.Where(x => EF.Functions.Like(x.Reference, pattern, "\\") || clientsByName!.Contains(x.ClientAccountId) ||
                                     invoicesByNumber!.Contains(x.InvoiceId));
                list.Add(p.Select(x => new Key { Kind = PaymentRecordKind.InvoicePayment, Id = x.Id, Date = x.PaidOn }));
            }

            if (Kind(PaymentRecordKind.InvoiceDue) && q.Status is null or PaymentHubStatus.Pending && q.Method is null)
            {
                var i = (await scope.ApplyAsync(db.Set<Invoice>().AsNoTracking(), x => x.ClientAccountId, ct))
                    .Where(x => Invoice.OpenStatuses.Contains(x.Status) && x.Balance > 0 && x.DueDate != null);
                if (q.OverdueOnly == true) i = i.Where(x => x.DueDate < today);
                if (currency is not null) i = i.Where(x => x.Currency == currency);
                if (q.ClientAccountId is { } c) i = i.Where(x => x.ClientAccountId == c);
                if (q.InvoiceId is { } inv) i = i.Where(x => x.Id == inv);
                if (q.From is { } from) i = i.Where(x => x.DueDate >= from);
                if (q.To is { } to) i = i.Where(x => x.DueDate <= to);
                if (pattern is not null)
                    i = i.Where(x => EF.Functions.Like(x.Number!, pattern, "\\") || EF.Functions.Like(x.Reference!, pattern, "\\") ||
                                     clientsByName!.Contains(x.ClientAccountId));
                list.Add(i.Select(x => new Key { Kind = PaymentRecordKind.InvoiceDue, Id = x.Id, Date = x.DueDate!.Value }));
            }

            if (Kind(PaymentRecordKind.PaymentClaim) && q.OverdueOnly != true && q.Status is null or PaymentHubStatus.Pending or PaymentHubStatus.Voided)
            {
                var cl = (await scope.ApplyAsync(db.Set<PaymentClaim>().AsNoTracking(), x => x.ClientAccountId, ct))
                    .Where(x => x.Status != PaymentClaimStatus.Confirmed); // a confirmed claim is shown as its payment
                cl = q.Status switch
                {
                    PaymentHubStatus.Pending => cl.Where(x => x.Status == PaymentClaimStatus.Pending),
                    PaymentHubStatus.Voided => cl.Where(x => x.Status == PaymentClaimStatus.Rejected),
                    _ => cl,
                };
                if (currency is not null) cl = cl.Where(x => x.Currency == currency);
                if (q.Method is { } method) cl = cl.Where(x => x.Method == method);
                if (q.ClientAccountId is { } c) cl = cl.Where(x => x.ClientAccountId == c);
                if (q.InvoiceId is { } inv) cl = cl.Where(x => x.InvoiceId == inv);
                if (q.From is { } from) cl = cl.Where(x => x.PaidOn >= from);
                if (q.To is { } to) cl = cl.Where(x => x.PaidOn <= to);
                if (pattern is not null)
                    cl = cl.Where(x => EF.Functions.Like(x.Reference, pattern, "\\") || clientsByName!.Contains(x.ClientAccountId) ||
                                       invoicesByNumber!.Contains(x.InvoiceId));
                list.Add(cl.Select(x => new Key { Kind = PaymentRecordKind.PaymentClaim, Id = x.Id, Date = x.PaidOn }));
            }
        }

        if (wantOutgoing && Kind(PaymentRecordKind.PayoutItem))
        {
            var items =
                from i in db.Set<PayoutItem>().AsNoTracking()
                join b in db.Set<PayoutBatch>() on i.BatchId equals b.Id
                select new { i, b };
            items = q.Status switch
            {
                null => items,
                PaymentHubStatus.Scheduled => items.Where(x => x.i.Status == PayoutItemStatus.Pending ||
                                                               (x.i.Status == PayoutItemStatus.Held && x.b.Status == PayoutBatchStatus.Draft)),
                PaymentHubStatus.Pending => items.Where(x => x.i.Status == PayoutItemStatus.AwaitingPayment),
                PaymentHubStatus.Paid => items.Where(x => x.i.Status == PayoutItemStatus.Paid),
                PaymentHubStatus.Failed => items.Where(x => x.i.Status == PayoutItemStatus.Failed),
                PaymentHubStatus.Voided => items.Where(x => x.i.Status == PayoutItemStatus.Cancelled ||
                                                            (x.i.Status == PayoutItemStatus.Held && x.b.Status != PayoutBatchStatus.Draft)),
                _ => items.Where(x => false),
            };
            if (currency is not null) items = items.Where(x => x.i.Currency == currency);
            if (q.UserId is { } user) items = items.Where(x => x.i.UserId == user);
            if (q.BatchId is { } batch) items = items.Where(x => x.i.BatchId == batch);
            if (q.From is { } from) items = items.Where(x => x.b.ScheduledPaymentDate >= from);
            if (q.To is { } to) items = items.Where(x => x.b.ScheduledPaymentDate <= to);
            if (pattern is not null)
            {
                var users = db.Set<User>().Where(u => EF.Functions.Like(u.DisplayName, pattern, "\\") || EF.Functions.Like(u.Email, pattern, "\\"))
                    .Select(u => u.Id);
                items = items.Where(x => EF.Functions.Like(x.b.Reference, pattern, "\\") || EF.Functions.Like(x.i.PaymentReference!, pattern, "\\") ||
                                         users.Contains(x.i.UserId));
            }
            list.Add(items.Select(x => new Key { Kind = PaymentRecordKind.PayoutItem, Id = x.i.Id, Date = x.b.ScheduledPaymentDate }));
        }
        return list;
    }

    // ------------------------------------------------------------------ Hydration

    private async Task<IReadOnlyList<PaymentRecordDto>> HydrateAsync(IReadOnlyList<Key> keys, CancellationToken ct)
    {
        var byKind = keys.GroupBy(k => k.Kind).ToDictionary(g => g.Key, g => g.Select(k => k.Id).ToList());
        var result = new Dictionary<(PaymentRecordKind, Guid), PaymentRecordDto>();
        if (byKind.TryGetValue(PaymentRecordKind.InvoicePayment, out var paymentIds))
            foreach (var r in await PaymentRecordsAsync(paymentIds, ct)) result[(r.Kind, r.Id)] = r;
        if (byKind.TryGetValue(PaymentRecordKind.InvoiceDue, out var invoiceIds))
            foreach (var r in await DueRecordsAsync(invoiceIds, ct)) result[(r.Kind, r.Id)] = r;
        if (byKind.TryGetValue(PaymentRecordKind.PaymentClaim, out var claimIds))
            foreach (var r in await ClaimRecordsAsync(claimIds, ct)) result[(r.Kind, r.Id)] = r;
        if (byKind.TryGetValue(PaymentRecordKind.PayoutItem, out var itemIds))
            foreach (var r in await PayoutRecordsAsync(itemIds, ct)) result[(r.Kind, r.Id)] = r;
        return keys.Where(k => result.ContainsKey((k.Kind, k.Id))).Select(k => result[(k.Kind, k.Id)]).ToList();
    }

    private async Task<Dictionary<Guid, string>> ClientNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<ClientAccount>().AsNoTracking().Where(c => list.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
    }

    private async Task<Dictionary<Guid, (string Name, string Email)>> UsersAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return (await db.Set<User>().AsNoTracking().Where(u => list.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName, u.Email }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => (u.DisplayName, u.Email));
    }

    private bool CanManageBilling => currentUser.HasPermission(Permissions.BillingManage);
    private bool CanRecordPayouts => currentUser.HasPermission(Permissions.PayoutsRecordPayment);

    public async Task<IReadOnlyList<PaymentRecordDto>> PaymentRecordsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var rows = await db.Set<Payment>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var invoiceIds = rows.Select(r => r.InvoiceId).Distinct().ToList();
        var invoices = await db.Set<Invoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Number, i.Balance, i.Status, i.ConcurrencyStamp }).ToDictionaryAsync(i => i.Id, ct);
        var clients = await ClientNamesAsync(rows.Select(r => r.ClientAccountId), ct);
        var users = await UsersAsync(rows.Where(r => r.RecordedByUserId.HasValue).Select(r => r.RecordedByUserId!.Value), ct);
        var withProof = await db.Set<PaymentProof>().AsNoTracking().Where(f => f.PaymentId != null && ids.Contains(f.PaymentId.Value))
            .Select(f => f.PaymentId!.Value).Distinct().ToListAsync(ct);
        return rows.Select(p =>
        {
            var inv = invoices.GetValueOrDefault(p.InvoiceId);
            var status = p.ReversedAt is null ? PaymentHubStatus.Paid
                : p.ReversalKind == PaymentReversalKind.Refund ? PaymentHubStatus.Refunded : PaymentHubStatus.Voided;
            var actions = new List<string>();
            if (CanManageBilling && p.ReversedAt is null && !p.IsReversal && inv is not null &&
                inv.Status is not (InvoiceStatus.Void or InvoiceStatus.WrittenOff))
            {
                actions.AddRange(new[] { PaymentActions.Edit, PaymentActions.Reverse, PaymentActions.UploadProof });
                // Four-eyes: whoever recorded the payment can't record its refund, so it isn't offered to them.
                if (p.RecordedByUserId != currentUser.IdOrNull) actions.Insert(2, PaymentActions.Refund);
            }
            return new PaymentRecordDto($"{PaymentRecordKind.InvoicePayment}:{p.Id}", PaymentRecordKind.InvoicePayment, p.Id, PaymentDirection.Incoming,
                status, p.ReversedAt is null ? "Recorded" : $"Reversed ({p.ReversalKind})",
                new PaymentPartyDto("client", p.ClientAccountId, clients.GetValueOrDefault(p.ClientAccountId, "—"), null),
                p.Amount, p.Currency, p.Method.ToString(), p.Reference, p.PaidOn, null, null, p.ReversedAt, p.ReversalReason,
                p.InvoiceId, inv?.Number, inv?.Balance, null, null, p.Notes,
                p.RecordedByUserId is { } u && users.TryGetValue(u, out var who) ? who.Name : null, 0, withProof.Contains(p.Id),
                p.ConcurrencyStamp, inv?.ConcurrencyStamp, p.CreatedAt, actions);
        }).ToList();
    }

    public async Task<IReadOnlyList<PaymentRecordDto>> DueRecordsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var rows = await db.Set<Invoice>().AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        var clients = await ClientNamesAsync(rows.Select(r => r.ClientAccountId), ct);
        var today = Today;
        return rows.Select(i =>
        {
            var open = Invoice.IsOpen(i.Status) && i.Balance > 0;
            var actions = new List<string>();
            if (CanManageBilling && open)
                actions.AddRange(new[] { PaymentActions.RecordPayment, PaymentActions.MarkPaidInFull, PaymentActions.SendReminder });
            return new PaymentRecordDto($"{PaymentRecordKind.InvoiceDue}:{i.Id}", PaymentRecordKind.InvoiceDue, i.Id, PaymentDirection.Incoming,
                open ? PaymentHubStatus.Pending : i.Status == InvoiceStatus.Paid ? PaymentHubStatus.Paid : PaymentHubStatus.Voided, i.Status.ToString(),
                new PaymentPartyDto("client", i.ClientAccountId, clients.GetValueOrDefault(i.ClientAccountId, "—"), null),
                i.Balance, i.Currency, null, i.Reference, i.DueDate ?? DateOnly.FromDateTime(i.CreatedAt), i.DueDate, null, null, null,
                i.Id, i.Number, i.Balance, null, null, null, null, InvoiceService.DaysOverdue(i, today), false,
                i.ConcurrencyStamp, i.ConcurrencyStamp, i.CreatedAt, actions);
        }).ToList();
    }

    public async Task<IReadOnlyList<PaymentRecordDto>> ClaimRecordsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var rows = await db.Set<PaymentClaim>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var invoiceIds = rows.Select(r => r.InvoiceId).Distinct().ToList();
        var invoices = await db.Set<Invoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Number, i.Balance, i.ConcurrencyStamp }).ToDictionaryAsync(i => i.Id, ct);
        var clients = await ClientNamesAsync(rows.Select(r => r.ClientAccountId), ct);
        var users = await UsersAsync(rows.Select(r => r.SubmittedByUserId), ct);
        var withProof = await db.Set<PaymentProof>().AsNoTracking().Where(f => f.PaymentClaimId != null && ids.Contains(f.PaymentClaimId.Value))
            .Select(f => f.PaymentClaimId!.Value).Distinct().ToListAsync(ct);
        return rows.Select(c =>
        {
            var inv = invoices.GetValueOrDefault(c.InvoiceId);
            var actions = CanManageBilling && c.Status == PaymentClaimStatus.Pending
                ? new List<string> { PaymentActions.ConfirmClaim, PaymentActions.RejectClaim }
                : new List<string>();
            var status = c.Status switch
            {
                PaymentClaimStatus.Pending => PaymentHubStatus.Pending,
                PaymentClaimStatus.Confirmed => PaymentHubStatus.Paid,
                _ => PaymentHubStatus.Voided,
            };
            return new PaymentRecordDto($"{PaymentRecordKind.PaymentClaim}:{c.Id}", PaymentRecordKind.PaymentClaim, c.Id, PaymentDirection.Incoming,
                status, $"Client report: {c.Status}",
                new PaymentPartyDto("client", c.ClientAccountId, clients.GetValueOrDefault(c.ClientAccountId, "—"), null),
                c.Amount, c.Currency, c.Method.ToString(), c.Reference, c.PaidOn, null, null, null, c.ReviewNote,
                c.InvoiceId, inv?.Number, inv?.Balance, null, null, c.Note,
                users.TryGetValue(c.SubmittedByUserId, out var who) ? who.Name : null, 0, withProof.Contains(c.Id),
                c.ConcurrencyStamp, inv?.ConcurrencyStamp, c.CreatedAt, actions);
        }).ToList();
    }

    public async Task<IReadOnlyList<PaymentRecordDto>> PayoutRecordsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var rows = await (from i in db.Set<PayoutItem>().AsNoTracking()
                          join b in db.Set<PayoutBatch>() on i.BatchId equals b.Id
                          where ids.Contains(i.Id)
                          select new { i, b.Reference, BatchStatus = b.Status, b.ScheduledPaymentDate, b.PreparedByUserId, b.FinalizedByUserId })
            .ToListAsync(ct);
        var me = currentUser.IdOrNull;
        var users = await UsersAsync(rows.Select(r => r.i.UserId).Concat(rows.Where(r => r.i.RecordedByUserId.HasValue).Select(r => r.i.RecordedByUserId!.Value)), ct);
        return rows.Select(r =>
        {
            var i = r.i;
            var status = i.Status switch
            {
                PayoutItemStatus.Pending => PaymentHubStatus.Scheduled,
                PayoutItemStatus.Held => r.BatchStatus == PayoutBatchStatus.Draft ? PaymentHubStatus.Scheduled : PaymentHubStatus.Voided,
                PayoutItemStatus.AwaitingPayment => PaymentHubStatus.Pending,
                PayoutItemStatus.Paid => PaymentHubStatus.Paid,
                PayoutItemStatus.Failed => PaymentHubStatus.Failed,
                _ => PaymentHubStatus.Voided,
            };
            var actions = CanRecordPayouts && i.Status == PayoutItemStatus.AwaitingPayment && r.BatchStatus == PayoutBatchStatus.Finalized
                ? new List<string> { PaymentActions.MarkPayoutPaid, PaymentActions.MarkPayoutFailed }
                : new List<string>();
            // Segregation of duties (PayoutPaymentService): the finalizer of a system-prepared batch can't record its payments.
            if (r.PreparedByUserId is null && r.FinalizedByUserId is not null && r.FinalizedByUserId == me) actions.Remove(PaymentActions.MarkPayoutPaid);
            var person = users.GetValueOrDefault(i.UserId);
            return new PaymentRecordDto($"{PaymentRecordKind.PayoutItem}:{i.Id}", PaymentRecordKind.PayoutItem, i.Id, PaymentDirection.Outgoing,
                status, i.Status.ToString(), new PaymentPartyDto("participant", i.UserId, person.Name ?? "—", person.Email),
                i.Amount, i.Currency, i.PaymentProvider, i.PaymentReference, r.ScheduledPaymentDate, null, i.PaidAt, null,
                i.FailureReason ?? i.HoldReason, null, null, null, i.BatchId, r.Reference, null,
                i.RecordedByUserId is { } u && users.TryGetValue(u, out var who) ? who.Name : null, 0, false,
                i.ConcurrencyStamp, null, i.CreatedAt, actions);
        }).ToList();
    }

    // ------------------------------------------------------------------ Detail

    public async Task<PaymentRecordDetailDto> DetailAsync(PaymentRecordKind kind, Guid id, CancellationToken ct)
    {
        RequireAny();
        PaymentRecordDto? record;
        Guid? invoiceId = null;
        switch (kind)
        {
            case PaymentRecordKind.InvoicePayment when CanIncoming:
                var payment = await (await scope.ApplyAsync(db.Set<Payment>().AsNoTracking(), p => p.ClientAccountId, ct))
                    .FirstOrDefaultAsync(p => p.Id == id && p.ReversalOfPaymentId == null, ct);
                record = payment is null ? null : (await PaymentRecordsAsync(new[] { id }, ct)).FirstOrDefault();
                invoiceId = payment?.InvoiceId;
                break;
            case PaymentRecordKind.InvoiceDue when CanIncoming:
                var invoice = await (await scope.ApplyAsync(db.Set<Invoice>().AsNoTracking(), i => i.ClientAccountId, ct))
                    .FirstOrDefaultAsync(i => i.Id == id && i.Status != InvoiceStatus.Draft, ct);
                record = invoice is null ? null : (await DueRecordsAsync(new[] { id }, ct)).FirstOrDefault();
                invoiceId = invoice?.Id;
                break;
            case PaymentRecordKind.PaymentClaim when CanIncoming:
                var claim = await (await scope.ApplyAsync(db.Set<PaymentClaim>().AsNoTracking(), c => c.ClientAccountId, ct))
                    .FirstOrDefaultAsync(c => c.Id == id, ct);
                record = claim is null ? null : (await ClaimRecordsAsync(new[] { id }, ct)).FirstOrDefault();
                invoiceId = claim?.InvoiceId;
                break;
            case PaymentRecordKind.PayoutItem when CanOutgoing:
                record = (await PayoutRecordsAsync(new[] { id }, ct)).FirstOrDefault();
                break;
            default:
                record = null;
                break;
        }
        if (record is null) throw DomainException.NotFound("Payment");

        var payments = new List<PaymentDto>();
        var reminders = new List<ReminderHistoryDto>();
        var proofs = new List<PaymentProofDto>();
        if (invoiceId is { } inv)
        {
            var rows = await db.Set<Payment>().AsNoTracking().Where(p => p.InvoiceId == inv).OrderBy(p => p.PaidOn).ThenBy(p => p.CreatedAt).ToListAsync(ct);
            var recorders = await UsersAsync(rows.Where(p => p.RecordedByUserId.HasValue).Select(p => p.RecordedByUserId!.Value), ct);
            var number = await db.Set<Invoice>().Where(i => i.Id == inv).Select(i => i.Number).FirstOrDefaultAsync(ct);
            payments = rows.Select(p => PaymentService.ToDto(p, number, record.Party.Name,
                p.RecordedByUserId is { } u && recorders.TryGetValue(u, out var who) ? who.Name : null)).ToList();
            var sent = await db.Set<InvoiceReminder>().AsNoTracking().Where(r => r.InvoiceId == inv).OrderByDescending(r => r.SentAt).ToListAsync(ct);
            var senders = await UsersAsync(sent.Where(r => r.SentByUserId.HasValue).Select(r => r.SentByUserId!.Value), ct);
            reminders = sent.Select(r => new ReminderHistoryDto(r.Kind, r.SentAt, r.IsManual,
                r.SentByUserId is { } u && senders.TryGetValue(u, out var who) ? who.Name : null)).ToList();
            var proofQuery = db.Set<PaymentProof>().AsNoTracking();
            proofQuery = kind switch
            {
                PaymentRecordKind.InvoicePayment => proofQuery.Where(f => f.PaymentId == id),
                PaymentRecordKind.PaymentClaim => proofQuery.Where(f => f.PaymentClaimId == id),
                _ => proofQuery.Where(f => f.InvoiceId == inv),
            };
            proofs = (await proofQuery.OrderBy(f => f.CreatedAt).ToListAsync(ct)).Select(PaymentProofService.ToDto).ToList();
        }

        var entityIds = new List<string> { id.ToString() };
        if (invoiceId is { } invId && kind != PaymentRecordKind.PaymentClaim) entityIds.Add(invId.ToString());
        var entityTypes = kind switch
        {
            PaymentRecordKind.PayoutItem => new[] { nameof(PayoutItem) },
            PaymentRecordKind.PaymentClaim => new[] { nameof(PaymentClaim) },
            PaymentRecordKind.InvoicePayment => new[] { nameof(Payment), nameof(Invoice) },
            _ => new[] { nameof(Invoice) },
        };
        var audits = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => entityTypes.Contains(a.EntityType) && entityIds.Contains(a.EntityId))
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(100).ToListAsync(ct);
        var actors = await UsersAsync(audits.Where(a => a.ActorUserId.HasValue).Select(a => a.ActorUserId!.Value), ct);
        var history = audits.Select(a => new PaymentHubHistoryDto(a.CreatedAt, a.Action,
            a.ActorUserId is { } u && actors.TryGetValue(u, out var who) ? who.Name : a.ActorType == "system" ? "System" : null,
            a.Reason, a.AfterJson)).ToList();
        return new PaymentRecordDetailDto(record, payments, proofs, reminders, history);
    }

    // ------------------------------------------------------------------ Summary

    public async Task<PaymentsSummaryDto> SummaryAsync(CancellationToken ct)
    {
        RequireAny();
        var today = Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        IncomingSummaryDto? incoming = null;
        OutgoingSummaryDto? outgoing = null;

        if (CanIncoming)
        {
            var payments = (await scope.ApplyAsync(db.Set<Payment>().AsNoTracking(), p => p.ClientAccountId, ct))
                .Where(p => p.PaidOn >= monthStart && p.PaidOn <= today);
            var received = await payments.GroupBy(p => p.Currency).Select(g => new { g.Key, Amount = g.Sum(p => p.Amount) }).ToListAsync(ct);
            var refunded = await payments.Where(p => p.ReversalOfPaymentId != null && p.ReversalKind == PaymentReversalKind.Refund)
                .GroupBy(p => p.Currency).Select(g => new { g.Key, Amount = g.Sum(p => p.Amount) }).ToListAsync(ct);
            var aging = await reports.AgingAsync(today, ct);
            var open = (await scope.ApplyAsync(db.Set<Invoice>().AsNoTracking(), i => i.ClientAccountId, ct))
                .Where(i => Invoice.OpenStatuses.Contains(i.Status) && i.Balance > 0);
            var pendingClaims = await (await scope.ApplyAsync(db.Set<PaymentClaim>().AsNoTracking(), c => c.ClientAccountId, ct))
                .CountAsync(c => c.Status == PaymentClaimStatus.Pending, ct);
            incoming = new IncomingSummaryDto(
                received.OrderBy(r => r.Key).Select(r => new CurrencyAmount(r.Key, Money.Round(r.Amount, r.Key))).ToList(),
                refunded.OrderBy(r => r.Key).Select(r => new CurrencyAmount(r.Key, Money.Round(-r.Amount, r.Key))).ToList(),
                aging.Totals,
                await open.CountAsync(ct),
                await open.CountAsync(i => i.DueDate < today, ct),
                pendingClaims);
        }

        if (CanOutgoing)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var monthStartUtc = monthStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var items = db.Set<PayoutItem>().AsNoTracking();
            var paid = await items.Where(i => i.Status == PayoutItemStatus.Paid && i.PaidAt >= monthStartUtc)
                .GroupBy(i => i.Currency).Select(g => new { g.Key, Amount = g.Sum(i => i.Amount) }).ToListAsync(ct);
            var due = await items.Where(i => i.Status == PayoutItemStatus.Pending || i.Status == PayoutItemStatus.AwaitingPayment)
                .GroupBy(i => i.Currency).Select(g => new { g.Key, Amount = g.Sum(i => i.Amount) }).ToListAsync(ct);
            var awaiting = await items.CountAsync(i => i.Status == PayoutItemStatus.AwaitingPayment, ct);
            var failed = await items.CountAsync(i => i.Status == PayoutItemStatus.Failed && i.UpdatedAt >= monthStartUtc, ct);
            var schedule = await schedules.GetActiveAsync(now, ct);
            var next = PayoutPeriodCalculator.PeriodContaining(schedule, now);
            // Test accounts are never paid (the planner always excludes them), so their earnings are not part of any cycle.
            var available = await db.Set<EarningEntry>().AsNoTracking()
                .Where(e => e.Status == EarningStatus.Approved && e.PayoutItemId == null && e.AvailableAt <= next.CutoffUtc)
                .Where(e => !db.Set<User>().Any(u => u.Id == e.UserId && u.IsTestAccount))
                .GroupBy(e => e.SettlementCurrency).Select(g => new { g.Key, Amount = g.Sum(e => e.SettlementAmount) }).ToListAsync(ct);
            outgoing = new OutgoingSummaryDto(
                paid.OrderBy(r => r.Key).Select(r => new CurrencyAmount(r.Key, Money.Round(r.Amount, r.Key))).ToList(),
                due.OrderBy(r => r.Key).Select(r => new CurrencyAmount(r.Key, Money.Round(r.Amount, r.Key))).ToList(),
                awaiting, failed,
                new NextPayoutCycleDto(next.PeriodKey, next.CutoffUtc, next.PaymentDate,
                    available.OrderBy(r => r.Key).Select(r => new CurrencyAmount(r.Key, Money.Round(r.Amount, r.Key))).ToList()));
        }
        return new PaymentsSummaryDto(today, monthStart, incoming, outgoing);
    }
}
