using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Ledger;

/// <summary>Participant view of their own earnings.</summary>
[ApiController]
[Route("api/v1/me/earnings")]
[HasPermission(Permissions.ParticipantPortal)]
public sealed class MyEarningsController(AppDbContext db, IEarningsSummaryService summaries, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Balance buckets in the settlement currency plus the next payout estimate.</summary>
    [HttpGet("summary")]
    public Task<EarningsSummaryDto> Summary(CancellationToken ct) => summaries.GetAsync(currentUser.Id, ct);

    /// <summary>The participant's ledger entries, newest first.</summary>
    [HttpGet]
    public async Task<PagedResult<EarningDto>> List([FromQuery] MyEarningsQuery query, CancellationToken ct)
    {
        var entries = LedgerQueries.Filter(db.Set<EarningEntry>().AsNoTracking().Where(e => e.UserId == currentUser.Id), query);
        var page = await LedgerQueries.Rows(db, entries)
            .OrderByDescending(r => r.Entry.CreatedAt).ThenByDescending(r => r.Entry.Id)
            .ToPagedAsync(query, ct);
        return new PagedResult<EarningDto>(page.Items.Select(r => r.ToEarningDto()).ToList(), page.Total, page.Page, page.PageSize);
    }
}

/// <summary>Finance ledger: search, export, balances, adjustments and reversals.</summary>
[ApiController]
[Route("api/v1/finance")]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class FinanceLedgerController(
    AppDbContext db, IEarningsSummaryService summaries, LedgerAdminService service) : ControllerBase
{
    public const int MaxExportRows = 100_000;

    [HttpGet("ledger")]
    [HasPermission(Permissions.LedgerView)]
    public async Task<PagedResult<LedgerRowDto>> Ledger([FromQuery] LedgerQuery query, CancellationToken ct)
    {
        var page = await LedgerQueries.FinanceLedger(db, query).ToPagedAsync(query, ct);
        return new PagedResult<LedgerRowDto>(page.Items.Select(r => r.ToLedgerDto()).ToList(), page.Total, page.Page, page.PageSize);
    }

    /// <summary>CSV of the ledger with the same filters (max 100,000 rows, newest first).</summary>
    [HttpGet("ledger/export.csv")]
    [HasPermission(Permissions.LedgerView)]
    public async Task<IActionResult> Export([FromQuery] LedgerQuery query, CancellationToken ct)
    {
        var rows = await LedgerQueries.FinanceLedger(db, query).Take(MaxExportRows).ToListAsync(ct);
        return Csv.File($"ledger-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv", LedgerCsv.Header, rows.Select(LedgerCsv.Row));
    }

    [HttpGet("users/{userId:guid}/balance")]
    [HasPermission(Permissions.LedgerView)]
    public async Task<EarningsSummaryDto> Balance(Guid userId, CancellationToken ct)
    {
        if (!await db.Set<User>().AnyAsync(u => u.Id == userId, ct)) throw DomainException.NotFound("User");
        return await summaries.GetAsync(userId, ct);
    }

    /// <summary>Manual credit/debit. Idempotent by <c>requestId</c>: 201 when created, 200 when replayed.</summary>
    [HttpPost("adjustments")]
    [HasPermission(Permissions.LedgerAdjust)]
    public async Task<IActionResult> CreateAdjustment(CreateAdjustmentRequest request, CancellationToken ct)
    {
        var result = await service.CreateAdjustmentAsync(request, ct);
        return result.Created ? StatusCode(StatusCodes.Status201Created, result) : Ok(result);
    }

    [HttpPost("earnings/{id:guid}/reverse")]
    [HasPermission(Permissions.LedgerAdjust)]
    public Task<ReversalResultDto> Reverse(Guid id, ReverseEarningRequest request, CancellationToken ct) =>
        service.ReverseAsync(id, request, ct);
}

