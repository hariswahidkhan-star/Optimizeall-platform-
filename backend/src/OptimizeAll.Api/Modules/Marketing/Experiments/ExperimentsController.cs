using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Experiments;

public sealed class VariantRequest
{
    [Required, RegularExpression("^[A-D]$", ErrorMessage = "Variant keys are A, B, C or D.")]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 100)]
    public int Weight { get; set; } = 50;

    [MaxLength(200)] public string? Title { get; set; }
    [MaxLength(20000)] public string? Instructions { get; set; }
    public Guid? AssetId { get; set; }
    [MaxLength(200)] public string? LandingHeadline { get; set; }
    [MaxLength(20000)] public string? LandingBody { get; set; }
}

public sealed class ExperimentRequest
{
    [Required]
    public Guid? CampaignId { get; set; }

    [Required, MinLength(2), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Hypothesis { get; set; }

    [Required]
    public ExperimentElement? Element { get; set; }

    [Required, MinLength(2), MaxLength(4)]
    public List<VariantRequest> Variants { get; set; } = new();

    /// <summary>For updates: the stamp the client last saw (stale writes get 409 concurrency.conflict).</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class CompleteExperimentRequest
{
    public Guid? WinningVariantId { get; set; }
}

public sealed record VariantDto(
    Guid Id, string Key, string Name, int Weight, string? Title, string? Instructions, Guid? AssetId,
    string? LandingHeadline, string? LandingBody);

public sealed record ExperimentDto(
    Guid Id, Guid CampaignId, string Name, string? Hypothesis, ExperimentElement Element, ExperimentStatus Status,
    DateTime? StartedAt, DateTime? EndedAt, Guid? WinningVariantId, IReadOnlyList<VariantDto> Variants,
    Guid ConcurrencyStamp, DateTime CreatedAt, DateTime UpdatedAt);

public sealed class ExperimentListQuery : PageQuery
{
    public Guid? CampaignId { get; set; }
    public ExperimentStatus? Status { get; set; }
}

public sealed record AssetOverlayDto(Guid Id, string? Url, string Title, CampaignAssetType Type);

public sealed record ParticipantVariantDto(
    Guid ExperimentId, ExperimentElement Element, Guid VariantId, string Key, string? Title, string? Instructions,
    AssetOverlayDto? Asset, string? LandingHeadline, string? LandingBody);

public sealed record VariantResultDto(
    Guid VariantId, string Key, string Name, int Weight, int Assigned, int Submissions, int Approved,
    int TotalSubmissions, int TotalApproved, double? SubmissionRate, double? ApprovalRate);

public sealed record VariantComparisonDto(
    string VariantKey, string ControlKey, double? AbsoluteLift, double? RelativeLift, double? ZScore, double? PValue,
    bool Significant, string Note);

public sealed record ExperimentResultsDto(
    Guid ExperimentId, ExperimentStatus Status, string Metric, string Measurement, IReadOnlyList<VariantResultDto> Variants,
    IReadOnlyList<VariantComparisonDto> Comparisons, string Method);

[ApiController]
public sealed class ExperimentsController(
    AppDbContext db, ExperimentAssignmentService assignments, IAuditLogger audit, ICurrentUser currentUser, TimeProvider clock)
    : ControllerBase
{
    public const string ControlKey = "A";
    public const string NotEnoughData = "Not enough data for a reliable conclusion";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    [HttpGet("api/v1/marketing/experiments")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<PagedResult<ExperimentDto>> List([FromQuery] ExperimentListQuery query, CancellationToken ct)
    {
        var q = db.Set<Experiment>().AsNoTracking().Include(e => e.Variants).AsQueryable();
        if (query.CampaignId is { } c) q = q.Where(e => e.CampaignId == c);
        if (query.Status is { } s) q = q.Where(e => e.Status == s);
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(e => EF.Functions.Like(e.Name, PagingExtensions.LikePattern(query.Search)));
        var page = await q.OrderByDescending(e => e.CreatedAt).ToPagedAsync(query, ct);
        return new PagedResult<ExperimentDto>(page.Items.Select(ToDto).ToList(), page.Total, page.Page, page.PageSize);
    }

    [HttpGet("api/v1/marketing/experiments/{id:guid}")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<ExperimentDto> Get(Guid id, CancellationToken ct) => ToDto(await LoadAsync(id, tracked: false, ct));

    [HttpPost("api/v1/marketing/experiments")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<ActionResult<ExperimentDto>> Create(ExperimentRequest request, CancellationToken ct)
    {
        await ValidateAsync(request, ct);
        var experiment = new Experiment
        {
            CampaignId = request.CampaignId!.Value,
            Name = request.Name.Trim(),
            Hypothesis = Clean(request.Hypothesis),
            Element = request.Element!.Value,
            CreatedByUserId = currentUser.Id,
        };
        experiment.Variants.AddRange(request.Variants.OrderBy(v => v.Key).Select(v => ToVariant(experiment.Id, v, experiment.Element)));
        db.Set<Experiment>().Add(experiment);
        audit.Record("experiment.created", nameof(Experiment), experiment.Id, after: Snapshot(experiment));
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = experiment.Id }, ToDto(experiment));
    }

    /// <summary>Replaces name, hypothesis, element and variants. Only allowed while the experiment is a Draft.</summary>
    [HttpPut("api/v1/marketing/experiments/{id:guid}")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<ExperimentDto> Update(Guid id, ExperimentRequest request, CancellationToken ct)
    {
        var experiment = await LoadAsync(id, tracked: true, ct);
        CheckStamp(experiment, request.ConcurrencyStamp);
        if (experiment.Status != ExperimentStatus.Draft)
            throw DomainException.Conflict("experiment.not_draft", "Only draft experiments can be edited.");
        if (request.CampaignId != experiment.CampaignId)
            throw new DomainException("experiment.campaign_immutable", "An experiment cannot be moved to another campaign.");
        await ValidateAsync(request, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var before = Snapshot(experiment);
        experiment.Name = request.Name.Trim();
        experiment.Hypothesis = Clean(request.Hypothesis);
        experiment.Element = request.Element!.Value;
        db.Set<ExperimentVariant>().RemoveRange(experiment.Variants);
        experiment.Variants.Clear();
        await db.SaveChangesAsync(ct); // delete old variants first (unique (ExperimentId, Key))
        // Add explicitly: variants have client-generated keys, so navigation fix-up alone would treat them as existing rows.
        var variants = request.Variants.OrderBy(v => v.Key).Select(v => ToVariant(experiment.Id, v, experiment.Element)).ToList();
        db.Set<ExperimentVariant>().AddRange(variants);
        experiment.Variants.AddRange(variants);
        audit.Record("experiment.updated", nameof(Experiment), id, before, Snapshot(experiment));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(experiment);
    }

    [HttpDelete("api/v1/marketing/experiments/{id:guid}")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var experiment = await LoadAsync(id, tracked: true, ct);
        if (experiment.Status != ExperimentStatus.Draft)
            throw DomainException.Conflict("experiment.not_draft", "Only draft experiments can be deleted; complete it instead.");
        db.Set<Experiment>().Remove(experiment);
        audit.Record("experiment.deleted", nameof(Experiment), id, before: Snapshot(experiment));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("api/v1/marketing/experiments/{id:guid}/start")]
    [HasPermission(Permissions.MarketingManage)]
    public Task<ExperimentDto> Start(Guid id, CancellationToken ct) =>
        TransitionAsync(id, ExperimentStatus.Draft, ExperimentStatus.Running, "experiment.started", null, ct);

    [HttpPost("api/v1/marketing/experiments/{id:guid}/pause")]
    [HasPermission(Permissions.MarketingManage)]
    public Task<ExperimentDto> Pause(Guid id, CancellationToken ct) =>
        TransitionAsync(id, ExperimentStatus.Running, ExperimentStatus.Paused, "experiment.paused", null, ct);

    [HttpPost("api/v1/marketing/experiments/{id:guid}/resume")]
    [HasPermission(Permissions.MarketingManage)]
    public Task<ExperimentDto> Resume(Guid id, CancellationToken ct) =>
        TransitionAsync(id, ExperimentStatus.Paused, ExperimentStatus.Running, "experiment.resumed", null, ct);

    [HttpPost("api/v1/marketing/experiments/{id:guid}/complete")]
    [HasPermission(Permissions.MarketingManage)]
    public Task<ExperimentDto> Complete(Guid id, CompleteExperimentRequest? request, CancellationToken ct) =>
        TransitionAsync(id, null, ExperimentStatus.Completed, "experiment.completed", request?.WinningVariantId, ct);

    /// <summary>
    /// Variant overlays for the caller on every Running experiment of the campaign (sticky assignment). The frontend
    /// overlays these on the campaign detail and sends the variantId with the submission.
    /// </summary>
    [HttpGet("api/v1/campaigns/{campaignId:guid}/experiment-variants")]
    [HasPermission(Permissions.ParticipantPortal)]
    public async Task<IReadOnlyList<ParticipantVariantDto>> ParticipantVariants(Guid campaignId, CancellationToken ct)
    {
        var status = await db.Set<Campaign>().AsNoTracking().Where(c => c.Id == campaignId)
            .Select(c => (CampaignStatus?)c.Status).FirstOrDefaultAsync(ct);
        if (status is null or CampaignStatus.Draft or CampaignStatus.Archived) throw DomainException.NotFound("Campaign");

        var running = await assignments.RunningAsync(campaignId, null, ct);
        var subject = VariantAssigner.UserSubject(currentUser.Id);
        var result = new List<ParticipantVariantDto>();
        foreach (var experiment in running.Where(e => e.Variants.Count > 0))
        {
            var variant = await assignments.AssignAsync(experiment, subject, ct);
            AssetOverlayDto? asset = null;
            if (experiment.Element == ExperimentElement.CreativeAsset && variant.AssetId is { } assetId)
                asset = await db.Set<CampaignAsset>().AsNoTracking().Where(a => a.Id == assetId)
                    .Select(a => new AssetOverlayDto(a.Id, a.Url, a.Title, a.Type)).FirstOrDefaultAsync(ct);
            result.Add(new ParticipantVariantDto(experiment.Id, experiment.Element, variant.Id, variant.Key,
                experiment.Element == ExperimentElement.Title ? variant.Title : null,
                experiment.Element == ExperimentElement.Instructions ? variant.Instructions : null,
                asset,
                experiment.Element == ExperimentElement.LandingPage ? variant.LandingHeadline : null,
                experiment.Element == ExperimentElement.LandingPage ? variant.LandingBody : null));
        }
        return result;
    }

    /// <summary>
    /// Measured results per variant and a two-proportion z-test of each variant's submission rate against control "A".
    /// A difference is only called significant when p &lt; 0.05 and every compared variant has ≥ 100 assignments.
    /// </summary>
    [HttpGet("api/v1/marketing/experiments/{id:guid}/results")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<ExperimentResultsDto> Results(Guid id, CancellationToken ct)
    {
        var experiment = await LoadAsync(id, tracked: false, ct);
        var variantIds = experiment.Variants.Select(v => v.Id).ToList();

        var assigned = await db.Set<ExperimentAssignment>().AsNoTracking()
            .Where(a => a.ExperimentId == id).GroupBy(a => a.VariantId)
            .Select(g => new { VariantId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var submissions = await db.Set<Submission>().AsNoTracking()
            .Where(s => s.ExperimentVariantId != null && variantIds.Contains(s.ExperimentVariantId.Value))
            .Select(s => new { VariantId = s.ExperimentVariantId!.Value, s.UserId, s.Status })
            .ToListAsync(ct);

        var rows = experiment.Variants.OrderBy(v => v.Key).Select(v =>
        {
            var subs = submissions.Where(s => s.VariantId == v.Id).ToList();
            var n = assigned.FirstOrDefault(a => a.VariantId == v.Id)?.Count ?? 0;
            var submitters = subs.Select(s => s.UserId).Distinct().Count();
            var approvedUsers = subs.Where(s => s.Status == SubmissionStatus.Approved).Select(s => s.UserId).Distinct().Count();
            return new VariantResultDto(v.Id, v.Key, v.Name, v.Weight, n, submitters, approvedUsers, subs.Count,
                subs.Count(s => s.Status == SubmissionStatus.Approved),
                ExperimentMath.Rate(submitters, n), ExperimentMath.Rate(approvedUsers, submitters));
        }).ToList();

        var control = rows.FirstOrDefault(r => r.Key == ControlKey);
        var comparisons = new List<VariantComparisonDto>();
        if (control is not null)
        {
            foreach (var row in rows.Where(r => r.Key != ControlKey))
            {
                // Submitters can exceed assignments if submissions carry a variant id without a stored assignment; cap for the test.
                var test = ExperimentMath.TwoProportionZTest(
                    Math.Min(control.Submissions, control.Assigned), control.Assigned,
                    Math.Min(row.Submissions, row.Assigned), row.Assigned);
                var enough = control.Assigned >= ExperimentMath.MinAssignmentsForConclusion &&
                             row.Assigned >= ExperimentMath.MinAssignmentsForConclusion;
                var significant = enough && test.PValue is < ExperimentMath.SignificanceLevel;
                double? absolute = row.SubmissionRate is { } r1 && control.SubmissionRate is { } r0 ? r1 - r0 : null;
                double? relative = absolute is { } a && control.SubmissionRate is > 0 ? a / control.SubmissionRate.Value : null;
                var note = !enough || test.PValue is null
                    ? NotEnoughData
                    : significant
                        ? "Statistically significant difference at the 5% level (two-sided two-proportion z-test)."
                        : "No statistically significant difference detected at the 5% level.";
                comparisons.Add(new VariantComparisonDto(row.Key, ControlKey, absolute, relative, test.Z, test.PValue, significant, note));
            }
        }

        return new ExperimentResultsDto(id, experiment.Status, "submissionRate", "measured", rows, comparisons,
            "Assigned = participants/visitors with a stored sticky assignment (exposures). Submissions/approved count distinct " +
            "participants whose submissions carry the variant id. submissionRate = submissions / assigned; approvalRate = " +
            "approved / submissions. Comparison: pooled two-proportion z-test of submissionRate vs control A; significant only " +
            "when p < 0.05 and each variant has at least 100 assignments.");
    }

    private async Task<ExperimentDto> TransitionAsync(
        Guid id, ExperimentStatus? from, ExperimentStatus to, string action, Guid? winningVariantId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var experiment = await LoadAsync(id, tracked: true, ct);
        // Lock the campaign row so two concurrent starts cannot both pass the one-running-experiment check.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM campaigns WHERE Id = {experiment.CampaignId.ToString()} FOR UPDATE", ct);

        var before = new { experiment.Status };
        if (to == ExperimentStatus.Completed)
        {
            if (experiment.Status is not (ExperimentStatus.Running or ExperimentStatus.Paused))
                throw DomainException.Conflict("experiment.invalid_transition", "Only running or paused experiments can be completed.");
            if (winningVariantId is { } w && experiment.Variants.All(v => v.Id != w))
                throw new DomainException("experiment.invalid_winner", "The winning variant does not belong to this experiment.");
            experiment.WinningVariantId = winningVariantId;
            experiment.EndedAt = Now;
        }
        else
        {
            if (experiment.Status != from)
                throw DomainException.Conflict("experiment.invalid_transition",
                    $"This experiment is {experiment.Status}; the action requires it to be {from}.");
            if (to == ExperimentStatus.Running)
            {
                var clash = await db.Set<Experiment>().AnyAsync(e => e.CampaignId == experiment.CampaignId &&
                    e.Element == experiment.Element && e.Status == ExperimentStatus.Running && e.Id != experiment.Id, ct);
                if (clash)
                    throw DomainException.Conflict("experiment.already_running",
                        $"Another {experiment.Element} experiment is already running for this campaign.");
                if (experiment.Variants.Count < 2)
                    throw DomainException.Conflict("experiment.not_enough_variants", "An experiment needs at least two variants.");
                experiment.StartedAt ??= Now;
            }
        }
        experiment.Status = to;
        audit.Record(action, nameof(Experiment), id, before, new { experiment.Status, experiment.WinningVariantId });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(experiment);
    }

    private async Task ValidateAsync(ExperimentRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var campaignId = request.CampaignId!.Value;
        if (!await db.Set<Campaign>().AnyAsync(c => c.Id == campaignId, ct))
            throw new DomainException("experiment.campaign_not_found", "The campaign does not exist.");

        var keys = request.Variants.Select(v => v.Key).ToList();
        if (keys.Distinct().Count() != keys.Count) errors["variants"] = new[] { "Variant keys must be unique." };
        else if (!keys.Contains(ControlKey)) errors["variants"] = new[] { "Variant A (control) is required." };
        else if (keys.OrderBy(k => k).SequenceEqual(new[] { "A", "B", "C", "D" }.Take(keys.Count)) == false)
            errors["variants"] = new[] { "Variant keys must be consecutive starting at A (A, B, C, D)." };

        var element = request.Element!.Value;
        var assetIds = request.Variants.Where(v => v.AssetId.HasValue).Select(v => v.AssetId!.Value).Distinct().ToList();
        var validAssets = assetIds.Count == 0
            ? new List<Guid>()
            : await db.Set<CampaignAsset>().Where(a => a.CampaignId == campaignId && assetIds.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct);

        for (var i = 0; i < request.Variants.Count; i++)
        {
            var v = request.Variants[i];
            var field = $"variants[{i}]";
            switch (element)
            {
                case ExperimentElement.Title when string.IsNullOrWhiteSpace(v.Title):
                    errors[field + ".title"] = new[] { "A title is required for title experiments." };
                    break;
                case ExperimentElement.Instructions when string.IsNullOrWhiteSpace(v.Instructions):
                    errors[field + ".instructions"] = new[] { "Instructions are required for instruction experiments." };
                    break;
                case ExperimentElement.CreativeAsset when v.AssetId is null:
                    errors[field + ".assetId"] = new[] { "An asset is required for creative experiments." };
                    break;
                case ExperimentElement.LandingPage when string.IsNullOrWhiteSpace(v.LandingHeadline) && string.IsNullOrWhiteSpace(v.LandingBody):
                    errors[field + ".landingHeadline"] = new[] { "A landing headline or body is required for landing page experiments." };
                    break;
            }
            if (element == ExperimentElement.CreativeAsset && v.AssetId is { } assetId && !validAssets.Contains(assetId))
                errors[field + ".assetId"] = new[] { "The asset must belong to the experiment's campaign." };
        }
        if (errors.Count > 0)
            throw new DomainException("experiment.invalid", "The experiment definition is invalid.", DomainErrorKind.Validation, errors);
    }

    private async Task<Experiment> LoadAsync(Guid id, bool tracked, CancellationToken ct)
    {
        var q = db.Set<Experiment>().Include(e => e.Variants).Where(e => e.Id == id);
        if (!tracked) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Experiment");
    }

    private void CheckStamp(Experiment experiment, Guid? stamp)
    {
        if (stamp is null) return;
        if (stamp != experiment.ConcurrencyStamp)
            throw DomainException.Conflict("concurrency.conflict", "This experiment was changed by someone else. Reload and try again.");
        db.Entry(experiment).Property(e => e.ConcurrencyStamp).OriginalValue = stamp.Value;
    }

    /// <summary>Keeps only the override field relevant to the element.</summary>
    private static ExperimentVariant ToVariant(Guid experimentId, VariantRequest v, ExperimentElement element) => new()
    {
        ExperimentId = experimentId,
        Key = v.Key,
        Name = v.Name.Trim(),
        Weight = v.Weight,
        Title = element == ExperimentElement.Title ? Clean(v.Title) : null,
        Instructions = element == ExperimentElement.Instructions ? Clean(v.Instructions) : null,
        AssetId = element == ExperimentElement.CreativeAsset ? v.AssetId : null,
        LandingHeadline = element == ExperimentElement.LandingPage ? Clean(v.LandingHeadline) : null,
        LandingBody = element == ExperimentElement.LandingPage ? Clean(v.LandingBody) : null,
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static object Snapshot(Experiment e) => new
    {
        e.Name, e.Element, e.Status, Variants = e.Variants.Select(v => new { v.Key, v.Name, v.Weight }).ToList(),
    };

    private static ExperimentDto ToDto(Experiment e) => new(
        e.Id, e.CampaignId, e.Name, e.Hypothesis, e.Element, e.Status, e.StartedAt, e.EndedAt, e.WinningVariantId,
        e.Variants.OrderBy(v => v.Key).Select(v => new VariantDto(v.Id, v.Key, v.Name, v.Weight, v.Title, v.Instructions,
            v.AssetId, v.LandingHeadline, v.LandingBody)).ToList(),
        e.ConcurrencyStamp, e.CreatedAt, e.UpdatedAt);
}
