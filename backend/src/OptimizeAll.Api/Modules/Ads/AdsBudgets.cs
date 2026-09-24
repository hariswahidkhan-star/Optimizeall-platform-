using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Ads;

public sealed class BudgetInput
{
    [Required] public Guid? ClientAccountId { get; set; }

    /// <summary>Any date in the month; stored as the first of the month.</summary>
    [Required] public DateOnly? Month { get; set; }
    public AdPlatform? Platform { get; set; }
    public Guid? CampaignId { get; set; }
    [Range(0.01, 1_000_000_000)] public decimal Amount { get; set; }
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = "USD";
    [Range(1.0, 5.0)] public decimal OverPacingThreshold { get; set; } = 1.15m;
    [Range(0.0, 1.0)] public decimal UnderPacingThreshold { get; set; } = 0.85m;
    [Range(typeof(decimal), "0", "1000000")] public decimal? TargetCpa { get; set; }
    [Range(typeof(decimal), "0", "1000")] public decimal? TargetRoas { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record BudgetDto(
    Guid Id, Guid ClientAccountId, string ClientName, DateOnly Month, AdPlatform? Platform, Guid? CampaignId, string? CampaignName, string ScopeLabel,
    decimal Amount, string Currency, decimal OverPacingThreshold, decimal UnderPacingThreshold, decimal? TargetCpa, decimal? TargetRoas, string? Notes,
    PacingResult Pacing, decimal? ActualCpa, decimal? ActualRoas, decimal Conversions, IReadOnlyList<string> FxMissing, Guid ConcurrencyStamp);

public sealed record AlertDto(
    Guid Id, Guid ClientAccountId, string ClientName, AdAlertKind Kind, AlertSeverity Severity, string Title, string Message, Guid? BudgetId,
    Guid? CampaignId, DateOnly EvaluatedFor, AdAlertStatus Status, DateTime CreatedAt, DateTime? AcknowledgedAt);

public sealed record BudgetEvaluation(AdBudget Budget, PacingResult Pacing, AdTotals Totals, IReadOnlyList<string> FxMissing);

/// <summary>Budget pacing: spend of the budget's scope (converted to the budget currency) vs expected-to-date and month-end projection.</summary>
public sealed class PacingService(AdsKpiService kpis)
{
    public async Task<BudgetEvaluation> EvaluateAsync(AdBudget budget, DateOnly asOf, CancellationToken ct)
    {
        var first = new DateOnly(budget.Month.Year, budget.Month.Month, 1);
        var last = first.AddDays(DateTime.DaysInMonth(first.Year, first.Month) - 1);
        var q = kpis.CampaignRows(first, last).Where(m => m.ClientAccountId == budget.ClientAccountId);
        if (budget.Platform is { } p) q = q.Where(m => m.Platform == p);
        if (budget.CampaignId is { } c) q = q.Where(m => m.CampaignId == c);
        var rows = await q.ToListAsync(ct);
        var toDate = rows.Where(r => r.Date <= asOf).ToList();
        var daily = await kpis.DailySpendAsync(toDate, budget.Currency, ct);
        var pacing = Pacing.Compute(budget.Amount, first, asOf, daily, budget.OverPacingThreshold, budget.UnderPacingThreshold, budget.Currency);
        var (totals, missing) = await kpis.ConvertAsync(toDate, budget.Currency, asOf, ct);
        return new BudgetEvaluation(budget, pacing, totals, missing);
    }
}

/// <summary>
/// Daily alert evaluation for the current month (data through yesterday): over/under pacing, CPA above target, ROAS
/// below target (budget or campaign targets) and spend without conversions. Each condition raises at most one alert per
/// scope per month (unique dedupe key), and notifies the client's account manager and the ad accounts' managers.
/// </summary>
public sealed class AdsAlertJob(AppDbContext db, PacingService pacing, AdsKpiService kpis, INotificationService notifications,
    IDatabaseDialect dialect, IAuditLogger audit, TimeProvider clock) : IJob
{
    public const int MinDaysForUnderPacing = 3;
    public const int MinSpendDaysWithoutConversions = 3;

    public string Name => nameof(AdsAlertJob);

    public Task<string> ExecuteAsync(CancellationToken ct) => EvaluateAsync(notify: true, ct);

    /// <summary>Evaluates and raises alerts; <paramref name="notify"/> is false only for the demo seed (no notifications for seeded data).</summary>
    public async Task<string> EvaluateAsync(bool notify, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var asOf = today.AddDays(-1);
        var month = new DateOnly(asOf.Year, asOf.Month, 1);
        var candidates = new List<AdAlert>();

        var budgets = await db.Set<AdBudget>().AsNoTracking().Where(b => b.Month == month).ToListAsync(ct);
        foreach (var b in budgets)
        {
            var e = await pacing.EvaluateAsync(b, asOf, ct);
            var p = e.Pacing;
            var scope = $"budget:{b.Id}";
            if (p.State == PacingState.Over)
                candidates.Add(Alert(b.ClientAccountId, AdAlertKind.OverPacing, AlertSeverity.Warning, scope, month, asOf, b.Id, b.CampaignId,
                    "Over pacing", $"Spend {Fmt(p.ActualToDate, b.Currency)} is {p.PacingRatio:P0} of the expected {Fmt(p.ExpectedToDate, b.Currency)}; " +
                                   $"projected month-end {Fmt(p.ProjectedMonthEnd, b.Currency)} vs budget {Fmt(b.Amount, b.Currency)}."));
            if (p.State == PacingState.Under && p.DaysElapsed >= MinDaysForUnderPacing)
                candidates.Add(Alert(b.ClientAccountId, AdAlertKind.UnderPacing, AlertSeverity.Info, scope, month, asOf, b.Id, b.CampaignId,
                    "Under pacing", $"Spend {Fmt(p.ActualToDate, b.Currency)} is {p.PacingRatio:P0} of the expected {Fmt(p.ExpectedToDate, b.Currency)}."));
            var k = AdKpis.From(e.Totals);
            if (b.TargetCpa is { } targetCpa && k.Cpa > targetCpa)
                candidates.Add(Alert(b.ClientAccountId, AdAlertKind.CpaAboveTarget, AlertSeverity.Warning, scope, month, asOf, b.Id, b.CampaignId,
                    "CPA above target", $"CPA {Fmt(k.Cpa!.Value, b.Currency)} is above the target {Fmt(targetCpa, b.Currency)}."));
            if (b.TargetRoas is { } targetRoas && e.Totals.Spend > 0 && (k.Roas ?? 0) < targetRoas)
                candidates.Add(Alert(b.ClientAccountId, AdAlertKind.RoasBelowTarget, AlertSeverity.Warning, scope, month, asOf, b.Id, b.CampaignId,
                    "ROAS below target", $"ROAS {k.Roas ?? 0:0.00} is below the target {targetRoas:0.00}."));
        }

        // Campaign-level checks (targets and spend without conversions).
        var rows = await kpis.CampaignRows(month, asOf).Where(m => m.CampaignId != null).ToListAsync(ct);
        var campaignIds = rows.Select(r => r.CampaignId!.Value).Distinct().ToList();
        var campaigns = await db.Set<AdCampaign>().AsNoTracking().Where(c => campaignIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        foreach (var g in rows.GroupBy(r => r.CampaignId!.Value))
        {
            if (!campaigns.TryGetValue(g.Key, out var c)) continue;
            var totals = AdsKpiService.Sum(g);
            var k = AdKpis.From(totals);
            var scope = $"campaign:{c.Id}";
            var spendDays = g.Where(r => r.Spend > 0).Select(r => r.Date).Distinct().Count();
            if (totals.Spend > 0 && totals.Conversions == 0 && spendDays >= MinSpendDaysWithoutConversions)
                candidates.Add(Alert(c.ClientAccountId, AdAlertKind.SpendWithoutConversions, AlertSeverity.Critical, scope, month, asOf, null, c.Id,
                    $"Spend without conversions: {c.Name}", $"{Fmt(totals.Spend, c.Currency)} spent over {spendDays} days this month with no conversions."));
            if (c.TargetCpa is { } cpa && k.Cpa > cpa)
                candidates.Add(Alert(c.ClientAccountId, AdAlertKind.CpaAboveTarget, AlertSeverity.Warning, scope, month, asOf, null, c.Id,
                    $"CPA above target: {c.Name}", $"CPA {Fmt(k.Cpa!.Value, c.Currency)} is above the target {Fmt(cpa, c.Currency)}."));
            if (c.TargetRoas is { } roas && totals.Spend > 0 && (k.Roas ?? 0) < roas)
                candidates.Add(Alert(c.ClientAccountId, AdAlertKind.RoasBelowTarget, AlertSeverity.Warning, scope, month, asOf, null, c.Id,
                    $"ROAS below target: {c.Name}", $"ROAS {k.Roas ?? 0:0.00} is below the target {roas:0.00}."));
        }

        var created = 0;
        foreach (var alert in candidates)
        {
            if (await db.Set<AdAlert>().AnyAsync(a => a.DedupeKey == alert.DedupeKey, ct)) continue;
            db.Set<AdAlert>().Add(alert);
            if (notify) await NotifyAsync(alert, ct);
            audit.RecordSystem("ads.alert.raised", nameof(AdAlert), alert.Id, new { alert.Kind, alert.DedupeKey });
            try
            {
                await db.SaveChangesAsync(ct);
                created++;
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                // A concurrent run raised it first.
            }
            db.ChangeTracker.Clear();
        }
        return $"evaluated {budgets.Count} budgets and {campaigns.Count} campaigns; {created} new alerts ({candidates.Count - created} already raised)";
    }

    private AdAlert Alert(Guid clientId, AdAlertKind kind, AlertSeverity severity, string scope, DateOnly month, DateOnly asOf, Guid? budgetId,
        Guid? campaignId, string title, string message) => new()
    {
        ClientAccountId = clientId, Kind = kind, Severity = severity, BudgetId = budgetId, CampaignId = campaignId,
        Title = title.Length > 200 ? title[..200] : title, Message = message.Length > 2000 ? message[..2000] : message,
        DedupeKey = $"{kind}:{scope}:{month:yyyy-MM}", EvaluatedFor = asOf, CreatedAt = clock.GetUtcNow().UtcDateTime,
    };

    private async Task NotifyAsync(AdAlert alert, CancellationToken ct)
    {
        var manager = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == alert.ClientAccountId).Select(c => new { c.AccountManagerUserId, c.Name }).FirstAsync(ct);
        var recipients = await db.Set<AdAccount>().AsNoTracking().Where(a => a.ClientAccountId == alert.ClientAccountId && a.ManagerUserId != null)
            .Select(a => a.ManagerUserId!.Value).Distinct().ToListAsync(ct);
        if (manager.AccountManagerUserId is { } am) recipients.Add(am);
        foreach (var userId in recipients.Distinct())
            await notifications.StageAsync(new NotificationRequest(userId, "ads.alert", $"{manager.Name}: {alert.Title}", alert.Message, AdsLinks.Alerts), ct);
    }

    private static string Fmt(decimal amount, string currency) => $"{Money.Round(amount, currency):N2} {currency}";
}

public static class AdsLinks
{
    public const string Alerts = "/agency/ads/alerts";
}

[ApiController]
[Route("api/v1/agency/ads")]
[HasPermission(Permissions.AdsManage)]
public sealed class AdsBudgetsController(AppDbContext db, SocialAccess access, PacingService pacing, IAuditLogger audit, ICurrentUser currentUser,
    TimeProvider clock) : ControllerBase
{
    /// <summary>Pacing board: every budget of the month with actual vs expected-to-date and the month-end projection.</summary>
    [HttpGet("pacing")]
    public async Task<IReadOnlyList<BudgetDto>> Board([FromQuery] DateOnly? month, [FromQuery] Guid? clientId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var m = month ?? today;
        var first = new DateOnly(m.Year, m.Month, 1);
        var q = (await access.ScopedAsync<AdBudget>(b => b.ClientAccountId, ct)).AsNoTracking().Where(b => b.Month == first);
        if (clientId is { } c) q = q.Where(b => b.ClientAccountId == c);
        var budgets = await q.OrderBy(b => b.ClientAccountId).ToListAsync(ct);
        var asOf = today.AddDays(-1);
        var result = new List<BudgetDto>();
        foreach (var b in budgets) result.Add(await ToDtoAsync(b, asOf, ct));
        return result.OrderByDescending(r => Math.Abs((r.Pacing.PacingRatio ?? 1) - 1)).ToList();
    }

    [HttpPost("budgets")]
    public async Task<BudgetDto> Create(BudgetInput input, CancellationToken ct)
    {
        var budget = new AdBudget { ClientAccountId = input.ClientAccountId!.Value };
        await ApplyAsync(budget, input, ct);
        db.Set<AdBudget>().Add(budget);
        audit.Record("ads.budget.created", nameof(AdBudget), budget.Id, after: new { budget.Month, budget.Platform, budget.CampaignId, budget.Amount, budget.Currency });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(budget, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-1), ct);
    }

    [HttpPut("budgets/{id:guid}")]
    public async Task<BudgetDto> Update(Guid id, BudgetInput input, CancellationToken ct)
    {
        var budget = await access.OwnedAsync<AdBudget>(id, b => b.ClientAccountId, "Budget", ct);
        if (input.ClientAccountId != budget.ClientAccountId) throw new DomainException("ads.client_mismatch", "A budget cannot move to another client.");
        if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != budget.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "The budget changed meanwhile; reload.");
            db.Entry(budget).Property(b => b.ConcurrencyStamp).OriginalValue = stamp;
        }
        var before = new { budget.Amount, budget.Currency, budget.OverPacingThreshold, budget.UnderPacingThreshold, budget.TargetCpa, budget.TargetRoas };
        await ApplyAsync(budget, input, ct);
        audit.Record("ads.budget.updated", nameof(AdBudget), budget.Id, before,
            new { budget.Amount, budget.Currency, budget.OverPacingThreshold, budget.UnderPacingThreshold, budget.TargetCpa, budget.TargetRoas });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(budget, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-1), ct);
    }

    [HttpDelete("budgets/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var budget = await access.OwnedAsync<AdBudget>(id, b => b.ClientAccountId, "Budget", ct);
        audit.Record("ads.budget.deleted", nameof(AdBudget), budget.Id, new { budget.Month, budget.Amount, budget.Currency });
        db.Remove(budget);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("alerts")]
    public async Task<IReadOnlyList<AlertDto>> Alerts([FromQuery] AdAlertStatus? status, [FromQuery] Guid? clientId, CancellationToken ct)
    {
        var q = (await access.ScopedAsync<AdAlert>(a => a.ClientAccountId, ct)).AsNoTracking();
        if (status is { } s) q = q.Where(a => a.Status == s);
        if (clientId is { } c) q = q.Where(a => a.ClientAccountId == c);
        var alerts = await q.OrderByDescending(a => a.CreatedAt).Take(300).ToListAsync(ct);
        var ids = alerts.Select(a => a.ClientAccountId).Distinct().ToList();
        var names = await db.Set<ClientAccount>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return alerts.Select(a => ToDto(a, names.GetValueOrDefault(a.ClientAccountId, ""))).ToList();
    }

    [HttpPost("alerts/{id:guid}/acknowledge")]
    public Task<AlertDto> Acknowledge(Guid id, CancellationToken ct) => SetStatus(id, AdAlertStatus.Acknowledged, ct);

    [HttpPost("alerts/{id:guid}/resolve")]
    public Task<AlertDto> Resolve(Guid id, CancellationToken ct) => SetStatus(id, AdAlertStatus.Resolved, ct);

    /// <summary>Reopens an acknowledged or resolved alert (e.g. resolved by mistake).</summary>
    [HttpPost("alerts/{id:guid}/reopen")]
    public Task<AlertDto> Reopen(Guid id, CancellationToken ct) => SetStatus(id, AdAlertStatus.Open, ct);

    private async Task<AlertDto> SetStatus(Guid id, AdAlertStatus status, CancellationToken ct)
    {
        var alert = await access.OwnedAsync<AdAlert>(id, a => a.ClientAccountId, "Alert", ct);
        alert.Status = status;
        alert.AcknowledgedByUserId = status == AdAlertStatus.Open ? null : currentUser.Id;
        alert.AcknowledgedAt = status == AdAlertStatus.Open ? null : clock.GetUtcNow().UtcDateTime;
        audit.Record($"ads.alert.{status.ToString().ToLowerInvariant()}", nameof(AdAlert), alert.Id);
        await db.SaveChangesAsync(ct);
        var name = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == alert.ClientAccountId).Select(c => c.Name).FirstAsync(ct);
        return ToDto(alert, name);
    }

    private async Task ApplyAsync(AdBudget budget, BudgetInput input, CancellationToken ct)
    {
        await access.ClientAsync(budget.ClientAccountId, ct);
        var currency = Money.Normalize(input.Currency);
        if (!Money.IsSupported(currency)) throw new DomainException("ads.currency_unsupported", $"Currency {currency} is not supported.");
        if (input.UnderPacingThreshold >= input.OverPacingThreshold)
            throw new DomainException("ads.thresholds_invalid", "The under-pacing threshold must be below the over-pacing threshold.");
        if (input.CampaignId is { } cid && !await db.Set<AdCampaign>().AnyAsync(c => c.Id == cid && c.ClientAccountId == budget.ClientAccountId, ct))
            throw DomainException.NotFound("Campaign");
        budget.Month = new DateOnly(input.Month!.Value.Year, input.Month.Value.Month, 1);
        budget.Platform = input.Platform;
        budget.CampaignId = input.CampaignId;
        budget.Amount = Money.Round(input.Amount, currency);
        budget.Currency = currency;
        budget.OverPacingThreshold = input.OverPacingThreshold;
        budget.UnderPacingThreshold = input.UnderPacingThreshold;
        budget.TargetCpa = input.TargetCpa;
        budget.TargetRoas = input.TargetRoas;
        budget.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
    }

    private async Task<BudgetDto> ToDtoAsync(AdBudget b, DateOnly asOf, CancellationToken ct)
    {
        var e = await pacing.EvaluateAsync(b, asOf, ct);
        var client = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == b.ClientAccountId).Select(c => c.Name).FirstAsync(ct);
        var campaign = b.CampaignId is { } cid ? await db.Set<AdCampaign>().AsNoTracking().Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct) : null;
        var scope = campaign is not null ? $"Campaign: {campaign}" : b.Platform is { } p ? $"Platform: {p}" : "All platforms";
        var k = AdKpis.From(e.Totals);
        return new BudgetDto(b.Id, b.ClientAccountId, client, b.Month, b.Platform, b.CampaignId, campaign, scope, b.Amount, b.Currency,
            b.OverPacingThreshold, b.UnderPacingThreshold, b.TargetCpa, b.TargetRoas, b.Notes, e.Pacing, k.Cpa, k.Roas, e.Totals.Conversions,
            e.FxMissing, b.ConcurrencyStamp);
    }

    private static AlertDto ToDto(AdAlert a, string clientName) => new(a.Id, a.ClientAccountId, clientName, a.Kind, a.Severity, a.Title, a.Message,
        a.BudgetId, a.CampaignId, a.EvaluatedFor, a.Status, a.CreatedAt, a.AcknowledgedAt);
}
