using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Automations;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Tracking;

public sealed record PreferenceTopic(Guid ListId, string Name, string? Description, bool Subscribed);

public sealed record PreferencesDto(string Workspace, string MaskedEmail, bool UnsubscribedFromAll, EmailFrequency Frequency, IReadOnlyList<PreferenceTopic> Topics);

public sealed class PreferencesUpdate
{
    public List<TopicChoice>? Topics { get; set; }
    public EmailFrequency? Frequency { get; set; }
    public bool UnsubscribeAll { get; set; }
}

public sealed class TopicChoice
{
    public Guid ListId { get; set; }
    public bool Subscribed { get; set; }
}

public sealed class SignupRequest
{
    [Required, MaxLength(254)] public string Email { get; set; } = string.Empty;
    [MaxLength(100)] public string? FirstName { get; set; }
    [MaxLength(100)] public string? LastName { get; set; }
    /// <summary>The visitor ticked the consent box (required).</summary>
    public bool Consent { get; set; }
    /// <summary>Honeypot: must stay empty (bots fill every field).</summary>
    [MaxLength(200)] public string? Website { get; set; }
}

public sealed record SignupForm(string ListName, string Workspace, string ConsentText, bool DoubleOptIn);

/// <summary>A message identified by a tracking token.</summary>
public sealed record TrackedMessage(TokenSource Source, Guid MessageId, Guid SubscriberId, Guid? ClientAccountId, Guid? CampaignId, Guid? AutomationId,
    string? StepKey, DateTime? SentAt, CampaignRecipient? Recipient);

