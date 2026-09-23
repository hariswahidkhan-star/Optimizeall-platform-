using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Ads;

/// <summary>Enforces the client's campaign naming template for internally planned campaigns.</summary>
public sealed class NamingService(AppDbContext db)
{
    public async Task<NamingCheck?> CheckAsync(AdAccount account, string name, string? objective, DateOnly? start, CancellationToken ct)
    {
        var settings = await db.Set<AdsClientSettings>().AsNoTracking().FirstOrDefaultAsync(s => s.ClientAccountId == account.ClientAccountId, ct);
        if (settings?.CampaignNamingTemplate is not { Length: > 0 } template) return null;
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == account.ClientAccountId, ct);
        return NamingConvention.Check(template, name, Values(client, account.Platform, objective, start));
    }

    public async Task EnforceAsync(AdAccount account, string name, string? objective, DateOnly? start, CancellationToken ct)
    {
        var check = await CheckAsync(account, name, objective, start, ct);
        if (check is { Compliant: false })
            throw new DomainException("ads.naming_convention", string.Join(" ", check.Problems) + (check.Expected is null ? "" : $" Example: {check.Expected}"),
                errors: new Dictionary<string, string[]> { ["name"] = check.Problems.ToArray() });
    }

    public static Dictionary<string, string?> Values(ClientAccount client, AdPlatform platform, string? objective, DateOnly? start) => new()
    {
        ["client"] = client.Slug,
        ["platform"] = PlatformSlug(platform),
        ["country"] = client.CountryCode.ToLowerInvariant(),
        ["objective"] = objective,
        ["yyyy"] = start?.ToString("yyyy"),
        ["mm"] = start?.ToString("MM"),
        ["yyyymm"] = start?.ToString("yyyyMM"),
    };

    public static string PlatformSlug(AdPlatform platform) => platform switch
    {
        AdPlatform.GoogleAds => "google",
        AdPlatform.MetaAds => "meta",
        AdPlatform.TikTokAds => "tiktok",
        AdPlatform.LinkedInAds => "linkedin",
        AdPlatform.MicrosoftAds => "microsoft",
        AdPlatform.SnapchatAds => "snapchat",
        _ => platform.ToString().ToLowerInvariant(),
    };
}

// ---------------------------------------------------------------- DTOs

public sealed record AdsSettingsDto(Guid ClientAccountId, string? CampaignNamingTemplate, string DefaultUtmSource, string DefaultUtmMedium, bool LowercaseUtm,
    IReadOnlyList<string> Tokens, Guid ConcurrencyStamp);

