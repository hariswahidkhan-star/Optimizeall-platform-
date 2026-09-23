using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Api.Modules.EmailMarketing.Templates;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Campaigns;

public sealed record CampaignListItem(
    Guid Id, Guid? ClientAccountId, string Name, MessageChannel Channel, CampaignType Type, CampaignStatus Status, ApprovalStatus ApprovalStatus,
    string? Subject, ScheduleMode ScheduleMode, DateTime? ScheduledAt, string? ScheduledLocalTime, int RecipientCount, int Sent, int UniqueOpens,
    int UniqueClicks, DateTime? CompletedAt, DateTime UpdatedAt);

public sealed record VariantDto(string Key, string? Subject, string? PreviewText, Guid? SenderProfileId, JsonElement? Design);

public sealed record CampaignDto(
    Guid Id, Guid? ClientAccountId, string Name, MessageChannel Channel, CampaignType Type, CampaignStatus Status, Guid? ListId, Guid? SegmentId,
    Guid? TemplateId, Guid? SenderProfileId, string Subject, string? PreviewText, JsonElement Design, string? Topic, string? SmsBody,
    string? WhatsAppTemplateName, string? WhatsAppTemplateLanguage, IReadOnlyList<string> WhatsAppParameters, ScheduleMode ScheduleMode,
    DateTime? ScheduledAt, string? ScheduledLocalTime, int? SendWindowStartHour, int? SendWindowEndHour, int ThrottlePerMinute, int AbTestPercent,
    AbWinnerMetric AbWinnerMetric, int AbWaitHours, string? AbWinnerVariant, DateTime? AbDecidedAt, IReadOnlyList<VariantDto> Variants,
    ApprovalStatus ApprovalStatus, string? ApprovalNote, DateTime? ApprovalDecidedAt, DateTime? SendConfirmedAt, DateTime? SendStartedAt,
    DateTime? CompletedAt, DateTime? PausedAt, string? PauseReason, DateTime? CancelledAt, int RecipientCount, DateTime CreatedAt, DateTime UpdatedAt,
    Guid ConcurrencyStamp);

public sealed class VariantRequest
{
    [Required, RegularExpression("^[A-C]$")] public string Key { get; set; } = "A";
    [MaxLength(200)] public string? Subject { get; set; }
    [MaxLength(200)] public string? PreviewText { get; set; }
    public Guid? SenderProfileId { get; set; }
    public JsonElement? Design { get; set; }
}

