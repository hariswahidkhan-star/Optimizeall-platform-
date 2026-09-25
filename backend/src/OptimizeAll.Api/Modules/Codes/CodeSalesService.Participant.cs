using System.Data;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Rewards;

namespace OptimizeAll.Api.Modules.Codes;

// Participant side: "My codes" and the sales they report. Every query is scoped to the caller (IDOR: another person's
// code or sale answers 404, never 403).
public sealed partial class CodeSalesService
{
    public async Task<IReadOnlyList<MyCodeDto>> MyCodesAsync(CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = Now;
        var groups = await db.Set<RateGroupMember>().AsNoTracking().Where(m => m.UserId == me).Select(m => m.GroupId).ToListAsync(ct);
        var assignments = await (from a in db.Set<DiscountCodeAssignment>().AsNoTracking()
                                 join p in db.Set<CodeProgram>() on a.ProgramId equals p.Id
                                 where (a.UserId == me || (a.GroupId != null && groups.Contains(a.GroupId.Value))) &&
                                       (p.Status == CodeProgramStatus.Active || p.Status == CodeProgramStatus.Paused)
                                 orderby a.ValidFrom descending, a.Id
                                 select a).Take(500).ToListAsync(ct);
        if (assignments.Count == 0) return Array.Empty<MyCodeDto>();

        var programIds = assignments.Select(a => a.ProgramId).Distinct().ToList();
        var programs = await db.Set<CodeProgram>().AsNoTracking().Include(p => p.Tiers).Where(p => programIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        // Keep live assignments and those that ended recently enough for their orders to still be reported.
        assignments = assignments.Where(a => a.IsLive(now) || a.EffectiveTo is not { } end || end >= now.AddDays(-programs[a.ProgramId].MaxOrderAgeDays))
            .GroupBy(a => a.CodeId).Select(g => g.OrderByDescending(a => a.IsLive(now)).ThenByDescending(a => a.ValidFrom).First()).ToList();
        var codeIds = assignments.Select(a => a.CodeId).ToList();
        var codes = await db.Set<DiscountCode>().AsNoTracking().Where(c => codeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var sales = await db.Set<CodeSale>().AsNoTracking().Where(s => s.UserId == me && codeIds.Contains(s.CodeId))
            .Select(s => new { s.Id, s.CodeId, s.Status, s.ProgramNetAmount, s.ProgramDiscountAmount, s.EstimatedCommission }).ToListAsync(ct);
        var saleIds = sales.Select(s => s.Id).ToList();
        var earnings = await db.Set<EarningEntry>().AsNoTracking()
            .Where(e => e.UserId == me && e.CodeSaleId != null && saleIds.Contains(e.CodeSaleId.Value) && e.ReversedByEntryId == null &&
                        e.Status != EarningStatus.Reversed && e.Status != EarningStatus.Declined &&
                        (e.Type == EarningType.SaleCommission || e.Type == EarningType.SaleTierBonus))
            .Select(e => new { e.CodeSaleId, e.Status, e.Amount }).ToListAsync(ct);
        var saleCode = sales.ToDictionary(s => s.Id, s => s.CodeId);
        var user = await db.Set<User>().AsNoTracking().Where(u => u.Id == me).Select(u => new { u.ReferralCode }).FirstAsync(ct);

        var campaignIds = programs.Values.Where(p => p.CampaignId != null).Select(p => p.CampaignId!.Value).ToList();
        var clicks = campaignIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await (from l in db.Set<TrackingLink>().AsNoTracking()
                     join c in db.Set<TrackingClick>() on l.Id equals c.TrackingLinkId
                     where l.UserId == me && campaignIds.Contains(l.CampaignId) && !c.IsSuspectedBot
                     group c by l.CampaignId into g
                     select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var result = new List<MyCodeDto>();
        foreach (var a in assignments.OrderByDescending(a => a.IsLive(now)).ThenBy(a => programs[a.ProgramId].BrandName).ThenBy(a => codes[a.CodeId].NormalizedCode))
        {
            var p = programs[a.ProgramId];
            var code = codes[a.CodeId];
            var mine = sales.Where(s => s.CodeId == code.Id).ToList();
            var paidOrApproved = earnings.Where(e => saleCode.TryGetValue(e.CodeSaleId!.Value, out var c) && c == code.Id).ToList();
            var status = code.EffectiveStatus(now, p.EndsAt);
            string? inactive = !a.IsLive(now) ? "No longer assigned to you — you can still report orders from while you held it."
                : p.Status == CodeProgramStatus.Paused ? "The program is paused."
                : status == DiscountCodeStatus.Paused ? "The code is paused."
                : status == DiscountCodeStatus.Expired ? "The code has expired."
                : status == DiscountCodeStatus.Retired ? "The code was retired."
                : a.ValidFrom > now ? $"Starts {a.ValidFrom:yyyy-MM-dd}."
                : null;
            var utm = TrackingUrl.Utm("optimizeall", "affiliate", Slug(p.Name), user.ReferralCode, null).ToList();
            utm.Insert(0, new KeyValuePair<string, string?>("code", code.Code));
            result.Add(new MyCodeDto(code.Id, code.Code, p.Id, p.Name, p.BrandName, p.DiscountLabel, p.Description, p.Terms, p.StoreUrl,
                p.StoreUrl is { } store && TrackingUrl.IsValidDestination(store) ? TrackingUrl.MergeQuery(store, utm) : null,
                a.Target == CodeAssignmentTarget.Group, a.ValidFrom, a.EffectiveTo, inactive is null, inactive, p.Currency,
                await payouts.DescribeRateForAsync(p, me, ct),
                p.Tiers.OrderBy(t => t.ThresholdSales).Select(t => $"From {t.ThresholdSales} approved sales: {CodePayoutService.TierText(t, p.Currency)}").ToList(),
                p.RequireProof, p.MaxOrderAgeDays, p.StartsAt, p.EndsAt,
                new MyCodeStatsDto(
                    mine.Count(s => CodeSale.HoldsOrder(s.Status)),
                    mine.Count(s => s.Status is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo),
                    mine.Count(s => s.Status == CodeSaleStatus.Approved),
                    mine.Where(s => s.Status is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo or CodeSaleStatus.Approved)
                        .Sum(s => s.ProgramNetAmount + s.ProgramDiscountAmount),
                    mine.Where(s => s.Status is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo).Sum(s => s.EstimatedCommission ?? 0m),
                    paidOrApproved.Where(e => e.Status != EarningStatus.Paid).Sum(e => e.Amount),
                    paidOrApproved.Where(e => e.Status == EarningStatus.Paid).Sum(e => e.Amount),
                    p.CampaignId is { } cid ? clicks.GetValueOrDefault(cid) : null)));
        }
        return result;
    }

    private static string Slug(string name)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length == 0 ? "codes" : slug.Length > 60 ? slug[..60] : slug;
    }

    public async Task<PagedResult<MyCodeSaleDto>> MySalesAsync(MyCodeSalesQuery query, CancellationToken ct)
    {
        var me = currentUser.Id;
        var q = db.Set<CodeSale>().AsNoTracking().Where(s => s.UserId == me);
        if (query.Status is { } st) q = q.Where(s => s.Status == st);
        if (query.CodeId is { } c) q = q.Where(s => s.CodeId == c);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search.ToUpperInvariant());
            q = q.Where(s => EF.Functions.Like(s.NormalizedOrderReference, like, "\\"));
        }
        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<MyCodeSaleDto>(await MyDtosAsync(page, withEvents: false, ct), total, query.Page, query.PageSize);
    }

