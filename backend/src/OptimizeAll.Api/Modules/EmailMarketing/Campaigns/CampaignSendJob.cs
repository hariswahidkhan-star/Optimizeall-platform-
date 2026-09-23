using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Campaigns;

/// <summary>
/// Sends campaigns. Each run:
/// <list type="number">
/// <item>starts due Scheduled campaigns (never one that still needs client approval);</item>
/// <item>expands the audience into <see cref="CampaignRecipient"/> rows — unique per (campaign, subscriber), so a retried
///   run or a second worker can never add anybody twice;</item>
/// <item>claims due recipients one by one with a conditional update (Pending → Sending with a claim id) and sends only the
///   rows it won, within the campaign's messages-per-minute budget, send window and SMS quiet hours;</item>
/// <item>decides A/B winners and releases the held remainder to the winning variant;</item>
/// <item>completes campaigns with nothing left to send and fails interrupted sends (never re-sent automatically).</item>
/// </list>
/// Pausing or cancelling stops new claims immediately; a message whose provider call is in flight completes.
/// </summary>
public sealed class CampaignSendJob(
    AppDbContext db,
    CampaignAudience audience,
    CampaignService campaigns,
    MessageComposer composer,
    EmailSettingsStore settingsStore,
    EmailProviderResolver emailProviders,
    ISmsProvider sms,
    IWhatsAppTemplateProvider whatsApp,
    EmailMarketingUrls urls,
    IAuditLogger audit,
    IDatabaseDialect dialect,
    TimeProvider clock,
    ILogger<CampaignSendJob> logger) : IJob
{
    public const int ExpansionBatch = 1000;
    public const int MaxAttempts = 3;
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);
    /// <summary>Recipient-time-zone campaigns start expanding this long before the local time (earliest zone is UTC+14).</summary>
    public static readonly TimeSpan EarliestZoneLead = TimeSpan.FromHours(14);

    public string Name => nameof(CampaignSendJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var started = await StartDueAsync(ct);
        var recovered = await RecoverInterruptedAsync(ct);
        var sending = await db.Set<EmailCampaign>().AsNoTracking().Where(c => c.Status == CampaignStatus.Sending).Select(c => c.Id).ToListAsync(ct);
        int sent = 0, skipped = 0, failed = 0, completed = 0;
        foreach (var id in sending)
        {
            ct.ThrowIfCancellationRequested();
            var campaign = await db.Set<EmailCampaign>().AsNoTracking().FirstAsync(c => c.Id == id, ct);
            if (campaign.ExpandedAt is null) await ExpandAsync(campaign, ct);
            if (campaign.Type == CampaignType.AbTest) await DecideWinnerIfDueAsync(id, ct);
            var result = await SendBatchAsync(id, ct);
            sent += result.Sent; skipped += result.Skipped; failed += result.Failed;
            if (await CompleteIfDoneAsync(id, ct)) completed++;
        }
        return $"Started {started}; {sending.Count} sending: {sent} sent, {skipped} skipped, {failed} failed; {completed} completed; {recovered} interrupted sends failed.";
    }

    // ---------- 1. Start ----------

    private async Task<int> StartDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var scheduled = await db.Set<EmailCampaign>().AsNoTracking().Where(c => c.Status == CampaignStatus.Scheduled).ToListAsync(ct);
        var started = 0;
        foreach (var c in scheduled)
        {
            var due = c.ScheduleMode switch
            {
                ScheduleMode.FixedTime => c.ScheduledAt is { } at && at <= now,
                ScheduleMode.RecipientTimeZone => SendTiming.TryParseLocal(c.ScheduledLocalTime, out var local) &&
                                                  DateTime.SpecifyKind(local, DateTimeKind.Utc) - EarliestZoneLead <= now,
                _ => true,
            };
            if (!due) continue;

            // Client approval gate (also when the setting was switched on after the campaign was confirmed).
            if (c.ClientAccountId is not null && c.ApprovalStatus != ApprovalStatus.Approved)
            {
                var settings = await settingsStore.GetAsync(c.ClientAccountId, ct);
                if (settings.RequireClientApproval || c.ApprovalStatus is ApprovalStatus.Pending or ApprovalStatus.Rejected)
                {
                    if (c.ApprovalStatus == ApprovalStatus.NotRequired)
                        await db.Set<EmailCampaign>().Where(x => x.Id == c.Id && x.ApprovalStatus == ApprovalStatus.NotRequired)
                            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ApprovalStatus, ApprovalStatus.Pending), ct);
                    continue;
                }
            }

            var changed = await db.Set<EmailCampaign>().Where(x => x.Id == c.Id && x.Status == CampaignStatus.Scheduled)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, CampaignStatus.Sending).SetProperty(x => x.SendStartedAt, now), ct);
            if (changed == 1)
            {
                started++;
                audit.RecordSystem("email.campaign.sending_started", nameof(EmailCampaign), c.Id);
                await db.SaveChangesAsync(ct);
            }
        }
        return started;
    }

    // ---------- 2. Expansion ----------

    /// <summary>Creates the recipient rows (idempotent; concurrent expansion is resolved by the unique index).</summary>
    public async Task ExpandAsync(EmailCampaign c, CancellationToken ct)
    {
        var settings = await settingsStore.GetAsync(c.ClientAccountId, ct);
        var variants = c.Type == CampaignType.AbTest
            ? await db.Set<CampaignVariant>().AsNoTracking().Where(v => v.CampaignId == c.Id).OrderBy(v => v.Key).Select(v => v.Key).ToListAsync(ct)
            : new List<string>();
        if (c.Channel == MessageChannel.Email) await EnsureLinksAsync(c, ct);

        var eligible = await audience.EligibleAsync(c, ct);
        var recipients = db.Set<CampaignRecipient>();
        for (var guard = 0; guard < 10_000; guard++)
        {
            var batch = await eligible.Where(s => !recipients.Any(r => r.CampaignId == c.Id && r.SubscriberId == s.Id))
                .OrderBy(s => s.Id).Take(ExpansionBatch)
                .Select(s => new { s.Id, s.NormalizedEmail, s.Phone, s.TimeZone, s.Frequency, s.LastSentAt, s.ClientAccountId })
                .ToListAsync(ct);
            if (batch.Count == 0) break;
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var s in batch)
            {
                var zone = SendTiming.Zone(s.TimeZone, settings.DefaultTimeZone);
                var variant = variants.Count >= 2 ? AbTesting.Assign(c.Id, s.Id, c.AbTestPercent, variants) : null;
                var held = variants.Count >= 2 && variant is null;
                var frequencySkip = c.Channel == MessageChannel.Email && s.Frequency switch
                {
                    EmailFrequency.Weekly => s.LastSentAt > now.AddDays(-7),
                    EmailFrequency.Monthly => s.LastSentAt > now.AddDays(-30),
                    _ => false,
                };
                db.Set<CampaignRecipient>().Add(new CampaignRecipient
                {
                    CampaignId = c.Id,
                    ClientAccountId = c.ClientAccountId,
                    SubscriberId = s.Id,
                    Channel = c.Channel,
                    Address = (c.Channel == MessageChannel.Email ? s.NormalizedEmail : s.Phone)!,
                    Variant = variant,
                    IsTestCohort = variant is not null,
                    Status = frequencySkip ? RecipientStatus.Skipped : held ? RecipientStatus.Held : RecipientStatus.Pending,
                    SkipReason = frequencySkip ? "The contact asked for fewer emails (frequency preference)." : null,
                    DueAt = held ? null : SendTiming.DueAt(c.ScheduleMode, c.ScheduledAt, c.ScheduledLocalTime, zone, now),
                    CreatedAt = now,
                });
            }
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUnique(ex))
            {
                // Another worker inserted some of these rows; the next query skips everything that exists.
                logger.LogDebug("Concurrent expansion of campaign {Campaign}; retrying", c.Id);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        var total = await recipients.CountAsync(r => r.CampaignId == c.Id, ct);
        var expandedAt = clock.GetUtcNow().UtcDateTime;
        await db.Set<EmailCampaign>().Where(x => x.Id == c.Id && x.ExpandedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ExpandedAt, expandedAt).SetProperty(x => x.RecipientCount, total), ct);
    }

    private bool IsUnique(DbUpdateException ex) => dialect.IsUniqueViolation(ex);

    private async Task EnsureLinksAsync(EmailCampaign c, CancellationToken ct)
    {
        var links = EmailRenderer.ExtractLinks(EmailDesign.Parse(c.DesignJson)).ToList();
        foreach (var json in await db.Set<CampaignVariant>().AsNoTracking().Where(v => v.CampaignId == c.Id && v.DesignJson != null).Select(v => v.DesignJson!).ToListAsync(ct))
            links.AddRange(EmailRenderer.ExtractLinks(EmailDesign.Parse(json)));
        await composer.EnsureLinksAsync(c.ClientAccountId, SourceKey(c.Id), links, ct);
    }

    public static string SourceKey(Guid campaignId) => "c:" + campaignId.ToString("N");

    // ---------- 3. Sending ----------

    public sealed record BatchResult(int Sent, int Skipped, int Failed);

    private async Task<BatchResult> SendBatchAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.Set<EmailCampaign>().AsNoTracking().FirstAsync(c => c.Id == campaignId, ct);
        if (campaign.Status != CampaignStatus.Sending || campaign.ExpandedAt is null) return new(0, 0, 0);
        var now = clock.GetUtcNow().UtcDateTime;
        var windowStart = now.AddMinutes(-1);
        var recent = await db.Set<CampaignRecipient>().CountAsync(r => r.CampaignId == campaignId &&
            (r.Status == RecipientStatus.Sending || (r.SentAt != null && r.SentAt > windowStart)), ct);
        var budget = campaign.ThrottlePerMinute - recent;
        if (budget <= 0) return new(0, 0, 0);

        var candidates = await db.Set<CampaignRecipient>().AsNoTracking()
            .Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Pending && r.DueAt <= now && (r.LockedUntil == null || r.LockedUntil < now))
            .OrderBy(r => r.DueAt).ThenBy(r => r.Id).Select(r => r.Id).Take(budget).ToListAsync(ct);
        if (candidates.Count == 0) return new(0, 0, 0);

        var context = await CreateContextAsync(campaign, ct);
        int sent = 0, skipped = 0, failed = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            // Honour pause/cancel between messages.
            if (i > 0 && i % 10 == 0 && !await db.Set<EmailCampaign>().AnyAsync(c => c.Id == campaignId && c.Status == CampaignStatus.Sending, ct)) break;
            var claimNow = clock.GetUtcNow().UtcDateTime;
            var claimId = Guid.NewGuid();
            var lease = claimNow.Add(ClaimLease);
            var id = candidates[i];
            var won = await db.Set<CampaignRecipient>()
                .Where(r => r.Id == id && r.Status == RecipientStatus.Pending && (r.LockedUntil == null || r.LockedUntil < claimNow))
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, RecipientStatus.Sending).SetProperty(r => r.ClaimId, claimId)
                    .SetProperty(r => r.LockedUntil, lease), ct);
            if (won != 1) continue; // another worker has it
            // Re-check the campaign after claiming so a pause that raced the claim wins.
            if (!await db.Set<EmailCampaign>().AnyAsync(c => c.Id == campaignId && c.Status == CampaignStatus.Sending, ct))
            {
                await Release(id, claimId, null, ct);
                break;
            }
            var outcome = await SendOneAsync(context, id, claimId, ct);
            if (outcome == Outcome.Sent) sent++;
            else if (outcome == Outcome.Skipped) skipped++;
            else if (outcome == Outcome.Failed) failed++;
            else if (outcome == Outcome.Stop) break;
        }
        return new(sent, skipped, failed);
    }

    private enum Outcome { Sent, Skipped, Failed, Retrying, Deferred, Stop }

    /// <summary>Per-campaign data loaded once per batch.</summary>
    private sealed class SendContext
    {
        public required EmailCampaign Campaign { get; init; }
        public required EmailWorkspaceSettings Settings { get; init; }
        public required Dictionary<string, Guid> LinkIds { get; init; }
        public required Dictionary<string, (EmailDesign Design, string Subject, string? Preview, SenderProfile? Sender)> Content { get; init; }
        public IEmailMarketingProvider? EmailProvider { get; init; }
    }

    private async Task<SendContext> CreateContextAsync(EmailCampaign c, CancellationToken ct)
    {
        var settings = await settingsStore.GetAsync(c.ClientAccountId, ct);
        var sourceKey = SourceKey(c.Id);
        var links = await db.Set<TrackedLink>().AsNoTracking().Where(l => l.SourceKey == sourceKey).ToListAsync(ct);
        var content = new Dictionary<string, (EmailDesign Design, string Subject, string? Preview, SenderProfile? Sender)>();
        if (c.Channel == MessageChannel.Email)
        {
            var keys = new List<string?> { null };
            if (c.Type == CampaignType.AbTest)
                keys.AddRange(await db.Set<CampaignVariant>().AsNoTracking().Where(v => v.CampaignId == c.Id).Select(v => v.Key).ToListAsync(ct));
            foreach (var key in keys)
            {
                var (design, subject, preview, senderId) = await campaigns.ContentForAsync(c, key, ct);
                var sender = senderId is null ? null : await db.Set<SenderProfile>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == senderId, ct);
                content[key ?? string.Empty] = (design, subject, preview, sender);
            }
        }
        return new SendContext
        {
            Campaign = c,
            Settings = settings,
            LinkIds = links.GroupBy(l => l.Url).ToDictionary(g => g.Key, g => g.First().Id),
            Content = content,
            EmailProvider = c.Channel == MessageChannel.Email ? await emailProviders.ForWorkspaceAsync(c.ClientAccountId, ct) : null,
        };
    }

    private async Task<Outcome> SendOneAsync(SendContext ctx, Guid recipientId, Guid claimId, CancellationToken ct)
    {
        var c = ctx.Campaign;
        var r = await db.Set<CampaignRecipient>().AsNoTracking().FirstAsync(x => x.Id == recipientId, ct);
        var s = await db.Set<Subscriber>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.SubscriberId, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        // Consent and suppression are checked again at send time: suppression always wins.
        if (await audience.BlockReasonAsync(s, c.Channel, r.Address, ct) is { } reason)
        {
            await Finish(recipientId, claimId, RecipientStatus.Skipped, null, reason, ct);
            return Outcome.Skipped;
        }
        var zone = SendTiming.Zone(s!.TimeZone, ctx.Settings.DefaultTimeZone);
        if (c.SendWindowStartHour is { } ws && c.SendWindowEndHour is { } we && !SendTiming.InHourRange(TimeZoneInfo.ConvertTimeFromUtc(now, zone).Hour, ws, we))
        {
            await Release(recipientId, claimId, SendTiming.NextInWindow(now, zone, ws, we), ct);
            return Outcome.Deferred;
        }
        if (c.Channel != MessageChannel.Email && SendTiming.IsQuietHours(now, zone, ctx.Settings.QuietHoursStart, ctx.Settings.QuietHoursEnd))
        {
            await Release(recipientId, claimId, SendTiming.AfterQuietHours(now, zone, ctx.Settings.QuietHoursStart, ctx.Settings.QuietHoursEnd), ct);
            return Outcome.Deferred;
        }

        ProviderResult result;
        var segments = 0;
        decimal cost = 0;
        try
        {
            var values = await composer.ValuesAsync(s, ctx.Settings, TokenSource.CampaignRecipient, r.Id, c.Name, null, ct);
            switch (c.Channel)
            {
                case MessageChannel.Email:
                {
                    var (design, subject, preview, sender) = ctx.Content.TryGetValue(r.Variant ?? string.Empty, out var v) ? v : ctx.Content[string.Empty];
                    if (sender?.VerifiedAt is null)
                    {
                        await Finish(recipientId, claimId, RecipientStatus.Failed, null, "The sender profile is not verified.", ct);
                        return Outcome.Failed;
                    }
                    var composed = composer.Compose(design, subject, preview, values, TokenSource.CampaignRecipient, r.Id, ctx.LinkIds,
                        feedbackId: $"{c.Id:N}:{c.ScopeKey}:oa", language: s.Language ?? "en");
                    result = await ctx.EmailProvider!.SendAsync(new OutboundEmail(c.ClientAccountId, r.Address, FullName(s), sender.FromEmail, sender.FromName,
                        sender.ReplyTo, composed.Subject, composed.Html, composed.Text, composed.Headers,
                        new Dictionary<string, string> { ["oa_ref"] = "c:" + r.Id.ToString("N") }), ct);
                    break;
                }
                case MessageChannel.Sms:
                {
                    var text = MessageComposer.ComposeText(c.SmsBody ?? string.Empty, values);
                    segments = SmsSegments.Calculate(text).Segments;
                    cost = segments * ctx.Settings.SmsCostPerSegment;
                    result = await sms.SendAsync(c.ClientAccountId, r.Address, text, $"{urls.PublicBaseUrl}/api/v1/public/sms/webhooks/twilio/{c.ScopeKey}/status", ct);
                    break;
                }
                default:
                {
                    var parameters = CampaignService.ParseParams(c.WhatsAppParametersJson).Select(p => MessageComposer.ComposeText(p, values)).ToList();
                    cost = ctx.Settings.WhatsAppCostPerMessage;
                    result = await whatsApp.SendTemplateAsync(c.ClientAccountId, r.Address, c.WhatsAppTemplateName ?? string.Empty,
                        c.WhatsAppTemplateLanguage ?? "en", parameters, ct);
                    break;
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Sending recipient {Recipient} threw", recipientId);
            result = ProviderResult.Transient($"{ex.GetType().Name}: {ex.Message}");
        }

        switch (result.Outcome)
        {
            case ProviderOutcome.Accepted:
                var sentAt = clock.GetUtcNow().UtcDateTime;
                await db.Set<CampaignRecipient>().Where(x => x.Id == recipientId && x.ClaimId == claimId && x.Status == RecipientStatus.Sending)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, RecipientStatus.Sent).SetProperty(x => x.SentAt, sentAt)
                        .SetProperty(x => x.ProviderMessageId, Text.Truncate(result.MessageId, 200)).SetProperty(x => x.Attempts, x => x.Attempts + 1)
                        .SetProperty(x => x.Segments, segments).SetProperty(x => x.Cost, cost).SetProperty(x => x.Error, (string?)null)
                        .SetProperty(x => x.LockedUntil, (DateTime?)null), CancellationToken.None);
                await db.Set<Subscriber>().Where(x => x.Id == s.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSentAt, sentAt), CancellationToken.None);
                return Outcome.Sent;

            case ProviderOutcome.NotConfigured:
                // Nothing was attempted: put the message back and pause the campaign so staff can fix the integration.
                await Release(recipientId, claimId, null, ct);
                var reasonText = Text.Truncate(result.Error ?? "The provider is not configured.", 500);
                await db.Set<EmailCampaign>().Where(x => x.Id == c.Id && x.Status == CampaignStatus.Sending)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, CampaignStatus.Paused).SetProperty(x => x.PausedAt, now)
                        .SetProperty(x => x.PauseReason, reasonText), CancellationToken.None);
                audit.RecordSystem("email.campaign.paused", nameof(EmailCampaign), c.Id, reason: reasonText);
                await db.SaveChangesAsync(CancellationToken.None);
                return Outcome.Stop;

            case ProviderOutcome.TransientFailure when r.Attempts + 1 < MaxAttempts:
                var retryAt = now.AddMinutes(r.Attempts == 0 ? 1 : 5);
                var error = Text.Truncate(result.Error, 1000);
                await db.Set<CampaignRecipient>().Where(x => x.Id == recipientId && x.ClaimId == claimId && x.Status == RecipientStatus.Sending)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, RecipientStatus.Pending).SetProperty(x => x.Attempts, x => x.Attempts + 1)
                        .SetProperty(x => x.DueAt, retryAt).SetProperty(x => x.Error, error).SetProperty(x => x.LockedUntil, (DateTime?)null)
                        .SetProperty(x => x.ClaimId, (Guid?)null), CancellationToken.None);
                return Outcome.Retrying;

            default:
                await Finish(recipientId, claimId, RecipientStatus.Failed, null, result.Error ?? "The provider rejected the message.", ct);
                return Outcome.Failed;
        }
    }

    private async Task Finish(Guid id, Guid claimId, RecipientStatus status, string? messageId, string reason, CancellationToken ct)
    {
        var text = Text.Truncate(reason, status == RecipientStatus.Skipped ? 200 : 1000);
        await db.Set<CampaignRecipient>().Where(x => x.Id == id && x.ClaimId == claimId && x.Status == RecipientStatus.Sending)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, status)
                .SetProperty(x => x.SkipReason, x => status == RecipientStatus.Skipped ? text : x.SkipReason)
                .SetProperty(x => x.Error, x => status == RecipientStatus.Failed ? text : x.Error)
                .SetProperty(x => x.Attempts, x => status == RecipientStatus.Failed ? x.Attempts + 1 : x.Attempts)
                .SetProperty(x => x.ProviderMessageId, messageId)
                .SetProperty(x => x.LockedUntil, (DateTime?)null), CancellationToken.None);
    }

    /// <summary>Returns a claimed recipient to Pending (optionally later), without counting an attempt.</summary>
    private async Task Release(Guid id, Guid claimId, DateTime? dueAt, CancellationToken ct)
    {
        await db.Set<CampaignRecipient>().Where(x => x.Id == id && x.ClaimId == claimId && x.Status == RecipientStatus.Sending)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, RecipientStatus.Pending).SetProperty(x => x.ClaimId, (Guid?)null)
                .SetProperty(x => x.LockedUntil, (DateTime?)null).SetProperty(x => x.DueAt, x => dueAt ?? x.DueAt), CancellationToken.None);
    }

    private static string? FullName(Subscriber s)
    {
        var name = string.Join(' ', new[] { s.FirstName, s.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));
        return name.Length == 0 ? null : name;
    }

    // ---------- 4. A/B ----------

    /// <summary>
    /// Once every test-cohort message is out, waits <c>AbWaitHours</c> and then picks the variant with the best unique
    /// open (or click) rate — machine opens excluded — and releases the held remainder with the winning content.
    /// </summary>
    public async Task DecideWinnerIfDueAsync(Guid campaignId, CancellationToken ct)
    {
        var c = await db.Set<EmailCampaign>().AsNoTracking().FirstAsync(x => x.Id == campaignId, ct);
        if (c.AbWinnerVariant is not null || c.ExpandedAt is null) return;
        var now = clock.GetUtcNow().UtcDateTime;
        var testPending = await db.Set<CampaignRecipient>().AnyAsync(r => r.CampaignId == campaignId && r.IsTestCohort &&
            (r.Status == RecipientStatus.Pending || r.Status == RecipientStatus.Sending), ct);
        if (testPending) return;
        if (c.AbTestCompletedAt is null)
        {
            await db.Set<EmailCampaign>().Where(x => x.Id == campaignId && x.AbTestCompletedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.AbTestCompletedAt, now), ct);
            if (c.AbWaitHours > 0) return;
        }
        else if (c.AbTestCompletedAt.Value.AddHours(c.AbWaitHours) > now) return;

        var stats = await db.Set<CampaignRecipient>().AsNoTracking().Where(r => r.CampaignId == campaignId && r.IsTestCohort && r.Variant != null)
            .GroupBy(r => r.Variant!)
            .Select(g => new VariantStats(g.Key,
                g.Count(r => r.Status == RecipientStatus.Sent && r.BounceType != BounceType.Hard),
                g.Count(r => r.OpenedAt != null),
                g.Count(r => r.ClickedAt != null)))
            .ToListAsync(ct);
        var keys = await db.Set<CampaignVariant>().AsNoTracking().Where(v => v.CampaignId == campaignId).Select(v => v.Key).ToListAsync(ct);
        foreach (var key in keys.Where(k => stats.All(s => s.Key != k))) stats.Add(new VariantStats(key, 0, 0, 0));
        if (stats.Count == 0) return;
        var winner = AbTesting.PickWinner(stats, c.AbWinnerMetric);
        var decided = await db.Set<EmailCampaign>().Where(x => x.Id == campaignId && x.AbWinnerVariant == null)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.AbWinnerVariant, winner).SetProperty(x => x.AbDecidedAt, now), ct);
        if (decided != 1) return;
        await db.Set<CampaignRecipient>().Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Held)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, RecipientStatus.Pending).SetProperty(r => r.Variant, winner).SetProperty(r => r.DueAt, now), ct);
        audit.RecordSystem("email.campaign.ab_winner", nameof(EmailCampaign), campaignId,
            after: new { winner, metric = c.AbWinnerMetric, stats = stats.Select(s => new { s.Key, s.Delivered, s.UniqueOpens, s.UniqueClicks }) });
        await db.SaveChangesAsync(ct);
    }

    // ---------- 5. Completion & recovery ----------

    private async Task<bool> CompleteIfDoneAsync(Guid campaignId, CancellationToken ct)
    {
        var remaining = await db.Set<CampaignRecipient>().AnyAsync(r => r.CampaignId == campaignId &&
            (r.Status == RecipientStatus.Pending || r.Status == RecipientStatus.Sending || r.Status == RecipientStatus.Held), ct);
        if (remaining) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        var total = await db.Set<CampaignRecipient>().CountAsync(r => r.CampaignId == campaignId, ct);
        var done = await db.Set<EmailCampaign>().Where(x => x.Id == campaignId && x.Status == CampaignStatus.Sending && x.ExpandedAt != null)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, CampaignStatus.Sent).SetProperty(x => x.CompletedAt, now).SetProperty(x => x.RecipientCount, total), ct);
        if (done == 1)
        {
            audit.RecordSystem("email.campaign.sent", nameof(EmailCampaign), campaignId, after: new { recipients = total });
            await db.SaveChangesAsync(ct);
        }
        return done == 1;
    }

    /// <summary>Recipients left in Sending past their lease (worker crash mid-send) are failed, never re-sent: the provider may have delivered them.</summary>
    private async Task<int> RecoverInterruptedAsync(CancellationToken ct)
    {
        var staleBefore = clock.GetUtcNow().UtcDateTime.AddMinutes(-5);
        return await db.Set<CampaignRecipient>()
            .Where(r => r.Status == RecipientStatus.Sending && r.LockedUntil != null && r.LockedUntil < staleBefore)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, RecipientStatus.Failed).SetProperty(r => r.LockedUntil, (DateTime?)null)
                .SetProperty(r => r.Error, "The send was interrupted before its outcome was recorded; not retried automatically to avoid a duplicate."), ct);
    }
}
