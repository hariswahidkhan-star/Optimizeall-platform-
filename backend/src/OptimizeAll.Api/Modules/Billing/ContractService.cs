using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

/// <summary>Contracts/retainers: recurring lines invoiced every billing period (see <see cref="RecurringInvoiceJob"/>).</summary>
public sealed class ContractService(
    AppDbContext db,
    IDatabaseDialect dialect,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    LineBuilder lineBuilder,
    DocumentNumberService numbers,
    BillingSettingsService settingsService,
    InvoiceService invoices,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<ContractSummaryDto>> ListAsync(ContractQuery query, CancellationToken ct)
    {
        var contracts = await scope.ApplyAsync(db.Set<Contract>().AsNoTracking(), c => c.ClientAccountId, ct);
        if (query.Status is { } status) contracts = contracts.Where(c => c.Status == status);
        if (query.ClientAccountId is { } clientId) contracts = contracts.Where(c => c.ClientAccountId == clientId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = PagingExtensions.LikePattern(query.Search);
            var clientIds = db.Set<ClientAccount>().Where(c => EF.Functions.Like(c.Name, pattern, "\\")).Select(c => c.Id);
            contracts = contracts.Where(c => EF.Functions.Like(c.Title, pattern, "\\") || EF.Functions.Like(c.Number, pattern, "\\") ||
                                             clientIds.Contains(c.ClientAccountId));
        }
        var total = await contracts.CountAsync(ct);
        var rows = await contracts.Include(c => c.Lines).OrderByDescending(c => c.CreatedAt).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var names = await ClientNamesAsync(rows.Select(r => r.ClientAccountId), ct);
        return new PagedResult<ContractSummaryDto>(rows.Select(c => Summary(c, names.GetValueOrDefault(c.ClientAccountId, "—"))).ToList(),
            total, query.Page, query.PageSize);
    }

    public static ContractSummaryDto Summary(Contract c, string clientName)
    {
        var perPeriod = c.Lines.Sum(l => l.Total);
        return new ContractSummaryDto(c.Id, c.Number, c.Title, c.ClientAccountId, clientName, c.Status, c.Currency, c.BillingFrequency,
            c.StartDate, c.EndDate, perPeriod, Pricing.MonthlyEquivalent(perPeriod, Pricing.ToRecurrence(c.BillingFrequency), c.Currency),
            NextInvoiceDate(c), c.AutoRenew);
    }

    public static DateOnly? NextInvoiceDate(Contract c)
    {
        if (c.Status is ContractStatus.Cancelled or ContractStatus.Ended) return null;
        var next = BillingPeriods.PeriodStart(c.StartDate, c.BillingFrequency, c.NextPeriodIndex);
        return c.EndDate is null || next <= c.EndDate || c.AutoRenew ? next : null;
    }

    public async Task<ContractDto> GetAsync(Guid id, CancellationToken ct)
    {
        var contracts = await scope.ApplyAsync(db.Set<Contract>().AsNoTracking().Include(c => c.Lines), c => c.ClientAccountId, ct);
        var contract = await contracts.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Contract");
        var clientName = (await ClientNamesAsync(new[] { contract.ClientAccountId }, ct)).GetValueOrDefault(contract.ClientAccountId, "—");
        var lines = contract.Lines.OrderBy(l => l.Position).ToList();
        var totals = LineBuilder.TotalsOf(lines, contract.Currency);
        var invoiceRows = await db.Set<Invoice>().AsNoTracking().Where(i => i.ContractId == id).OrderByDescending(i => i.PeriodStart)
            .ThenByDescending(i => i.CreatedAt).Take(24).ToListAsync(ct);
        var proposalNumber = contract.ProposalId is { } pid
            ? await db.Set<Proposal>().AsNoTracking().Where(p => p.Id == pid).Select(p => p.Number).FirstOrDefaultAsync(ct)
            : null;
        return new ContractDto(contract.Id, contract.Number, contract.Title, contract.ClientAccountId, clientName, contract.Status,
            contract.Currency, contract.StartDate, contract.EndDate, contract.BillingFrequency, contract.AutoRenew, contract.RenewalTermMonths,
            contract.NoticePeriodDays, contract.PaymentTermsDays, contract.AutoIssueInvoices, contract.NextPeriodIndex, NextInvoiceDate(contract),
            contract.ProposalId, contract.ProposalVersion, proposalNumber, contract.Notes, lines.Select(LineBuilder.ToDto).ToList(),
            LineBuilder.ToDto(totals), Pricing.MonthlyEquivalent(totals.Total, Pricing.ToRecurrence(contract.BillingFrequency), contract.Currency),
            await invoices.SummariesAsync(invoiceRows, ct), contract.ActivatedAt, contract.CancelledAt, contract.CancelReason, contract.CreatedAt,
            contract.ConcurrencyStamp);
    }

    public async Task<ContractDto> CreateAsync(ContractRequest request, CancellationToken ct)
    {
        var clientId = request.ClientAccountId!.Value;
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == clientId, ct);
        var settings = await settingsService.GetAsync(ct);
        var currency = LineBuilder.NormalizeCurrency(request.Currency ?? client.Currency);
        ValidateDates(request.StartDate!.Value, request.EndDate);
        var built = await lineBuilder.BuildAsync(request.Lines, currency, () => new ContractLine(), ct);
        await numbers.EnsureAsync(db, DocumentNumberService.ContractSeries, ct);
        Contract contract;
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            contract = new Contract
            {
                Number = await numbers.NextAsync(db, DocumentNumberService.ContractSeries, settings.ContractPrefix, settings.NumberPadding, ct),
                Title = request.Title.Trim(),
                ClientAccountId = clientId,
                Currency = currency,
                StartDate = request.StartDate!.Value,
                EndDate = request.EndDate,
                BillingFrequency = request.BillingFrequency,
                AutoRenew = request.AutoRenew,
                RenewalTermMonths = request.RenewalTermMonths,
                NoticePeriodDays = request.NoticePeriodDays,
                PaymentTermsDays = request.PaymentTermsDays ?? settings.PaymentTermsDays,
                AutoIssueInvoices = request.AutoIssueInvoices,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                CreatedByUserId = currentUser.IdOrNull,
                Lines = built.Lines,
            };
            db.Set<Contract>().Add(contract);
            audit.Record("billing.contract_created", nameof(Contract), contract.Id,
                after: new { contract.Number, contract.ClientAccountId, contract.BillingFrequency, PerPeriod = built.Totals.Total, contract.Currency });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        return await GetAsync(contract.Id, ct);
    }

    public async Task<ContractDto> UpdateAsync(Guid id, ContractRequest request, CancellationToken ct)
    {
        await GetAsync(id, ct);
        var contract = await db.Set<Contract>().Include(c => c.Lines).FirstAsync(c => c.Id == id, ct);
        invoices.RequireStamp(contract, request.ConcurrencyStamp);
        if (contract.Status is ContractStatus.Cancelled or ContractStatus.Ended)
            throw DomainException.Conflict("billing.contract_closed", "Cancelled or ended contracts can't be edited.");
        if (request.ClientAccountId != contract.ClientAccountId)
            throw new DomainException("billing.contract_client_locked", "A contract's client can't be changed.");
        ValidateDates(request.StartDate!.Value, request.EndDate);
        var invoiced = contract.NextPeriodIndex > 0;
        if (invoiced && (request.StartDate != contract.StartDate || request.BillingFrequency != contract.BillingFrequency ||
                         (request.Currency is not null && LineBuilder.NormalizeCurrency(request.Currency) != contract.Currency)))
            throw DomainException.Conflict("billing.contract_invoiced",
                "Start date, billing frequency and currency can't change after the first invoice. Cancel and create a new contract instead.");
        var before = new { contract.Title, contract.EndDate, contract.AutoRenew, PerPeriod = contract.Lines.Sum(l => l.Total) };
        var currency = invoiced ? contract.Currency : LineBuilder.NormalizeCurrency(request.Currency ?? contract.Currency);
        var built = await lineBuilder.BuildAsync(request.Lines, currency, () => new ContractLine { ContractId = id }, ct);
        db.Set<ContractLine>().RemoveRange(contract.Lines);
        foreach (var line in built.Lines) db.Set<ContractLine>().Add(line);
        contract.Title = request.Title.Trim();
        contract.Currency = currency;
        contract.StartDate = request.StartDate!.Value;
        contract.EndDate = request.EndDate;
        contract.BillingFrequency = request.BillingFrequency;
        contract.AutoRenew = request.AutoRenew;
        contract.RenewalTermMonths = request.RenewalTermMonths;
        contract.NoticePeriodDays = request.NoticePeriodDays;
        contract.PaymentTermsDays = request.PaymentTermsDays ?? contract.PaymentTermsDays;
        contract.AutoIssueInvoices = request.AutoIssueInvoices;
        contract.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        audit.Record("billing.contract_updated", nameof(Contract), id, before,
            new { contract.Title, contract.EndDate, contract.AutoRenew, PerPeriod = built.Totals.Total });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return await GetAsync(id, ct);
    }

    public Task<ContractDto> ActivateAsync(Guid id, StampRequest request, CancellationToken ct) =>
        TransitionAsync(id, request.ConcurrencyStamp, new[] { ContractStatus.Draft }, ContractStatus.Active, null, ct);

    public Task<ContractDto> PauseAsync(Guid id, StampRequest request, CancellationToken ct) =>
        TransitionAsync(id, request.ConcurrencyStamp, new[] { ContractStatus.Active }, ContractStatus.Paused, null, ct);

    public Task<ContractDto> ResumeAsync(Guid id, StampRequest request, CancellationToken ct) =>
        TransitionAsync(id, request.ConcurrencyStamp, new[] { ContractStatus.Paused }, ContractStatus.Active, null, ct);

    public Task<ContractDto> CancelAsync(Guid id, CancelContractRequest request, CancellationToken ct)
    {
        if (!request.Confirm) throw new DomainException("request.confirm_required", "Confirm this action by sending \"confirm\": true.");
        return TransitionAsync(id, request.ConcurrencyStamp,
            new[] { ContractStatus.Draft, ContractStatus.Active, ContractStatus.Paused }, ContractStatus.Cancelled, request.Reason.Trim(), ct);
    }

    /// <summary>
    /// Deletes a draft contract that never billed anything. Contracts that were activated, invoiced or created by an
    /// accepted proposal are part of the billing record and are cancelled instead.
    /// </summary>
    public async Task DeleteDraftAsync(Guid id, Guid? stamp, CancellationToken ct)
    {
        await GetAsync(id, ct); // tenancy: another client's contract answers 404
        var contract = await db.Set<Contract>().Include(c => c.Lines).FirstAsync(c => c.Id == id, ct);
        invoices.RequireStamp(contract, stamp);
        if (contract.Status != ContractStatus.Draft || contract.ActivatedAt is not null)
            throw DomainException.Conflict("billing.contract_not_deletable", "Only a draft contract can be deleted. Cancel it instead.");
        if (contract.ProposalId is not null)
            throw DomainException.Conflict("billing.contract_from_proposal",
                "This contract was created when the client accepted a proposal. Cancel it instead so the acceptance record stays complete.");
        if (await db.Set<Invoice>().AnyAsync(i => i.ContractId == id, ct))
            throw DomainException.Conflict("billing.contract_invoiced", "This contract already has invoices. Cancel it instead.");
        db.Remove(contract);
        audit.Record("billing.contract_deleted", nameof(Contract), id, before: new { contract.Number, contract.Title, contract.ClientAccountId });
        await db.SaveChangesAsync(ct);
    }

    private async Task<ContractDto> TransitionAsync(Guid id, Guid? stamp, ContractStatus[] from, ContractStatus to, string? reason, CancellationToken ct)
    {
        await GetAsync(id, ct);
        var contract = await db.Set<Contract>().Include(c => c.Lines).FirstAsync(c => c.Id == id, ct);
        invoices.RequireStamp(contract, stamp);
        if (!from.Contains(contract.Status))
            throw DomainException.Conflict("billing.contract_state", $"A {contract.Status} contract can't become {to}.");
        if (to == ContractStatus.Active && contract.Lines.Count == 0)
            throw new DomainException("billing.no_lines", "Add at least one line before activating the contract.");
        var before = contract.Status;
        contract.Status = to;
        if (to == ContractStatus.Active) contract.ActivatedAt ??= Now;
        int? skippedFrom = null;
        if (before == ContractStatus.Paused && to == ContractStatus.Active)
        {
            // Periods that started while the contract was paused are not billed later: resume from the next period.
            var today = BillingDates.Today(clock);
            var index = contract.NextPeriodIndex;
            while (BillingPeriods.PeriodStart(contract.StartDate, contract.BillingFrequency, index) <= today) index++;
            if (index != contract.NextPeriodIndex)
            {
                skippedFrom = contract.NextPeriodIndex;
                contract.NextPeriodIndex = index;
            }
        }
        if (to == ContractStatus.Cancelled)
        {
            contract.CancelledAt = Now;
            contract.CancelReason = reason;
        }
        audit.Record($"billing.contract_{to.ToString().ToLowerInvariant()}", nameof(Contract), id,
            new { Status = before, NextPeriodIndex = skippedFrom ?? contract.NextPeriodIndex },
            new { Status = to, contract.NextPeriodIndex, SkippedPeriods = skippedFrom is { } f ? contract.NextPeriodIndex - f : 0 }, reason);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return await GetAsync(id, ct);
    }

    private static void ValidateDates(DateOnly start, DateOnly? end)
    {
        if (start.Year is < 2000 or > 2100)
            throw new DomainException("billing.invalid_start", "Enter a valid start date.", errors: LineBuilder.Errors("startDate", "Enter a valid start date."));
        if (end is { } e && e < start)
            throw new DomainException("billing.invalid_end", "The end date must be after the start date.",
                errors: LineBuilder.Errors("endDate", "The end date must be after the start date."));
    }

    private async Task<Dictionary<Guid, string>> ClientNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<ClientAccount>().AsNoTracking().Where(c => list.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
    }
}