    public async Task<MyCodeSaleDto> MySaleAsync(Guid id, CancellationToken ct)
    {
        var me = currentUser.Id;
        var sale = await db.Set<CodeSale>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == me, ct) ?? throw NotFound();
        return (await MyDtosAsync(new[] { sale }, withEvents: true, ct))[0];
    }

    private async Task<List<MyCodeSaleDto>> MyDtosAsync(IReadOnlyList<CodeSale> sales, bool withEvents, CancellationToken ct)
    {
        var me = currentUser.Id;
        var programIds = sales.Select(s => s.ProgramId).Distinct().ToList();
        var programs = await db.Set<CodeProgram>().AsNoTracking().Where(p => programIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var codeIds = sales.Select(s => s.CodeId).Distinct().ToList();
        var codes = await db.Set<DiscountCode>().AsNoTracking().Where(c => codeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
        var ids = sales.Select(s => s.Id).ToList();
        var events = withEvents
            ? await db.Set<CodeSaleEvent>().AsNoTracking().Where(e => ids.Contains(e.SaleId)).OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync(ct)
            : new List<CodeSaleEvent>();
        var myName = withEvents ? await db.Set<User>().AsNoTracking().Where(u => u.Id == me).Select(u => u.DisplayName).FirstAsync(ct) : "";
        return sales.Select(s =>
        {
            var p = programs[s.ProgramId];
            // Staff names are not shown to participants.
            var timeline = events.Where(e => e.SaleId == s.Id)
                .Select(e => new CodeSaleEventDto(e.FromStatus, e.ToStatus, e.Action, e.ActorUserId == me ? new UserRefDto(me, myName) : null,
                    e.Action is "rejected" or "info_requested" or "refunded" or "cancelled" or "withdrawn" ? e.Reason : null, e.At)).ToList();
            return new MyCodeSaleDto(s.Id, p.Id, p.Name, p.BrandName, s.CodeId, codes.GetValueOrDefault(s.CodeId, "?"), s.OrderReference, s.OrderDate,
                s.NetAmount, s.DiscountAmount, s.Currency, s.ProductNote, s.ProofFileId is { } f ? FileUrls.For(f) : null, s.Status, s.Source,
                s.SubmittedAt, s.Status is CodeSaleStatus.Rejected or CodeSaleStatus.NeedsInfo ? s.DecisionReason : s.Status is CodeSaleStatus.Refunded or CodeSaleStatus.Cancelled ? s.RefundReason : null,
                s.EstimatedCommission, s.CommissionAmount, p.Currency, s.IsOpen, s.IsOpen, timeline, s.ConcurrencyStamp);
        }).ToList();
    }

    public async Task<MyCodeSaleDto> CreateAsync(CreateCodeSaleForm form, CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = Now;
        var orderDate = form.OrderDate!.Value.UtcDateTime;
        var code = await db.Set<DiscountCode>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == form.CodeId, ct) ?? throw DomainException.NotFound("DiscountCode");
        var program = await LoadProgramAsync(code.ProgramId, ct);
        // Participants never see draft or archived programs.
        if (program.Status is CodeProgramStatus.Draft or CodeProgramStatus.Archived) throw DomainException.NotFound("DiscountCode");
        await EnsureEverHeldAsync(code.Id, me, ct); // 404 for someone else's code before anything else is revealed
        if (program.Status == CodeProgramStatus.Paused)
            throw DomainException.Conflict("code_program.paused", "This program is paused; sales can't be reported right now.");
        await EnsureActiveUserAsync(me, ct);
        ValidateOrderDate(program, orderDate, now, staff: false);
        await CoveringAssignmentAsync(code.Id, me, orderDate, ct);
        ValidateCodeOnDate(code, orderDate);
        if (program.RequireProof && form.Proof is null)
            throw new DomainException("code_sale.proof_required", "Attach a screenshot of the order or receipt.",
                errors: new Dictionary<string, string[]> { ["proof"] = new[] { "Required for this program." } });
        var money = await PriceOrderAsync(program, form.NetAmount!.Value, form.DiscountAmount ?? 0m, form.Currency, orderDate, ct);
        var reference = form.OrderReference.Trim();
        var key = CodeSale.NormalizeOrderReference(reference);

        StoredFile? proof = null;
        CodeSale sale;
        try
        {
            await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(program.Id), CodeLocks.Timeout, ct))
            await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
            {
                await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, program.Id, ct);
                // Re-read under the lock: the code may have been paused or reassigned meanwhile.
                code = await db.Set<DiscountCode>().AsNoTracking().FirstAsync(c => c.Id == code.Id, ct);
                ValidateCodeOnDate(code, orderDate);
                var assignment = await CoveringAssignmentAsync(code.Id, me, orderDate, ct);
                await EnsureOrderFreeAsync(program.Id, key, null, me, ct);
                if (form.Proof is not null)
                    proof = await files.SaveImageAsync(form.Proof, FilePurpose.SaleProof, me, isPublic: false, ct);
                sale = NewSale(program, code, assignment, me, reference, key, orderDate, money, CodeQueries.Trimmed(form.ProductNote), proof?.Id,
                    CodeSaleSource.Participant, now);
                sale.EstimatedCommission = await payouts.EstimateAsync(program, me, money.ProgramNet, null, ct);
                db.Set<CodeSale>().Add(sale);
                AddEvent(sale.Id, null, CodeSaleStatus.Pending, "submitted", null, now);
                audit.Record("code_sale.submitted", nameof(CodeSale), sale.Id, after: SaleSnapshot(sale));
                await SaveClaimAsync(assignment.GroupId is not null, ct);
                await tx.CommitAsync(ct);
            }
        }
        catch
        {
            if (proof is not null) files.Discard(proof);
            throw;
        }
        return await MySaleAsync(sale.Id, ct);
    }

    public async Task<MyCodeSaleDto> UpdateAsync(Guid id, UpdateCodeSaleForm form, CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = Now;
        var existing = await db.Set<CodeSale>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == me, ct) ?? throw NotFound();
        if (!existing.IsOpen)
            throw DomainException.Conflict("code_sale.not_editable", $"Only pending sales can be edited (this one is {existing.Status}).");
        var program = await LoadProgramAsync(existing.ProgramId, ct);
        await EnsureActiveUserAsync(me, ct);
        var orderDate = form.OrderDate?.UtcDateTime ?? existing.OrderDate;
        ValidateOrderDate(program, orderDate, now, staff: false);
        if (program.RequireProof && form.Proof is null && existing.ProofFileId is null)
            throw new DomainException("code_sale.proof_required", "Attach a screenshot of the order or receipt.");
        var money = await PriceOrderAsync(program, form.NetAmount ?? existing.NetAmount, form.DiscountAmount ?? existing.DiscountAmount,
            form.Currency ?? existing.Currency, orderDate, ct);
        var reference = form.OrderReference?.Trim() ?? existing.OrderReference;
        var key = CodeSale.NormalizeOrderReference(reference);

        StoredFile? proof = null;
        try
        {
            await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(program.Id), CodeLocks.Timeout, ct))
            await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
            {
                await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, program.Id, ct);
                var sale = await db.Set<CodeSale>().FirstAsync(s => s.Id == id, ct);
                if (!sale.IsOpen)
                    throw DomainException.Conflict("code_sale.not_editable", $"Only pending sales can be edited (this one is {sale.Status}).");
                ConcurrencyGuard.Apply(db, sale, form.ConcurrencyStamp!.Value);
                var code = await db.Set<DiscountCode>().AsNoTracking().FirstAsync(c => c.Id == sale.CodeId, ct);
                ValidateCodeOnDate(code, orderDate);
                var assignment = await CoveringAssignmentAsync(code.Id, me, orderDate, ct);
                await EnsureOrderFreeAsync(program.Id, key, sale.Id, me, ct);
                if (form.Proof is not null)
                    proof = await files.SaveImageAsync(form.Proof, FilePurpose.SaleProof, me, isPublic: false, ct);
                var before = SaleSnapshot(sale);
                var changedFacts = key != sale.NormalizedOrderReference || money.Net != sale.NetAmount || money.Currency != sale.Currency || orderDate != sale.OrderDate;
                sale.OrderReference = reference;
                sale.NormalizedOrderReference = key;
                sale.ActiveOrderKey = key;
                sale.OrderDate = orderDate;
                sale.AssignmentId = assignment.Id;
                sale.GroupId = assignment.GroupId;
                sale.NetAmount = money.Net;
                sale.DiscountAmount = money.Discount;
                sale.Currency = money.Currency;
                sale.ExchangeRate = money.Rate;
                sale.ExchangeRateId = money.RateId;
                sale.ProgramNetAmount = money.ProgramNet;
                sale.ProgramDiscountAmount = money.ProgramDiscount;
                if (form.ProductNote is not null) sale.ProductNote = CodeQueries.Trimmed(form.ProductNote);
                if (proof is not null) sale.ProofFileId = proof.Id;
                if (changedFacts && sale.Verification is CodeSaleVerification.Matched or CodeSaleVerification.Mismatch)
                {
                    sale.Verification = CodeSaleVerification.Unverified;
                    sale.VerificationNote = "Edited after the brand's report was matched; re-import to verify again.";
                }
                sale.EstimatedCommission = await payouts.EstimateAsync(program, me, money.ProgramNet, sale.Id, ct);
                var from = sale.Status;
                sale.Status = CodeSaleStatus.Pending;
                AddEvent(sale.Id, from, CodeSaleStatus.Pending, from == CodeSaleStatus.NeedsInfo ? "resubmitted" : "edited", null, now);
                audit.Record("code_sale.edited", nameof(CodeSale), sale.Id, before, SaleSnapshot(sale));
                await SaveClaimAsync(assignment.GroupId is not null, ct);
                await tx.CommitAsync(ct);
            }
        }
        catch
        {
            if (proof is not null) files.Discard(proof);
            throw;
        }
        return await MySaleAsync(id, ct);
    }

    public async Task<MyCodeSaleDto> WithdrawAsync(Guid id, WithdrawCodeSaleRequest request, CancellationToken ct)
    {
        var me = currentUser.Id;
        var existing = await db.Set<CodeSale>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == me, ct) ?? throw NotFound();
        var now = Now;
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(existing.ProgramId), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, existing.ProgramId, ct);
            var sale = await db.Set<CodeSale>().FirstAsync(s => s.Id == id, ct);
            if (!sale.IsOpen)
                throw DomainException.Conflict("code_sale.not_withdrawable", $"Only pending sales can be withdrawn (this one is {sale.Status}).");
            var from = sale.Status;
            sale.Status = CodeSaleStatus.Withdrawn;
            sale.ActiveOrderKey = null;
            var reason = CodeQueries.Trimmed(request.Reason);
            AddEvent(sale.Id, from, CodeSaleStatus.Withdrawn, "withdrawn", reason, now);
            audit.Record("code_sale.withdrawn", nameof(CodeSale), sale.Id, new { Status = from.ToString() }, new { Status = "Withdrawn" }, reason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await MySaleAsync(id, ct);
    }
}
