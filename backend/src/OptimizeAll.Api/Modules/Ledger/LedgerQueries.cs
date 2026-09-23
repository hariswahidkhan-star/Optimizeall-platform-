using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Ledger;

/// <summary>Flat projection of an earning with its user and campaign (mapped to DTOs after paging).</summary>
public sealed class LedgerRow
{
    public EarningEntry Entry { get; init; } = null!;
    public string UserEmail { get; init; } = string.Empty;
    public string UserDisplayName { get; init; } = string.Empty;
    public string? CampaignTitle { get; init; }

    public EarningDto ToEarningDto() => new(
        Entry.Id, Entry.CreatedAt, Entry.Type, Entry.Status, Entry.Description, Campaign(), Entry.SubmissionId,
        Entry.Amount, Entry.Currency, Entry.ExchangeRate, Entry.SettlementAmount, Entry.SettlementCurrency,
        Entry.RewardRuleSetVersion, Entry.AvailableAt, Entry.PaidAt, Entry.Reason);

    public LedgerRowDto ToLedgerDto() => new(
        Entry.Id, Entry.CreatedAt, new UserRefDto(Entry.UserId, UserDisplayName, UserEmail), Entry.Type, Entry.Status,
        Entry.Description, Campaign(), Entry.SubmissionId, Entry.Amount, Entry.Currency, Entry.ExchangeRate,
        Entry.ExchangeRateId, Entry.SettlementAmount, Entry.SettlementCurrency, Entry.RewardRuleSetVersion,
        Entry.AvailableAt, Entry.ApprovedAt, Entry.ApprovedByUserId, Entry.CreatedByUserId, Entry.PayoutItemId,
        Entry.PaidAt, Entry.ReversedAt, Entry.ReversesEntryId, Entry.ReversedByEntryId, Entry.Reason, Entry.ConcurrencyStamp);

    private CampaignRefDto? Campaign() =>
        Entry.CampaignId is { } id ? new CampaignRefDto(id, CampaignTitle ?? string.Empty) : null;
}

public static class LedgerQueries
{
    public static IQueryable<LedgerRow> Rows(AppDbContext db, IQueryable<EarningEntry> entries) =>
        from e in entries
        join u in db.Set<User>() on e.UserId equals u.Id
        select new LedgerRow
        {
            Entry = e,
            UserEmail = u.Email,
            UserDisplayName = u.DisplayName,
            CampaignTitle = db.Set<Campaign>().Where(c => c.Id == e.CampaignId).Select(c => c.Title).FirstOrDefault(),
        };

    public static IQueryable<EarningEntry> Filter(IQueryable<EarningEntry> query, MyEarningsQuery filter)
    {
        if (filter.Type is { } type) query = query.Where(e => e.Type == type);
        if (filter.Status is { } status) query = query.Where(e => e.Status == status);
        if (filter.CampaignId is { } campaignId) query = query.Where(e => e.CampaignId == campaignId);
        if (filter.From is { } from) query = query.Where(e => e.CreatedAt >= from.ToUniversalTime());
        if (filter.To is { } to) query = query.Where(e => e.CreatedAt < to.ToUniversalTime());
        return query;
    }

    /// <summary>Finance ledger query: filters plus user and free-text search (email, name, description, exact id).</summary>
    public static IQueryable<LedgerRow> FinanceLedger(AppDbContext db, LedgerQuery filter)
    {
        var entries = Filter(db.Set<EarningEntry>().AsNoTracking(), filter);
        if (filter.UserId is { } userId) entries = entries.Where(e => e.UserId == userId);
        var rows = Rows(db, entries);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            if (Guid.TryParse(term, out var id))
            {
                rows = rows.Where(r => r.Entry.Id == id || r.Entry.UserId == id || r.Entry.SubmissionId == id || r.Entry.PayoutItemId == id);
            }
            else
            {
                var like = PagingExtensions.LikePattern(term);
                rows = rows.Where(r => EF.Functions.Like(r.UserEmail, like) || EF.Functions.Like(r.UserDisplayName, like) ||
                                       EF.Functions.Like(r.Entry.Description, like));
            }
        }
        return rows.OrderByDescending(r => r.Entry.CreatedAt).ThenByDescending(r => r.Entry.Id);
    }

    public static Task<LedgerRow?> SingleAsync(AppDbContext db, Guid earningId, CancellationToken ct) =>
        Rows(db, db.Set<EarningEntry>().AsNoTracking().Where(e => e.Id == earningId)).FirstOrDefaultAsync(ct);
}
