using System.Data;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Codes;

public interface ICodeSalesService
{
    // participant
    Task<IReadOnlyList<MyCodeDto>> MyCodesAsync(CancellationToken ct);
    Task<PagedResult<MyCodeSaleDto>> MySalesAsync(MyCodeSalesQuery query, CancellationToken ct);
    Task<MyCodeSaleDto> MySaleAsync(Guid id, CancellationToken ct);
    Task<MyCodeSaleDto> CreateAsync(CreateCodeSaleForm form, CancellationToken ct);
    Task<MyCodeSaleDto> UpdateAsync(Guid id, UpdateCodeSaleForm form, CancellationToken ct);
    Task<MyCodeSaleDto> WithdrawAsync(Guid id, WithdrawCodeSaleRequest request, CancellationToken ct);

    // staff
    Task<PagedResult<CodeSaleListItemDto>> ListAsync(CodeSaleQuery query, CancellationToken ct);
    Task<CodeSaleDto> GetAsync(Guid id, CancellationToken ct);
    Task<CodeSaleDto> AdminCreateAsync(Guid programId, AdminCreateSaleRequest request, CancellationToken ct);
    Task<CodeSaleDto> DecideAsync(Guid id, CodeSaleDecisionRequest request, CancellationToken ct);
    Task<BulkDecisionResultDto> BulkApproveAsync(BulkApproveSalesRequest request, CancellationToken ct);
    Task<CodeSaleDto> RefundAsync(Guid id, RefundCodeSaleRequest request, CancellationToken ct);
    Task<SalesImportResultDto> ImportReportAsync(Guid programId, IFormFile file, bool dryRun, CancellationToken ct);
    Task<(string FileName, IReadOnlyList<string> Header, IReadOnlyList<object?[]> Rows)> ExportAsync(CodeSaleQuery query, CancellationToken ct);
}

