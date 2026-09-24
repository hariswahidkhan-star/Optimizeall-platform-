using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Referrals;

public sealed record ReferralProgramDto(bool Enabled, decimal RewardAmount, string Currency, string QualifyingAction, int QualifyWithinDays);

public sealed record ReferralStatsDto(int Registered, int Qualified, int Rewarded, int PendingReward, int Expired, int Rejected);

public sealed record MyReferralItemDto(
    Guid Id, string MaskedName, ReferralStatus Status, DateTime RegisteredAt, DateTime? QualifiedAt, DateTime QualifyBy,
    EarningStatus? RewardStatus);

public sealed record MyReferralsDto(
    string Code, string Link, ReferralProgramDto Program, ReferralStatsDto Stats, IReadOnlyList<MyReferralItemDto> Items);

public sealed record ReferralUserDto(Guid Id, string DisplayName, string Email);

public sealed record ReferralAdminDto(
    Guid Id,
    ReferralUserDto Referrer,
    ReferralUserDto Referred,
    string CodeUsed,
    ReferralStatus Status,
    ReferralQualifyingAction QualifyingAction,
    DateTime RegisteredAt,
    DateTime QualifyBy,
    DateTime? QualifiedAt,
    IReadOnlyList<string> FraudSignals,
    string? RejectionReason,
    Guid? EarningEntryId,
    EarningStatus? RewardStatus,
    decimal? RewardAmount,
    string? RewardCurrency);

public sealed class ReferralListQuery : PageQuery
{
    public ReferralStatus? Status { get; set; }

    /// <summary>true = only referrals with fraud signals; false = only without.</summary>
    public bool? Flagged { get; set; }
}

public sealed class RejectReferralRequest
{
    [Required, MinLength(3), MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

[ApiController]
public sealed class ReferralsController(
    AppDbContext db, ReferralService referrals, MarketingUrls urls, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>The caller's referral code, link, program terms, stats and referred participants (masked).</summary>
    [HttpGet("api/v1/me/referrals")]
    [HasPermission(Permissions.ParticipantPortal)]
    public async Task<MyReferralsDto> Mine(CancellationToken ct)
    {
        var userId = currentUser.Id;
        var code = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(u => u.ReferralCode).FirstAsync(ct);
        var program = await referrals.GetProgramAsync(ct);

        var rows = await (
            from r in db.Set<Referral>().AsNoTracking()
            join u in db.Set<User>() on r.ReferredUserId equals u.Id
            join e in db.Set<EarningEntry>() on r.EarningEntryId equals e.Id into ej
            from e in ej.DefaultIfEmpty()
            where r.ReferrerUserId == userId
            orderby r.CreatedAt descending
            select new
            {
                r.Id, u.DisplayName, r.Status, r.CreatedAt, r.QualifiedAt, r.QualifyBy,
                RewardStatus = e == null ? (EarningStatus?)null : e.Status,
            }).ToListAsync(ct);

        var stats = new ReferralStatsDto(
            Registered: rows.Count(r => r.Status == ReferralStatus.Registered),
            Qualified: rows.Count(r => r.Status == ReferralStatus.Qualified),
            Rewarded: rows.Count(r => r.RewardStatus is EarningStatus.Approved or EarningStatus.Scheduled or EarningStatus.Paid),
            PendingReward: rows.Count(r => r.RewardStatus == EarningStatus.PendingApproval),
            Expired: rows.Count(r => r.Status == ReferralStatus.Expired),
            Rejected: rows.Count(r => r.Status == ReferralStatus.Rejected));

        return new MyReferralsDto(
            code,
            urls.ReferralLink(code),
            new ReferralProgramDto(program.Enabled, program.ReferrerRewardAmount, program.Currency,
                ReferralService.ParseAction(program.QualifyingAction).ToString(), program.QualifyWithinDays),
            stats,
            rows.Take(200).Select(r => new MyReferralItemDto(r.Id, Mask(r.DisplayName), r.Status, r.CreatedAt, r.QualifiedAt,
                r.QualifyBy, r.RewardStatus)).ToList());
    }

    [HttpGet("api/v1/marketing/referrals")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<PagedResult<ReferralAdminDto>> List([FromQuery] ReferralListQuery query, CancellationToken ct)
    {
        var q =
            from r in db.Set<Referral>().AsNoTracking()
            join a in db.Set<User>() on r.ReferrerUserId equals a.Id
            join b in db.Set<User>() on r.ReferredUserId equals b.Id
            join e in db.Set<EarningEntry>() on r.EarningEntryId equals e.Id into ej
            from e in ej.DefaultIfEmpty()
            select new { r, a, b, e };

        if (query.Status is { } status) q = q.Where(x => x.r.Status == status);
        if (query.Flagged == true) q = q.Where(x => x.r.FraudSignals != null && x.r.FraudSignals != "");
        if (query.Flagged == false) q = q.Where(x => x.r.FraudSignals == null || x.r.FraudSignals == "");
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(x => EF.Functions.Like(x.a.DisplayName, like, "\\") || EF.Functions.Like(x.a.Email, like, "\\") ||
                             EF.Functions.Like(x.b.DisplayName, like, "\\") || EF.Functions.Like(x.b.Email, like, "\\") ||
                             EF.Functions.Like(x.r.CodeUsed, like, "\\"));
        }

        var page = await q.OrderByDescending(x => x.r.CreatedAt).ThenByDescending(x => x.r.Id)
            .Select(x => new
            {
                x.r.Id, ReferrerId = x.a.Id, ReferrerName = x.a.DisplayName, ReferrerEmail = x.a.Email,
                ReferredId = x.b.Id, ReferredName = x.b.DisplayName, ReferredEmail = x.b.Email,
                x.r.CodeUsed, x.r.Status, x.r.QualifyingAction, x.r.CreatedAt, x.r.QualifyBy, x.r.QualifiedAt,
                x.r.FraudSignals, x.r.RejectionReason, x.r.EarningEntryId,
                RewardStatus = x.e == null ? (EarningStatus?)null : x.e.Status,
                RewardAmount = x.e == null ? (decimal?)null : x.e.Amount,
                RewardCurrency = x.e == null ? null : x.e.Currency,
            })
            .ToPagedAsync(query, ct);

        return new PagedResult<ReferralAdminDto>(page.Items.Select(x => new ReferralAdminDto(
            x.Id, new ReferralUserDto(x.ReferrerId, x.ReferrerName, x.ReferrerEmail),
            new ReferralUserDto(x.ReferredId, x.ReferredName, x.ReferredEmail), x.CodeUsed, x.Status, x.QualifyingAction,
            x.CreatedAt, x.QualifyBy, x.QualifiedAt, ReferralFraudRules.Split(x.FraudSignals), x.RejectionReason,
            x.EarningEntryId, x.RewardStatus, x.RewardAmount, x.RewardCurrency)).ToList(), page.Total, page.Page, page.PageSize);
    }

    /// <summary>Rejects a referral; declines a pending reward or reverses an approved (unpaid) one. Audited.</summary>
    [DeniedWhileImpersonating] // cancels referral rewards
    [HttpPost("api/v1/marketing/referrals/{id:guid}/reject")]
    [HasPermission(Permissions.MarketingManage)]
    public Task<ReferralRejectResult> Reject(Guid id, RejectReferralRequest request, CancellationToken ct) =>
        referrals.RejectAsync(id, request.Reason, currentUser.Id, ct);

    public static string Mask(string displayName)
    {
        var trimmed = displayName.Trim();
        return trimmed.Length == 0 ? "***" : char.ToUpperInvariant(trimmed[0]) + "***";
    }
}