public sealed class CampaignRequest
{
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public MessageChannel Channel { get; set; } = MessageChannel.Email;
    public CampaignType Type { get; set; } = CampaignType.Regular;
    public Guid? ListId { get; set; }
    public Guid? SegmentId { get; set; }
    /// <summary>Copies the template's subject/design when the campaign has no design yet (or when <see cref="ApplyTemplate"/>).</summary>
    public Guid? TemplateId { get; set; }
    public bool ApplyTemplate { get; set; }
    public Guid? SenderProfileId { get; set; }
    [MaxLength(200)] public string? Subject { get; set; }
    [MaxLength(200)] public string? PreviewText { get; set; }
    public JsonElement? Design { get; set; }
    [MaxLength(60)] public string? Topic { get; set; }
    [MaxLength(1600)] public string? SmsBody { get; set; }
    [MaxLength(100)] public string? WhatsAppTemplateName { get; set; }
    [MaxLength(12)] public string? WhatsAppTemplateLanguage { get; set; }
    public List<string>? WhatsAppParameters { get; set; }
    public ScheduleMode ScheduleMode { get; set; } = ScheduleMode.Immediate;
    public DateTime? ScheduledAt { get; set; }
    [MaxLength(20)] public string? ScheduledLocalTime { get; set; }
    [Range(0, 23)] public int? SendWindowStartHour { get; set; }
    [Range(0, 23)] public int? SendWindowEndHour { get; set; }
    [Range(1, 100_000)] public int? ThrottlePerMinute { get; set; }
    [Range(5, 50)] public int AbTestPercent { get; set; } = 20;
    public AbWinnerMetric AbWinnerMetric { get; set; } = AbWinnerMetric.OpenRate;
    [Range(1, 72)] public int AbWaitHours { get; set; } = 4;
    public List<VariantRequest>? Variants { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class CampaignQuery : PageQuery
{
    public Guid? ClientAccountId { get; set; }
    public CampaignStatus? Status { get; set; }
    public MessageChannel? Channel { get; set; }
}

/// <summary>Sensitive: sending needs <c>email.send</c>, <c>confirm: true</c> and the campaign name typed back.</summary>
public sealed class SendCampaignRequest
{
    public bool Confirm { get; set; }
    [Required, MaxLength(150)] public string ConfirmName { get; set; } = string.Empty;
    public Guid? ConcurrencyStamp { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}

public sealed class CampaignActionRequest
{
    public Guid? ConcurrencyStamp { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}

public sealed class ApprovalDecisionRequest
{
    public bool Approve { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
}

/// <summary>Campaign authoring and lifecycle. The send job (<see cref="CampaignSendJob"/>) does the actual sending.</summary>
public sealed class CampaignService(
    AppDbContext db,
    EmailAccess access,
    IAuditLogger audit,
    ICurrentUser currentUser,
    CampaignAudience audience,
    EmailSettingsStore settingsStore,
    ProviderStatus providerStatus,
    MessageComposer composer,
    TemplateService templates,
    INotificationService notifications,
    TimeProvider clock)
{
    // ---------- Queries ----------

    public async Task<PagedResult<CampaignListItem>> ListAsync(CampaignQuery q, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(q.ClientAccountId, ct);
        var key = Workspace.Key(q.ClientAccountId);
        var query = db.Set<EmailCampaign>().AsNoTracking().Where(c => c.ScopeKey == key && channels.Contains(c.Channel));
        if (q.Status is { } status) query = query.Where(c => c.Status == status);
        if (q.Channel is { } channel) query = query.Where(c => c.Channel == channel);
        if (!string.IsNullOrWhiteSpace(q.Search)) { var p = PagingExtensions.LikePattern(q.Search); query = query.Where(c => EF.Functions.Like(c.Name, p)); }
        var page = await query.OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id).ToPagedAsync(q, ct);
        return new PagedResult<CampaignListItem>(await ToListItemsAsync(page.Items, ct), page.Total, page.Page, page.PageSize);
    }

    public async Task<List<CampaignListItem>> ToListItemsAsync(IReadOnlyList<EmailCampaign> campaigns, CancellationToken ct)
    {
        var ids = campaigns.Select(c => c.Id).ToList();
        var stats = await db.Set<CampaignRecipient>().AsNoTracking().Where(r => ids.Contains(r.CampaignId)).GroupBy(r => r.CampaignId)
            .Select(g => new
            {
                Id = g.Key,
                Sent = g.Count(r => r.Status == RecipientStatus.Sent),
                Opens = g.Count(r => r.OpenedAt != null),
                Clicks = g.Count(r => r.ClickedAt != null),
            }).ToListAsync(ct);
        return campaigns.Select(c =>
        {
            var s = stats.FirstOrDefault(x => x.Id == c.Id);
            return new CampaignListItem(c.Id, c.ClientAccountId, c.Name, c.Channel, c.Type, c.Status, c.ApprovalStatus,
                c.Channel == MessageChannel.Email ? c.Subject : null, c.ScheduleMode, c.ScheduledAt, c.ScheduledLocalTime, c.RecipientCount,
                s?.Sent ?? 0, s?.Opens ?? 0, s?.Clicks ?? 0, c.CompletedAt, c.UpdatedAt);
        }).ToList();
    }

    public Task<EmailCampaign> LoadAsync(Guid id, CancellationToken ct) =>
        access.LoadAsync(db.Set<EmailCampaign>().Where(c => c.Id == id), c => c.ClientAccountId, "Campaign", ct);

    /// <summary>Loads a campaign and checks the caller may manage its channel (SMS/WhatsApp need sms.manage; email needs email.manage).</summary>
    public async Task<EmailCampaign> LoadForChannelAsync(Guid id, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        var campaign = await LoadAsync(id, ct);
        if (!channels.Contains(campaign.Channel)) throw DomainException.NotFound("Campaign");
        return campaign;
    }

    public async Task<CampaignDto> GetAsync(Guid id, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct) =>
        await ToDtoAsync(await LoadForChannelAsync(id, channels, ct), ct);

    public async Task<CampaignDto> ToDtoAsync(EmailCampaign c, CancellationToken ct)
    {
        var variants = await db.Set<CampaignVariant>().AsNoTracking().Where(v => v.CampaignId == c.Id).OrderBy(v => v.Key).ToListAsync(ct);
        return new CampaignDto(c.Id, c.ClientAccountId, c.Name, c.Channel, c.Type, c.Status, c.ListId, c.SegmentId, c.TemplateId, c.SenderProfileId,
            c.Subject, c.PreviewText, Json(c.DesignJson), c.Topic, c.SmsBody, c.WhatsAppTemplateName, c.WhatsAppTemplateLanguage,
            ParseParams(c.WhatsAppParametersJson), c.ScheduleMode, c.ScheduledAt, c.ScheduledLocalTime, c.SendWindowStartHour, c.SendWindowEndHour,
            c.ThrottlePerMinute, c.AbTestPercent, c.AbWinnerMetric, c.AbWaitHours, c.AbWinnerVariant, c.AbDecidedAt,
            variants.Select(v => new VariantDto(v.Key, v.Subject, v.PreviewText, v.SenderProfileId, v.DesignJson is null ? null : Json(v.DesignJson))).ToList(),
            c.ApprovalStatus, c.ApprovalNote, c.ApprovalDecidedAt, c.SendConfirmedAt, c.SendStartedAt, c.CompletedAt, c.PausedAt, c.PauseReason,
            c.CancelledAt, c.RecipientCount, c.CreatedAt, c.UpdatedAt, c.ConcurrencyStamp);
    }

    // ---------- Authoring ----------

    public async Task<CampaignDto> CreateAsync(CampaignRequest r, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        if (!channels.Contains(r.Channel)) throw DomainException.Forbidden("auth.forbidden", "You do not have permission to manage campaigns on this channel.");
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        var settings = await settingsStore.GetAsync(r.ClientAccountId, ct);
        var campaign = new EmailCampaign
        {
            ClientAccountId = r.ClientAccountId,
            ScopeKey = Workspace.Key(r.ClientAccountId),
            Channel = r.Channel,
            CreatedByUserId = currentUser.IdOrNull,
            ThrottlePerMinute = settings.DefaultThrottlePerMinute,
        };
        await ApplyAsync(campaign, r, isNew: true, ct);
        db.Set<EmailCampaign>().Add(campaign);
        await ReplaceVariantsAsync(campaign, r, ct);
        audit.Record("email.campaign.created", nameof(EmailCampaign), campaign.Id, after: Snapshot(campaign));
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(campaign, ct);
    }

    public async Task<CampaignDto> UpdateAsync(Guid id, CampaignRequest r, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        var campaign = await LoadForChannelAsync(id, channels, ct);
        AudienceService.ExpectStamp(campaign.ConcurrencyStamp, r.ConcurrencyStamp);
        if (campaign.Status != CampaignStatus.Draft)
            throw DomainException.Conflict("email.campaign_not_editable", "Only draft campaigns can be edited. Unschedule it first.");
        if (r.Channel != campaign.Channel) throw new DomainException("email.campaign_channel_fixed", "A campaign's channel cannot be changed.");
        var before = Snapshot(campaign);
        await ApplyAsync(campaign, r, isNew: false, ct);
        await ReplaceVariantsAsync(campaign, r, ct);
        // Any edit invalidates an earlier client decision.
        campaign.ApprovalStatus = ApprovalStatus.NotRequired;
        campaign.ApprovalDecidedAt = null;
        campaign.ApprovalDecidedByUserId = null;
        audit.Record("email.campaign.updated", nameof(EmailCampaign), campaign.Id, before, Snapshot(campaign));
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(campaign, ct);
    }

    public async Task DeleteAsync(Guid id, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        var campaign = await LoadForChannelAsync(id, channels, ct);
        if (campaign.Status != CampaignStatus.Draft) throw DomainException.Conflict("email.campaign_not_draft", "Only drafts can be deleted; cancel it instead.");
        audit.Record("email.campaign.deleted", nameof(EmailCampaign), campaign.Id, before: Snapshot(campaign));
        db.Set<EmailCampaign>().Remove(campaign);
        await db.SaveChangesAsync(ct);
    }

    public async Task<CampaignDto> DuplicateAsync(Guid id, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        var source = await LoadForChannelAsync(id, channels, ct);
        var copy = new EmailCampaign
        {
            ClientAccountId = source.ClientAccountId, ScopeKey = source.ScopeKey, Name = Text.Truncate(source.Name + " (copy)", 150), Channel = source.Channel,
            Type = source.Type, ListId = source.ListId, SegmentId = source.SegmentId, TemplateId = source.TemplateId, SenderProfileId = source.SenderProfileId,
            Subject = source.Subject, PreviewText = source.PreviewText, DesignJson = source.DesignJson, Topic = source.Topic, SmsBody = source.SmsBody,
            WhatsAppTemplateName = source.WhatsAppTemplateName, WhatsAppTemplateLanguage = source.WhatsAppTemplateLanguage,
            WhatsAppParametersJson = source.WhatsAppParametersJson, ThrottlePerMinute = source.ThrottlePerMinute, AbTestPercent = source.AbTestPercent,
            AbWinnerMetric = source.AbWinnerMetric, AbWaitHours = source.AbWaitHours, SendWindowStartHour = source.SendWindowStartHour,
            SendWindowEndHour = source.SendWindowEndHour, CreatedByUserId = currentUser.IdOrNull,
        };
        db.Set<EmailCampaign>().Add(copy);
        foreach (var v in await db.Set<CampaignVariant>().AsNoTracking().Where(v => v.CampaignId == source.Id).ToListAsync(ct))
            db.Set<CampaignVariant>().Add(new CampaignVariant { CampaignId = copy.Id, Key = v.Key, Subject = v.Subject, PreviewText = v.PreviewText, SenderProfileId = v.SenderProfileId, DesignJson = v.DesignJson });
        audit.Record("email.campaign.duplicated", nameof(EmailCampaign), copy.Id, after: new { from = source.Id });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(copy, ct);
    }

    private async Task ApplyAsync(EmailCampaign c, CampaignRequest r, bool isNew, CancellationToken ct)
    {
        var errors = new List<string>();
        c.Name = r.Name.Trim();
        c.Type = r.Channel == MessageChannel.Email ? r.Type : CampaignType.Regular;
        if (r.ListId is { } listId && !await db.Set<EmailList>().AnyAsync(l => l.Id == listId && l.ScopeKey == c.ScopeKey, ct)) errors.Add("The list does not belong to this workspace.");
        if (r.SegmentId is { } segmentId && !await db.Set<Segment>().AnyAsync(s => s.Id == segmentId && s.ScopeKey == c.ScopeKey, ct)) errors.Add("The segment does not belong to this workspace.");
        if (r.SenderProfileId is { } senderId && !await db.Set<SenderProfile>().AnyAsync(s => s.Id == senderId && s.ScopeKey == c.ScopeKey, ct)) errors.Add("The sender profile does not belong to this workspace.");
        c.ListId = r.ListId;
        c.SegmentId = r.SegmentId;
        c.SenderProfileId = r.SenderProfileId;
        c.Topic = Text.Clean(r.Topic, 60);

        if (c.Channel == MessageChannel.Email)
        {
            if (r.TemplateId is { } templateId && (r.ApplyTemplate || isNew && r.Design is null))
            {
                var template = await templates.LoadAsync(templateId, ct);
                if (template.ClientAccountId is not null && template.ScopeKey != c.ScopeKey) errors.Add("The template belongs to another workspace.");
                c.TemplateId = template.Id;
                c.DesignJson = template.DesignJson;
                c.Subject = string.IsNullOrWhiteSpace(r.Subject) ? template.Subject : r.Subject.Trim();
                c.PreviewText = Text.Clean(r.PreviewText, 200) ?? template.PreviewText;
            }
            else
            {
                c.TemplateId = r.TemplateId ?? c.TemplateId;
                c.Subject = r.Subject?.Trim() ?? string.Empty;
                c.PreviewText = Text.Clean(r.PreviewText, 200);
                if (r.Design is { } designJson)
                {
                    var design = ContentValidation.Parse(designJson);
                    var designErrors = ContentValidation.Errors(design, c.Subject, c.PreviewText);
                    errors.AddRange(designErrors);
                    ContentValidation.Sanitize(design);
                    c.DesignJson = design.ToJson();
                }
            }
            if (MergeTags.Unknown(c.Subject).Count > 0) errors.Add($"Unknown merge tags in the subject: {string.Join(", ", MergeTags.Unknown(c.Subject))}.");
        }
        else
        {
            c.SmsBody = Text.Clean(r.SmsBody, 1600);
            if (c.SmsBody is not null && MergeTags.Unknown(c.SmsBody).Count > 0) errors.Add($"Unknown merge tags: {string.Join(", ", MergeTags.Unknown(c.SmsBody))}.");
            if (c.Channel == MessageChannel.WhatsApp)
            {
                c.WhatsAppTemplateName = Text.Clean(r.WhatsAppTemplateName, 100);
                c.WhatsAppTemplateLanguage = Text.Clean(r.WhatsAppTemplateLanguage, 12) ?? "en";
                var parameters = (r.WhatsAppParameters ?? new List<string>()).Select(p => p.Trim()).ToList();
                if (parameters.Count > 10) errors.Add("At most 10 template parameters.");
                if (parameters.Any(p => p.Length > 1024)) errors.Add("Template parameters are limited to 1024 characters.");
                if (parameters.SelectMany(p => MergeTags.Unknown(p)).Any()) errors.Add("A template parameter uses an unknown merge tag.");
                c.WhatsAppParametersJson = JsonSerializer.Serialize(parameters);
                c.SmsBody ??= string.Join(" | ", parameters);
            }
        }

        c.ScheduleMode = r.ScheduleMode;
        c.ScheduledAt = r.ScheduleMode == ScheduleMode.FixedTime ? r.ScheduledAt?.ToUniversalTime() : null;
        c.ScheduledLocalTime = r.ScheduleMode == ScheduleMode.RecipientTimeZone ? Text.Clean(r.ScheduledLocalTime, 20) : null;
        if (r.ScheduleMode == ScheduleMode.FixedTime && r.ScheduledAt is null) errors.Add("Choose the date and time to send.");
        if (r.ScheduleMode == ScheduleMode.RecipientTimeZone && !SendTiming.TryParseLocal(c.ScheduledLocalTime, out _))
            errors.Add("Local send time must be yyyy-MM-ddTHH:mm.");
        if (r.SendWindowStartHour.HasValue != r.SendWindowEndHour.HasValue || (r.SendWindowStartHour is { } ws && ws == r.SendWindowEndHour))
            errors.Add("A send window needs different start and end hours.");
        c.SendWindowStartHour = r.SendWindowStartHour;
        c.SendWindowEndHour = r.SendWindowEndHour;
        if (r.ThrottlePerMinute is { } throttle) c.ThrottlePerMinute = throttle;
        c.AbTestPercent = r.AbTestPercent;
        c.AbWinnerMetric = r.AbWinnerMetric;
        c.AbWaitHours = r.AbWaitHours;
        if (errors.Count > 0) throw EmailProblem.Invalid("email.campaign_invalid", "The campaign is invalid.", errors);
    }

    private async Task ReplaceVariantsAsync(EmailCampaign c, CampaignRequest r, CancellationToken ct)
    {
        if (c.Type != CampaignType.AbTest || r.Variants is null) return;
        var keys = r.Variants.Select(v => v.Key).ToList();
        if (keys.Count is < 2 or > 3 || keys.Distinct().Count() != keys.Count || !keys.Contains("A"))
            throw new DomainException("email.campaign_invalid", "An A/B test needs variants A and B (and optionally C).");
        var errors = new List<string>();
        var existing = await db.Set<CampaignVariant>().Where(v => v.CampaignId == c.Id).ToListAsync(ct);
        db.Set<CampaignVariant>().RemoveRange(existing);
        foreach (var v in r.Variants)
        {
            string? designJson = null;
            if (v.Design is { } d && d.ValueKind == JsonValueKind.Object)
            {
                var design = ContentValidation.Parse(d);
                errors.AddRange(ContentValidation.Errors(design, v.Subject, v.PreviewText).Select(e => $"Variant {v.Key}: {e}"));
                ContentValidation.Sanitize(design);
                designJson = design.ToJson();
            }
            if (MergeTags.Unknown(v.Subject).Count > 0) errors.Add($"Variant {v.Key}: unknown merge tags in the subject.");
            if (v.SenderProfileId is { } sid && !await db.Set<SenderProfile>().AnyAsync(s => s.Id == sid && s.ScopeKey == c.ScopeKey, ct))
                errors.Add($"Variant {v.Key}: the sender profile does not belong to this workspace.");
            db.Set<CampaignVariant>().Add(new CampaignVariant
            {
                CampaignId = c.Id, Key = v.Key, Subject = Text.Clean(v.Subject, 200), PreviewText = Text.Clean(v.PreviewText, 200),
                SenderProfileId = v.SenderProfileId, DesignJson = designJson,
            });
        }
        if (errors.Count > 0) throw EmailProblem.Invalid("email.campaign_invalid", "The A/B variants are invalid.", errors);
    }

    // ---------- Checklist ----------

    public async Task<Checklist> ChecklistAsync(Guid id, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct) =>
        await BuildChecklistAsync(await LoadForChannelAsync(id, channels, ct), ct);

    public async Task<Checklist> BuildChecklistAsync(EmailCampaign c, CancellationToken ct)
    {
        var items = new List<ChecklistItem>();
        var settings = await settingsStore.GetAsync(c.ClientAccountId, ct);
        var count = c.ListId is null && c.SegmentId is null ? 0 : await (await audience.EligibleAsync(c, ct)).CountAsync(ct);
        items.Add(c.ListId is null && c.SegmentId is null
            ? new("audience", "Audience selected", CheckStatus.Fail, "Choose a list or segment.")
            : count == 0
                ? new("audience", "Audience", CheckStatus.Fail, "Nobody in the audience can receive this campaign (consent, suppression or no address).")
                : new("audience", "Audience", CheckStatus.Pass, $"{count:N0} contacts with consent will receive it; suppressed and unconsented contacts are excluded."));

        int? smsSegments = null;
        string? encoding = null;
        decimal? cost = null;
        if (c.Channel == MessageChannel.Email) await EmailChecksAsync(c, settings, items, ct);
        else
        {
            var (ready, detail) = await providerStatus.SmsAsync(c.ClientAccountId, c.Channel, ct);
            items.Add(new("provider", c.Channel == MessageChannel.WhatsApp ? "WhatsApp Business connected" : "SMS provider connected", ready ? CheckStatus.Pass : CheckStatus.Fail, detail));
            if (c.Channel == MessageChannel.Sms)
            {
                if (string.IsNullOrWhiteSpace(c.SmsBody)) items.Add(new("content", "Message text", CheckStatus.Fail, "Write the SMS text."));
                else
                {
                    var info = SmsSegments.Calculate(MergeTags.Render(c.SmsBody, composer.SampleValues(settings), MergeContext.Text));
                    smsSegments = info.Segments;
                    encoding = info.Encoding.ToString();
                    cost = Math.Round(info.Segments * (decimal)count * settings.SmsCostPerSegment, 2);
                    items.Add(new("content", "Message text", info.Segments > 4 ? CheckStatus.Warning : CheckStatus.Pass,
                        $"{info.Units} {(info.Encoding == SmsEncoding.Gsm7 ? "GSM-7 characters" : "UCS-2 characters (non-GSM characters present)")} → {info.Segments} segment(s) per message."));
                    items.Add(new("opt-out", "Opt-out instructions", SmsSegments.HasOptOutInstruction(c.SmsBody) ? CheckStatus.Pass : CheckStatus.Warning,
                        SmsSegments.HasOptOutInstruction(c.SmsBody) ? "Mentions STOP." : "Add \"Reply STOP to opt out\" (required by carriers and TCPA/PECR guidance)."));
                }
            }
            else
            {
                items.Add(string.IsNullOrWhiteSpace(c.WhatsAppTemplateName)
                    ? new("content", "Approved template", CheckStatus.Fail, "Enter the approved WhatsApp template name.")
                    : new("content", "Approved template", CheckStatus.Pass, $"Template {c.WhatsAppTemplateName} ({c.WhatsAppTemplateLanguage})."));
                cost = Math.Round(count * settings.WhatsAppCostPerMessage, 2);
            }
            items.Add(new("quiet-hours", "Quiet hours", CheckStatus.Info,
                $"Messages are held between {settings.QuietHoursStart:00}:00 and {settings.QuietHoursEnd:00}:00 in each recipient's time zone."));
        }

        var now = clock.GetUtcNow().UtcDateTime;
        items.Add(c.ScheduleMode switch
        {
            ScheduleMode.FixedTime when c.ScheduledAt is null || c.ScheduledAt < now.AddMinutes(-1) =>
                new("schedule", "Schedule", CheckStatus.Fail, "The scheduled time is in the past."),
            ScheduleMode.FixedTime => new("schedule", "Schedule", CheckStatus.Pass, $"Sends at {c.ScheduledAt:yyyy-MM-dd HH:mm} UTC."),
            ScheduleMode.RecipientTimeZone => SendTiming.TryParseLocal(c.ScheduledLocalTime, out _)
                ? new("schedule", "Schedule", CheckStatus.Pass, $"Sends at {c.ScheduledLocalTime} in each recipient's time zone.")
                : new("schedule", "Schedule", CheckStatus.Fail, "Set the local send time."),
            _ => new("schedule", "Schedule", CheckStatus.Pass, "Sends as soon as it is confirmed."),
        });
        items.Add(new("throttle", "Sending speed", CheckStatus.Info, $"At most {c.ThrottlePerMinute:N0} messages per minute."));

        var requiresApproval = c.ClientAccountId is not null && settings.RequireClientApproval;
        if (requiresApproval)
            items.Add(new("approval", "Client approval", c.ApprovalStatus == ApprovalStatus.Approved ? CheckStatus.Pass : CheckStatus.Info,
                c.ApprovalStatus switch
                {
                    ApprovalStatus.Approved => "Approved by the client.",
                    ApprovalStatus.Rejected => "The client requested changes: " + c.ApprovalNote,
                    _ => "The client must approve the campaign in their portal before it is sent.",
                }));

        var canSend = items.All(i => i.Status != CheckStatus.Fail);
        return new Checklist(items, canSend, count, smsSegments, encoding, cost, c.Channel == MessageChannel.Email ? null : settings.CostCurrency, requiresApproval);
    }

    private async Task EmailChecksAsync(EmailCampaign c, EmailWorkspaceSettings settings, List<ChecklistItem> items, CancellationToken ct)
    {
        var variants = await db.Set<CampaignVariant>().AsNoTracking().Where(v => v.CampaignId == c.Id).ToListAsync(ct);
        var contents = new List<(string Label, EmailDesign Design, string Subject, string? Preview, Guid? SenderId)>
        {
            ("", EmailDesign.Parse(c.DesignJson), c.Subject, c.PreviewText, c.SenderProfileId),
        };
        if (c.Type == CampaignType.AbTest)
        {
            if (variants.Count < 2) items.Add(new("variants", "A/B variants", CheckStatus.Fail, "Add variants A and B."));
            else items.Add(new("variants", "A/B variants", CheckStatus.Pass,
                $"{variants.Count} variants; {c.AbTestPercent}% test cohort; winner by {(c.AbWinnerMetric == AbWinnerMetric.OpenRate ? "open" : "click")} rate after {c.AbWaitHours} h."));
            contents.AddRange(variants.Where(v => v.Key != "A").Select(v => ($"Variant {v.Key}: ", v.DesignJson is null ? contents[0].Design : EmailDesign.Parse(v.DesignJson),
                v.Subject ?? c.Subject, v.PreviewText ?? c.PreviewText, v.SenderProfileId ?? c.SenderProfileId)));
        }

        var (providerReady, providerDetail) = await providerStatus.EmailAsync(settings, ct);
        items.Add(new("provider", "Email provider", providerReady ? CheckStatus.Pass : CheckStatus.Fail, providerDetail));

        var senderIds = contents.Select(x => x.SenderId).Distinct().ToList();
        if (senderIds.Any(s => s is null)) items.Add(new("sender", "Sender verified", CheckStatus.Fail, "Choose a sender profile."));
        else
        {
            var ids = senderIds.Select(s => s!.Value).ToList();
            var senders = await db.Set<SenderProfile>().AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);
            var unverified = senders.Where(s => s.VerifiedAt is null).Select(s => s.FromEmail).ToList();
            items.Add(unverified.Count == 0 && senders.Count == ids.Count
                ? new("sender", "Sender verified", CheckStatus.Pass, string.Join(", ", senders.Select(s => $"{s.FromName} <{s.FromEmail}>")))
                : new("sender", "Sender verified", CheckStatus.Fail, $"Verify {string.Join(", ", unverified)} before sending."));
        }

        items.Add(string.IsNullOrWhiteSpace(settings.PhysicalAddress)
            ? new("address", "Physical postal address", CheckStatus.Fail, "Add the sender's physical address in Email settings (CAN-SPAM).")
            : new("address", "Physical postal address", CheckStatus.Pass, settings.PhysicalAddress));

        foreach (var (label, design, subject, preview, _) in contents)
        {
            var prefix = label.Length == 0 ? "" : label;
            if (string.IsNullOrWhiteSpace(subject)) items.Add(new("subject", prefix + "Subject line", CheckStatus.Fail, "Write a subject line."));
            var errors = ContentValidation.Errors(design, subject, preview);
            if (design.Blocks.Count == 0) errors.Add("The email has no content.");
            items.Add(errors.Count == 0
                ? new("content", prefix + "Content valid", CheckStatus.Pass, null)
                : new("content", prefix + "Content valid", CheckStatus.Fail, string.Join(" ", errors)));
            items.Add(DesignRules.HasFooter(design)
                ? new("footer", prefix + "Footer with unsubscribe link and address", CheckStatus.Pass, null)
                : new("footer", prefix + "Footer with unsubscribe link and address", CheckStatus.Fail, "Add the footer block (unsubscribe link + physical address)."));

            var links = EmailRenderer.ExtractLinks(design);
            var badLinks = DesignRules.AllBlocks(design.Blocks).Where(b => b.Href is not null && !DesignRules.IsLink(b.Href)).Select(b => b.Href!).ToList();
            items.Add(badLinks.Count == 0
                ? new("links", prefix + "Links valid", CheckStatus.Pass, $"{links.Count} tracked link(s).")
                : new("links", prefix + "Links valid", CheckStatus.Fail, "Invalid links: " + string.Join(", ", badLinks.Take(5))));

            var alt = TemplateService.ImageAltWarnings(design).ToList();
            items.Add(alt.Count == 0 ? new("alt-text", prefix + "Image alt text", CheckStatus.Pass, null) : new("alt-text", prefix + "Image alt text", CheckStatus.Warning, alt[0]));

            var composed = composer.Compose(design, subject, preview, composer.SampleValues(settings), TokenSource.Subscriber, Guid.Empty, null);
            var spam = ContentChecks.FindSpamPhrases(subject, composed.Text).Concat(ContentChecks.SubjectWarnings(subject)).ToList();
            items.Add(spam.Count == 0
                ? new("spam", prefix + "Spam trigger words", CheckStatus.Pass, null)
                : new("spam", prefix + "Spam trigger words", CheckStatus.Warning, string.Join("; ", spam)));
            var size = ContentChecks.SizeBytes(composed.Html);
            items.Add(size > ContentChecks.GmailClipBytes
                ? new("size", prefix + "Message size", CheckStatus.Warning, $"{size / 1024} KB — Gmail clips emails over 102 KB.")
                : new("size", prefix + "Message size", CheckStatus.Pass, $"{Math.Max(1, size / 1024)} KB."));
        }
    }

    // ---------- Lifecycle ----------

    /// <summary>
    /// Confirms a campaign for sending (Draft → Scheduled). Requires <c>confirm</c>, the campaign name typed back and a
    /// clean checklist. When the client requires approval the send job waits for it.
    /// </summary>
    public async Task<CampaignDto> SendAsync(Guid id, SendCampaignRequest r, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        var c = await LoadForChannelAsync(id, channels, ct);
        if (!r.Confirm) throw new DomainException("email.confirm_required", "Confirm the send (confirm: true).");
        if (!string.Equals(r.ConfirmName.Trim(), c.Name, StringComparison.Ordinal))
            throw new DomainException("email.confirm_name_mismatch", "Type the campaign name exactly to confirm the send.");
        AudienceService.ExpectStamp(c.ConcurrencyStamp, r.ConcurrencyStamp);
        if (c.Status != CampaignStatus.Draft) throw DomainException.Conflict("email.campaign_not_draft", $"The campaign is {c.Status}.");
        var checklist = await BuildChecklistAsync(c, ct);
        if (!checklist.CanSend)
            throw EmailProblem.Invalid("email.checklist_failed", "The pre-send checklist has blocking issues.",
                checklist.Items.Where(i => i.Status == CheckStatus.Fail).Select(i => $"{i.Label}: {i.Detail}"), "checklist");

        var now = clock.GetUtcNow().UtcDateTime;
        var approval = checklist.RequiresClientApproval ? (c.ApprovalStatus == ApprovalStatus.Approved ? ApprovalStatus.Approved : ApprovalStatus.Pending) : ApprovalStatus.NotRequired;
        var scheduledAt = c.ScheduleMode == ScheduleMode.FixedTime ? c.ScheduledAt : now;
        var userId = currentUser.Id;
        var stamp = c.ConcurrencyStamp;
        var newStamp = Guid.NewGuid();
        var changed = await db.Set<EmailCampaign>()
            .Where(x => x.Id == c.Id && x.Status == CampaignStatus.Draft && x.ConcurrencyStamp == stamp)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, CampaignStatus.Scheduled).SetProperty(x => x.ScheduledAt, scheduledAt)
                .SetProperty(x => x.ApprovalStatus, approval).SetProperty(x => x.SendConfirmedAt, now).SetProperty(x => x.SendConfirmedByUserId, userId)
                .SetProperty(x => x.ConcurrencyStamp, newStamp).SetProperty(x => x.UpdatedAt, now), ct);
        if (changed != 1) throw DomainException.Conflict("concurrency.conflict", "The campaign was changed by someone else. Reload and try again.");
        audit.Record("email.campaign.send_confirmed", nameof(EmailCampaign), c.Id, before: new { Status = CampaignStatus.Draft },
            after: new { Status = CampaignStatus.Scheduled, checklist.AudienceCount, approval, c.ScheduleMode, scheduledAt, c.ScheduledLocalTime }, reason: r.Reason);
        if (approval == ApprovalStatus.Pending) await NotifyApproversAsync(c, ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear(); // the row was changed with a conditional update; reload it
        return await GetAsync(id, channels, ct);
    }

    private async Task NotifyApproversAsync(EmailCampaign c, CancellationToken ct)
    {
        var approvers = await db.Set<ClientMember>().AsNoTracking()
            .Where(m => m.ClientAccountId == c.ClientAccountId && (m.Role == ClientMemberRole.Approver || m.Role == ClientMemberRole.Owner))
            .Select(m => m.UserId).ToListAsync(ct);
        foreach (var userId in approvers)
            await notifications.StageAsync(new NotificationRequest(userId, "email.campaign_approval", "A campaign is waiting for your approval",
                $"\"{c.Name}\" is ready to send and needs your approval.", "/client/email/approvals", new[] { NotificationChannel.Email }), ct);
    }

    public async Task<CampaignDto> UnscheduleAsync(Guid id, CampaignActionRequest r, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct) =>
        await TransitionAsync(id, r, channels, new[] { CampaignStatus.Scheduled }, CampaignStatus.Draft, "email.campaign.unscheduled", onlyBeforeStart: true, ct);

    public async Task<CampaignDto> PauseAsync(Guid id, CampaignActionRequest r, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct) =>
        await TransitionAsync(id, r, channels, new[] { CampaignStatus.Scheduled, CampaignStatus.Sending }, CampaignStatus.Paused, "email.campaign.paused", false, ct);

    public async Task<CampaignDto> ResumeAsync(Guid id, CampaignActionRequest r, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        var c = await LoadForChannelAsync(id, channels, ct);
        var target = c.SendStartedAt is null ? CampaignStatus.Scheduled : CampaignStatus.Sending;
        return await TransitionAsync(id, r, channels, new[] { CampaignStatus.Paused }, target, "email.campaign.resumed", false, ct);
    }

    /// <summary>Cancels a scheduled, sending or paused campaign; recipients not yet sent are cancelled (in-flight sends finish).</summary>
    public async Task<CampaignDto> CancelAsync(Guid id, CampaignActionRequest r, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        var dto = await TransitionAsync(id, r, channels, new[] { CampaignStatus.Scheduled, CampaignStatus.Sending, CampaignStatus.Paused },
            CampaignStatus.Cancelled, "email.campaign.cancelled", false, ct);
        await db.Set<CampaignRecipient>().Where(x => x.CampaignId == id && (x.Status == RecipientStatus.Pending || x.Status == RecipientStatus.Held))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, RecipientStatus.Cancelled).SetProperty(x => x.SkipReason, "Campaign cancelled"), ct);
        return dto;
    }