/// <summary>Records opens and clicks (bot-filtered), resolves click destinations safely, unsubscribes and preferences.</summary>
public sealed class EngagementService(
    AppDbContext db,
    TrackingTokens tokens,
    AudienceService audiences,
    MessageComposer composer,
    EmailSettingsStore settings,
    IPrivacyHasher hasher,
    TimeProvider clock,
    ILogger<EngagementService> logger)
{
    public async Task<TrackedMessage?> ResolveAsync(TrackingToken token, CancellationToken ct)
    {
        switch (token.Source)
        {
            case TokenSource.CampaignRecipient:
            {
                var r = await db.Set<CampaignRecipient>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == token.MessageId, ct);
                return r is null ? null : new TrackedMessage(token.Source, r.Id, r.SubscriberId, r.ClientAccountId, r.CampaignId, null, null, r.SentAt, r);
            }
            case TokenSource.AutomationStepRun:
            {
                var run = await db.Set<AutomationStepRun>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == token.MessageId, ct);
                return run is null ? null : new TrackedMessage(token.Source, run.Id, run.SubscriberId, run.ClientAccountId, null, run.AutomationId, run.StepKey, run.SentAt, null);
            }
            case TokenSource.Subscriber:
            {
                var s = await db.Set<Subscriber>().AsNoTracking().Where(x => x.Id == token.MessageId).Select(x => new { x.Id, x.ClientAccountId }).FirstOrDefaultAsync(ct);
                return s is null ? null : new TrackedMessage(token.Source, s.Id, s.Id, s.ClientAccountId, null, null, null, null, null);
            }
            default:
                return null;
        }
    }

    /// <summary>Records an open. Machine opens (Apple MPP, scanners) are stored separately and never count as human opens.</summary>
    public async Task RecordOpenAsync(string token, string? userAgent, string? ip, CancellationToken ct)
    {
        if (tokens.Read(token, TokenPurpose.Open) is not { } t || await ResolveAsync(t, ct) is not { } m) return;
        var now = clock.GetUtcNow().UtcDateTime;
        var c = EngagementHeuristics.ClassifyOpen(userAgent);
        db.Set<EngagementEvent>().Add(new EngagementEvent
        {
            ClientAccountId = m.ClientAccountId, SubscriberId = m.SubscriberId, CampaignId = m.CampaignId, RecipientId = m.Recipient?.Id,
            AutomationId = m.AutomationId, AutomationStepRunId = m.Source == TokenSource.AutomationStepRun ? m.MessageId : null,
            Type = EngagementType.Open, OccurredAt = now, IsMachine = c.IsMachine, Device = c.Device, MailClient = c.MailClient,
            IpHash = hasher.Hash(ip), Detail = c.Reason,
        });
        await db.SaveChangesAsync(ct);
        await ApplyEngagementAsync(m, EngagementType.Open, c.IsMachine, now, ct);
    }

    /// <summary>
    /// Resolves a click to the stored link's URL (merge tags resolved URL-safely for the contact) and records it.
    /// Returns null for forged tokens, unknown links or a link that does not belong to the token's message, so the
    /// endpoint can never redirect to a URL that was not part of the content.
    /// </summary>
    public async Task<string?> RecordClickAsync(string token, string? userAgent, string? ip, CancellationToken ct)
    {
        if (tokens.Read(token, TokenPurpose.Click) is not { } t || await ResolveAsync(t, ct) is not { } m) return null;
        var link = await db.Set<TrackedLink>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == t.Extra, ct);
        if (link is null) return null;
        var expectedSource = m.Source switch
        {
            TokenSource.CampaignRecipient => CampaignSendJob.SourceKey(m.CampaignId!.Value),
            TokenSource.AutomationStepRun => AutomationSources.Key(m.AutomationId!.Value, m.StepKey!),
            _ => null,
        };
        if (expectedSource is null || link.SourceKey != expectedSource) return null;

        var subscriber = await db.Set<Subscriber>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == m.SubscriberId, ct);
        if (subscriber is null) return null;
        var ws = await settings.GetAsync(subscriber.ClientAccountId, ct);
        var values = await composer.ValuesAsync(subscriber, ws, m.Source, m.MessageId, null, null, ct);
        var destination = MergeTags.Render(link.Url, values, MergeContext.Url);
        if (!HtmlSanitizer.IsHttpUrl(destination)) return null;

        var now = clock.GetUtcNow().UtcDateTime;
        var c = EngagementHeuristics.ClassifyClick(userAgent, m.SentAt, now);
        db.Set<EngagementEvent>().Add(new EngagementEvent
        {
            ClientAccountId = m.ClientAccountId, SubscriberId = m.SubscriberId, CampaignId = m.CampaignId, RecipientId = m.Recipient?.Id,
            AutomationId = m.AutomationId, AutomationStepRunId = m.Source == TokenSource.AutomationStepRun ? m.MessageId : null, LinkId = link.Id,
            Type = EngagementType.Click, OccurredAt = now, IsMachine = c.IsMachine, Device = c.Device, MailClient = c.MailClient, IpHash = hasher.Hash(ip),
            Detail = Text.Truncate(c.Reason, 200),
        });
        await db.SaveChangesAsync(ct);
        await ApplyEngagementAsync(m, EngagementType.Click, c.IsMachine, now, ct);
        return destination;
    }

    /// <summary>Atomic counters on the message and the contact (a human click also counts as an open).</summary>
    private async Task ApplyEngagementAsync(TrackedMessage m, EngagementType type, bool machine, DateTime now, CancellationToken ct)
    {
        if (m.Source == TokenSource.CampaignRecipient)
        {
            var q = db.Set<CampaignRecipient>().Where(r => r.Id == m.MessageId);
            if (type == EngagementType.Open)
            {
                if (machine) await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.MachineOpenCount, r => r.MachineOpenCount + 1), ct);
                else await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.OpenCount, r => r.OpenCount + 1).SetProperty(r => r.OpenedAt, r => r.OpenedAt ?? now), ct);
            }
            else if (!machine)
                await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.ClickCount, r => r.ClickCount + 1).SetProperty(r => r.ClickedAt, r => r.ClickedAt ?? now)
                    .SetProperty(r => r.OpenedAt, r => r.OpenedAt ?? now), ct);
        }
        else if (m.Source == TokenSource.AutomationStepRun && !machine)
        {
            var q = db.Set<AutomationStepRun>().Where(r => r.Id == m.MessageId);
            if (type == EngagementType.Open) await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.OpenedAt, r => r.OpenedAt ?? now), ct);
            else await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.ClickedAt, r => r.ClickedAt ?? now).SetProperty(r => r.OpenedAt, r => r.OpenedAt ?? now), ct);
        }
        if (machine) return;
        var s = db.Set<Subscriber>().Where(x => x.Id == m.SubscriberId);
        if (type == EngagementType.Open) await s.ExecuteUpdateAsync(u => u.SetProperty(x => x.LastOpenAt, now).SetProperty(x => x.SoftBounceCount, 0), ct);
        else await s.ExecuteUpdateAsync(u => u.SetProperty(x => x.LastClickAt, now).SetProperty(x => x.LastOpenAt, now), ct);
    }

    /// <summary>Unsubscribes the token's contact from everything in the workspace (RFC 8058 one-click and the unsubscribe page).</summary>
    public async Task<bool> UnsubscribeAsync(string token, string source, string? ip, CancellationToken ct)
    {
        var t = tokens.Read(token, TokenPurpose.Unsubscribe) ?? tokens.Read(token, TokenPurpose.Preferences);
        if (t is null || await ResolveAsync(t.Value, ct) is not { } m) return false;
        var s = await db.Set<Subscriber>().FirstOrDefaultAsync(x => x.Id == m.SubscriberId, ct);
        if (s is null) return false;
        await audiences.UnsubscribeAsync(s, null, source, hasher.Hash(ip), m.Recipient, ct);
        logger.LogInformation("Contact unsubscribed via {Source}", source);
        return true;
    }

    public async Task<PreferencesDto?> PreferencesAsync(string token, CancellationToken ct)
    {
        var (s, _) = await SubscriberForAsync(token, ct);
        return s is null ? null : await BuildPreferencesAsync(s, ct);
    }

    public async Task<PreferencesDto?> UpdatePreferencesAsync(string token, PreferencesUpdate update, string? ip, CancellationToken ct)
    {
        var (s, message) = await SubscriberForAsync(token, ct);
        if (s is null) return null;
        var ipHash = hasher.Hash(ip);
        if (update.UnsubscribeAll)
        {
            // Reached from a campaign email: the unsubscribe is that campaign's (report, activity), like the unsubscribe link.
            await audiences.UnsubscribeAsync(s, null, "preference-center", ipHash, message?.Recipient, ct);
            return await BuildPreferencesAsync(await db.Set<Subscriber>().AsNoTracking().FirstAsync(x => x.Id == s.Id, ct), ct);
        }
        if (update.Frequency is { } frequency) s.Frequency = frequency;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var choice in update.Topics ?? new List<TopicChoice>())
        {
            var list = await db.Set<EmailList>().FirstOrDefaultAsync(l => l.Id == choice.ListId && l.ScopeKey == s.ScopeKey && !l.IsArchived, ct);
            if (list is null) continue;
            if (!choice.Subscribed) { await audiences.UnsubscribeAsync(s, list.Id, "preference-center", ipHash, null, ct); continue; }
            // Opting in from the preference center: the contact proved control of the inbox (signed link), so this is
            // a confirmed opt-in and lifts an earlier unsubscribe (never a bounce or complaint).
            if (s.Status is SubscriberStatus.Unsubscribed or SubscriberStatus.Cleaned) s.Status = SubscriberStatus.Subscribed;
            if (s.EmailConsent != ConsentStatus.Granted && s.Status == SubscriberStatus.Subscribed)
            {
                await audiences.SetConsentAsync(s, MessageChannel.Email, ConsentStatus.Granted, "preference-center", ipHash, list.ConsentTextVersion, null, null, ct);
                if (s.NormalizedEmail is not null)
                    await db.Set<Suppression>().Where(x => x.ScopeKey == s.ScopeKey && x.Channel == MessageChannel.Email && x.Value == s.NormalizedEmail &&
                                                           x.Reason == SuppressionReason.Unsubscribed).ExecuteDeleteAsync(ct);
            }
            await db.SaveChangesAsync(ct);
            if (s.Status == SubscriberStatus.Subscribed)
                await audiences.SubscribeAsync(list, s, "preference-center", requireConfirmation: false, ipHash, ct);
        }
        await db.SaveChangesAsync(ct);
        return await BuildPreferencesAsync(await db.Set<Subscriber>().AsNoTracking().FirstAsync(x => x.Id == s.Id, ct), ct);
    }

    private async Task<(Subscriber? Subscriber, TrackedMessage? Message)> SubscriberForAsync(string token, CancellationToken ct)
    {
        var t = tokens.Read(token, TokenPurpose.Preferences) ?? tokens.Read(token, TokenPurpose.Unsubscribe);
        if (t is null || await ResolveAsync(t.Value, ct) is not { } m) return (null, null);
        return (await db.Set<Subscriber>().FirstOrDefaultAsync(x => x.Id == m.SubscriberId, ct), m);
    }

    private async Task<PreferencesDto> BuildPreferencesAsync(Subscriber s, CancellationToken ct)
    {
        var memberships = await db.Set<ListMembership>().AsNoTracking().Where(m => m.SubscriberId == s.Id).ToListAsync(ct);
        var memberListIds = memberships.Select(m => m.ListId).ToList();
        var lists = await db.Set<EmailList>().AsNoTracking()
            .Where(l => l.ScopeKey == s.ScopeKey && !l.IsArchived && (l.ShowInPreferenceCenter || memberListIds.Contains(l.Id)))
            .OrderBy(l => l.Name).ToListAsync(ct);
        var ws = await settings.GetAsync(s.ClientAccountId, ct);
        var unsubscribedAll = s.Status != SubscriberStatus.Subscribed || s.EmailConsent == ConsentStatus.Withdrawn;
        return new PreferencesDto(ws.OrganizationName, Mask(s.Email), unsubscribedAll, s.Frequency,
            lists.Select(l => new PreferenceTopic(l.Id, l.Name, l.Description,
                !unsubscribedAll && memberships.Any(m => m.ListId == l.Id && m.Status == MembershipStatus.Subscribed))).ToList());
    }

    public static string Mask(string? email)
    {
        var at = email?.IndexOf('@') ?? -1;
        if (email is null || at <= 0) return "your address";
        var local = email[..at];
        return (local.Length <= 2 ? local[..1] : local[..2]) + new string('•', Math.Max(1, local.Length - 2)) + email[at..];
    }

    // ---------- Hosted signup form ----------

    public async Task<SignupForm?> SignupFormAsync(string key, CancellationToken ct)
    {
        var list = await db.Set<EmailList>().AsNoTracking().FirstOrDefaultAsync(l => l.PublicKey == key && !l.IsArchived, ct);
        if (list is null) return null;
        var ws = await settings.GetAsync(list.ClientAccountId, ct);
        return new SignupForm(list.Name, ws.OrganizationName, list.ConsentText, list.DoubleOptIn);
    }

    /// <summary>
    /// Public sign-up. Consent is mandatory and recorded with the consent text version and a hashed IP. With double
    /// opt-in the contact receives only the confirmation email until they confirm. The response never reveals
    /// whether the address was already known.
    /// </summary>
    public async Task<bool> SignupAsync(string key, SignupRequest r, string? ip, CancellationToken ct)
    {
        var list = await db.Set<EmailList>().AsNoTracking().FirstOrDefaultAsync(l => l.PublicKey == key && !l.IsArchived, ct);
        if (list is null) return false;
        if (!string.IsNullOrEmpty(r.Website)) return true; // honeypot: pretend success
        if (!r.Consent) throw new DomainException("email.consent_required", "Tick the box to agree to receive emails.");
        var email = ContactRules.NormalizeEmail(r.Email) ?? throw new DomainException("email.invalid_email", "Enter a valid email address.");
        var ipHash = hasher.Hash(ip);
        var consent = new ConsentGrant(list.DoubleOptIn ? ConsentStatus.Pending : ConsentStatus.Granted, "form", ipHash, list.ConsentTextVersion, null, list.ConsentText);
        var input = new ContactInput(email, null, Text.Clean(r.FirstName, 100), Text.Clean(r.LastName, 100), null, null, null, null, null, "form");
        var (subscriber, _) = await audiences.UpsertContactAsync(list.ClientAccountId, input, consent, null, allowResubscribe: false, ct);
        if (subscriber.EmailConsent == ConsentStatus.Granted && !list.DoubleOptIn)
            await audiences.SubscribeAsync(list, subscriber, "form", requireConfirmation: false, ipHash, ct);
        else
            await audiences.SubscribeAsync(list, subscriber, "form", requireConfirmation: true, ipHash, ct);
        return true;
    }

    public async Task<(string ListName, string Workspace)?> ConfirmAsync(string token, string? ip, CancellationToken ct)
    {
        if (tokens.Read(token, TokenPurpose.Confirm) is not { } t) return null;
        return await audiences.ConfirmAsync(t, hasher.Hash(ip), ct);
    }
}