/// <summary>Queue of earnings that need a separate approval (bonuses, referral rewards).</summary>
[ApiController]
[Route("api/v1/finance/pending-earnings")]
[HasPermission(Permissions.RewardsApproveBonus)]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class PendingEarningsController(AppDbContext db, LedgerAdminService service) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<PendingEarningDto>> List([FromQuery] PendingEarningsQuery query, CancellationToken ct)
    {
        var entries = db.Set<EarningEntry>().AsNoTracking().Where(e => e.Status == EarningStatus.PendingApproval);
        if (query.Type is { } type) entries = entries.Where(e => e.Type == type);
        var rows = LedgerQueries.Rows(db, entries);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            rows = rows.Where(r => EF.Functions.Like(r.UserEmail, like, "\\") || EF.Functions.Like(r.UserDisplayName, like, "\\") ||
                                   EF.Functions.Like(r.Entry.Description, like, "\\"));
        }
        var page = await rows.OrderBy(r => r.Entry.CreatedAt).ThenBy(r => r.Entry.Id).ToPagedAsync(query, ct);

        var submissionIds = page.Items.Where(r => r.Entry.SubmissionId != null).Select(r => r.Entry.SubmissionId!.Value).Distinct().ToList();
        var submissions = await db.Set<Submission>().AsNoTracking()
            .Where(s => submissionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.LiveCheckStatus, s.LiveCheckDueAt, s.RiskScore })
            .ToDictionaryAsync(s => s.Id, ct);

        var items = page.Items.Select(r =>
        {
            var e = r.Entry;
            var s = e.SubmissionId is { } sid && submissions.TryGetValue(sid, out var found) ? found : null;
            return new PendingEarningDto(
                e.Id, e.CreatedAt, e.Type, e.Description, new UserRefDto(e.UserId, r.UserDisplayName, r.UserEmail),
                e.CampaignId is { } cid ? new CampaignRefDto(cid, r.CampaignTitle ?? string.Empty) : null,
                e.SubmissionId, e.ReferralId, e.Amount, e.Currency, e.SettlementAmount, e.SettlementCurrency, e.CreatedByUserId,
                s?.LiveCheckStatus == LiveCheckStatus.Pending, s?.LiveCheckDueAt, s?.RiskScore, e.ConcurrencyStamp);
        }).ToList();
        return new PagedResult<PendingEarningDto>(items, page.Total, page.Page, page.PageSize);
    }

    [HttpPost("{id:guid}/approve")]
    public Task<LedgerRowDto> Approve(Guid id, ApprovePendingEarningRequest request, CancellationToken ct) =>
        service.ApprovePendingAsync(id, request.ConcurrencyStamp!.Value, ct);

    [HttpPost("{id:guid}/decline")]
    public Task<LedgerRowDto> Decline(Guid id, DeclinePendingEarningRequest request, CancellationToken ct) =>
        service.DeclinePendingAsync(id, request, ct);
}

/// <summary>Immutable exchange-rate history used to convert earnings into the settlement currency.</summary>
[ApiController]
[Route("api/v1/finance/exchange-rates")]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class ExchangeRatesController(AppDbContext db, LedgerAdminService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.PayoutsView)]
    public async Task<PagedResult<ExchangeRateDto>> List([FromQuery] ExchangeRateQuery query, CancellationToken ct)
    {
        var rates = db.Set<ExchangeRate>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Base))
        {
            var b = Money.Normalize(query.Base);
            rates = rates.Where(r => r.BaseCurrency == b);
        }
        if (!string.IsNullOrWhiteSpace(query.Quote))
        {
            var q = Money.Normalize(query.Quote);
            rates = rates.Where(r => r.QuoteCurrency == q);
        }
        var page = await rates.OrderByDescending(r => r.EffectiveAt).ThenBy(r => r.BaseCurrency).ThenBy(r => r.QuoteCurrency)
            .ToPagedAsync(query, ct);
        return new PagedResult<ExchangeRateDto>(page.Items.Select(LedgerAdminService.ToDto).ToList(), page.Total, page.Page, page.PageSize);
    }

    [HttpPost]
    [HasPermission(Permissions.PayoutSettingsEdit)]
    public async Task<IActionResult> Create(CreateExchangeRateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await service.CreateExchangeRateAsync(request, ct));
}

public static class LedgerCsv
{
    public static readonly string[] Header =
    {
        "Earning ID", "Created at (UTC)", "User ID", "User email", "User name", "Type", "Status", "Description",
        "Campaign", "Submission ID", "Original amount", "Original currency", "Exchange rate", "Settlement amount",
        "Settlement currency", "Rule version", "Available at (UTC)", "Approved at (UTC)", "Payout item ID",
        "Paid at (UTC)", "Reverses earning ID", "Reversed by earning ID", "Reason",
    };

    public static IEnumerable<object?> Row(LedgerRow r)
    {
        var e = r.Entry;
        return new object?[]
        {
            e.Id, e.CreatedAt, e.UserId, r.UserEmail, r.UserDisplayName, e.Type, e.Status, e.Description,
            r.CampaignTitle, e.SubmissionId, e.Amount, e.Currency, e.ExchangeRate, e.SettlementAmount,
            e.SettlementCurrency, e.RewardRuleSetVersion, e.AvailableAt, e.ApprovedAt, e.PayoutItemId,
            e.PaidAt, e.ReversesEntryId, e.ReversedByEntryId, e.Reason,
        };
    }
}