    private async Task<CampaignDto> TransitionAsync(Guid id, CampaignActionRequest r, IReadOnlyCollection<MessageChannel> channels, CampaignStatus[] from,
        CampaignStatus to, string action, bool onlyBeforeStart, CancellationToken ct)
    {
        var c = await LoadForChannelAsync(id, channels, ct);
        AudienceService.ExpectStamp(c.ConcurrencyStamp, r.ConcurrencyStamp);
        if (!from.Contains(c.Status)) throw DomainException.Conflict("email.campaign_invalid_state", $"The campaign is {c.Status}.");
        if (onlyBeforeStart && c.SendStartedAt is not null) throw DomainException.Conflict("email.campaign_started", "Sending has started; pause or cancel it instead.");
        var now = clock.GetUtcNow().UtcDateTime;
        var status = c.Status;
        var newStamp = Guid.NewGuid();
        var reason = Text.Clean(r.Reason, 500);
        var changed = await db.Set<EmailCampaign>().Where(x => x.Id == id && x.Status == status && (!onlyBeforeStart || x.SendStartedAt == null))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, to).SetProperty(x => x.ConcurrencyStamp, newStamp).SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.PausedAt, x => to == CampaignStatus.Paused ? now : x.PausedAt)
                .SetProperty(x => x.PauseReason, x => to == CampaignStatus.Paused ? reason : to == CampaignStatus.Sending || to == CampaignStatus.Scheduled ? null : x.PauseReason)
                .SetProperty(x => x.CancelledAt, x => to == CampaignStatus.Cancelled ? now : x.CancelledAt)
                .SetProperty(x => x.ApprovalStatus, x => to == CampaignStatus.Draft ? ApprovalStatus.NotRequired : x.ApprovalStatus), ct);
        if (changed != 1) throw DomainException.Conflict("concurrency.conflict", "The campaign changed state meanwhile. Reload and try again.");
        audit.Record(action, nameof(EmailCampaign), id, before: new { Status = status }, after: new { Status = to }, reason: r.Reason);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return await GetAsync(id, channels, ct);
    }

    /// <summary>Client approval decision (Approver/Owner of the client). Enforced by the send job, which never starts an unapproved campaign.</summary>
    public async Task<CampaignDto> DecideApprovalAsync(Guid id, ApprovalDecisionRequest r, CancellationToken ct)
    {
        var memberIds = await access.ClientMemberIdsAsync(ct);
        var c = await db.Set<EmailCampaign>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ClientAccountId != null && memberIds.Contains(x.ClientAccountId.Value), ct)
                ?? throw DomainException.NotFound("Campaign");
        await access.EnsureClientMemberAsync(c.ClientAccountId!.Value, ClientMemberRole.Approver, ct);
        if (c.ApprovalStatus != ApprovalStatus.Pending || c.Status is not (CampaignStatus.Scheduled or CampaignStatus.Paused))
            throw DomainException.Conflict("email.approval_not_pending", "This campaign is not waiting for approval.");
        if (!r.Approve && string.IsNullOrWhiteSpace(r.Note)) throw new DomainException("email.approval_note_required", "Tell the agency what to change.");
        var now = clock.GetUtcNow().UtcDateTime;
        var decision = r.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        var userId = currentUser.Id;
        var note = Text.Clean(r.Note, 1000);
        var newStamp = Guid.NewGuid();
        // A rejected campaign goes back to draft so the agency can edit it.
        var changed = await db.Set<EmailCampaign>().Where(x => x.Id == id && x.ApprovalStatus == ApprovalStatus.Pending && x.SendStartedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ApprovalStatus, decision).SetProperty(x => x.ApprovalDecidedAt, now)
                .SetProperty(x => x.ApprovalDecidedByUserId, userId).SetProperty(x => x.ApprovalNote, note)
                .SetProperty(x => x.Status, x => r.Approve ? x.Status : CampaignStatus.Draft)
                .SetProperty(x => x.ConcurrencyStamp, newStamp).SetProperty(x => x.UpdatedAt, now), ct);
        if (changed != 1) throw DomainException.Conflict("email.approval_not_pending", "This campaign is not waiting for approval.");
        audit.Record(r.Approve ? "email.campaign.client_approved" : "email.campaign.client_rejected", nameof(EmailCampaign), id,
            before: new { ApprovalStatus = ApprovalStatus.Pending }, after: new { ApprovalStatus = decision }, reason: note);
        if (c.CreatedByUserId is { } creator)
            await notifications.StageAsync(new NotificationRequest(creator, "email.campaign_approval_decided",
                r.Approve ? "Campaign approved by the client" : "Client requested changes",
                r.Approve ? $"\"{c.Name}\" was approved and will be sent as scheduled." : $"\"{c.Name}\": {note}", null), ct);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(await db.Set<EmailCampaign>().AsNoTracking().FirstAsync(x => x.Id == id, ct), ct);
    }

    /// <summary>Renders the campaign (variant) with sample data.</summary>
    public async Task<RenderResult> PreviewAsync(Guid id, string? variant, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        var c = await LoadForChannelAsync(id, channels, ct);
        return await PreviewCampaignAsync(c, variant, ct);
    }

    public async Task<RenderResult> PreviewCampaignAsync(EmailCampaign c, string? variant, CancellationToken ct)
    {
        var settings = await settingsStore.GetAsync(c.ClientAccountId, ct);
        if (c.Channel != MessageChannel.Email)
        {
            var text = MergeTags.Render(c.SmsBody, composer.SampleValues(settings), MergeContext.Text);
            return new RenderResult(c.Name, string.Empty, text, System.Text.Encoding.UTF8.GetByteCount(text), Array.Empty<string>(), Array.Empty<string>());
        }
        var (design, subject, preview, _) = await ContentForAsync(c, variant, ct);
        var composed = composer.Compose(design, subject, preview, composer.SampleValues(settings, c.Name), TokenSource.Subscriber, Guid.Empty, null);
        return new RenderResult(composed.Subject, composed.Html, composed.Text, ContentChecks.SizeBytes(composed.Html), ContentValidation.Errors(design, subject, preview),
            TemplateService.ImageAltWarnings(design).ToList());
    }

    /// <summary>Content of a variant (falls back to the campaign's own content).</summary>
    public async Task<(EmailDesign Design, string Subject, string? Preview, Guid? SenderId)> ContentForAsync(EmailCampaign c, string? variant, CancellationToken ct)
    {
        CampaignVariant? v = null;
        if (variant is not null) v = await db.Set<CampaignVariant>().AsNoTracking().FirstOrDefaultAsync(x => x.CampaignId == c.Id && x.Key == variant, ct);
        return (EmailDesign.Parse(v?.DesignJson ?? c.DesignJson), v?.Subject ?? c.Subject, v?.PreviewText ?? c.PreviewText, v?.SenderProfileId ?? c.SenderProfileId);
    }

    public async Task<TestSendResult> TestSendAsync(Guid id, TestSendRequest r, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        var c = await LoadForChannelAsync(id, channels, ct);
        if (c.Channel != MessageChannel.Email) throw new DomainException("email.test_email_only", "Test sends are available for email campaigns.");
        var (design, subject, preview, senderId) = await ContentForAsync(c, null, ct);
        r.SenderProfileId ??= senderId;
        return await templates.TestSendAsync(c.ClientAccountId, design, subject, preview, r, ct);
    }

    private static object Snapshot(EmailCampaign c) => new
    {
        c.Name, c.Channel, c.Type, c.Status, c.ListId, c.SegmentId, c.SenderProfileId, c.Subject, c.ScheduleMode, c.ScheduledAt, c.ScheduledLocalTime,
        c.ThrottlePerMinute, c.AbTestPercent, c.AbWinnerMetric, c.AbWaitHours,
    };

    private static JsonElement Json(string json) => JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json).RootElement.Clone();

    public static IReadOnlyList<string> ParseParams(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }
}