public sealed class AdsSettingsInput
{
    [MaxLength(300)] public string? CampaignNamingTemplate { get; set; }
    [Required, MaxLength(100)] public string DefaultUtmSource { get; set; } = "{platform}";
    [Required, MaxLength(100)] public string DefaultUtmMedium { get; set; } = "cpc";
    public bool LowercaseUtm { get; set; } = true;
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class NamingCheckInput
{
    [Required] public AdPlatform? Platform { get; set; }
    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? Objective { get; set; }
    public DateOnly? Month { get; set; }
}

public sealed class NamingGenerateInput
{
    [Required] public AdPlatform? Platform { get; set; }
    [MaxLength(100)] public string? Objective { get; set; }
    [MaxLength(100)] public string? Audience { get; set; }
    [MaxLength(100)] public string? Name { get; set; }
    public DateOnly? Month { get; set; }
}

public sealed record NamingResultDto(bool TemplateConfigured, bool Compliant, string? Template, string? Suggested, IReadOnlyList<string> Problems);

public sealed class UtmInput
{
    [Required, MaxLength(2000)] public string Url { get; set; } = string.Empty;
    [MaxLength(100)] public string? Source { get; set; }
    [MaxLength(100)] public string? Medium { get; set; }
    [Required, MaxLength(150)] public string Campaign { get; set; } = string.Empty;
    [MaxLength(150)] public string? Term { get; set; }
    [MaxLength(150)] public string? Content { get; set; }
    public AdPlatform? Platform { get; set; }
    public bool Save { get; set; } = true;
}

public sealed record UtmLinkDto(Guid? Id, string BaseUrl, string Source, string Medium, string Campaign, string? Term, string? Content, string TaggedUrl, DateTime? CreatedAt);

public sealed class MediaPlanLineInput
{
    [Required] public AdPlatform? Platform { get; set; }
    [Required, MaxLength(150)] public string Channel { get; set; } = string.Empty;
    [MaxLength(100)] public string? Objective { get; set; }
    [Range(0, 1_000_000_000)] public decimal PlannedBudget { get; set; }
    [Required] public DateOnly? FlightStart { get; set; }
    [Required] public DateOnly? FlightEnd { get; set; }
    [Required, RegularExpression("^(CPA|ROAS|CPC|CPM|CTR|Conversions|Clicks|Impressions)$")] public string KpiName { get; set; } = "CPA";
    [Range(0, 1_000_000_000)] public decimal? KpiTarget { get; set; }
    [Range(0, long.MaxValue)] public long? PlannedImpressions { get; set; }
    [Range(0, long.MaxValue)] public long? PlannedClicks { get; set; }
    [Range(0, 1_000_000_000)] public decimal? PlannedConversions { get; set; }
}

public sealed class MediaPlanInput
{
    [Required] public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required] public DateOnly? Month { get; set; }
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = "USD";
    public MediaPlanStatus Status { get; set; } = MediaPlanStatus.Draft;
    [MaxLength(4000)] public string? Notes { get; set; }
    [MaxLength(50)] public List<MediaPlanLineInput> Lines { get; set; } = new();
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record MediaPlanLineDto(Guid Id, AdPlatform Platform, string Channel, string? Objective, decimal PlannedBudget, DateOnly FlightStart, DateOnly FlightEnd,
    string KpiName, decimal? KpiTarget, long? PlannedImpressions, long? PlannedClicks, decimal? PlannedConversions);

public sealed record MediaPlanDto(Guid Id, Guid ClientAccountId, string ClientName, string Name, DateOnly Month, string Currency, MediaPlanStatus Status, string? Notes,
    decimal PlannedTotal, IReadOnlyList<MediaPlanLineDto> Lines, Guid ConcurrencyStamp);

public sealed record PlanActualLineDto(MediaPlanLineDto Line, decimal ActualSpend, decimal? SpendVsPlan, long Impressions, long Clicks, decimal Conversions,
    decimal ConversionValue, decimal? KpiActual, bool? KpiMet, string KpiDirection);

public sealed record PlanActualDto(MediaPlanDto Plan, IReadOnlyList<PlanActualLineDto> Lines, decimal PlannedTotal, decimal ActualTotal, IReadOnlyList<string> FxMissing,
    string SourceLabel, string Note);

public sealed class CreativeInput
{
    [Required] public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required] public AdPlatform? Platform { get; set; }
    public AdCreativeFormat Format { get; set; } = AdCreativeFormat.SingleImage;
    [MaxLength(20)] public List<string> Headlines { get; set; } = new();
    [MaxLength(10)] public List<string> Descriptions { get; set; } = new();
    [MaxLength(2000)] public string? PrimaryText { get; set; }
    [MaxLength(60)] public string? CallToAction { get; set; }
    [MaxLength(2000)] public string? FinalUrl { get; set; }
    [MaxLength(20)] public List<Guid> MediaAssetIds { get; set; } = new();
    public Guid? CampaignId { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record CreativeDto(Guid Id, Guid ClientAccountId, string Name, AdPlatform Platform, AdCreativeFormat Format, IReadOnlyList<string> Headlines,
    IReadOnlyList<string> Descriptions, string? PrimaryText, string? CallToAction, string? FinalUrl, IReadOnlyList<Guid> MediaAssetIds, Guid? CampaignId,
    SocialPostStatus Status, string? ReviewNote, IReadOnlyList<CopyIssue> Issues, IReadOnlyList<CopyLimit> Limits, IReadOnlyList<string> AllowedActions,
    DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class CreativeActionInput
{
    [MaxLength(2000)] public string? Note { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ExperimentVariantInput
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public bool IsControl { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    [Range(0, long.MaxValue)] public long Impressions { get; set; }
    [Range(0, long.MaxValue)] public long Clicks { get; set; }
    [Range(0, 1_000_000_000)] public decimal Conversions { get; set; }
    [Range(0, 1_000_000_000)] public decimal Spend { get; set; }
}

public sealed class ExperimentInput
{
    [Required] public Guid? ClientAccountId { get; set; }
    public Guid? AdAccountId { get; set; }
    public Guid? CampaignId { get; set; }
    [Required] public AdPlatform? Platform { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(2000)] public string Hypothesis { get; set; } = string.Empty;
    [Required, RegularExpression("^(ConversionRate|Ctr)$")] public string Metric { get; set; } = "ConversionRate";
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public AdExperimentStatus Status { get; set; } = AdExperimentStatus.Planned;
    [MaxLength(2000)] public string? Result { get; set; }
    [MaxLength(100)] public string? WinnerVariant { get; set; }
    [Range(0, 1)] public decimal? EnteredPValue { get; set; }
    [MinLength(2), MaxLength(6)] public List<ExperimentVariantInput> Variants { get; set; } = new();
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record ExperimentVariantDto(Guid Id, string Name, bool IsControl, string? Description, long Impressions, long Clicks, decimal Conversions,
    decimal Spend, double? Rate, double? ZScore, double? PValue, bool? Significant);

public sealed record ExperimentDto(Guid Id, Guid ClientAccountId, Guid? AdAccountId, Guid? CampaignId, AdPlatform Platform, string Name, string Hypothesis,
    string Metric, DateOnly? StartDate, DateOnly? EndDate, AdExperimentStatus Status, string? Result, string? WinnerVariant, decimal? EnteredPValue,
    IReadOnlyList<ExperimentVariantDto> Variants, string SignificanceSource, string Method, Guid ConcurrencyStamp);

// ---------------------------------------------------------------- controller

/// <summary>Naming conventions, UTM builder, media plans (actual vs plan), creative & copy library and experiments log.</summary>
[ApiController]
[Route("api/v1/agency/ads")]
[HasPermission(Permissions.AdsManage)]
public sealed class AdsPlanningController(
    AppDbContext db, SocialAccess access, AdsKpiService kpis, NamingService naming, ICurrentUser currentUser, IAuditLogger audit, TimeProvider clock)
    : ControllerBase
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------ settings & naming

    [HttpGet("clients/{clientId:guid}/settings")]
    public async Task<AdsSettingsDto> Settings(Guid clientId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var s = await db.Set<AdsClientSettings>().AsNoTracking().FirstOrDefaultAsync(x => x.ClientAccountId == clientId, ct) ?? new AdsClientSettings { ClientAccountId = clientId };
        return new AdsSettingsDto(clientId, s.CampaignNamingTemplate, s.DefaultUtmSource, s.DefaultUtmMedium, s.LowercaseUtm, NamingConvention.Tokens, s.ConcurrencyStamp);
    }

    [HttpPut("clients/{clientId:guid}/settings")]
    public async Task<AdsSettingsDto> UpdateSettings(Guid clientId, AdsSettingsInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        if (!string.IsNullOrWhiteSpace(input.CampaignNamingTemplate) && NamingConvention.TemplateProblems(input.CampaignNamingTemplate) is { Count: > 0 } problems)
            throw new DomainException("ads.naming_template_invalid", string.Join(" ", problems),
                errors: new Dictionary<string, string[]> { ["campaignNamingTemplate"] = problems.ToArray() });
        var s = await db.Set<AdsClientSettings>().FirstOrDefaultAsync(x => x.ClientAccountId == clientId, ct);
        if (s is null)
        {
            s = new AdsClientSettings { ClientAccountId = clientId };
            db.Set<AdsClientSettings>().Add(s);
        }
        else if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != s.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "Settings changed meanwhile; reload.");
            db.Entry(s).Property(x => x.ConcurrencyStamp).OriginalValue = stamp;
        }
        var before = new { s.CampaignNamingTemplate, s.DefaultUtmSource, s.DefaultUtmMedium };
        s.CampaignNamingTemplate = string.IsNullOrWhiteSpace(input.CampaignNamingTemplate) ? null : input.CampaignNamingTemplate.Trim();
        s.DefaultUtmSource = input.DefaultUtmSource.Trim();
        s.DefaultUtmMedium = input.DefaultUtmMedium.Trim();
        s.LowercaseUtm = input.LowercaseUtm;
        s.UpdatedAt = Now;
        audit.Record("ads.settings.updated", nameof(AdsClientSettings), clientId, before, new { s.CampaignNamingTemplate, s.DefaultUtmSource, s.DefaultUtmMedium });
        await db.SaveChangesAsync(ct);

        // Re-flag existing campaigns against the new template.
        var accounts = await db.Set<AdAccount>().AsNoTracking().Where(a => a.ClientAccountId == clientId).ToListAsync(ct);
        foreach (var account in accounts)
        {
            var campaigns = await db.Set<AdCampaign>().Where(c => c.AdAccountId == account.Id).ToListAsync(ct);
            foreach (var c in campaigns) c.NamingCompliant = (await naming.CheckAsync(account, c.Name, c.Objective, c.StartDate, ct))?.Compliant ?? true;
        }
        await db.SaveChangesAsync(ct);
        return new AdsSettingsDto(clientId, s.CampaignNamingTemplate, s.DefaultUtmSource, s.DefaultUtmMedium, s.LowercaseUtm, NamingConvention.Tokens, s.ConcurrencyStamp);
    }

    [HttpPost("clients/{clientId:guid}/naming/check")]
    public async Task<NamingResultDto> CheckName(Guid clientId, NamingCheckInput input, CancellationToken ct)
    {
        var client = await access.ClientAsync(clientId, ct);
        var template = await TemplateAsync(clientId, ct);
        if (template is null) return new NamingResultDto(false, true, null, null, Array.Empty<string>());
        var check = NamingConvention.Check(template, input.Name.Trim(), NamingService.Values(client, input.Platform!.Value, input.Objective, input.Month));
        return new NamingResultDto(true, check.Compliant, template, check.Expected, check.Problems);
    }

    [HttpPost("clients/{clientId:guid}/naming/generate")]
    public async Task<NamingResultDto> Generate(Guid clientId, NamingGenerateInput input, CancellationToken ct)
    {
        var client = await access.ClientAsync(clientId, ct);
        var template = await TemplateAsync(clientId, ct) ?? throw DomainException.Conflict("ads.naming_template_missing", "Set a naming template for this client first.");
        var values = NamingService.Values(client, input.Platform!.Value, input.Objective, input.Month ?? DateOnly.FromDateTime(Now));
        values["audience"] = input.Audience;
        values["name"] = input.Name;
        var name = NamingConvention.Build(template, values);
        var check = NamingConvention.Check(template, name, NamingService.Values(client, input.Platform!.Value, input.Objective, input.Month ?? DateOnly.FromDateTime(Now)));
        return new NamingResultDto(true, check.Compliant, template, name, check.Problems);
    }

    // ------------------------------ UTM builder

    [HttpPost("clients/{clientId:guid}/utm")]
    public async Task<UtmLinkDto> BuildUtm(Guid clientId, UtmInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var s = await db.Set<AdsClientSettings>().AsNoTracking().FirstOrDefaultAsync(x => x.ClientAccountId == clientId, ct) ?? new AdsClientSettings();
        var platformSlug = input.Platform is { } p ? NamingService.PlatformSlug(p) : "web";
        var source = string.IsNullOrWhiteSpace(input.Source) ? s.DefaultUtmSource.Replace("{platform}", platformSlug) : input.Source.Trim();
        var medium = string.IsNullOrWhiteSpace(input.Medium) ? s.DefaultUtmMedium : input.Medium.Trim();
        var tagged = UtmBuilder.Build(input.Url, new UtmParameters(source, medium, input.Campaign, input.Term, input.Content), s.LowercaseUtm);
        if (tagged.Length > 3000) throw new DomainException("utm.too_long", "The tagged URL is longer than 3,000 characters.");
        if (!input.Save) return new UtmLinkDto(null, input.Url.Trim(), source, medium, input.Campaign, input.Term, input.Content, tagged, null);
        var link = new AdUtmLink
        {
            ClientAccountId = clientId, BaseUrl = input.Url.Trim(), Source = Cap(source, 100), Medium = Cap(medium, 100), Campaign = Cap(input.Campaign, 150),
            Term = input.Term, Content = input.Content, TaggedUrl = tagged, CreatedByUserId = currentUser.Id, CreatedAt = Now,
        };
        db.Set<AdUtmLink>().Add(link);
        await db.SaveChangesAsync(ct);
        return new UtmLinkDto(link.Id, link.BaseUrl, link.Source, link.Medium, link.Campaign, link.Term, link.Content, link.TaggedUrl, link.CreatedAt);
    }

    [HttpGet("clients/{clientId:guid}/utm")]
    public async Task<IReadOnlyList<UtmLinkDto>> UtmHistory(Guid clientId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        return await db.Set<AdUtmLink>().AsNoTracking().Where(l => l.ClientAccountId == clientId).OrderByDescending(l => l.CreatedAt).Take(100)
            .Select(l => new UtmLinkDto(l.Id, l.BaseUrl, l.Source, l.Medium, l.Campaign, l.Term, l.Content, l.TaggedUrl, l.CreatedAt)).ToListAsync(ct);
    }

    // ------------------------------ media plans

    [HttpGet("media-plans")]
    public async Task<IReadOnlyList<MediaPlanDto>> Plans([FromQuery] Guid? clientId, [FromQuery] DateOnly? month, CancellationToken ct)
    {
        var q = (await access.ScopedAsync<MediaPlan>(p => p.ClientAccountId, ct)).AsNoTracking().Include(p => p.Lines).AsQueryable();
        if (clientId is { } c) q = q.Where(p => p.ClientAccountId == c);
        if (month is { } m) { var first = new DateOnly(m.Year, m.Month, 1); q = q.Where(p => p.Month == first); }
        var plans = await q.OrderByDescending(p => p.Month).Take(200).ToListAsync(ct);
        var names = await ClientNamesAsync(plans.Select(p => p.ClientAccountId), ct);
        return plans.Select(p => PlanDto(p, names.GetValueOrDefault(p.ClientAccountId, ""))).ToList();
    }

    [HttpPost("media-plans")]
    public async Task<MediaPlanDto> CreatePlan(MediaPlanInput input, CancellationToken ct)
    {
        var client = await access.ClientAsync(input.ClientAccountId!.Value, ct);
        var plan = new MediaPlan { ClientAccountId = client.Id };
        ApplyPlan(plan, input);
        db.Set<MediaPlan>().Add(plan);
        audit.Record("ads.media_plan.created", nameof(MediaPlan), plan.Id, after: new { plan.Name, plan.Month, total = plan.Lines.Sum(l => l.PlannedBudget) });
        await db.SaveChangesAsync(ct);
        return PlanDto(plan, client.Name);
    }

    [HttpPut("media-plans/{id:guid}")]
    public async Task<MediaPlanDto> UpdatePlan(Guid id, MediaPlanInput input, CancellationToken ct)
    {
        var q = await access.ScopedAsync<MediaPlan>(p => p.ClientAccountId, ct);
        var plan = await q.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Media plan");
        if (input.ClientAccountId != plan.ClientAccountId) throw new DomainException("ads.client_mismatch", "A plan cannot move to another client.");
        if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != plan.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "The plan changed meanwhile; reload.");
            db.Entry(plan).Property(p => p.ConcurrencyStamp).OriginalValue = stamp;
        }
        foreach (var line in plan.Lines.ToList()) db.Remove(line);
        plan.Lines.Clear();
        ApplyPlan(plan, input);
        foreach (var line in plan.Lines) db.Add(line);
        audit.Record("ads.media_plan.updated", nameof(MediaPlan), plan.Id, after: new { plan.Name, plan.Status, total = plan.Lines.Sum(l => l.PlannedBudget) });
        await db.SaveChangesAsync(ct);
        var name = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == plan.ClientAccountId).Select(c => c.Name).FirstAsync(ct);
        return PlanDto(plan, name);
    }

