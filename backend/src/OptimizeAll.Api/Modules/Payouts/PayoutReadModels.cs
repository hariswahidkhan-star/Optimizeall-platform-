using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Ledger;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Domain.Support;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Payouts;

/// <summary>Read-side projections for the payout screens.</summary>
public static class PayoutReadModels
{
    public static async Task<PayoutBatchSummaryDto> SummaryAsync(AppDbContext db, Guid batchId, CancellationToken ct) =>
        (await SummariesAsync(db, db.Set<PayoutBatch>().AsNoTracking().Where(b => b.Id == batchId), ct)).FirstOrDefault()
        ?? throw DomainException.NotFound("PayoutBatch");

    public static async Task<IReadOnlyList<PayoutBatchSummaryDto>> SummariesAsync(AppDbContext db, IQueryable<PayoutBatch> batches, CancellationToken ct)
    {
        var list = await batches.ToListAsync(ct);
        var ids = list.Select(b => b.Id).ToList();
        var paid = await db.Set<PayoutItem>().AsNoTracking()
            .Where(i => ids.Contains(i.BatchId) && i.Status == PayoutItemStatus.Paid)
            .GroupBy(i => i.BatchId)
            .Select(g => new { BatchId = g.Key, Count = g.Count(), Amount = g.Sum(i => i.Amount) })
            .ToDictionaryAsync(x => x.BatchId, ct);
        var users = await UserRefsAsync(db, list.SelectMany(b => new[] { b.PreparedByUserId, b.FinalizedByUserId }), ct);
        return list.Select(b =>
        {
            paid.TryGetValue(b.Id, out var p);
            return new PayoutBatchSummaryDto(b.Id, b.Reference, b.PeriodKey, b.PeriodStart, b.CutoffAt, b.ScheduledPaymentDate,
                b.Status, b.ItemCount, b.TotalAmount, b.Currency, p?.Count ?? 0, p?.Amount ?? 0m,
                b.PreparedByUserId is { } pr ? users.GetValueOrDefault(pr) : null,
                b.FinalizedByUserId is { } fi ? users.GetValueOrDefault(fi) : null,
                b.CreatedAt, b.FinalizedAt, b.CompletedAt, b.CancelledAt, b.InstructionsExportedAt);
        }).ToList();
    }

    private static async Task<Dictionary<Guid, UserRefDto>> UserRefsAsync(AppDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        return await db.Set<User>().AsNoTracking().Where(u => set.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserRefDto(u.Id, u.DisplayName, u.Email), ct);
    }

    public static async Task<Dictionary<Guid, PayoutUserDto>> PayoutUsersAsync(AppDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var set = ids.Distinct().ToList();
        return await db.Set<User>().AsNoTracking().Where(u => set.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new PayoutUserDto(u.Id, u.DisplayName, u.Email, u.CountryCode), ct);
    }

    private static PayoutItemDto ToItemDto(PayoutItem i, PayoutUserDto user) =>
        new(i.Id, user, i.Amount, i.Currency, i.EarningCount, i.Status, i.PaymentProvider, i.DestinationHint, i.PaymentReference,
            i.PaidAt, i.HoldReason, i.FailureReason, i.ConcurrencyStamp);

    private static PayoutUserDto UnknownUser(Guid id) => new(id, "(deleted user)", string.Empty, string.Empty);