/// <summary>
/// Discount-code sales: participants report them, staff enter or import them, reviewers approve (commission to the ledger)
/// or reject them, finance refunds them (ledger reversal). See docs/DISCOUNT_CODES.md.
/// </summary>
public sealed partial class CodeSalesService(
    AppDbContext db,
    ICurrentUser currentUser,
    ICodePayoutService payouts,
    ILedgerWriter ledger,
    IExchangeRateProvider fx,
    IFileService files,
    IAuditLogger audit,
    INotificationService notifications,
    IPayoutReversalCoordinator payoutReversals,
    TimeProvider clock) : ICodeSalesService
{
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(10);
    public const string RefundHoldReason = "Code sale refund pending";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ shared validation

    /// <summary>Order values normalized and converted to the program currency at the order date (409 fx.rate_missing, never 0).</summary>
    private sealed record Money3(decimal Net, decimal Discount, string Currency, decimal Rate, Guid? RateId, decimal ProgramNet, decimal ProgramDiscount);

    private async Task<Money3> PriceOrderAsync(CodeProgram program, decimal net, decimal discount, string currencyRaw, DateTime orderDate, CancellationToken ct)
    {
        var currency = Money.Normalize(currencyRaw);
        if (!Money.IsSupported(currency))
            throw new DomainException("code_sale.currency_unsupported", $"Currency {currency} is not supported.",
                errors: new Dictionary<string, string[]> { ["currency"] = new[] { "Choose a supported currency." } });
        net = Money.Round(net, currency);
        discount = Money.Round(discount, currency);
        if (net <= 0)
            throw new DomainException("code_sale.invalid_amount", "The order value must be greater than 0.",
                errors: new Dictionary<string, string[]> { ["netAmount"] = new[] { "Must be greater than 0." } });
        ResolvedRate rate;
        try
        {
            rate = await fx.GetRateAsync(currency, program.Currency, orderDate, ct);
        }
        catch (DomainException ex) when (ex.Code == "fx.rate_missing")
        {
            throw DomainException.Conflict("fx.rate_missing",
                $"No exchange rate from {currency} to {program.Currency} on the order date, so the sale can't be priced. " +
                "Report it in the program currency or ask the team to add the rate.");
        }
        return new Money3(net, discount, currency, rate.Rate, rate.ExchangeRateId, Money.Convert(net, rate.Rate, program.Currency),
            Money.Convert(discount, rate.Rate, program.Currency));
    }

    private void ValidateOrderDate(CodeProgram program, DateTime orderDate, DateTime now, bool staff)
    {
        if (orderDate > now + MaxFutureSkew)
            throw new DomainException("code_sale.order_in_future", "The order date is in the future.",
                errors: new Dictionary<string, string[]> { ["orderDate"] = new[] { "Can't be in the future." } });
        if (!staff && orderDate < now.AddDays(-program.MaxOrderAgeDays))
            throw new DomainException("code_sale.order_too_old", $"Orders older than {program.MaxOrderAgeDays} days can't be reported.",
                errors: new Dictionary<string, string[]> { ["orderDate"] = new[] { $"At most {program.MaxOrderAgeDays} days ago." } });
        if (!program.IsWithinWindow(orderDate))
            throw DomainException.Conflict("code_sale.outside_program",
                $"The order date is outside the program ({program.StartsAt:yyyy-MM-dd} – {(program.EndsAt is { } e ? e.ToString("yyyy-MM-dd") : "open")}).");
    }

    private static void ValidateCodeOnDate(DiscountCode code, DateTime orderDate)
    {
        if (code.Status == DiscountCodeStatus.Retired)
            throw DomainException.Conflict("code_sale.code_retired", "This code has been retired.");
        if (code.Status == DiscountCodeStatus.Paused)
            throw DomainException.Conflict("code_sale.code_paused", "This code is paused; sales can't be reported with it right now.");
        if (code.ValidTo is { } to && orderDate > to)
            throw DomainException.Conflict("code_sale.code_expired", $"The code expired on {to:yyyy-MM-dd}, before this order was placed.");
        if (code.ValidFrom is { } from && orderDate < from)
            throw DomainException.Conflict("code_sale.code_not_yet_valid", $"The code was only valid from {from:yyyy-MM-dd}.");
    }

    /// <summary>
    /// The assignment through which <paramref name="userId"/> may claim an order placed at <paramref name="orderDate"/>: their
    /// personal assignment, or a shared one of a rate group they are a member of. 404 when they never held the code (no
    /// enumeration of other people's codes); 409 when they held it but not on that date.
    /// </summary>
    private async Task<DiscountCodeAssignment> CoveringAssignmentAsync(Guid codeId, Guid userId, DateTime orderDate, CancellationToken ct)
    {
        var groups = await db.Set<RateGroupMember>().AsNoTracking().Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync(ct);
        var mine = await db.Set<DiscountCodeAssignment>().AsNoTracking()
            .Where(a => a.CodeId == codeId && (a.UserId == userId || (a.GroupId != null && groups.Contains(a.GroupId.Value))))
            .OrderBy(a => a.ValidFrom).ThenBy(a => a.Id).ToListAsync(ct);
        if (mine.Count == 0) throw DomainException.NotFound("DiscountCode");
        var covering = mine.FirstOrDefault(a => a.Covers(orderDate));
        if (covering is not null) return covering;
        if (mine.All(a => a.ValidFrom > orderDate))
            throw DomainException.Conflict("code_sale.before_assignment",
                $"The order was placed before the code was assigned to you ({mine[0].ValidFrom:yyyy-MM-dd}).");
        throw DomainException.Conflict("code_sale.not_assigned_on_date", "The code wasn't assigned to you on the order date.");
    }

    /// <summary>404 unless the person holds (or held) the code personally or through one of their groups.</summary>
    private async Task EnsureEverHeldAsync(Guid codeId, Guid userId, CancellationToken ct)
    {
        var groups = await db.Set<RateGroupMember>().AsNoTracking().Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync(ct);
        if (!await db.Set<DiscountCodeAssignment>().AnyAsync(a => a.CodeId == codeId &&
                (a.UserId == userId || (a.GroupId != null && groups.Contains(a.GroupId.Value))), ct))
            throw DomainException.NotFound("DiscountCode");
    }

    private async Task EnsureOrderFreeAsync(Guid programId, string key, Guid? exceptSaleId, Guid userId, CancellationToken ct)
    {
        var other = await db.Set<CodeSale>().AsNoTracking()
            .Where(s => s.ProgramId == programId && s.ActiveOrderKey == key && s.Id != exceptSaleId)
            .Select(s => new { s.UserId, s.GroupId }).FirstOrDefaultAsync(ct);
        if (other is not null) throw DuplicateOrder(other.UserId == userId, other.GroupId is not null);
    }

    private static DomainException DuplicateOrder(bool mine, bool shared) =>
        new("code_sale.duplicate_order",
            mine ? "You already reported this order."
            : shared ? "This order was already reported by another member of your group (the first report counts)."
            : "This order has already been reported.",
            DomainErrorKind.Conflict, new Dictionary<string, string[]> { ["orderReference"] = new[] { "Already reported." } });

    private async Task SaveClaimAsync(bool shared, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (db.Dialect().IsUniqueViolation(ex))
        {
            // Final guard: a concurrent report of the same order won the unique (program, active order) index.
            throw DuplicateOrder(false, shared);
        }
    }

    private async Task EnsureActiveUserAsync(Guid userId, CancellationToken ct, bool lockRow = false)
    {
        if (lockRow) await db.Dialect().LockRowAsync(db, "users", userId, ct, RowLockMode.Share);
        var status = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(u => (UserStatus?)u.Status).FirstOrDefaultAsync(ct)
                     ?? throw DomainException.NotFound("User");
        if (status != UserStatus.Active)
            throw DomainException.Conflict("participant.not_active",
                $"The participant's account is {status.ToString().ToLowerInvariant()}, so no commission can be granted.");
    }

    private void AddEvent(Guid saleId, CodeSaleStatus? from, CodeSaleStatus to, string action, string? reason, DateTime at, Guid? actor = null) =>
        db.Set<CodeSaleEvent>().Add(new CodeSaleEvent
        {
            SaleId = saleId, FromStatus = from, ToStatus = to, Action = action, ActorUserId = actor ?? currentUser.IdOrNull,
            Reason = reason is null ? null : CodeQueries.Fit(reason, 1000), At = at,
        });

    private static string? RequireReason(string? raw, string prompt)
    {
        var reason = CodeQueries.Trimmed(raw);
        if (reason is null || reason.Length < 5)
            throw new DomainException("code_sale.reason_required", $"{prompt} (at least 5 characters).",
                errors: new Dictionary<string, string[]> { ["reason"] = new[] { "At least 5 characters." } });
        return reason;
    }

    private async Task<CodeProgram> LoadProgramAsync(Guid id, CancellationToken ct, bool tracked = false)
    {
        var q = db.Set<CodeProgram>().Include(p => p.Tiers).AsQueryable();
        if (!tracked) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("CodeProgram");
    }

    // ------------------------------------------------------------------ staff: list / get

    public async Task<PagedResult<CodeSaleListItemDto>> ListAsync(CodeSaleQuery query, CancellationToken ct)
    {
        var q = Filter(query);
        var total = await q.CountAsync(ct);
        var open = query.Status is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo;
        var ordered = open ? q.OrderBy(s => s.SubmittedAt).ThenBy(s => s.Id) : q.OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id);
        var page = await ordered.Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<CodeSaleListItemDto>(await ListDtosAsync(page, ct), total, query.Page, query.PageSize);
    }

    private IQueryable<CodeSale> Filter(CodeSaleQuery query)
    {
        var q = db.Set<CodeSale>().AsNoTracking();
        if (query.ProgramId is { } p) q = q.Where(s => s.ProgramId == p);
        if (query.Status is { } st) q = q.Where(s => s.Status == st);
        if (query.Verification is { } v) q = q.Where(s => s.Verification == v);
        if (query.Source is { } src) q = q.Where(s => s.Source == src);
        if (query.UserId is { } u) q = q.Where(s => s.UserId == u);
        if (query.CodeId is { } c) q = q.Where(s => s.CodeId == c);
        if (query.GroupId is { } g) q = q.Where(s => s.GroupId == g);
        if (query.From is { } from) q = q.Where(s => s.OrderDate >= from);
        if (query.To is { } to) q = q.Where(s => s.OrderDate <= to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            var upper = PagingExtensions.LikePattern(query.Search.ToUpperInvariant());
            q = q.Where(s => EF.Functions.Like(s.NormalizedOrderReference, upper, "\\") ||
                             db.Set<DiscountCode>().Any(c => c.Id == s.CodeId && EF.Functions.Like(c.NormalizedCode, upper, "\\")) ||
                             db.Set<User>().Any(x => x.Id == s.UserId && (EF.Functions.Like(x.DisplayName, like, "\\") || EF.Functions.Like(x.Email, like, "\\"))));
        }
        return q;
    }

    private async Task<List<CodeSaleListItemDto>> ListDtosAsync(IReadOnlyList<CodeSale> page, CancellationToken ct)
    {
        var programIds = page.Select(s => s.ProgramId).Distinct().ToList();
        var programs = await db.Set<CodeProgram>().AsNoTracking().Where(p => programIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new ProgramRefDto(p.Id, p.Name, p.BrandName, p.Currency), ct);
        var codeIds = page.Select(s => s.CodeId).Distinct().ToList();
        var codes = await db.Set<DiscountCode>().AsNoTracking().Where(c => codeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
        var userIds = page.Select(s => s.UserId).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.DisplayName, u.Status, u.IsTestAccount }, ct);
        return page.Select(s =>
        {
            var u = users.GetValueOrDefault(s.UserId);
            return new CodeSaleListItemDto(s.Id, programs[s.ProgramId], new NamedRefDto(s.CodeId, codes.GetValueOrDefault(s.CodeId, "?")),
                new UserRefDto(s.UserId, u?.DisplayName ?? "Unknown user"), s.GroupId is not null, s.OrderReference, s.OrderDate, s.NetAmount,
                s.DiscountAmount, s.Currency, s.ProgramNetAmount, s.Status, s.Source, s.Verification, s.SubmittedAt, s.EstimatedCommission,
                s.CommissionAmount, u?.IsTestAccount ?? false, u?.Status ?? UserStatus.Active, CannotDecide(s) is null, s.ConcurrencyStamp);
        }).ToList();
    }

    /// <summary>Why the caller can't decide the sale (four-eyes / self-review / state), or null.</summary>
    private string? CannotDecide(CodeSale s)
    {
        if (!currentUser.HasPermission(Permissions.SalesReview)) return "You don't have permission to review sales.";
        if (s.UserId == currentUser.IdOrNull) return "You can't review your own sale.";
        if (s.Source != CodeSaleSource.Participant && s.CreatedByUserId == currentUser.IdOrNull)
            return "You entered or imported this sale, so someone else must review it (four-eyes).";
        if (!s.IsOpen) return $"This sale is {s.Status}.";
        return null;
    }

    public async Task<CodeSaleDto> GetAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Set<CodeSale>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var program = await LoadProgramAsync(s.ProgramId, ct);
        var code = await db.Set<DiscountCode>().AsNoTracking().Where(c => c.Id == s.CodeId).Select(c => c.Code).FirstAsync(ct);
        var user = await db.Set<User>().AsNoTracking().Where(u => u.Id == s.UserId)
            .Select(u => new { u.DisplayName, u.Email, u.Status, u.IsTestAccount }).FirstAsync(ct);
        var events = await db.Set<CodeSaleEvent>().AsNoTracking().Where(e => e.SaleId == id).OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync(ct);
        var names = await db.UserNamesAsync(events.Select(e => e.ActorUserId).Append(s.CreatedByUserId).Append(s.DecidedByUserId), ct);
        var groups = await db.GroupNamesAsync(new[] { s.GroupId }, ct);
        var earnings = await db.Set<EarningEntry>().AsNoTracking().Where(e => e.CodeSaleId == id)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new CodeSaleEarningDto(e.Id, e.Type, e.Status, e.Amount, e.Currency, e.RateSourceLabel, e.CreatedAt)).ToListAsync(ct);
        return new CodeSaleDto(s.Id, new ProgramRefDto(program.Id, program.Name, program.BrandName, program.Currency), new NamedRefDto(s.CodeId, code),
            new UserRefDto(s.UserId, user.DisplayName), user.Email,
            s.GroupId is { } g ? new NamedRefDto(g, groups.GetValueOrDefault(g, "Unknown group")) : null, s.OrderReference, s.OrderDate, s.NetAmount,
            s.DiscountAmount, s.Currency, s.ExchangeRate, s.ProgramNetAmount, s.ProgramDiscountAmount, s.ProductNote,
            s.ProofFileId is { } f ? FileUrls.For(f) : null, s.Status, s.Source, names.Ref(s.CreatedByUserId), s.SubmittedAt, s.EstimatedCommission,
            s.CommissionAmount, s.PayoutSourceLabel, s.AppliedCaps is { Length: > 0 } caps ? caps.Split(',') : Array.Empty<string>(), s.PayoutVersion,
            s.Verification, s.VerificationNote, s.ReportedNetAmount, s.ReportedOrderDate, s.DecidedAt, names.Ref(s.DecidedByUserId), s.DecisionReason,
            s.RefundedAt, s.RefundReason, user.IsTestAccount, user.Status, CannotDecide(s) is null, CannotDecide(s),
            events.Select(e => new CodeSaleEventDto(e.FromStatus, e.ToStatus, e.Action, names.Ref(e.ActorUserId), e.Reason, e.At)).ToList(),
            earnings, s.ConcurrencyStamp);
    }

    // ------------------------------------------------------------------ staff: add a sale

    public async Task<CodeSaleDto> AdminCreateAsync(Guid programId, AdminCreateSaleRequest request, CancellationToken ct)
    {
        var now = Now;
        var me = currentUser.Id;
        var orderDate = request.OrderDate!.Value.UtcDateTime;
        var reason = request.Reason.Trim();
        CodeSale sale;
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(programId), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, programId, ct);
            var program = await LoadProgramAsync(programId, ct);
            if (program.Status == CodeProgramStatus.Archived) throw CodeProgramsService.Archived();
            var normalizedCode = DiscountCode.Normalize(request.Code);
            var code = await db.Set<DiscountCode>().AsNoTracking().FirstOrDefaultAsync(c => c.ProgramId == programId && c.NormalizedCode == normalizedCode, ct)
                       ?? throw new DomainException("code.unknown", $"The program has no code {request.Code.Trim()}.", DomainErrorKind.NotFound,
                           new Dictionary<string, string[]> { ["code"] = new[] { "Unknown code." } });
            ValidateOrderDate(program, orderDate, now, staff: true);
            ValidateCodeOnDate(code, orderDate);
            var userId = request.UserId;
            if (userId is null)
            {
                var holder = await db.Set<DiscountCodeAssignment>().AsNoTracking().Where(a => a.CodeId == code.Id)
                    .OrderBy(a => a.ValidFrom).ThenBy(a => a.Id).ToListAsync(ct);
                var covering = holder.FirstOrDefault(a => a.Covers(orderDate))
                               ?? throw DomainException.Conflict("code_sale.not_assigned_on_date", "The code wasn't assigned to anyone on the order date.");
                userId = covering.UserId ?? throw new DomainException("code_sale.user_required",
                    "This is a shared (group) code: choose which member made the sale.",
                    errors: new Dictionary<string, string[]> { ["userId"] = new[] { "Choose the group member." } });
            }
            var assignment = await CoveringAssignmentAsync(code.Id, userId.Value, orderDate, ct);
            var money = await PriceOrderAsync(program, request.NetAmount!.Value, request.DiscountAmount ?? 0m, request.Currency, orderDate, ct);
            var reference = request.OrderReference.Trim();
            var key = CodeSale.NormalizeOrderReference(reference);
            await EnsureOrderFreeAsync(programId, key, null, userId.Value, ct);
            sale = NewSale(program, code, assignment, userId.Value, reference, key, orderDate, money, CodeQueries.Trimmed(request.ProductNote), null,
                CodeSaleSource.Admin, now);
            sale.EstimatedCommission = await payouts.EstimateAsync(program, userId.Value, money.ProgramNet, null, ct);
            db.Set<CodeSale>().Add(sale);
            AddEvent(sale.Id, null, CodeSaleStatus.Pending, "entered", reason, now);
            audit.Record("code_sale.entered", nameof(CodeSale), sale.Id, after: SaleSnapshot(sale), reason: reason);
            await SaveClaimAsync(assignment.GroupId is not null, ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(sale.Id, ct);
    }

    private CodeSale NewSale(CodeProgram program, DiscountCode code, DiscountCodeAssignment assignment, Guid userId, string reference, string key,
        DateTime orderDate, Money3 money, string? note, Guid? proofId, CodeSaleSource source, DateTime now) => new()
    {
        ProgramId = program.Id, CodeId = code.Id, UserId = userId, AssignmentId = assignment.Id, GroupId = assignment.GroupId,
        OrderReference = reference, NormalizedOrderReference = key, ActiveOrderKey = key, OrderDate = orderDate, NetAmount = money.Net,
        DiscountAmount = money.Discount, Currency = money.Currency, ExchangeRate = money.Rate, ExchangeRateId = money.RateId,
        ProgramNetAmount = money.ProgramNet, ProgramDiscountAmount = money.ProgramDiscount, ProductNote = note, ProofFileId = proofId,
        Status = CodeSaleStatus.Pending, Source = source, CreatedByUserId = currentUser.Id, SubmittedAt = now,
    };

    private static object SaleSnapshot(CodeSale s) => new
    {
        s.ProgramId, s.CodeId, s.UserId, s.GroupId, s.OrderReference, s.OrderDate, s.NetAmount, s.DiscountAmount, s.Currency, s.ExchangeRate,
        s.ProgramNetAmount, Status = s.Status.ToString(), Source = s.Source.ToString(), s.EstimatedCommission,
    };

    // ------------------------------------------------------------------ review

    public async Task<CodeSaleDto> DecideAsync(Guid id, CodeSaleDecisionRequest request, CancellationToken ct)
    {
        var decision = request.Decision!.Value;
        var reason = decision == CodeSaleDecision.Approve
            ? CodeQueries.Trimmed(request.Reason)
            : RequireReason(request.Reason, decision == CodeSaleDecision.Reject ? "Explain the rejection to the participant" : "Say what information you need");
        var existing = await db.Set<CodeSale>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw NotFound();
        EnsureMayDecide(existing);
        var now = Now;
        var me = currentUser.Id;

        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(existing.ProgramId), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, existing.ProgramId, ct);
            var sale = await db.Set<CodeSale>().FirstAsync(s => s.Id == id, ct);
            if (!sale.IsOpen)
                throw DomainException.Conflict("code_sale.already_decided", sale.Status == CodeSaleStatus.Withdrawn
                    ? "The participant withdrew this sale."
                    : $"This sale was already decided ({sale.Status}).");
            if (decision == CodeSaleDecision.RequestInfo && sale.Status == CodeSaleStatus.NeedsInfo)
                throw DomainException.Conflict("code_sale.already_decided", "More information has already been requested.");
            ConcurrencyGuard.Apply(db, sale, request.ConcurrencyStamp!.Value);
            var program = await LoadProgramAsync(sale.ProgramId, ct);
            var from = sale.Status;
            string action;
            string notice;
            switch (decision)
            {
                case CodeSaleDecision.Approve:
                    await EnsureActiveUserAsync(sale.UserId, ct, lockRow: true);
                    var priced = await ApproveCoreAsync(program, sale, me, ct);
                    sale.Status = CodeSaleStatus.Approved;
                    action = "approved";
                    notice = priced.Result.Total > 0
                        ? $"Your {program.BrandName} sale {sale.OrderReference} was approved. You earned {Money.Format(priced.Result.Total, program.Currency)}."
                        : $"Your {program.BrandName} sale {sale.OrderReference} was approved (the program's limits were reached, so no commission this time).";
                    break;
                case CodeSaleDecision.Reject:
                    sale.Status = CodeSaleStatus.Rejected;
                    sale.ActiveOrderKey = null;
                    action = "rejected";
                    notice = $"Your {program.BrandName} sale {sale.OrderReference} was not approved: {reason}";
                    break;
                default:
                    sale.Status = CodeSaleStatus.NeedsInfo;
                    action = "info_requested";
                    notice = $"We need more information about your {program.BrandName} sale {sale.OrderReference}: {reason}";
                    break;
            }
            sale.DecidedAt = now;
            sale.DecidedByUserId = me;
            sale.DecisionReason = reason;
            var eventReason = reason;
            if (decision == CodeSaleDecision.Approve && sale.AppliedCaps is { Length: > 0 } caps)
                eventReason = $"{reason ?? "Approved"} (limits applied: {caps})";
            AddEvent(sale.Id, from, sale.Status, action, eventReason, now);
            audit.Record($"code_sale.{action}", nameof(CodeSale), sale.Id, new { Status = from.ToString() },
                new { Status = sale.Status.ToString(), sale.CommissionAmount, program.Currency, sale.AppliedCaps, sale.PayoutSourceLabel }, eventReason);
            await notifications.StageAsync(new NotificationRequest(sale.UserId, NotificationTypes.CodeSaleDecision,
                decision switch { CodeSaleDecision.Approve => "Sale approved", CodeSaleDecision.Reject => "Sale not approved", _ => "More information needed" },
                CodeQueries.Fit(notice, 2000), AppLinks.MyCodeSale(sale.Id), new[] { NotificationChannel.Email }), ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    /// <summary>Prices the sale and records its commission (and any tier bonus) through the ledger. Program lock held.</summary>
    private async Task<PricedCodeSale> ApproveCoreAsync(CodeProgram program, CodeSale sale, Guid actor, CancellationToken ct)
    {
        var priced = await payouts.PriceAsync(program, sale, ct);
        var source = priced.RateSource(program);
        foreach (var line in priced.Result.Lines)
        {
            var key = line.Kind == CodePayoutLineKind.Commission
                ? CodePayoutService.CommissionKey(sale.Id)
                : CodePayoutService.TierBonusKey(program.Id, sale.UserId, line.TierThreshold!.Value);
            var description = line.Kind == CodePayoutLineKind.Commission
                ? $"Sale commission — {program.BrandName} order {sale.OrderReference}"
                : $"{line.Label} — {program.BrandName}";
            await ledger.RecordAsync(new NewEarning(sale.UserId,
                line.Kind == CodePayoutLineKind.Commission ? EarningType.SaleCommission : EarningType.SaleTierBonus,
                line.Amount, program.Currency, key, description, RequiresApproval: false, CampaignId: program.CampaignId,
                CreatedByUserId: actor, RateSource: line.Kind == CodePayoutLineKind.Commission ? source : source with { Label = $"{program.Name} v{priced.PayoutVersion} · {line.Label}" },
                CodeProgramId: program.Id, CodeSaleId: sale.Id), ct);
        }
        sale.CommissionAmount = priced.Result.Total;
        sale.PayoutSourceLabel = CodeQueries.Fit(source.Label, 200);
        sale.AppliedCaps = priced.Result.AppliedCaps.Count > 0 ? string.Join(",", priced.Result.AppliedCaps) : null;
        sale.PayoutVersion = priced.PayoutVersion;
        return priced;
    }

    private void EnsureMayDecide(CodeSale s)
    {
        if (s.UserId == currentUser.Id)
            throw DomainException.Forbidden("code_sale.self_review", "You can't review your own sale.");
        if (s.Source != CodeSaleSource.Participant && s.CreatedByUserId == currentUser.Id)
            throw DomainException.Forbidden("code_sale.four_eyes", "You entered or imported this sale, so someone else must review it.");
    }

    public async Task<BulkDecisionResultDto> BulkApproveAsync(BulkApproveSalesRequest request, CancellationToken ct)
    {
        var items = new List<BulkDecisionItemDto>();
        foreach (var id in request.SaleIds.Distinct())
        {
            var sale = await db.Set<CodeSale>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
            if (sale is null) { items.Add(new BulkDecisionItemDto(id, false, "codesale.not_found", "Sale not found.")); continue; }
            if (request.OnlyMatched && sale.Verification != CodeSaleVerification.Matched)
            {
                items.Add(new BulkDecisionItemDto(id, false, "code_sale.not_matched", "Not matched by the brand's report; review it individually."));
                continue;
            }
            try
            {
                await DecideAsync(id, new CodeSaleDecisionRequest
                {
                    Decision = CodeSaleDecision.Approve, Reason = request.Reason ?? "Bulk approval of verified sales", ConcurrencyStamp = sale.ConcurrencyStamp,
                }, ct);
                items.Add(new BulkDecisionItemDto(id, true, null, null));
            }
            catch (DomainException ex)
            {
                db.ChangeTracker.Clear();
                items.Add(new BulkDecisionItemDto(id, false, ex.Code, ex.Message));
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                items.Add(new BulkDecisionItemDto(id, false, "concurrency.conflict", "Changed by someone else meanwhile."));
            }
        }
        var approved = items.Count(i => i.Succeeded);
        return new BulkDecisionResultDto(request.SaleIds.Count, approved, items.Count - approved, items);
    }

    // ------------------------------------------------------------------ refunds

    public async Task<CodeSaleDto> RefundAsync(Guid id, RefundCodeSaleRequest request, CancellationToken ct)
    {
        if (!request.Confirm) throw new DomainException("confirmation.required", "Confirm the refund by sending \"confirm\": true.");
        var reason = request.Reason.Trim();
        var existing = await db.Set<CodeSale>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw NotFound();
        if (existing.UserId == currentUser.Id)
            throw DomainException.Forbidden("code_sale.self_review", "You can't refund your own sale.");
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(existing.ProgramId), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, existing.ProgramId, ct);
            var sale = await db.Set<CodeSale>().FirstAsync(s => s.Id == id, ct);
            var program = await LoadProgramAsync(sale.ProgramId, ct);
            await RefundCoreAsync(program, sale, reason, "refunded", ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Approved → Refunded with its commission reversed (a clawback when already paid; a draft payout batch item is held
    /// first, a finalized batch refuses); Pending/NeedsInfo → Cancelled with no earnings. Tier bonuses already earned are kept.
    /// </summary>
    private async Task RefundCoreAsync(CodeProgram program, CodeSale sale, string reason, string action, CancellationToken ct)
    {
        var now = Now;
        var from = sale.Status;
        if (sale.Status is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo)
        {
            sale.Status = CodeSaleStatus.Cancelled;
            sale.RefundedAt = now;
            sale.RefundReason = CodeQueries.Fit(reason, 1000);
            AddEvent(sale.Id, from, sale.Status, "cancelled", reason, now);
            audit.Record("code_sale.cancelled", nameof(CodeSale), sale.Id, new { Status = from.ToString() }, new { Status = sale.Status.ToString() }, reason);
            return;
        }
        if (sale.Status != CodeSaleStatus.Approved)
            throw DomainException.Conflict("code_sale.not_refundable", $"Only approved or pending sales can be refunded (this one is {sale.Status}).");

        var candidates = await db.Set<EarningEntry>().AsNoTracking()
            .Where(e => e.CodeSaleId == sale.Id && e.Type == EarningType.SaleCommission && e.Status == EarningStatus.Scheduled && e.ReversedByEntryId == null)
            .Select(e => e.Id).ToListAsync(ct);
        var scheduled = await payoutReversals.FindScheduledAsync(candidates, ct);
        if (scheduled.FirstOrDefault(x => x.BatchStatus != PayoutBatchStatus.Draft) is { } frozen)
            throw DomainException.Conflict("ledger.in_payout_batch",
                $"This sale's commission is in finalized payout batch {frozen.BatchReference} awaiting payment. " +
                $"Ask finance to mark the participant's payout item ({frozen.ItemId}) failed first, then refund again.");
        foreach (var item in scheduled.GroupBy(x => x.ItemId).Select(g => g.First()))
            await payoutReversals.HoldForReversalAsync(item, RefundHoldReason, ct);

        var entries = await db.Set<EarningEntry>()
            .Where(e => e.CodeSaleId == sale.Id && e.Type == EarningType.SaleCommission && e.ReversedByEntryId == null &&
                        e.Status != EarningStatus.Reversed && e.Status != EarningStatus.Declined)
            .ToListAsync(ct);
        // Loaded after the hold above, so released entries are read in their current (Approved) state.
        foreach (var e in entries)
            await ledger.ReverseAsync(e, $"Order refunded: {reason}", currentUser.IdOrNull, ct);
        sale.Status = CodeSaleStatus.Refunded;
        sale.RefundedAt = now;
        sale.RefundReason = CodeQueries.Fit(reason, 1000);
        AddEvent(sale.Id, from, sale.Status, action, reason, now);
        audit.Record("code_sale.refunded", nameof(CodeSale), sale.Id, new { Status = from.ToString() },
            new { Status = sale.Status.ToString(), Reversed = entries.Select(e => new { e.Id, e.Amount, e.Currency, WasPaid = e.Status == EarningStatus.Paid }) }, reason);
        await notifications.StageAsync(new NotificationRequest(sale.UserId, NotificationTypes.CodeSaleDecision, "Sale refunded",
            CodeQueries.Fit($"The {program.BrandName} order {sale.OrderReference} was refunded, so its commission was reversed: {reason}", 2000),
            AppLinks.MyCodeSale(sale.Id), new[] { NotificationChannel.Email }), ct);
    }

    // ------------------------------------------------------------------ export

    public async Task<(string FileName, IReadOnlyList<string> Header, IReadOnlyList<object?[]> Rows)> ExportAsync(CodeSaleQuery query, CancellationToken ct)
    {
        var header = new[]
        {
            "saleId", "program", "brand", "code", "participantId", "participant", "email", "sharedCode", "orderReference", "orderDate", "netAmount",
            "discountAmount", "currency", "programNetAmount", "programCurrency", "status", "source", "verification", "submittedAt", "commission",
            "payoutSource", "appliedCaps", "decidedAt", "decisionReason", "refundedAt",
        };
        var rows = new List<object?[]>();
        var q = Filter(query).OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id);
        const int batch = 1000;
        for (var skip = 0; skip < 100_000; skip += batch)
        {
            var page = await q.Skip(skip).Take(batch).ToListAsync(ct);
            if (page.Count == 0) break;
            var programIds = page.Select(s => s.ProgramId).Distinct().ToList();
            var programs = await db.Set<CodeProgram>().AsNoTracking().Where(p => programIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            var codeIds = page.Select(s => s.CodeId).Distinct().ToList();
            var codes = await db.Set<DiscountCode>().AsNoTracking().Where(c => codeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            var names = await db.UserNamesAsync(page.Select(s => (Guid?)s.UserId), ct);
            foreach (var s in page)
            {
                var p = programs[s.ProgramId];
                var n = names.GetValueOrDefault(s.UserId);
                rows.Add(new object?[]
                {
                    s.Id, p.Name, p.BrandName, codes.GetValueOrDefault(s.CodeId), s.UserId, n.Name, n.Email, s.GroupId is not null, s.OrderReference,
                    s.OrderDate, s.NetAmount, s.DiscountAmount, s.Currency, s.ProgramNetAmount, p.Currency, s.Status.ToString(), s.Source.ToString(),
                    s.Verification.ToString(), s.SubmittedAt, s.CommissionAmount, s.PayoutSourceLabel, s.AppliedCaps, s.DecidedAt, s.DecisionReason, s.RefundedAt,
                });
            }
            if (page.Count < batch) break;
        }
        return ($"code-sales-{Now:yyyyMMdd}.csv", header, rows);
    }

    private static DomainException NotFound() => DomainException.NotFound("CodeSale");
}