    /// <summary>Actual vs plan: per line, actual spend of the platform's accounts during the flight (converted to the plan currency) and the KPI.</summary>
    [HttpGet("media-plans/{id:guid}/actuals")]
    public async Task<PlanActualDto> Actuals(Guid id, CancellationToken ct)
    {
        var q = await access.ScopedAsync<MediaPlan>(p => p.ClientAccountId, ct);
        var plan = await q.AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Media plan");
        var name = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == plan.ClientAccountId).Select(c => c.Name).FirstAsync(ct);
        var result = new List<PlanActualLineDto>();
        var missingAll = new List<string>();
        var sources = new List<AdMetricSource>();
        foreach (var line in plan.Lines.OrderBy(l => l.FlightStart))
        {
            var rows = await kpis.CampaignRows(line.FlightStart, line.FlightEnd)
                .Where(m => m.ClientAccountId == plan.ClientAccountId && m.Platform == line.Platform).ToListAsync(ct);
            sources.AddRange(rows.Select(r => r.Source));
            var (totals, missing) = await kpis.ConvertAsync(rows, plan.Currency, line.FlightEnd, ct);
            missingAll.AddRange(missing.Where(m => !missingAll.Contains(m)));
            var k = AdKpis.From(totals);
            var (actual, higherIsBetter) = line.KpiName switch
            {
                "CPA" => (k.Cpa, false),
                "ROAS" => (k.Roas, true),
                "CPC" => (k.Cpc, false),
                "CPM" => (k.Cpm, false),
                "CTR" => (k.Ctr, true),
                "Conversions" => ((decimal?)totals.Conversions, true),
                "Clicks" => ((decimal?)totals.Clicks, true),
                _ => ((decimal?)totals.Impressions, true),
            };
            bool? met = line.KpiTarget is { } target && actual is { } a ? (higherIsBetter ? a >= target : a <= target) : null;
            result.Add(new PlanActualLineDto(LineDto(line), totals.Spend, AdKpis.Ratio(totals.Spend, line.PlannedBudget, 4), totals.Impressions, totals.Clicks,
                totals.Conversions, totals.ConversionValue, actual, met, higherIsBetter ? "Higher is better" : "Lower is better"));
        }
        return new PlanActualDto(PlanDto(plan, name), result, plan.Lines.Sum(l => l.PlannedBudget), result.Sum(r => r.ActualSpend), missingAll,
            AdsKpiService.Label(sources),
            "Actuals include every campaign of the line's platform for this client during the flight dates.");
    }

    // ------------------------------ creatives

    [HttpGet("creatives")]
    public async Task<IReadOnlyList<CreativeDto>> Creatives([FromQuery] Guid? clientId, [FromQuery] SocialPostStatus? status, CancellationToken ct)
    {
        var q = (await access.ScopedAsync<AdCreative>(c => c.ClientAccountId, ct)).AsNoTracking();
        if (clientId is { } c) q = q.Where(x => x.ClientAccountId == c);
        if (status is { } s) q = q.Where(x => x.Status == s);
        var list = await q.OrderByDescending(x => x.UpdatedAt).Take(300).ToListAsync(ct);
        var result = new List<CreativeDto>();
        foreach (var creative in list) result.Add(await CreativeDtoAsync(creative, ct));
        return result;
    }

    [HttpPost("creatives")]
    public async Task<CreativeDto> CreateCreative(CreativeInput input, CancellationToken ct)
    {
        var client = await access.ClientAsync(input.ClientAccountId!.Value, ct);
        var creative = new AdCreative { ClientAccountId = client.Id, CreatedByUserId = currentUser.Id };
        await ApplyCreativeAsync(creative, input, ct);
        db.Set<AdCreative>().Add(creative);
        audit.Record("ads.creative.created", nameof(AdCreative), creative.Id, after: new { creative.Name, creative.Platform });
        await db.SaveChangesAsync(ct);
        return await CreativeDtoAsync(creative, ct);
    }

    [HttpPut("creatives/{id:guid}")]
    public async Task<CreativeDto> UpdateCreative(Guid id, CreativeInput input, CancellationToken ct)
    {
        var creative = await access.OwnedAsync<AdCreative>(id, c => c.ClientAccountId, "Creative", ct);
        if (input.ClientAccountId != creative.ClientAccountId) throw new DomainException("ads.client_mismatch", "A creative cannot move to another client.");
        if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != creative.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "The creative changed meanwhile; reload.");
            db.Entry(creative).Property(c => c.ConcurrencyStamp).OriginalValue = stamp;
        }
        await ApplyCreativeAsync(creative, input, ct);
        if (creative.Status != SocialPostStatus.Draft)
        {
            creative.Status = SocialPostStatus.Draft;
            creative.ApprovedAt = null;
            creative.ApprovedByUserId = null;
            creative.ReviewNote = "Edited after review; needs approval again.";
        }
        audit.Record("ads.creative.updated", nameof(AdCreative), creative.Id, after: new { creative.Name, creative.Status });
        await db.SaveChangesAsync(ct);
        return await CreativeDtoAsync(creative, ct);
    }

    /// <summary>
    /// Approval workflow (same states as social posts): submit, approve (to ClientApproval when the client requires it),
    /// request-changes, and record-client-approval (a note of the client's written approval, audited).
    /// </summary>
    [HttpPost("creatives/{id:guid}/{step:regex(^(submit|approve|request-changes|record-client-approval)$)}")]
    public async Task<CreativeDto> CreativeAction(Guid id, string step, CreativeActionInput input, CancellationToken ct)
    {
        var creative = await access.OwnedAsync<AdCreative>(id, c => c.ClientAccountId, "Creative", ct);
        if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != creative.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "The creative changed meanwhile; reload.");
            db.Entry(creative).Property(c => c.ConcurrencyStamp).OriginalValue = stamp;
        }
        var settings = await access.SettingsAsync(creative.ClientAccountId, ct);
        var action = step;
        var workflowAction = action switch
        {
            "submit" => WorkflowAction.Submit,
            "approve" => WorkflowAction.ApproveInternal,
            "request-changes" => creative.Status == SocialPostStatus.ClientApproval ? WorkflowAction.ClientRequestChanges : WorkflowAction.RequestChanges,
            _ => WorkflowAction.ClientApprove,
        };
        if (workflowAction is WorkflowAction.Submit or WorkflowAction.ApproveInternal
            && AdCopyLimits.Validate(creative.Platform, creative.Headlines, creative.Descriptions, creative.PrimaryText).Any(i => i.Severity == "Error"))
            throw new DomainException("ads.copy_invalid", "Fix the copy errors before submitting.");
        if (workflowAction is WorkflowAction.RequestChanges or WorkflowAction.ClientRequestChanges or WorkflowAction.ClientApprove && string.IsNullOrWhiteSpace(input.Note))
            throw new DomainException("ads.note_required", "Add a note (what should change, or how the client approved).");
        var from = creative.Status;
        creative.Status = PostWorkflow.Next(from, workflowAction, settings.RequireClientApproval);
        creative.ReviewNote = input.Note;
        if (creative.Status == SocialPostStatus.Approved)
        {
            creative.ApprovedAt = Now;
            creative.ApprovedByUserId = currentUser.Id;
        }
        audit.Record($"ads.creative.{action}", nameof(AdCreative), creative.Id, new { Status = from }, new { creative.Status }, input.Note);
        await db.SaveChangesAsync(ct);
        return await CreativeDtoAsync(creative, ct);
    }

    [HttpGet("copy-limits")]
    public IReadOnlyDictionary<AdPlatform, IReadOnlyList<CopyLimit>> CopyLimits() =>
        Enum.GetValues<AdPlatform>().ToDictionary(p => p, AdCopyLimits.For);

    // ------------------------------ experiments

    [HttpGet("experiments")]
    public async Task<IReadOnlyList<ExperimentDto>> Experiments([FromQuery] Guid? clientId, CancellationToken ct)
    {
        var q = (await access.ScopedAsync<AdExperiment>(e => e.ClientAccountId, ct)).AsNoTracking().Include(e => e.Variants).AsQueryable();
        if (clientId is { } c) q = q.Where(e => e.ClientAccountId == c);
        return (await q.OrderByDescending(e => e.CreatedAt).Take(200).ToListAsync(ct)).Select(ExperimentDtoFrom).ToList();
    }

    [HttpPost("experiments")]
    public async Task<ExperimentDto> CreateExperiment(ExperimentInput input, CancellationToken ct)
    {
        var client = await access.ClientAsync(input.ClientAccountId!.Value, ct);
        var e = new AdExperiment { ClientAccountId = client.Id };
        await ApplyExperimentAsync(e, input, ct);
        db.Set<AdExperiment>().Add(e);
        audit.Record("ads.experiment.created", nameof(AdExperiment), e.Id, after: new { e.Name, e.Platform });
        await db.SaveChangesAsync(ct);
        return ExperimentDtoFrom(e);
    }

    [HttpPut("experiments/{id:guid}")]
    public async Task<ExperimentDto> UpdateExperiment(Guid id, ExperimentInput input, CancellationToken ct)
    {
        var q = await access.ScopedAsync<AdExperiment>(e => e.ClientAccountId, ct);
        var e = await q.Include(x => x.Variants).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Experiment");
        if (input.ClientAccountId != e.ClientAccountId) throw new DomainException("ads.client_mismatch", "An experiment cannot move to another client.");
        if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != e.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "The experiment changed meanwhile; reload.");
            db.Entry(e).Property(x => x.ConcurrencyStamp).OriginalValue = stamp;
        }
        foreach (var v in e.Variants.ToList()) db.Remove(v);
        e.Variants.Clear();
        await ApplyExperimentAsync(e, input, ct);
        foreach (var v in e.Variants) db.Add(v);
        audit.Record("ads.experiment.updated", nameof(AdExperiment), e.Id, after: new { e.Name, e.Status, e.WinnerVariant });
        await db.SaveChangesAsync(ct);
        return ExperimentDtoFrom(e);
    }

    // ------------------------------ helpers

    private async Task<string?> TemplateAsync(Guid clientId, CancellationToken ct) =>
        await db.Set<AdsClientSettings>().AsNoTracking().Where(s => s.ClientAccountId == clientId).Select(s => s.CampaignNamingTemplate).FirstOrDefaultAsync(ct);

    private static void ApplyPlan(MediaPlan plan, MediaPlanInput input)
    {
        var currency = Money.Normalize(input.Currency);
        if (!Money.IsSupported(currency)) throw new DomainException("ads.currency_unsupported", $"Currency {currency} is not supported.");
        var month = new DateOnly(input.Month!.Value.Year, input.Month.Value.Month, 1);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        plan.Name = input.Name.Trim();
        plan.Month = month;
        plan.Currency = currency;
        plan.Status = input.Status;
        plan.Notes = input.Notes;
        foreach (var l in input.Lines)
        {
            if (l.FlightEnd < l.FlightStart) throw new DomainException("ads.flight_invalid", $"{l.Channel}: the flight end is before its start.");
            if (l.FlightStart > monthEnd || l.FlightEnd < month)
                throw new DomainException("ads.flight_outside_month", $"{l.Channel}: the flight must overlap {month:MMMM yyyy}.");
            plan.Lines.Add(new MediaPlanLine
            {
                PlanId = plan.Id, ClientAccountId = plan.ClientAccountId, Platform = l.Platform!.Value, Channel = l.Channel.Trim(), Objective = l.Objective,
                PlannedBudget = Money.Round(l.PlannedBudget, currency), FlightStart = l.FlightStart!.Value, FlightEnd = l.FlightEnd!.Value, KpiName = l.KpiName,
                KpiTarget = l.KpiTarget, PlannedImpressions = l.PlannedImpressions, PlannedClicks = l.PlannedClicks, PlannedConversions = l.PlannedConversions,
            });
        }
    }

    private static MediaPlanLineDto LineDto(MediaPlanLine l) => new(l.Id, l.Platform, l.Channel, l.Objective, l.PlannedBudget, l.FlightStart, l.FlightEnd,
        l.KpiName, l.KpiTarget, l.PlannedImpressions, l.PlannedClicks, l.PlannedConversions);

    private static MediaPlanDto PlanDto(MediaPlan p, string clientName) => new(p.Id, p.ClientAccountId, clientName, p.Name, p.Month, p.Currency, p.Status, p.Notes,
        p.Lines.Sum(l => l.PlannedBudget), p.Lines.OrderBy(l => l.FlightStart).Select(LineDto).ToList(), p.ConcurrencyStamp);

    private async Task ApplyCreativeAsync(AdCreative c, CreativeInput input, CancellationToken ct)
    {
        if (input.MediaAssetIds.Count > 0)
        {
            var ids = input.MediaAssetIds.Distinct().ToList();
            var found = await db.Set<SocialMediaAsset>().CountAsync(m => m.ClientAccountId == c.ClientAccountId && ids.Contains(m.Id), ct);
            if (found != ids.Count) throw new DomainException("ads.media_not_found", "Link media from this client's library.");
        }
        if (input.CampaignId is { } cid && !await db.Set<AdCampaign>().AnyAsync(x => x.Id == cid && x.ClientAccountId == c.ClientAccountId, ct))
            throw DomainException.NotFound("Campaign");
        if (!string.IsNullOrWhiteSpace(input.FinalUrl) && !PostValidator.IsHttpUrl(input.FinalUrl))
            throw new DomainException("ads.invalid_url", "The final URL must be an absolute http(s) URL.");
        c.Name = input.Name.Trim();
        c.Platform = input.Platform!.Value;
        c.Format = input.Format;
        c.Headlines = input.Headlines.Select(h => h.Trim()).Where(h => h.Length > 0).ToList();
        c.Descriptions = input.Descriptions.Select(h => h.Trim()).Where(h => h.Length > 0).ToList();
        c.PrimaryText = string.IsNullOrWhiteSpace(input.PrimaryText) ? null : input.PrimaryText.Trim();
        c.CallToAction = input.CallToAction;
        c.FinalUrl = string.IsNullOrWhiteSpace(input.FinalUrl) ? null : input.FinalUrl.Trim();
        c.MediaAssetIds = input.MediaAssetIds.Distinct().ToList();
        c.CampaignId = input.CampaignId;
    }

    private async Task<CreativeDto> CreativeDtoAsync(AdCreative c, CancellationToken ct)
    {
        var settings = await access.SettingsAsync(c.ClientAccountId, ct);
        var allowed = new List<string>();
        if (c.Status == SocialPostStatus.Draft) allowed.Add("submit");
        if (c.Status == SocialPostStatus.InternalReview) allowed.AddRange(new[] { "approve", "request-changes" });
        if (c.Status == SocialPostStatus.ClientApproval) allowed.AddRange(new[] { "record-client-approval", "request-changes" });
        return new CreativeDto(c.Id, c.ClientAccountId, c.Name, c.Platform, c.Format, c.Headlines, c.Descriptions, c.PrimaryText, c.CallToAction, c.FinalUrl,
            c.MediaAssetIds, c.CampaignId, c.Status, c.ReviewNote, AdCopyLimits.Validate(c.Platform, c.Headlines, c.Descriptions, c.PrimaryText),
            AdCopyLimits.For(c.Platform), settings.RequireClientApproval || c.Status == SocialPostStatus.ClientApproval ? allowed : allowed, c.UpdatedAt, c.ConcurrencyStamp);
    }

    private async Task ApplyExperimentAsync(AdExperiment e, ExperimentInput input, CancellationToken ct)
    {
        if (input.AdAccountId is { } aid && !await db.Set<AdAccount>().AnyAsync(a => a.Id == aid && a.ClientAccountId == e.ClientAccountId, ct))
            throw DomainException.NotFound("Ad account");
        if (input.CampaignId is { } cid && !await db.Set<AdCampaign>().AnyAsync(c => c.Id == cid && c.ClientAccountId == e.ClientAccountId, ct))
            throw DomainException.NotFound("Campaign");
        if (input.Variants.Count(v => v.IsControl) > 1) throw new DomainException("ads.experiment_controls", "Mark at most one variant as the control.");
        foreach (var v in input.Variants)
        {
            if (v.Clicks > v.Impressions && v.Impressions > 0) throw new DomainException("ads.experiment_counts", $"{v.Name}: clicks exceed impressions.");
            if (v.Conversions > v.Clicks && v.Clicks > 0 && input.Metric == "ConversionRate")
                throw new DomainException("ads.experiment_counts", $"{v.Name}: conversions exceed clicks.");
        }
        e.AdAccountId = input.AdAccountId;
        e.CampaignId = input.CampaignId;
        e.Platform = input.Platform!.Value;
        e.Name = input.Name.Trim();
        e.Hypothesis = input.Hypothesis.Trim();
        e.Metric = input.Metric;
        e.StartDate = input.StartDate;
        e.EndDate = input.EndDate;
        e.Status = input.Status;
        e.Result = input.Result;
        e.WinnerVariant = input.WinnerVariant;
        e.EnteredPValue = input.EnteredPValue;
        var hasControl = input.Variants.Any(v => v.IsControl);
        for (var i = 0; i < input.Variants.Count; i++)
        {
            var v = input.Variants[i];
            e.Variants.Add(new AdExperimentVariant
            {
                ExperimentId = e.Id, Name = v.Name.Trim(), IsControl = v.IsControl || (!hasControl && i == 0), Description = v.Description,
                Impressions = v.Impressions, Clicks = v.Clicks, Conversions = v.Conversions, Spend = v.Spend,
            });
        }
    }

    /// <summary>Two-proportion z-test of each variant against the control (Domain/Marketing ExperimentMath).</summary>
    public static ExperimentDto ExperimentDtoFrom(AdExperiment e)
    {
        var control = e.Variants.FirstOrDefault(v => v.IsControl) ?? e.Variants.FirstOrDefault();
        (long Successes, long Trials) Counts(AdExperimentVariant v) => e.Metric == "Ctr"
            ? (v.Clicks, v.Impressions)
            : ((long)Math.Round(v.Conversions), v.Clicks);
        var variants = e.Variants.Select(v =>
        {
            var (x, n) = Counts(v);
            var rate = ExperimentMath.Rate(x, n);
            if (control is null || v.Id == control.Id) return new ExperimentVariantDto(v.Id, v.Name, v.IsControl, v.Description, v.Impressions, v.Clicks, v.Conversions, v.Spend, rate, null, null, null);
            var (cx, cn) = Counts(control);
            var test = cx <= cn && x <= n ? ExperimentMath.TwoProportionZTest(cx, cn, x, n) : new ProportionTest(null, null);
            bool? significant = test.PValue is null ? null
                : test.PValue < ExperimentMath.SignificanceLevel && cn >= ExperimentMath.MinAssignmentsForConclusion && n >= ExperimentMath.MinAssignmentsForConclusion;
            return new ExperimentVariantDto(v.Id, v.Name, v.IsControl, v.Description, v.Impressions, v.Clicks, v.Conversions, v.Spend, rate, test.Z, test.PValue, significant);
        }).ToList();
        return new ExperimentDto(e.Id, e.ClientAccountId, e.AdAccountId, e.CampaignId, e.Platform, e.Name, e.Hypothesis, e.Metric, e.StartDate, e.EndDate, e.Status,
            e.Result, e.WinnerVariant, e.EnteredPValue, variants, e.EnteredPValue is not null ? "Entered (platform report)" : "Computed",
            $"Two-sided two-proportion z-test vs the control ({(e.Metric == "Ctr" ? "clicks ÷ impressions" : "conversions ÷ clicks")}); significant when p < " +
            $"{ExperimentMath.SignificanceLevel} and both groups have at least {ExperimentMath.MinAssignmentsForConclusion} trials.", e.ConcurrencyStamp);
    }

    private async Task<Dictionary<Guid, string>> ClientNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<ClientAccount>().AsNoTracking().Where(c => list.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
    }

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max];
}
