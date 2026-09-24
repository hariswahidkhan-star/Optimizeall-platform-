using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Ads;

public sealed class AdAccountInput
{
    [Required] public Guid? ClientAccountId { get; set; }
    [Required] public AdPlatform? Platform { get; set; }
    [Required, MaxLength(64)] public string ExternalAccountId { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = "USD";
    [Required, MaxLength(64)] public string TimeZone { get; set; } = "UTC";
    public Guid? ManagerUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record AdAccountDto(
    Guid Id, Guid ClientAccountId, string ClientName, AdPlatform Platform, string ExternalAccountId, string Name, string Currency, string TimeZone,
    AdAccountStatus Status, string? StatusMessage, bool SyncSupported, DateTime? LastSyncedAt, string? LastSyncMessage, Guid? ManagerUserId,
    string? ManagerName, bool IsActive, TotalsDto Last30Days, AdKpis Last30DaysKpis, Guid ConcurrencyStamp);

public sealed record CampaignRowDto(
    Guid Id, Guid AdAccountId, string? ExternalId, string Name, string? Objective, AdEntityStatus Status, BudgetType BudgetType, decimal? BudgetAmount,
    string Currency, string? BidStrategy, DateOnly? StartDate, DateOnly? EndDate, string? TargetingSummary, decimal? TargetCpa, decimal? TargetRoas,
    AdEntitySource Source, bool NamingCompliant, TotalsDto Totals, AdKpis Kpis, IReadOnlyList<decimal> SpendSparkline, string SourceLabel,
    Guid ConcurrencyStamp);

public sealed class CampaignInput
{
    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? Objective { get; set; }
    public AdEntityStatus Status { get; set; } = AdEntityStatus.Draft;
    public BudgetType BudgetType { get; set; } = BudgetType.Daily;
    [Range(0, 1_000_000_000)] public decimal? BudgetAmount { get; set; }
    [MaxLength(100)] public string? BidStrategy { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    [MaxLength(2000)] public string? TargetingSummary { get; set; }
    [Range(0, 1_000_000)] public decimal? TargetCpa { get; set; }
    [Range(0, 1000)] public decimal? TargetRoas { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class AdGroupInput
{
    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    [DefinedEnum] public AdEntityStatus Status { get; set; } = AdEntityStatus.Draft;
    [Range(0, 1_000_000_000)] public decimal? BudgetAmount { get; set; }
    [MaxLength(100)] public string? BidStrategy { get; set; }
    [MaxLength(2000)] public string? TargetingSummary { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class AdInput
{
    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    [DefinedEnum] public AdEntityStatus Status { get; set; } = AdEntityStatus.Draft;
    public Guid? CreativeId { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record AdGroupDto(Guid Id, Guid CampaignId, string? ExternalId, string Name, AdEntityStatus Status, decimal? BudgetAmount, string? BidStrategy,
    string? TargetingSummary, AdEntitySource Source, TotalsDto Totals, AdKpis Kpis, Guid ConcurrencyStamp = default);

public sealed record AdDto(Guid Id, Guid AdGroupId, string? ExternalId, string Name, AdEntityStatus Status, Guid? CreativeId, AdEntitySource Source, TotalsDto Totals, AdKpis Kpis,
    Guid ConcurrencyStamp = default);

public sealed record OverviewRowDto(Guid ClientAccountId, string ClientName, string Currency, TotalsDto Totals, AdKpis Kpis, IReadOnlyList<string> FxMissing,
    int Accounts, int OpenAlerts);

public sealed record OverviewDto(DateOnly From, DateOnly To, string ReportingCurrency, TotalsDto Totals, AdKpis Kpis, IReadOnlyList<OverviewRowDto> Clients,
    IReadOnlyList<DailyPointDto> Daily, IReadOnlyList<string> FxMissing, string Definitions);

public sealed record AccountDetailDto(AdAccountDto Account, TotalsDto Totals, AdKpis Kpis, IReadOnlyList<DailyPointDto> Daily, IReadOnlyList<CampaignRowDto> Campaigns,
    string SourceLabel);

public sealed record StaffOptionDto(Guid Id, string Name);

/// <summary>Ad accounts, the campaign structure mirror, sync, KPIs (overview, per account, per client).</summary>
[ApiController]
[Route("api/v1/agency/ads")]
public sealed class AdsAccountsController(
    AppDbContext db, SocialAccess access, AdsKpiService kpis, AdsSyncService sync, AdsProviderRegistry providers, NamingService naming,
    IAuditLogger audit, ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    [HttpGet("clients")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<IReadOnlyList<ClientOptionDto>> Clients(CancellationToken ct) => await SocialProfilesController.ClientOptionsAsync(db, access.Scope, ct);

    [HttpGet("staff")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<IReadOnlyList<StaffOptionDto>> Staff(CancellationToken ct)
    {
        // Owners of ad accounts: holders of ads.manage (the ads team) or clients.manage (account managers), through
        // built-in or custom roles — for the built-in roles AdsSpecialist, Strategist, AccountManager and Admin.
        var holders = await new PermissionDirectory(db).UsersWithAnyPermissionAsync(new[] { Permissions.AdsManage, Permissions.ClientsManage }, ct);
        return await holders.Where(u => u.Status == UserStatus.Active)
            .OrderBy(u => u.DisplayName).Select(u => new StaffOptionDto(u.Id, u.DisplayName)).Take(300).ToListAsync(ct);
    }

    [HttpGet("accounts")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<IReadOnlyList<AdAccountDto>> Accounts([FromQuery] Guid? clientId, CancellationToken ct)
    {
        var q = (await access.ScopedAsync<AdAccount>(a => a.ClientAccountId, ct)).AsNoTracking();
        if (clientId is { } c) q = q.Where(a => a.ClientAccountId == c);
        var accounts = await q.OrderBy(a => a.Name).ToListAsync(ct);
        var result = new List<AdAccountDto>();
        foreach (var a in accounts) result.Add(await ToDtoAsync(a, ct));
        return result;
    }

    [HttpPost("accounts")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<AdAccountDto> Create(AdAccountInput input, CancellationToken ct)
    {
        await access.ClientAsync(input.ClientAccountId!.Value, ct);
        var account = new AdAccount { ClientAccountId = input.ClientAccountId.Value, Platform = input.Platform!.Value };
        await ApplyAsync(account, input, ct);
        if (await db.Set<AdAccount>().AnyAsync(a => a.ClientAccountId == account.ClientAccountId && a.Platform == account.Platform
                                                     && a.ExternalAccountId == account.ExternalAccountId, ct))
            throw DomainException.Conflict("ads.account_exists", "This ad account is already registered for the client.");
        db.Set<AdAccount>().Add(account);
        audit.Record("ads.account.created", nameof(AdAccount), account.Id, after: new { account.Platform, account.ExternalAccountId, account.Currency });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(account, ct);
    }

    [HttpPut("accounts/{id:guid}")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<AdAccountDto> Update(Guid id, AdAccountInput input, CancellationToken ct)
    {
        var account = await access.OwnedAsync<AdAccount>(id, a => a.ClientAccountId, "Ad account", ct);
        if (input.ClientAccountId != account.ClientAccountId || input.Platform != account.Platform)
            throw new DomainException("ads.account_immutable", "Client and platform of an ad account cannot change.");
        if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != account.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "The account changed meanwhile; reload.");
            db.Entry(account).Property(a => a.ConcurrencyStamp).OriginalValue = stamp;
        }
        var before = new { account.ExternalAccountId, account.Currency, account.TimeZone, account.IsActive };
        if (!string.Equals(Money.Normalize(input.Currency), account.Currency, StringComparison.Ordinal)
            && await db.Set<AdDailyMetric>().AnyAsync(m => m.AdAccountId == account.Id, ct))
            throw DomainException.Conflict("ads.currency_locked", "The currency cannot change once metrics exist; create a new account.");
        await ApplyAsync(account, input, ct);
        audit.Record("ads.account.updated", nameof(AdAccount), account.Id, before, new { account.ExternalAccountId, account.Currency, account.TimeZone, account.IsActive });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(account, ct);
    }

    /// <summary>Runs the reporting sync now. Without credentials the result is NotConfigured and nothing is written.</summary>
    [HttpPost("accounts/{id:guid}/sync")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<AccountSyncDto> Sync(Guid id, CancellationToken ct)
    {
        var account = await access.OwnedAsync<AdAccount>(id, a => a.ClientAccountId, "Ad account", ct);
        return await sync.SyncAsync(account.Id, ct);
    }

    [HttpGet("accounts/{id:guid}")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<AccountDetailDto> Detail(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var account = await access.OwnedAsync<AdAccount>(id, a => a.ClientAccountId, "Ad account", ct);
        var (f, t) = Range(from, to);
        var rows = await kpis.CampaignRows(f, t).Where(m => m.AdAccountId == id).ToListAsync(ct);
        var totals = AdsKpiService.Sum(rows);
        return new AccountDetailDto(await ToDtoAsync(account, ct), TotalsDto.From(totals), AdKpis.From(totals), Daily(rows, f, t),
            await CampaignRowsAsync(account, rows, f, t, ct), AdsKpiService.Label(rows.Select(r => r.Source)));
    }

    [HttpGet("accounts/{id:guid}/campaigns")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<IReadOnlyList<CampaignRowDto>> Campaigns(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var account = await access.OwnedAsync<AdAccount>(id, a => a.ClientAccountId, "Ad account", ct);
        var (f, t) = Range(from, to);
        var rows = await kpis.CampaignRows(f, t).Where(m => m.AdAccountId == id).ToListAsync(ct);
        return await CampaignRowsAsync(account, rows, f, t, ct);
    }

    /// <summary>Plans a campaign internally; the client's naming template is enforced.</summary>
    [HttpPost("accounts/{id:guid}/campaigns")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<CampaignRowDto> CreateCampaign(Guid id, CampaignInput input, CancellationToken ct)
    {
        var account = await access.OwnedAsync<AdAccount>(id, a => a.ClientAccountId, "Ad account", ct);
        await naming.EnforceAsync(account, input.Name.Trim(), input.Objective, input.StartDate, ct);
        var campaign = new AdCampaign
        {
            ClientAccountId = account.ClientAccountId, AdAccountId = account.Id, Currency = account.Currency, Source = AdEntitySource.Plan, NamingCompliant = true,
        };
        Apply(campaign, input);
        db.Set<AdCampaign>().Add(campaign);
        audit.Record("ads.campaign.created", nameof(AdCampaign), campaign.Id, after: new { campaign.Name, campaign.BudgetAmount, campaign.BudgetType });
        await db.SaveChangesAsync(ct);
        return (await CampaignRowsAsync(account, Array.Empty<AdDailyMetric>(), Today, Today, ct)).First(c => c.Id == campaign.Id);
    }

    [HttpPut("campaigns/{id:guid}")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<CampaignRowDto> UpdateCampaign(Guid id, CampaignInput input, CancellationToken ct)
    {
        var campaign = await access.OwnedAsync<AdCampaign>(id, c => c.ClientAccountId, "Campaign", ct);
        var account = await db.Set<AdAccount>().AsNoTracking().FirstAsync(a => a.Id == campaign.AdAccountId, ct);
        if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != campaign.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "The campaign changed meanwhile; reload.");
            db.Entry(campaign).Property(c => c.ConcurrencyStamp).OriginalValue = stamp;
        }
        if (campaign.Source == AdEntitySource.Plan && input.Name.Trim() != campaign.Name)
            await naming.EnforceAsync(account, input.Name.Trim(), input.Objective, input.StartDate, ct);
        var before = new { campaign.Name, campaign.Status, campaign.BudgetAmount, campaign.TargetCpa, campaign.TargetRoas };
        Apply(campaign, input);
        campaign.NamingCompliant = (await naming.CheckAsync(account, campaign.Name, campaign.Objective, campaign.StartDate, ct))?.Compliant ?? true;
        audit.Record("ads.campaign.updated", nameof(AdCampaign), campaign.Id, before, new { campaign.Name, campaign.Status, campaign.BudgetAmount, campaign.TargetCpa, campaign.TargetRoas });
        await db.SaveChangesAsync(ct);
        return (await CampaignRowsAsync(account, Array.Empty<AdDailyMetric>(), Today, Today, ct)).First(c => c.Id == campaign.Id);
    }

    [HttpGet("campaigns/{id:guid}/ad-groups")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<IReadOnlyList<AdGroupDto>> AdGroups(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var campaign = await access.OwnedAsync<AdCampaign>(id, c => c.ClientAccountId, "Campaign", ct);
        var (f, t) = Range(from, to);
        var groups = await db.Set<AdGroup>().AsNoTracking().Where(g => g.CampaignId == campaign.Id).OrderBy(g => g.Name).ToListAsync(ct);
        var metrics = await db.Set<AdDailyMetric>().AsNoTracking()
            .Where(m => m.CampaignId == campaign.Id && m.Level == AdLevel.AdGroup && m.Date >= f && m.Date <= t).ToListAsync(ct);
        return groups.Select(g =>
        {
            var totals = AdsKpiService.Sum(metrics.Where(m => m.AdGroupId == g.Id));
            return new AdGroupDto(g.Id, g.CampaignId, g.ExternalId, g.Name, g.Status, g.BudgetAmount, g.BidStrategy, g.TargetingSummary, g.Source,
                TotalsDto.From(totals), AdKpis.From(totals), g.ConcurrencyStamp);
        }).ToList();
    }

    [HttpPost("campaigns/{id:guid}/ad-groups")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<AdGroupDto> CreateAdGroup(Guid id, AdGroupInput input, CancellationToken ct)
    {
        var campaign = await access.OwnedAsync<AdCampaign>(id, c => c.ClientAccountId, "Campaign", ct);
        var g = new AdGroup
        {
            ClientAccountId = campaign.ClientAccountId, AdAccountId = campaign.AdAccountId, CampaignId = campaign.Id, Name = input.Name.Trim(),
            Status = input.Status, BudgetAmount = input.BudgetAmount, BidStrategy = input.BidStrategy, TargetingSummary = input.TargetingSummary,
            Source = AdEntitySource.Plan,
        };
        db.Set<AdGroup>().Add(g);
        await db.SaveChangesAsync(ct);
        return new AdGroupDto(g.Id, g.CampaignId, null, g.Name, g.Status, g.BudgetAmount, g.BidStrategy, g.TargetingSummary, g.Source,
            TotalsDto.From(AdTotals.Zero), AdKpis.From(AdTotals.Zero), g.ConcurrencyStamp);
    }

    [HttpGet("ad-groups/{id:guid}/ads")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<IReadOnlyList<AdDto>> Ads(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var group = await access.OwnedAsync<AdGroup>(id, g => g.ClientAccountId, "Ad group", ct);
        var (f, t) = Range(from, to);
        var ads = await db.Set<Ad>().AsNoTracking().Where(a => a.AdGroupId == group.Id).OrderBy(a => a.Name).ToListAsync(ct);
        var metrics = await db.Set<AdDailyMetric>().AsNoTracking()
            .Where(m => m.AdGroupId == group.Id && m.Level == AdLevel.Ad && m.Date >= f && m.Date <= t).ToListAsync(ct);
        return ads.Select(a =>
        {
            var totals = AdsKpiService.Sum(metrics.Where(m => m.AdId == a.Id));
            return new AdDto(a.Id, a.AdGroupId, a.ExternalId, a.Name, a.Status, a.CreativeId, a.Source, TotalsDto.From(totals), AdKpis.From(totals), a.ConcurrencyStamp);
        }).ToList();
    }

    [HttpPost("ad-groups/{id:guid}/ads")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<AdDto> CreateAd(Guid id, AdInput input, CancellationToken ct)
    {
        var group = await access.OwnedAsync<AdGroup>(id, g => g.ClientAccountId, "Ad group", ct);
        if (input.CreativeId is { } cid && !await db.Set<AdCreative>().AnyAsync(c => c.Id == cid && c.ClientAccountId == group.ClientAccountId, ct))
            throw DomainException.NotFound("Creative");
        var ad = new Ad
        {
            ClientAccountId = group.ClientAccountId, AdAccountId = group.AdAccountId, CampaignId = group.CampaignId, AdGroupId = group.Id,
            Name = input.Name.Trim(), Status = input.Status, CreativeId = input.CreativeId, Source = AdEntitySource.Plan,
        };
        db.Set<Ad>().Add(ad);
        await db.SaveChangesAsync(ct);
        return new AdDto(ad.Id, ad.AdGroupId, null, ad.Name, ad.Status, ad.CreativeId, ad.Source, TotalsDto.From(AdTotals.Zero), AdKpis.From(AdTotals.Zero),
            ad.ConcurrencyStamp);
    }

    // ------------------------------ edit / delete of the structure

    [HttpPut("ad-groups/{id:guid}")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<AdGroupDto> UpdateAdGroup(Guid id, AdGroupInput input, CancellationToken ct)
    {
        var g = await access.OwnedAsync<AdGroup>(id, x => x.ClientAccountId, "Ad group", ct);
        StampGuard.Expect(db, g, input.ConcurrencyStamp, "ad group");
        var before = new { g.Name, g.Status, g.BudgetAmount };
        g.Name = input.Name.Trim();
        g.Status = input.Status;
        g.BudgetAmount = input.BudgetAmount;
        g.BidStrategy = input.BidStrategy;
        g.TargetingSummary = input.TargetingSummary;
        audit.Record("ads.ad_group.updated", nameof(AdGroup), g.Id, before, new { g.Name, g.Status, g.BudgetAmount });
        await db.SaveChangesAsync(ct);
        return new AdGroupDto(g.Id, g.CampaignId, g.ExternalId, g.Name, g.Status, g.BudgetAmount, g.BidStrategy, g.TargetingSummary, g.Source,
            TotalsDto.From(AdTotals.Zero), AdKpis.From(AdTotals.Zero), g.ConcurrencyStamp);
    }

    [HttpPut("ads/{id:guid}")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<AdDto> UpdateAd(Guid id, AdInput input, CancellationToken ct)
    {
        var ad = await access.OwnedAsync<Ad>(id, x => x.ClientAccountId, "Ad", ct);
        StampGuard.Expect(db, ad, input.ConcurrencyStamp, "ad");
        if (input.CreativeId is { } cid && !await db.Set<AdCreative>().AnyAsync(c => c.Id == cid && c.ClientAccountId == ad.ClientAccountId, ct))
            throw DomainException.NotFound("Creative");
        var before = new { ad.Name, ad.Status, ad.CreativeId };
        ad.Name = input.Name.Trim();
        ad.Status = input.Status;
        ad.CreativeId = input.CreativeId;
        audit.Record("ads.ad.updated", nameof(Ad), ad.Id, before, new { ad.Name, ad.Status, ad.CreativeId });
        await db.SaveChangesAsync(ct);
        return new AdDto(ad.Id, ad.AdGroupId, ad.ExternalId, ad.Name, ad.Status, ad.CreativeId, ad.Source, TotalsDto.From(AdTotals.Zero), AdKpis.From(AdTotals.Zero),
            ad.ConcurrencyStamp);
    }

    /// <summary>
    /// Deletes a planned campaign without reported metrics, with its ad groups and ads. Synced or imported campaigns (or any
    /// with metrics) are history: set their status to Removed instead.
    /// </summary>
    [HttpDelete("campaigns/{id:guid}")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<IActionResult> DeleteCampaign(Guid id, CancellationToken ct)
    {
        var campaign = await access.OwnedAsync<AdCampaign>(id, c => c.ClientAccountId, "Campaign", ct);
        if (campaign.Source != AdEntitySource.Plan || await db.Set<AdDailyMetric>().AnyAsync(m => m.CampaignId == id, ct))
            throw DomainException.Conflict("ads.campaign_has_history", "This campaign has platform data; set its status to Removed instead of deleting it.");
        if (await db.Set<AdBudget>().AnyAsync(b => b.CampaignId == id, ct))
            throw DomainException.Conflict("ads.campaign_has_budget", "A budget targets this campaign; delete or retarget the budget first.");
        await db.Set<Ad>().Where(a => a.CampaignId == id).ExecuteDeleteAsync(ct);
        await db.Set<AdGroup>().Where(g => g.CampaignId == id).ExecuteDeleteAsync(ct);
        await db.Set<AdCreative>().Where(c => c.CampaignId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CampaignId, (Guid?)null), ct);
        await db.Set<AdExperiment>().Where(e => e.CampaignId == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.CampaignId, (Guid?)null), ct);
        db.Remove(campaign);
        audit.Record("ads.campaign.deleted", nameof(AdCampaign), id, before: new { campaign.Name, campaign.Status });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("ad-groups/{id:guid}")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<IActionResult> DeleteAdGroup(Guid id, CancellationToken ct)
    {
        var g = await access.OwnedAsync<AdGroup>(id, x => x.ClientAccountId, "Ad group", ct);
        if (g.Source != AdEntitySource.Plan || await db.Set<AdDailyMetric>().AnyAsync(m => m.AdGroupId == id, ct))
            throw DomainException.Conflict("ads.ad_group_has_history", "This ad group has platform data; set its status to Removed instead of deleting it.");
        await db.Set<Ad>().Where(a => a.AdGroupId == id).ExecuteDeleteAsync(ct);
        db.Remove(g);
        audit.Record("ads.ad_group.deleted", nameof(AdGroup), id, before: new { g.Name });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("ads/{id:guid}")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<IActionResult> DeleteAd(Guid id, CancellationToken ct)
    {
        var ad = await access.OwnedAsync<Ad>(id, x => x.ClientAccountId, "Ad", ct);
        if (ad.Source != AdEntitySource.Plan || await db.Set<AdDailyMetric>().AnyAsync(m => m.AdId == id, ct))
            throw DomainException.Conflict("ads.ad_has_history", "This ad has platform data; set its status to Removed instead of deleting it.");
        db.Remove(ad);
        audit.Record("ads.ad.deleted", nameof(Ad), id, before: new { ad.Name });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Deletes an ad account registered by mistake (no campaigns, metrics or imports). Otherwise deactivate it.</summary>
    [HttpDelete("accounts/{id:guid}")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<IActionResult> DeleteAccount(Guid id, CancellationToken ct)
    {
        var account = await access.OwnedAsync<AdAccount>(id, a => a.ClientAccountId, "Ad account", ct);
        if (await db.Set<AdCampaign>().AnyAsync(c => c.AdAccountId == id, ct) || await db.Set<AdDailyMetric>().AnyAsync(m => m.AdAccountId == id, ct)
            || await db.Set<AdImportBatch>().AnyAsync(b => b.AdAccountId == id, ct))
            throw DomainException.Conflict("ads.account_has_history", "This account has campaigns or reported data; deactivate it instead so reporting keeps it.");
        if (account.IntegrationConnectionId is not null)
            throw DomainException.Conflict("ads.account_connected", "Disconnect the account's integration before deleting it.");
        db.Remove(account);
        audit.Record("ads.account.deleted", nameof(AdAccount), id, before: new { account.Platform, account.ExternalAccountId, account.Name });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Spend, ROAS and CPA across all clients (each in its own currency, plus agency totals in <paramref name="currency"/>).</summary>
    [HttpGet("overview")]
    [HasPermission(Permissions.AdsManage)]
    public async Task<OverviewDto> Overview([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string currency = "USD", CancellationToken ct = default)
    {
        var (f, t) = Range(from, to);
        currency = Money.Normalize(currency);
        if (!Money.IsSupported(currency)) throw new DomainException("ads.currency_unsupported", $"Currency {currency} is not supported.");
        var clients = await (await access.ScopedAsync<ClientAccount>(c => c.Id, ct)).AsNoTracking().ToListAsync(ct);
        var clientIds = clients.Select(c => c.Id).ToList();
        var rows = await kpis.CampaignRows(f, t).Where(m => clientIds.Contains(m.ClientAccountId)).ToListAsync(ct);
        var accountCounts = await db.Set<AdAccount>().AsNoTracking().Where(a => clientIds.Contains(a.ClientAccountId))
            .GroupBy(a => a.ClientAccountId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var alertCounts = await db.Set<AdAlert>().AsNoTracking().Where(a => clientIds.Contains(a.ClientAccountId) && a.Status == AdAlertStatus.Open)
            .GroupBy(a => a.ClientAccountId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var result = new List<OverviewRowDto>();
        foreach (var client in clients.Where(c => rows.Any(r => r.ClientAccountId == c.Id) || accountCounts.ContainsKey(c.Id)).OrderBy(c => c.Name))
        {
            var (converted, missing) = await kpis.ConvertAsync(rows.Where(r => r.ClientAccountId == client.Id), client.Currency, t, ct);
            result.Add(new OverviewRowDto(client.Id, client.Name, client.Currency, TotalsDto.From(converted), AdKpis.From(converted), missing,
                accountCounts.GetValueOrDefault(client.Id), alertCounts.GetValueOrDefault(client.Id)));
        }
        var (agency, agencyMissing) = await kpis.ConvertAsync(rows, currency, t, ct);
        var daily = await kpis.DailySpendAsync(rows, currency, ct);
        return new OverviewDto(f, t, currency, TotalsDto.From(agency), AdKpis.From(agency), result.OrderByDescending(r => r.Totals.Spend).ToList(),
            Enumerable.Range(0, t.DayNumber - f.DayNumber + 1).Select(i => f.AddDays(i))
                .Select(d => new DailyPointDto(d, daily.GetValueOrDefault(d), 0, 0, 0, 0)).ToList(),
            agencyMissing, AdsDefinitions.Kpis);
    }

    /// <summary>Ads KPIs of one client for reports (reports.manage or ads.manage); see docs/api/social-ads.md.</summary>
    [HttpGet("clients/{clientId:guid}/kpis")]
    public async Task<ClientAdsKpisDto> ClientKpis(Guid clientId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!currentUser.HasPermission(Permissions.ReportsManage) && !currentUser.HasPermission(Permissions.AdsManage))
            throw DomainException.Forbidden("auth.forbidden", "You do not have permission to perform this action.");
        await access.ClientAsync(clientId, ct);
        var (f, t) = Range(from, to);
        return await kpis.ClientKpisAsync(clientId, f, t, ct);
    }

    private async Task<IReadOnlyList<CampaignRowDto>> CampaignRowsAsync(AdAccount account, IReadOnlyList<AdDailyMetric> rows, DateOnly f, DateOnly t, CancellationToken ct)
    {
        var campaigns = await db.Set<AdCampaign>().AsNoTracking().Where(c => c.AdAccountId == account.Id).OrderBy(c => c.Name).ToListAsync(ct);
        var days = Enumerable.Range(0, Math.Min(t.DayNumber - f.DayNumber + 1, 90)).Select(i => t.AddDays(-i)).Reverse().ToList();
        return campaigns.Select(c =>
        {
            var mine = rows.Where(r => r.CampaignId == c.Id).ToList();
            var totals = AdsKpiService.Sum(mine);
            var byDay = mine.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.Sum(r => r.Spend));
            return new CampaignRowDto(c.Id, c.AdAccountId, c.ExternalId, c.Name, c.Objective, c.Status, c.BudgetType, c.BudgetAmount, c.Currency, c.BidStrategy,
                c.StartDate, c.EndDate, c.TargetingSummary, c.TargetCpa, c.TargetRoas, c.Source, c.NamingCompliant, TotalsDto.From(totals), AdKpis.From(totals),
                days.Select(d => byDay.GetValueOrDefault(d)).ToList(), AdsKpiService.Label(mine.Select(r => r.Source)), c.ConcurrencyStamp);
        }).OrderByDescending(c => c.Totals.Spend).ToList();
    }

    private static IReadOnlyList<DailyPointDto> Daily(IReadOnlyList<AdDailyMetric> rows, DateOnly f, DateOnly t)
    {
        var byDay = rows.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => AdsKpiService.Sum(g));
        return Enumerable.Range(0, t.DayNumber - f.DayNumber + 1).Select(i => f.AddDays(i)).Select(d =>
        {
            var x = byDay.GetValueOrDefault(d) ?? AdTotals.Zero;
            return new DailyPointDto(d, x.Spend, x.ConversionValue, x.Conversions, x.Clicks, x.Impressions);
        }).ToList();
    }

    private async Task ApplyAsync(AdAccount account, AdAccountInput input, CancellationToken ct)
    {
        var currency = Money.Normalize(input.Currency);
        if (!Money.IsSupported(currency)) throw new DomainException("ads.currency_unsupported", $"Currency {currency} is not supported.");
        if (QueueScheduler.Zone(input.TimeZone) == TimeZoneInfo.Utc && input.TimeZone is not ("UTC" or "Etc/UTC"))
            throw new DomainException("ads.invalid_timezone", "Use an IANA time zone such as Europe/London.");
        if (input.ManagerUserId is { } m && !await db.Set<User>().AnyAsync(u => u.Id == m, ct)) throw DomainException.NotFound("User");
        account.ExternalAccountId = input.ExternalAccountId.Trim();
        account.Name = input.Name.Trim();
        account.Currency = currency;
        account.TimeZone = input.TimeZone.Trim();
        account.ManagerUserId = input.ManagerUserId;
        account.IsActive = input.IsActive;
    }

    private static void Apply(AdCampaign c, CampaignInput input)
    {
        if (input.StartDate is { } s && input.EndDate is { } e && e < s)
            throw new DomainException("ads.dates_invalid", "The end date must be on or after the start date.");
        c.Name = input.Name.Trim();
        c.Objective = string.IsNullOrWhiteSpace(input.Objective) ? null : input.Objective.Trim();
        c.Status = input.Status;
        c.BudgetType = input.BudgetType;
        c.BudgetAmount = input.BudgetAmount is { } b ? Money.Round(b, c.Currency) : null;
        c.BidStrategy = input.BidStrategy;
        c.StartDate = input.StartDate;
        c.EndDate = input.EndDate;
        c.TargetingSummary = input.TargetingSummary;
        c.TargetCpa = input.TargetCpa;
        c.TargetRoas = input.TargetRoas;
    }

    private async Task<AdAccountDto> ToDtoAsync(AdAccount a, CancellationToken ct)
    {
        var client = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == a.ClientAccountId).Select(c => c.Name).FirstAsync(ct);
        var manager = a.ManagerUserId is { } m ? await db.Set<User>().AsNoTracking().Where(u => u.Id == m).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) : null;
        var rows = await kpis.CampaignRows(Today.AddDays(-30), Today.AddDays(-1)).Where(r => r.AdAccountId == a.Id).ToListAsync(ct);
        var totals = AdsKpiService.Sum(rows);
        var supported = providers.For(a.Platform) is not NotConfiguredAdsProvider;
        return new AdAccountDto(a.Id, a.ClientAccountId, client, a.Platform, a.ExternalAccountId, a.Name, a.Currency, a.TimeZone, a.Status, a.StatusMessage,
            supported, a.LastSyncedAt, a.LastSyncMessage, a.ManagerUserId, manager, a.IsActive, TotalsDto.From(totals), AdKpis.From(totals), a.ConcurrencyStamp);
    }

    private (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var t = to ?? Today.AddDays(-1);
        var f = from ?? t.AddDays(-29);
        if (f > t) throw new DomainException("ads.invalid_range", "'from' must not be after 'to'.");
        if (t.DayNumber - f.DayNumber > 400) throw new DomainException("ads.range_too_long", "Choose at most 400 days.");
        return (f, t);
    }
}