    public static async Task<PayoutItemDto> ItemAsync(AppDbContext db, Guid itemId, CancellationToken ct)
    {
        var item = await db.Set<PayoutItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct)
                   ?? throw DomainException.NotFound("PayoutItem");
        var users = await PayoutUsersAsync(db, new[] { item.UserId }, ct);
        return ToItemDto(item, users.GetValueOrDefault(item.UserId) ?? UnknownUser(item.UserId));
    }

    public static async Task<PayoutBatchDetailDto> DetailAsync(
        AppDbContext db, ISettingsService settings, Guid batchId, BatchItemsQuery query, CancellationToken ct)
    {
        var batch = await db.Set<PayoutBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct)
                    ?? throw DomainException.NotFound("PayoutBatch");
        var summary = await SummaryAsync(db, batchId, ct);

        var totals = await db.Set<PayoutItem>().AsNoTracking().Where(i => i.BatchId == batchId)
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(i => i.Amount) })
            .ToListAsync(ct);

        var allItems = await db.Set<PayoutItem>().AsNoTracking().Where(i => i.BatchId == batchId)
            .Select(i => new { i.Id, i.UserId, i.Status, i.HoldReason, i.Amount }).ToListAsync(ct);
        var userIds = allItems.Select(i => i.UserId).ToList();
        var exclusions = PayoutBatchService.ReadExclusions(batch.ExclusionsJson);
        var users = await PayoutUsersAsync(db, userIds.Concat(exclusions.Select(e => e.UserId)), ct);
        PayoutUserDto U(Guid id) => users.GetValueOrDefault(id) ?? UnknownUser(id);
        var itemByUser = allItems.GroupBy(i => i.UserId).ToDictionary(g => g.Key, g => g.First().Id);

        var missingDetails = allItems
            .Where(i => i.Status == PayoutItemStatus.Held && i.HoldReason == PayoutPlanner.MissingPayoutProfileReason)
            .Select(i => new UserWarningDto(U(i.UserId), i.Id, $"No payout details on file ({i.Amount:0.00} {batch.Currency} held)."))
            .ToList();

        var appeals = await db.Set<Appeal>().AsNoTracking()
            .Where(a => userIds.Contains(a.UserId) && a.Status == AppealStatus.Open)
            .GroupBy(a => a.UserId).Select(g => new { UserId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var openAppeals = appeals.Select(a => new UserWarningDto(U(a.UserId), itemByUser.GetValueOrDefault(a.UserId),
            $"{a.Count} open appeal(s).")).ToList();

        var disputes = await db.Set<SupportTicket>().AsNoTracking()
            .Where(t => userIds.Contains(t.UserId) && (t.Category == TicketCategory.Dispute || t.Category == TicketCategory.Payout) &&
                        t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .Select(t => new { t.UserId, t.Reference, t.Category }).ToListAsync(ct);
        var openDisputes = disputes.Select(t => new UserWarningDto(U(t.UserId), itemByUser.GetValueOrDefault(t.UserId),
            $"Open {t.Category.ToString().ToLowerInvariant()} ticket {t.Reference}.")).ToList();

        var threshold = await settings.GetAsync(SettingKeys.HighRiskThreshold, 50, ct);
        var itemIds = allItems.Select(i => i.Id).ToList();
        var linkedSubmissionIds = db.Set<EarningEntry>()
            .Where(e => e.PayoutItemId != null && itemIds.Contains(e.PayoutItemId.Value) && e.SubmissionId != null)
            .Select(e => e.SubmissionId!.Value);
        var risky = await db.Set<Submission>().AsNoTracking()
            .Where(s => userIds.Contains(s.UserId) && s.RiskScore >= threshold &&
                        ((s.SubmittedAt > batch.PeriodStart && s.SubmittedAt <= batch.CutoffAt) || linkedSubmissionIds.Contains(s.Id)))
            .Select(s => new { s.UserId, s.Id, s.RiskScore }).ToListAsync(ct);
        var highRisk = risky.Select(s => new UserWarningDto(U(s.UserId), itemByUser.GetValueOrDefault(s.UserId),
            $"Submission {s.Id} has risk score {s.RiskScore}.")).ToList();

        var exclusionDtos = exclusions.Select(e => new PayoutExclusionDto(U(e.UserId), e.Reason, e.Amount, e.EarningCount)).ToList();

        var items = db.Set<PayoutItem>().AsNoTracking().Where(i => i.BatchId == batchId);
        if (query.ItemStatus is { } status) items = items.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            var matching = db.Set<User>().Where(u => EF.Functions.Like(u.Email, like) || EF.Functions.Like(u.DisplayName, like)).Select(u => u.Id);
            items = items.Where(i => matching.Contains(i.UserId));
        }
        var page = await items.OrderByDescending(i => i.Amount).ThenBy(i => i.Id).ToPagedAsync(query, ct);
        var itemDtos = page.Items.Select(i => ToItemDto(i, U(i.UserId))).ToList();

        return new PayoutBatchDetailDto(
            summary, batch.ConcurrencyStamp, batch.Notes, batch.CancelReason,
            totals.OrderBy(t => t.Status).Select(t => new StatusTotalDto(t.Status, t.Count, t.Amount)).ToList(),
            new BatchWarningsDto(missingDetails, openAppeals, openDisputes, highRisk, threshold, exclusionDtos),
            new PagedResult<PayoutItemDto>(itemDtos, page.Total, page.Page, page.PageSize));
    }

    public static async Task<PayoutItemDetailDto> ItemDetailAsync(AppDbContext db, Guid batchId, Guid itemId, CancellationToken ct)
    {
        var batch = await db.Set<PayoutBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct)
                    ?? throw DomainException.NotFound("PayoutBatch");
        var item = await db.Set<PayoutItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.BatchId == batchId, ct)
                   ?? throw DomainException.NotFound("PayoutItem");
        var users = await PayoutUsersAsync(db, new[] { item.UserId }, ct);
        var earnings = await EarningsOfItemAsync(db, itemId, ct);
        var attempts = await db.Set<PaymentAttempt>().AsNoTracking().Where(a => a.PayoutItemId == itemId)
            .OrderBy(a => a.CreatedAt).ToListAsync(ct);
        return new PayoutItemDetailDto(batch.Id, batch.Reference, batch.PeriodKey, batch.Status,
            ToItemDto(item, users.GetValueOrDefault(item.UserId) ?? UnknownUser(item.UserId)),
            Money.Round(earnings.Sum(e => e.SettlementAmount), item.Currency),
            earnings,
            attempts.Select(a => new PaymentAttemptDto(a.Id, a.Provider, a.IdempotencyKey, a.Status, a.ProviderReference, a.Message,
                a.CreatedAt, a.UpdatedAt)).ToList());
    }

    public static async Task<List<ItemEarningDto>> EarningsOfItemAsync(AppDbContext db, Guid itemId, CancellationToken ct)
    {
        var rows = await (from e in db.Set<EarningEntry>().AsNoTracking()
                          where e.PayoutItemId == itemId
                          orderby e.CreatedAt, e.Id
                          select new
                          {
                              Entry = e,
                              Title = db.Set<Campaign>().Where(c => c.Id == e.CampaignId).Select(c => c.Title).FirstOrDefault(),
                          }).ToListAsync(ct);
        return rows.Select(r => new ItemEarningDto(r.Entry.Id, r.Entry.Type, r.Entry.Status, r.Entry.Description,
            r.Entry.CampaignId is { } cid ? new CampaignRefDto(cid, r.Title ?? string.Empty) : null, r.Entry.SubmissionId,
            r.Entry.Amount, r.Entry.Currency, r.Entry.ExchangeRate, r.Entry.SettlementAmount, r.Entry.SettlementCurrency,
            r.Entry.CreatedAt, r.Entry.AvailableAt)).ToList();
    }

    // ---------- Exports ----------

    public sealed record ExportRow(PayoutItem Item, PayoutUserDto User, PayoutProfile? Profile);

    public static async Task<(PayoutBatch Batch, List<ExportRow> Rows)> ExportRowsAsync(
        AppDbContext db, Guid batchId, Func<IQueryable<PayoutItem>, IQueryable<PayoutItem>>? filter, CancellationToken ct)
    {
        var batch = await db.Set<PayoutBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct)
                    ?? throw DomainException.NotFound("PayoutBatch");
        var query = db.Set<PayoutItem>().AsNoTracking().Where(i => i.BatchId == batchId);
        if (filter is not null) query = filter(query);
        var items = await query.OrderBy(i => i.Status).ThenByDescending(i => i.Amount).ThenBy(i => i.Id).ToListAsync(ct);
        var userIds = items.Select(i => i.UserId).ToList();
        var users = await PayoutUsersAsync(db, userIds, ct);
        var profiles = (await db.Set<PayoutProfile>().AsNoTracking().Where(p => userIds.Contains(p.UserId)).ToListAsync(ct))
            .GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.UpdatedAt).First());
        return (batch, items.Select(i => new ExportRow(i, users.GetValueOrDefault(i.UserId) ?? UnknownUser(i.UserId),
            profiles.GetValueOrDefault(i.UserId))).ToList());
    }

    public static readonly string[] ExportHeader =
    {
        "Batch reference", "Period", "Item ID", "Participant name", "Email", "Country", "Amount", "Currency",
        "Earning count", "Status", "Method", "Account holder", "Masked destination", "Payment reference", "Paid at (UTC)",
    };

    public static object?[] ExportCells(PayoutBatch batch, ExportRow r) => new object?[]
    {
        batch.Reference, batch.PeriodKey, r.Item.Id, r.User.DisplayName, r.User.Email, r.User.Country, r.Item.Amount,
        r.Item.Currency, r.Item.EarningCount, r.Item.Status, r.Profile?.Method, r.Profile?.AccountHolderName,
        r.Item.DestinationHint ?? r.Profile?.MaskedDestination, r.Item.PaymentReference, r.Item.PaidAt,
    };

    // ---------- Reconciliation ----------

    /// <summary>
    /// Repeated-payment checks that look beyond the single <c>EarningEntry.PayoutItemId</c> link, using the immutable
    /// <see cref="PayoutItemEarning"/> rows written when a payment is recorded:
    /// <list type="bullet">
    /// <item><c>duplicate_earning_payment</c> — an earning paid by more than one item (paid-earning rows plus the current
    /// link when that item is Paid).</item>
    /// <item><c>paid_earning_linked_elsewhere</c> — an earning paid by an item of this batch is now linked to another item.</item>
    /// <item><c>duplicate_payment_across_periods</c> — the same participant was paid the same earning set by an item of
    /// another batch.</item>
    /// </list>
    /// </summary>
    private static async Task<Dictionary<Guid, List<(string Type, string Message, Guid? EarningId)>>> RepeatedPaymentFindingsAsync(
        AppDbContext db, PayoutBatch batch, List<PayoutItem> items, List<(Guid EarningId, Guid ItemId)> linked, CancellationToken ct)
    {
        var findings = new Dictionary<Guid, List<(string Type, string Message, Guid? EarningId)>>();
        void Add(Guid itemId, string type, string message, Guid? earningId)
        {
            if (!findings.TryGetValue(itemId, out var list)) findings[itemId] = list = new();
            list.Add((type, message, earningId));
        }

        var itemIds = items.Select(i => i.Id).ToList();
        var itemById = items.ToDictionary(i => i.Id);
        var mine = await db.Set<PayoutItemEarning>().AsNoTracking().Where(p => itemIds.Contains(p.PayoutItemId)).ToListAsync(ct);
        var earningIds = mine.Select(p => p.EarningEntryId).Concat(linked.Select(l => l.EarningId)).Distinct().ToList();
        var others = await db.Set<PayoutItemEarning>().AsNoTracking()
            .Where(p => earningIds.Contains(p.EarningEntryId) && !itemIds.Contains(p.PayoutItemId)).ToListAsync(ct);
        var currentLinks = await db.Set<EarningEntry>().AsNoTracking().Where(e => earningIds.Contains(e.Id))
            .Select(e => new { e.Id, e.PayoutItemId }).ToDictionaryAsync(e => e.Id, e => e.PayoutItemId, ct);

        // Other paid items of the same participants (for the cross-period check) and every other item referenced above.
        var paidMine = items.Where(i => i.Status == PayoutItemStatus.Paid).ToList();
        var paidUserIds = paidMine.Select(i => i.UserId).Distinct().ToList();
        var otherPaidIds = await db.Set<PayoutItem>().AsNoTracking()
            .Where(i => paidUserIds.Contains(i.UserId) && i.Status == PayoutItemStatus.Paid && i.BatchId != batch.Id)
            .Select(i => i.Id).ToListAsync(ct);
        var otherItemIds = others.Select(o => o.PayoutItemId)
            .Concat(currentLinks.Values.Where(v => v != null).Select(v => v!.Value))
            .Concat(otherPaidIds)
            .Where(id => !itemById.ContainsKey(id)).Distinct().ToList();
        var otherItems = await (from i in db.Set<PayoutItem>().AsNoTracking()
                                join b in db.Set<PayoutBatch>() on i.BatchId equals b.Id
                                where otherItemIds.Contains(i.Id)
                                select new { i.Id, i.UserId, i.Status, b.Reference }).ToDictionaryAsync(x => x.Id, ct);

        bool IsPaid(Guid id) => itemById.TryGetValue(id, out var it)
            ? it.Status == PayoutItemStatus.Paid
            : otherItems.TryGetValue(id, out var o) && o.Status == PayoutItemStatus.Paid;
        string Describe(Guid id) => itemById.ContainsKey(id) ? $"item {id} (this batch)"
            : otherItems.TryGetValue(id, out var o) ? $"item {id} (batch {o.Reference})" : $"item {id}";

        // 1. An earning paid by more than one item.
        var payersByEarning = mine.Concat(others).ToLookup(p => p.EarningEntryId, p => p.PayoutItemId);
        foreach (var earningId in earningIds)
        {
            var payers = payersByEarning[earningId].ToHashSet();
            if (currentLinks.GetValueOrDefault(earningId) is { } link && IsPaid(link)) payers.Add(link);
            if (payers.Count < 2) continue;
            var list = string.Join(", ", payers.Select(Describe));
            foreach (var payer in payers.Where(itemById.ContainsKey))
                Add(payer, "duplicate_earning_payment", $"Earning {earningId} was paid by {payers.Count} payout items: {list}.", earningId);
        }

        // 2. An earning paid by an item of this batch that is now linked to another item (it may be paid again).
        foreach (var row in mine.Where(r => itemById[r.PayoutItemId].Status == PayoutItemStatus.Paid))
        {
            var link = currentLinks.GetValueOrDefault(row.EarningEntryId);
            if (link != row.PayoutItemId)
                Add(row.PayoutItemId, "paid_earning_linked_elsewhere",
                    $"Earning {row.EarningEntryId} was paid by this item but is now linked to {(link is { } l ? Describe(l) : "no item")}.",
                    row.EarningEntryId);
        }

        // 3. The same participant paid the same earning set in two batches (e.g. two periods).
        if (paidMine.Count > 0 && otherPaidIds.Count > 0)
        {
            var otherRows = await db.Set<PayoutItemEarning>().AsNoTracking().Where(p => otherPaidIds.Contains(p.PayoutItemId))
                .Select(p => new { p.PayoutItemId, p.EarningEntryId }).ToListAsync(ct);
            var otherLinks = await db.Set<EarningEntry>().AsNoTracking()
                .Where(e => e.PayoutItemId != null && otherPaidIds.Contains(e.PayoutItemId.Value))
                .Select(e => new { ItemId = e.PayoutItemId!.Value, e.Id }).ToListAsync(ct);
            // An item's paid set is its paid-earning rows, or (for payments recorded before those rows existed) its links.
            var paidRows = mine.Select(r => (Item: r.PayoutItemId, Earning: r.EarningEntryId))
                .Concat(otherRows.Select(r => (Item: r.PayoutItemId, Earning: r.EarningEntryId)))
                .ToLookup(r => r.Item, r => r.Earning);
            var links = linked.Select(l => (Item: l.ItemId, Earning: l.EarningId))
                .Concat(otherLinks.Select(l => (Item: l.ItemId, Earning: l.Id)))
                .ToLookup(l => l.Item, l => l.Earning);
            HashSet<Guid> SetOf(Guid itemId) =>
                paidRows[itemId].Any() ? paidRows[itemId].ToHashSet() : links[itemId].ToHashSet();
            var otherPaidByUser = otherPaidIds.Where(otherItems.ContainsKey).ToLookup(id => otherItems[id].UserId);
            foreach (var item in paidMine)
            {
                var set = SetOf(item.Id);
                if (set.Count == 0) continue;
                foreach (var otherId in otherPaidByUser[item.UserId])
                {
                    if (!set.SetEquals(SetOf(otherId))) continue;
                    Add(item.Id, "duplicate_payment_across_periods",
                        $"The same {set.Count} earning(s) were also paid to this participant by {Describe(otherId)}.", null);
                }
            }
        }
        return findings;
    }

    public static async Task<ReconciliationDto> ReconcileAsync(AppDbContext db, Guid batchId, CancellationToken ct)
    {
        var batch = await db.Set<PayoutBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct)
                    ?? throw DomainException.NotFound("PayoutBatch");
        var items = await db.Set<PayoutItem>().AsNoTracking().Where(i => i.BatchId == batchId).ToListAsync(ct);
        var itemIds = items.Select(i => i.Id).ToList();
        var earnings = await db.Set<EarningEntry>().AsNoTracking()
            .Where(e => e.PayoutItemId != null && itemIds.Contains(e.PayoutItemId.Value))
            .Select(e => new
            {
                e.Id, ItemId = e.PayoutItemId!.Value, e.Status, e.SettlementAmount, e.SettlementCurrency, e.ReferralId,
                e.ReversedByEntryId,
            })
            .ToListAsync(ct);
        var byItem = earnings.GroupBy(e => e.ItemId).ToDictionary(g => g.Key, g => g.ToList());
        var users = await PayoutUsersAsync(db, items.Select(i => i.UserId), ct);
        var discrepancies = new List<ReconciliationDiscrepancyDto>();
        var itemRows = new List<ReconciliationItemDto>();
        var repeated = await RepeatedPaymentFindingsAsync(db, batch, items, earnings.Select(e => (e.Id, e.ItemId)).ToList(), ct);

        // Rewards of rejected referrals that could not be reversed because they were in a finalized batch: finance must
        // review the item (mark it failed and reverse the reward, or reverse the paid reward to claw it back).
        var referralIds = earnings.Where(e => e.ReferralId != null && e.ReversedByEntryId == null && e.Status != EarningStatus.Reversed)
            .Select(e => e.ReferralId!.Value).Distinct().ToList();
        var rejectedReferrals = (await db.Set<Referral>().AsNoTracking()
            .Where(r => referralIds.Contains(r.Id) && r.Status == ReferralStatus.Rejected)
            .Select(r => r.Id).ToListAsync(ct)).ToHashSet();

        // Once a batch is finalized, held (and failed) items have released their earnings back to the ledger.
        var holdsReleased = batch.Status is PayoutBatchStatus.Finalized or PayoutBatchStatus.Completed;

        foreach (var item in items.OrderBy(i => i.Id))
        {
            var linked = byItem.GetValueOrDefault(item.Id) ?? new();
            var sum = Money.Round(linked.Sum(e => e.SettlementAmount), item.Currency);
            var before = discrepancies.Count;
            void Add(string type, string message, Guid? earningId = null, string severity = "error") =>
                discrepancies.Add(new ReconciliationDiscrepancyDto(type, severity, item.Id, item.UserId, earningId, message));

            switch (item.Status)
            {
                case PayoutItemStatus.Held when holdsReleased:
                case PayoutItemStatus.Failed or PayoutItemStatus.Cancelled:
                    if (linked.Count > 0)
                        Add("released_item_has_earnings", $"A {item.Status} item still has {linked.Count} linked earnings.");
                    break;
                // A draft item held for a reversal released its earnings immediately (nothing linked is expected).
                case PayoutItemStatus.Held when linked.Count == 0:
                    break;
                case PayoutItemStatus.Pending or PayoutItemStatus.Held or PayoutItemStatus.AwaitingPayment or PayoutItemStatus.Paid:
                    if (item.Amount != sum)
                        Add("item_amount_mismatch", $"Item amount {item.Amount:0.####} differs from the sum of its earnings {sum:0.####}.");
                    if (linked.Count != item.EarningCount)
                        Add("earning_count_mismatch", $"Item lists {item.EarningCount} earnings but {linked.Count} are linked.");
                    break;
            }
            foreach (var (type, message, earningId) in repeated.GetValueOrDefault(item.Id) ?? new())
                Add(type, message, earningId);
            foreach (var e in linked.Where(e => e.ReferralId is { } rid && rejectedReferrals.Contains(rid) &&
                                                e.ReversedByEntryId == null && e.Status != EarningStatus.Reversed))
                Add("pending_reversal",
                    $"Earning {e.Id} is the reward of a rejected referral and still has to be reversed (PendingReversal). " +
                    (item.Status == PayoutItemStatus.Paid
                        ? "Reverse the paid earning to claw it back."
                        : "Mark the item failed, then reverse the earning (or pay it and reverse it afterwards)."),
                    e.Id, "warning");

            var expectedStatus = item.Status == PayoutItemStatus.Paid ? EarningStatus.Paid : EarningStatus.Scheduled;
            foreach (var e in linked)
            {
                if (item.Status == PayoutItemStatus.Paid && e.Status != EarningStatus.Paid)
                    Add("paid_item_unpaid_earning", $"Item is Paid but earning {e.Id} is {e.Status}.", e.Id);
                else if (item.Status != PayoutItemStatus.Paid && e.Status == EarningStatus.Paid)
                    Add("unpaid_item_paid_earning", $"Item is {item.Status} but earning {e.Id} is Paid.", e.Id);
                else if (e.Status != expectedStatus && item.Status is not (PayoutItemStatus.Failed or PayoutItemStatus.Cancelled))
                    Add("earning_status_mismatch", $"Earning {e.Id} is {e.Status}; expected {expectedStatus}.", e.Id);
                if (e.SettlementCurrency != item.Currency)
                    Add("currency_mismatch", $"Earning {e.Id} is settled in {e.SettlementCurrency}, item in {item.Currency}.", e.Id);
            }

            itemRows.Add(new ReconciliationItemDto(item.Id, users.GetValueOrDefault(item.UserId) ?? UnknownUser(item.UserId),
                item.Status, item.Amount, sum, item.EarningCount, linked.Count, item.PaymentReference, item.PaidAt,
                discrepancies.Count == before));
        }

        // No participant may be paid twice for the same period across batches.
        var paidUsers = items.Where(i => i.Status == PayoutItemStatus.Paid).Select(i => i.UserId).ToList();
        var paidElsewhere = await (from i in db.Set<PayoutItem>().AsNoTracking()
                                   join b in db.Set<PayoutBatch>() on i.BatchId equals b.Id
                                   where b.Id != batchId && b.PeriodKey == batch.PeriodKey && b.Currency == batch.Currency &&
                                         i.Status == PayoutItemStatus.Paid && paidUsers.Contains(i.UserId)
                                   select new { i.UserId, b.Reference }).ToListAsync(ct);
        foreach (var dup in paidElsewhere)
        {
            var mine = items.First(i => i.UserId == dup.UserId && i.Status == PayoutItemStatus.Paid);
            discrepancies.Add(new ReconciliationDiscrepancyDto("duplicate_period_payment", "error", mine.Id, dup.UserId, null,
                $"Participant was also paid for period {batch.PeriodKey} in batch {dup.Reference}."));
        }

        // The same payment reference on different items is suspicious (possible double entry).
        var references = items.Where(i => i.PaymentReference != null).Select(i => i.PaymentReference!).Distinct().ToList();
        var shared = await db.Set<PayoutItem>().AsNoTracking()
            .Where(i => i.PaymentReference != null && references.Contains(i.PaymentReference))
            .GroupBy(i => i.PaymentReference!)
            .Select(g => new { Reference = g.Key, Count = g.Count() })
            .Where(g => g.Count > 1).ToListAsync(ct);
        foreach (var s in shared)
        {
            foreach (var item in items.Where(i => i.PaymentReference == s.Reference))
                discrepancies.Add(new ReconciliationDiscrepancyDto("duplicate_payment_reference", "warning", item.Id, item.UserId, null,
                    $"Payment reference ending {FinanceGuards.MaskReference(s.Reference)} is used on {s.Count} payout items."));
        }

        decimal SumOf(params PayoutItemStatus[] statuses) =>
            items.Where(i => statuses.Contains(i.Status)).Sum(i => i.Amount);
        var expected = SumOf(PayoutItemStatus.Pending, PayoutItemStatus.AwaitingPayment, PayoutItemStatus.Paid, PayoutItemStatus.Failed);
        if (batch.Status != PayoutBatchStatus.Cancelled && batch.TotalAmount != expected)
            discrepancies.Add(new ReconciliationDiscrepancyDto("batch_total_mismatch", "error", null, null, null,
                $"Batch total {batch.TotalAmount:0.####} differs from the sum of payable items {expected:0.####}."));

        return new ReconciliationDto(batch.Id, batch.Reference, batch.PeriodKey, batch.Status, batch.Currency,
            expected,
            SumOf(PayoutItemStatus.Paid),
            SumOf(PayoutItemStatus.AwaitingPayment, PayoutItemStatus.Pending),
            SumOf(PayoutItemStatus.Failed),
            SumOf(PayoutItemStatus.Held),
            SumOf(PayoutItemStatus.Cancelled),
            items.Count(i => i.Status == PayoutItemStatus.Paid),
            items.Count(i => i.Status == PayoutItemStatus.AwaitingPayment),
            discrepancies.All(d => d.Severity != "error"),
            discrepancies,
            itemRows);
    }
}
