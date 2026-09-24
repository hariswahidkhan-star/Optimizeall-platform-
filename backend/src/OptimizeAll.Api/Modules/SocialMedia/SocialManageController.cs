using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

// ---------------------------------------------------------------- DTOs

public sealed class ListeningQueryUpdateInput
{
    [Required, MinLength(2), MaxLength(150)] public string Term { get; set; } = string.Empty;
    [MaxLength(8), DefinedEnum] public List<SocialNetwork> Networks { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class CompetitorUpdateInput
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, DefinedEnum] public SocialNetwork? Network { get; set; }
    [Required, MaxLength(150)] public string Handle { get; set; } = string.Empty;
    [MaxLength(500)] public string? ProfileUrl { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class StampInput
{
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record AdminPresetDto(PresetDto Preset, bool IsCustomized, DateTime? UpdatedAt, Guid ConcurrencyStamp);

public sealed class PresetUpdateInput
{
    [Range(1, 100_000)] public int MaxTextLength { get; set; }
    [Range(1, 1000)] public int? MaxTitleLength { get; set; }
    [Range(0, 100)] public int MaxHashtags { get; set; }
    [Range(0, 100)] public int? RecommendedHashtags { get; set; }
    [Range(0, 100)] public int MaxMentions { get; set; }
    [Range(0, 35)] public int MaxMedia { get; set; }
    [Range(0, 35)] public int MaxVideos { get; set; }
    [Range(0, 100_000)] public int MaxAltTextLength { get; set; }
    public bool SupportsFirstComment { get; set; }
    [Range(1, 24 * 3600)] public int? MaxVideoSeconds { get; set; }
    [Range(1, 1024L * 1024 * 1024)] public long? MaxImageBytes { get; set; }

    /// <summary>Recommended local posting times as "Mon 09:00".</summary>
    [MaxLength(40)] public List<string> RecommendedTimes { get; set; } = new();
    [Required, MaxLength(500)] public string Source { get; set; } = string.Empty;
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record AwarenessDayAdminDto(
    Guid Id, int Month, int Day, int? Year, string Name, IReadOnlyList<string> Countries, string SourceUrl, bool IsActive, bool IsBuiltIn,
    Guid ConcurrencyStamp);

public sealed class AwarenessDayInput
{
    [Range(1, 12)] public int Month { get; set; }
    [Range(1, 31)] public int Day { get; set; }
    [Range(2000, 2100)] public int? Year { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(30)] public List<string> Countries { get; set; } = new();
    [Required, MaxLength(500)] public string SourceUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Guid? ConcurrencyStamp { get; set; }
}

/// <summary>
/// The edit/archive/delete and "mark done" actions of the social workspace that the original controllers lack: listening
/// queries, mentions, competitors, UTM campaigns, post duplication, resolving review feedback, and the agency-wide
/// network presets and awareness-day calendar (settings.manage).
/// </summary>
[ApiController]
[Route("api/v1/agency/social")]
[HasPermission(Permissions.SocialManage)]
public sealed class SocialManageController(
    AppDbContext db, SocialAccess access, IDatabaseDialect dialect, ICurrentUser currentUser, IAuditLogger audit, TimeProvider clock) : ControllerBase
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------ listening

    [HttpPut("listening/queries/{id:guid}")]
    public async Task<ListeningQueryDto> UpdateQuery(Guid id, ListeningQueryUpdateInput input, CancellationToken ct)
    {
        var q = await access.OwnedAsync<SocialListeningQuery>(id, x => x.ClientAccountId, "Query", ct);
        StampGuard.Expect(db, q, input.ConcurrencyStamp, "listening query");
        var before = new { q.Term, q.IsActive };
        q.Term = q.Kind switch
        {
            ListeningQueryKind.Hashtag => PostValidator.NormalizeHashtag(input.Term),
            ListeningQueryKind.CompetitorHandle => "@" + Normalization.Handle(input.Term),
            _ => input.Term.Trim(),
        };
        q.Networks = input.Networks.Distinct().ToList();
        q.IsActive = input.IsActive;
        audit.Record("social.listening.query_updated", nameof(SocialListeningQuery), q.Id, before, new { q.Term, q.IsActive });
        await db.SaveChangesAsync(ct);
        return new ListeningQueryDto(q.Id, q.ClientAccountId, q.Kind, q.Term, q.Networks, q.IsActive, q.ConcurrencyStamp);
    }

    [HttpDelete("listening/mentions/{id:guid}")]
    public async Task<IActionResult> DeleteMention(Guid id, CancellationToken ct)
    {
        var m = await access.OwnedAsync<SocialMention>(id, x => x.ClientAccountId, "Mention", ct);
        db.Remove(m);
        audit.Record("social.listening.mention_deleted", nameof(SocialMention), id, before: new { m.Network, m.AuthorHandle });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------ competitors

    [HttpPut("competitors/{id:guid}")]
    public async Task<CompetitorDto> UpdateCompetitor(Guid id, CompetitorUpdateInput input, CancellationToken ct)
    {
        var c = await access.OwnedAsync<SocialCompetitor>(id, x => x.ClientAccountId, "Competitor", ct);
        StampGuard.Expect(db, c, input.ConcurrencyStamp, "competitor");
        var url = input.ProfileUrl?.Trim();
        if (!string.IsNullOrEmpty(url) && !PostValidator.IsHttpUrl(url))
            throw new DomainException("validation.failed", "The profile URL must be an absolute http(s) URL.",
                errors: new Dictionary<string, string[]> { ["profileUrl"] = new[] { "Enter an absolute http(s) URL." } });
        var before = new { c.Name, c.Network, c.Handle };
        c.Name = input.Name.Trim();
        c.Network = input.Network!.Value;
        c.Handle = Normalization.Handle(input.Handle);
        c.ProfileUrl = string.IsNullOrEmpty(url) ? null : url;
        audit.Record("social.competitor.updated", nameof(SocialCompetitor), c.Id, before, new { c.Name, c.Network, c.Handle });
        await db.SaveChangesAsync(ct);
        var snaps = await db.Set<SocialCompetitorSnapshot>().AsNoTracking().Where(s => s.CompetitorId == c.Id).OrderBy(s => s.Date).ToListAsync(ct);
        return new CompetitorDto(c.Id, c.ClientAccountId, c.Name, c.Network, c.Handle, c.ProfileUrl,
            snaps.Select(s => new CompetitorSnapshotDto(s.Id, s.Date, s.Followers, s.EngagementRate, s.PostsLast30Days, s.Source, MetricSources.Label(s.Source))).ToList(),
            c.ConcurrencyStamp);
    }

    [HttpDelete("competitors/{id:guid}/snapshots/{snapshotId:guid}")]
    public async Task<IActionResult> DeleteSnapshot(Guid id, Guid snapshotId, CancellationToken ct)
    {
        var c = await access.OwnedAsync<SocialCompetitor>(id, x => x.ClientAccountId, "Competitor", ct);
        var snap = await db.Set<SocialCompetitorSnapshot>().FirstOrDefaultAsync(s => s.Id == snapshotId && s.CompetitorId == c.Id, ct)
                   ?? throw DomainException.NotFound("Snapshot");
        db.Remove(snap);
        audit.Record("social.competitor.snapshot_deleted", nameof(SocialCompetitor), c.Id, before: new { snap.Date, snap.Followers });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------ profiles

    /// <summary>Brings an archived brand profile back (it shows in the composer again; reconnect it if its token expired).</summary>
    [HttpPost("profiles/{id:guid}/restore")]
    [HasPermission(Permissions.SocialPublish)]
    public async Task<IActionResult> RestoreProfile(Guid id, CancellationToken ct)
    {
        var profile = await access.ProfileAsync(id, ct);
        if (profile.IsActive) throw DomainException.Conflict("social.profile_active", "The profile is not archived.");
        profile.IsActive = true;
        audit.Record("social.profile.restored", nameof(BrandProfile), profile.Id);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------ campaigns

    [HttpPost("campaigns/{id:guid}/archive")]
    public Task<SocialCampaignDto> ArchiveCampaign(Guid id, StampInput input, CancellationToken ct) => SetCampaignArchivedAsync(id, true, input, ct);

    [HttpPost("campaigns/{id:guid}/restore")]
    public Task<SocialCampaignDto> RestoreCampaign(Guid id, StampInput input, CancellationToken ct) => SetCampaignArchivedAsync(id, false, input, ct);

    /// <summary>Deletes a campaign no post uses; campaigns with posts are archived instead so reporting keeps its UTM values.</summary>
    [HttpDelete("campaigns/{id:guid}")]
    public async Task<IActionResult> DeleteCampaign(Guid id, CancellationToken ct)
    {
        var c = await access.OwnedAsync<SocialCampaign>(id, x => x.ClientAccountId, "Campaign", ct);
        if (await db.Set<SocialPost>().AnyAsync(p => p.CampaignId == id, ct))
            throw DomainException.Conflict("social.campaign_in_use", "Posts use this campaign; archive it instead so their UTM values are kept.");
        db.Remove(c);
        audit.Record("social.campaign.deleted", nameof(SocialCampaign), id, before: new { c.Name, c.UtmCampaign });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<SocialCampaignDto> SetCampaignArchivedAsync(Guid id, bool archived, StampInput input, CancellationToken ct)
    {
        var c = await access.OwnedAsync<SocialCampaign>(id, x => x.ClientAccountId, "Campaign", ct);
        StampGuard.Expect(db, c, input.ConcurrencyStamp, "campaign");
        if (c.IsArchived != archived)
        {
            c.IsArchived = archived;
            audit.Record(archived ? "social.campaign.archived" : "social.campaign.restored", nameof(SocialCampaign), c.Id);
            await db.SaveChangesAsync(ct);
        }
        return new SocialCampaignDto(c.Id, c.ClientAccountId, c.Name, c.UtmCampaign, c.UtmSource, c.UtmMedium, c.UtmContent, c.UtmTerm, c.ConcurrencyStamp,
            c.IsArchived);
    }

    // ------------------------------ posts

    /// <summary>Copies a post (any status, including published ones) into a new unscheduled draft.</summary>
    [HttpPost("posts/{id:guid}/duplicate")]
    public async Task<ActionResult<object>> DuplicatePost(Guid id, CancellationToken ct)
    {
        var source = await access.PostAsync(id, ct, track: false);
        var campaignArchived = source.CampaignId is { } cid && await db.Set<SocialCampaign>().AnyAsync(c => c.Id == cid && c.IsArchived, ct);
        var copy = new SocialPost
        {
            ClientAccountId = source.ClientAccountId,
            Title = (source.Title.Length > 190 ? source.Title[..190] : source.Title) + " (copy)",
            Status = SocialPostStatus.Draft,
            CampaignId = campaignArchived ? null : source.CampaignId,
            AutoAppendUtm = source.AutoAppendUtm,
            IsEvergreen = source.IsEvergreen,
            EvergreenIntervalDays = source.EvergreenIntervalDays,
            EvergreenMaxRepeats = source.EvergreenMaxRepeats,
            CreatedByUserId = currentUser.Id,
        };
        foreach (var v in source.Variants)
        {
            copy.Variants.Add(new SocialPostVariant
            {
                PostId = copy.Id, ClientAccountId = copy.ClientAccountId, ProfileId = v.ProfileId, Network = v.Network, Text = v.Text, Title = v.Title,
                MediaIds = v.MediaIds.ToList(), AltTexts = v.AltTexts.ToList(), Link = v.Link, FirstComment = v.FirstComment,
                Hashtags = v.Hashtags.ToList(), Mentions = v.Mentions.ToList(),
            });
        }
        db.Set<SocialPost>().Add(copy);
        audit.Record("social.post.duplicated", nameof(SocialPost), copy.Id, after: new { From = id, copy.Title });
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/agency/social/posts/{copy.Id}", new { id = copy.Id, copy.Title, copy.Status });
    }

    /// <summary>Marks review feedback on a post as addressed (or reopens it). Workflow history entries cannot be resolved.</summary>
    [HttpPost("posts/{postId:guid}/comments/{commentId:guid}/{op:regex(^(resolve|reopen)$)}")]
    public async Task<CommentDto> ResolveComment(Guid postId, Guid commentId, string op, CancellationToken ct)
    {
        var comment = await CommentAsync(postId, commentId, ct);
        if (comment.Kind is not (PostCommentKind.Comment or PostCommentKind.ChangesRequested))
            throw DomainException.Conflict("social.comment_not_resolvable", "Only comments and change requests can be marked done.");
        var resolve = op == "resolve";
        comment.IsResolved = resolve;
        comment.ResolvedAt = resolve ? Now : null;
        comment.ResolvedByUserId = resolve ? currentUser.Id : null;
        audit.Record(resolve ? "social.post.comment_resolved" : "social.post.comment_reopened", nameof(SocialPost), postId, after: new { commentId });
        await db.SaveChangesAsync(ct);
        return ToDto(comment);
    }

    /// <summary>Deletes your own comment. Workflow history and client feedback are kept.</summary>
    [HttpDelete("posts/{postId:guid}/comments/{commentId:guid}")]
    public async Task<IActionResult> DeleteComment(Guid postId, Guid commentId, CancellationToken ct)
    {
        var comment = await CommentAsync(postId, commentId, ct);
        if (comment.Kind != PostCommentKind.Comment || comment.IsClient)
            throw DomainException.Conflict("social.comment_not_deletable", "Workflow history and client feedback are kept on the post.");
        if (comment.AuthorUserId != currentUser.Id)
            throw DomainException.Forbidden("social.comment_not_yours", "You can only delete your own comments.");
        db.Remove(comment);
        audit.Record("social.post.comment_deleted", nameof(SocialPost), postId, before: new { commentId, comment.Body });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<SocialPostComment> CommentAsync(Guid postId, Guid commentId, CancellationToken ct)
    {
        var post = await access.PostAsync(postId, ct, track: false);
        return await db.Set<SocialPostComment>().FirstOrDefaultAsync(c => c.Id == commentId && c.PostId == post.Id, ct)
               ?? throw DomainException.NotFound("Comment");
    }

    private static CommentDto ToDto(SocialPostComment c) =>
        new(c.Id, c.AuthorName, c.IsClient, c.IsInternal, c.Kind, c.Body, c.CreatedAt, c.AuthorUserId, c.IsResolved, c.ResolvedAt);

    // ------------------------------ network presets (agency-wide)

    [HttpGet("admin/presets")]
    public async Task<IReadOnlyList<AdminPresetDto>> AdminPresets(CancellationToken ct)
    {
        var stored = await db.Set<SocialNetworkPreset>().AsNoTracking().ToDictionaryAsync(p => p.Network, ct);
        return NetworkPresets.Defaults.OrderBy(d => d.Network).Select(d =>
        {
            var row = stored.GetValueOrDefault(d.Network);
            var rules = row is null ? d : NetworkRules.From(row);
            return new AdminPresetDto(PresetDto.From(rules), row is not null && IsCustomized(row, d), row?.UpdatedAt, row?.ConcurrencyStamp ?? Guid.Empty);
        }).ToList();
    }

    [HttpPut("admin/presets/{network}")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<AdminPresetDto> UpdatePreset(SocialNetwork network, PresetUpdateInput input, CancellationToken ct)
    {
        var row = await PresetRowAsync(network, ct);
        StampGuard.Expect(db, row, input.ConcurrencyStamp, "preset");
        var times = new List<string>();
        foreach (var t in input.RecommendedTimes.Select(t => t.Trim()).Where(t => t.Length > 0))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(t, "^(Mon|Tue|Wed|Thu|Fri|Sat|Sun) ([01][0-9]|2[0-3]):[0-5][0-9]$"))
                throw new DomainException("validation.failed", $"'{t}' is not a posting time like \"Mon 09:00\".",
                    errors: new Dictionary<string, string[]> { ["recommendedTimes"] = new[] { "Use entries like \"Mon 09:00\"." } });
            if (!times.Contains(t)) times.Add(t);
        }
        if (input.RecommendedHashtags is { } rec && rec > input.MaxHashtags)
            throw new DomainException("validation.failed", "The recommended hashtag count cannot exceed the maximum.",
                errors: new Dictionary<string, string[]> { ["recommendedHashtags"] = new[] { "Must not exceed the maximum." } });
        var before = new { row.MaxTextLength, row.MaxHashtags, row.MaxMedia };
        row.MaxTextLength = input.MaxTextLength;
        row.MaxTitleLength = input.MaxTitleLength;
        row.MaxHashtags = input.MaxHashtags;
        row.RecommendedHashtags = input.RecommendedHashtags;
        row.MaxMentions = input.MaxMentions;
        row.MaxMedia = input.MaxMedia;
        row.MaxVideos = input.MaxVideos;
        row.MaxAltTextLength = input.MaxAltTextLength;
        row.SupportsFirstComment = input.SupportsFirstComment;
        row.MaxVideoSeconds = input.MaxVideoSeconds;
        row.MaxImageBytes = input.MaxImageBytes;
        row.RecommendedTimes = times;
        row.Source = input.Source.Trim();
        row.UpdatedAt = Now;
        audit.Record("social.preset.updated", nameof(SocialNetworkPreset), network.ToString(), before, new { row.MaxTextLength, row.MaxHashtags, row.MaxMedia });
        await db.SaveChangesAsync(ct);
        return new AdminPresetDto(PresetDto.From(NetworkRules.From(row)), IsCustomized(row, Default(network)), row.UpdatedAt, row.ConcurrencyStamp);
    }

    [HttpPost("admin/presets/{network}/reset")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<AdminPresetDto> ResetPreset(SocialNetwork network, CancellationToken ct)
    {
        var existing = await db.Set<SocialNetworkPreset>().FirstOrDefaultAsync(p => p.Network == network, ct);
        if (existing is not null) db.Remove(existing);
        await db.SaveChangesAsync(ct);
        var row = Default(network).ToEntity(Now);
        db.Add(row);
        audit.Record("social.preset.reset", nameof(SocialNetworkPreset), network.ToString());
        await db.SaveChangesAsync(ct);
        return new AdminPresetDto(PresetDto.From(NetworkRules.From(row)), false, row.UpdatedAt, row.ConcurrencyStamp);
    }

    private async Task<SocialNetworkPreset> PresetRowAsync(SocialNetwork network, CancellationToken ct)
    {
        var row = await db.Set<SocialNetworkPreset>().FirstOrDefaultAsync(p => p.Network == network, ct);
        if (row is not null) return row;
        row = Default(network).ToEntity(Now);
        db.Add(row);
        return row;
    }

    private static NetworkRules Default(SocialNetwork network) =>
        NetworkPresets.Defaults.FirstOrDefault(d => d.Network == network) ?? throw DomainException.NotFound("Preset");

    private static bool IsCustomized(SocialNetworkPreset p, NetworkRules d) =>
        p.MaxTextLength != d.MaxTextLength || p.MaxTitleLength != d.MaxTitleLength || p.MaxHashtags != d.MaxHashtags
        || p.RecommendedHashtags != d.RecommendedHashtags || p.MaxMentions != d.MaxMentions || p.MaxMedia != d.MaxMedia || p.MaxVideos != d.MaxVideos
        || p.MaxAltTextLength != d.MaxAltTextLength || p.SupportsFirstComment != d.SupportsFirstComment || p.MaxVideoSeconds != d.MaxVideoSeconds
        || p.MaxImageBytes != d.MaxImageBytes || p.Source != d.Source || !p.RecommendedTimes.SequenceEqual(d.RecommendedTimes);

    // ------------------------------ awareness days (agency-wide)

    [HttpGet("admin/awareness-days")]
    public async Task<IReadOnlyList<AwarenessDayAdminDto>> AwarenessDays(CancellationToken ct) =>
        (await db.Set<SocialAwarenessDay>().AsNoTracking().OrderBy(d => d.Month).ThenBy(d => d.Day).ThenBy(d => d.Name).ToListAsync(ct))
        .Select(ToDto).ToList();

    [HttpPost("admin/awareness-days")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<AwarenessDayAdminDto> CreateAwarenessDay(AwarenessDayInput input, CancellationToken ct)
    {
        var day = new SocialAwarenessDay();
        Apply(day, input);
        db.Add(day);
        audit.Record("social.awareness_day.created", nameof(SocialAwarenessDay), day.Id, after: new { day.Name, day.Month, day.Day });
        await SaveDayAsync(ct);
        return ToDto(day);
    }

    [HttpPut("admin/awareness-days/{id:guid}")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<AwarenessDayAdminDto> UpdateAwarenessDay(Guid id, AwarenessDayInput input, CancellationToken ct)
    {
        var day = await db.Set<SocialAwarenessDay>().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw DomainException.NotFound("Awareness day");
        StampGuard.Expect(db, day, input.ConcurrencyStamp, "awareness day");
        var before = new { day.Name, day.Month, day.Day, day.IsActive };
        Apply(day, input);
        audit.Record("social.awareness_day.updated", nameof(SocialAwarenessDay), day.Id, before, new { day.Name, day.Month, day.Day, day.IsActive });
        await SaveDayAsync(ct);
        return ToDto(day);
    }

    /// <summary>Deletes a day the agency added; built-in days are hidden instead (the seeder would otherwise not know it was removed).</summary>
    [HttpDelete("admin/awareness-days/{id:guid}")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> DeleteAwarenessDay(Guid id, CancellationToken ct)
    {
        var day = await db.Set<SocialAwarenessDay>().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw DomainException.NotFound("Awareness day");
        if (day.SeedKey is not null)
        {
            day.IsActive = false;
            audit.Record("social.awareness_day.hidden", nameof(SocialAwarenessDay), id, before: new { day.Name });
        }
        else
        {
            db.Remove(day);
            audit.Record("social.awareness_day.deleted", nameof(SocialAwarenessDay), id, before: new { day.Name, day.Month, day.Day });
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(SocialAwarenessDay d, AwarenessDayInput r)
    {
        var year = r.Year ?? 2024; // leap year: 29 February is valid for yearly days
        if (r.Day > DateTime.DaysInMonth(year, r.Month))
            throw new DomainException("validation.failed", "That day does not exist in the month.",
                errors: new Dictionary<string, string[]> { ["day"] = new[] { "Choose a valid day of the month." } });
        var url = r.SourceUrl.Trim();
        if (!PostValidator.IsHttpUrl(url))
            throw new DomainException("validation.failed", "The source must be an absolute http(s) URL.",
                errors: new Dictionary<string, string[]> { ["sourceUrl"] = new[] { "Enter an absolute http(s) URL." } });
        var countries = r.Countries.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        if (countries.Any(c => c.Length != 2 || !c.All(char.IsAsciiLetterUpper)))
            throw new DomainException("validation.failed", "Countries are two-letter ISO codes such as GB or US.",
                errors: new Dictionary<string, string[]> { ["countries"] = new[] { "Use two-letter ISO country codes." } });
        d.Month = r.Month;
        d.Day = r.Day;
        d.Year = r.Year;
        d.Name = r.Name.Trim();
        d.Countries = countries;
        d.SourceUrl = url;
        d.IsActive = r.IsActive;
    }

    private async Task SaveDayAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("social.awareness_day_exists", "A day with this name already exists on that date.");
        }
    }

    private static AwarenessDayAdminDto ToDto(SocialAwarenessDay d) =>
        new(d.Id, d.Month, d.Day, d.Year, d.Name, d.Countries, d.SourceUrl, d.IsActive, d.SeedKey is not null, d.ConcurrencyStamp);
}
