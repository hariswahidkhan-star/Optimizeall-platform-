using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Ledger;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Payouts;

/// <summary>
/// The participant's payout history. Only items of finalized/completed batches that are awaiting payment, paid or
/// failed are shown (draft batches are still under finance review; held/cancelled items released their earnings).
/// </summary>
[ApiController]
[Route("api/v1/me/payouts")]
[HasPermission(Permissions.ParticipantPortal)]
public sealed class MyPayoutsController(AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    private static readonly PayoutItemStatus[] VisibleStatuses =
        { PayoutItemStatus.AwaitingPayment, PayoutItemStatus.Paid, PayoutItemStatus.Failed };

    private IQueryable<MyPayoutRow> Rows() =>
        from i in db.Set<PayoutItem>().AsNoTracking()
        join b in db.Set<PayoutBatch>() on i.BatchId equals b.Id
        where i.UserId == currentUser.Id && VisibleStatuses.Contains(i.Status) &&
              (b.Status == PayoutBatchStatus.Finalized || b.Status == PayoutBatchStatus.Completed)
        select new MyPayoutRow
        {
            ItemId = i.Id, Reference = b.Reference, PeriodKey = b.PeriodKey, CutoffAt = b.CutoffAt,
            PaymentDate = b.ScheduledPaymentDate, Amount = i.Amount, Currency = i.Currency, Status = i.Status,
            PaidAt = i.PaidAt, PaymentReference = i.PaymentReference, EarningCount = i.EarningCount,
        };

    [HttpGet]
    public async Task<PagedResult<MyPayoutDto>> List([FromQuery] PageQuery query, CancellationToken ct)
    {
        var page = await Rows().OrderByDescending(r => r.CutoffAt).ThenBy(r => r.ItemId).ToPagedAsync(query, ct);
        return new PagedResult<MyPayoutDto>(page.Items.Select(r => r.ToDto()).ToList(), page.Total, page.Page, page.PageSize);
    }

    [HttpGet("{itemId:guid}")]
    public async Task<MyPayoutDetailDto> Get(Guid itemId, CancellationToken ct)
    {
        var row = await Rows().FirstOrDefaultAsync(r => r.ItemId == itemId, ct) ?? throw DomainException.NotFound("Payout");
        var entries = LedgerQueries.Rows(db, db.Set<EarningEntry>().AsNoTracking()
            .Where(e => e.PayoutItemId == itemId && e.UserId == currentUser.Id));
        var earnings = await entries.OrderBy(r => r.Entry.CreatedAt).ThenBy(r => r.Entry.Id).ToListAsync(ct);
        return new MyPayoutDetailDto(row.ToDto(), earnings.Select(e => e.ToEarningDto()).ToList());
    }

    private sealed class MyPayoutRow
    {
        public Guid ItemId { get; init; }
        public string Reference { get; init; } = string.Empty;
        public string PeriodKey { get; init; } = string.Empty;
        public DateTime CutoffAt { get; init; }
        public DateOnly PaymentDate { get; init; }
        public decimal Amount { get; init; }
        public string Currency { get; init; } = string.Empty;
        public PayoutItemStatus Status { get; init; }
        public DateTime? PaidAt { get; init; }
        public string? PaymentReference { get; init; }
        public int EarningCount { get; init; }

        public MyPayoutDto ToDto() => new(ItemId, Reference, PeriodKey, CutoffAt, PaymentDate, Amount, Currency, Status, PaidAt,
            FinanceGuards.MaskReference(PaymentReference), EarningCount);
    }
}
