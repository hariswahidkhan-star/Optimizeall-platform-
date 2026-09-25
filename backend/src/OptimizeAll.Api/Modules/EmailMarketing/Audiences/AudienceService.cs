using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Automations;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Audiences;

// ---------- DTOs ----------

public sealed record WorkspaceDto(string Key, Guid? ClientAccountId, string Name, string? Slug, string TimeZone, string Currency);

public sealed record EmailListDto(
    Guid Id, Guid? ClientAccountId, string Name, string? Description, bool DoubleOptIn, bool ShowInPreferenceCenter, string PublicKey,
    string SignupUrl, string ConsentText, string ConsentTextVersion, int Subscribed, int Pending, int Unsubscribed, bool IsArchived,
    DateTime CreatedAt, Guid ConcurrencyStamp);

public sealed class EmailListRequest
{
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Description { get; set; }
    public bool DoubleOptIn { get; set; } = true;
    public bool ShowInPreferenceCenter { get; set; } = true;
    [MaxLength(1000)] public string? ConsentText { get; set; }
    [MaxLength(40)] public string? ConsentTextVersion { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record SubscriberListItem(
    Guid Id, string? Email, string? Phone, string? FirstName, string? LastName, SubscriberStatus Status, ConsentStatus EmailConsent,
    ConsentStatus SmsConsent, string? CountryCode, string Source, DateTime CreatedAt, DateTime? LastOpenAt, DateTime? LastClickAt,
    IReadOnlyList<string> Tags, EngagementTier Tier);

public sealed record MembershipDto(Guid ListId, string ListName, MembershipStatus Status, DateTime? SubscribedAt, DateTime? UnsubscribedAt, string Source);

public sealed record ConsentRecordDto(MessageChannel Channel, ConsentStatus Status, DateTime RecordedAt, string Source, string? ConsentTextVersion, bool HasIpHash, string? Note);

public sealed record ActivityDto(EngagementType Type, DateTime OccurredAt, bool IsMachine, string? Detail, Guid? CampaignId, string? CampaignName);

public sealed record SubscriberDetail(
    Guid Id, Guid? ClientAccountId, string? Email, string? Phone, string? FirstName, string? LastName, string? Language, string? CountryCode,
    string? TimeZone, string Source, SubscriberStatus Status, ConsentStatus EmailConsent, DateTime? EmailConsentAt, ConsentStatus SmsConsent,
    DateTime? SmsConsentAt, ConsentStatus WhatsAppConsent, DateTime? WhatsAppConsentAt, EmailFrequency Frequency, IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> CustomFields, IReadOnlyList<MembershipDto> Lists, IReadOnlyList<ConsentRecordDto> ConsentHistory,
    IReadOnlyList<ActivityDto> Activity, bool EmailSuppressed, bool SmsSuppressed, EngagementTier Tier, DateTime CreatedAt, Guid ConcurrencyStamp);

public sealed class SubscriberQuery : PageQuery
{
    public Guid? ClientAccountId { get; set; }
    public Guid? ListId { get; set; }
    public SubscriberStatus? Status { get; set; }
    [MaxLength(50)] public string? Tag { get; set; }
}

public sealed class SubscriberRequest
{
    public Guid? ClientAccountId { get; set; }
    [MaxLength(254)] public string? Email { get; set; }
    [MaxLength(32)] public string? Phone { get; set; }
    [MaxLength(100)] public string? FirstName { get; set; }
    [MaxLength(100)] public string? LastName { get; set; }
    [MaxLength(12)] public string? Language { get; set; }
    [MaxLength(2)] public string? CountryCode { get; set; }
    [MaxLength(64)] public string? TimeZone { get; set; }
    public JsonElement? CustomFields { get; set; }
    public List<string>? Tags { get; set; }
    public List<Guid>? ListIds { get; set; }

    /// <summary>
    /// The staff member attests the contact gave consent (source required). Without it the contact gets no marketing
    /// email until they confirm through a double opt-in email (lists with double opt-in) or record consent later.
    /// </summary>
    public bool AttestEmailConsent { get; set; }
    public bool AttestSmsConsent { get; set; }
    [MaxLength(200)] public string? ConsentSource { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class TagChangeRequest
{
    public List<string>? Add { get; set; }
    public List<string>? Remove { get; set; }
}

public sealed class ConsentChangeRequest
{
    public MessageChannel Channel { get; set; }
    public ConsentStatus Status { get; set; }
    [Required, MaxLength(200)] public string Source { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Note { get; set; }
}

public sealed class MembershipChangeRequest
{
    public Guid ListId { get; set; }
    /// <summary>subscribe or unsubscribe.</summary>
    [Required] public string Action { get; set; } = "subscribe";
}

public sealed record SuppressionDto(Guid Id, MessageChannel Channel, string Value, SuppressionReason Reason, string Source, string? Note, DateTime CreatedAt);

public sealed class SuppressionQuery : PageQuery
{
    public Guid? ClientAccountId { get; set; }
    public MessageChannel? Channel { get; set; }
    public SuppressionReason? Reason { get; set; }
}

public sealed class SuppressionRequest
{
    public Guid? ClientAccountId { get; set; }
    public MessageChannel Channel { get; set; } = MessageChannel.Email;
    [Required, MaxLength(254)] public string Value { get; set; } = string.Empty;
    public SuppressionReason Reason { get; set; } = SuppressionReason.Manual;
    [MaxLength(500)] public string? Note { get; set; }
}

public sealed class BounceImportRequest
{
    public Guid? ClientAccountId { get; set; }
    /// <summary>One address per line, or CSV with an "email" column and optional "type" (hard/soft/complaint).</summary>
    [Required, MaxLength(2_000_000)] public string Content { get; set; } = string.Empty;
    public BounceType DefaultType { get; set; } = BounceType.Hard;
}

public sealed record BounceImportResult(int Hard, int Soft, int Complaints, int Invalid);

/// <summary>Contact data to create or update.</summary>
public sealed record ContactInput(
    string? Email, string? Phone, string? FirstName, string? LastName, string? Language, string? CountryCode, string? TimeZone,
    IReadOnlyDictionary<string, string?>? CustomFields, IReadOnlyList<string>? Tags, string Source);

/// <summary>Consent evidence to record with a contact change.</summary>
public sealed record ConsentGrant(ConsentStatus Status, string Source, string? IpHash, string? TextVersion, Guid? UserId, string? Note = null);

public enum SubscribeOutcome { AlreadySubscribed, Subscribed, ConfirmationSent, ConfirmationPending, Suppressed }

/// <summary>
/// Contacts, lists, consent and the suppression list. Consent is recorded per channel with an append-only history;
/// the suppression list always wins over any list, segment, import or automation.
/// </summary>
public sealed class AudienceService(
    AppDbContext db,
    ICurrentUser currentUser,
    IAuditLogger audit,
    IDatabaseDialect dialect,
    EmailAccess access,
    EmailMarketingUrls urls,
    TrackingTokens tokens,
    EmailSettingsStore settingsStore,
    EmailProviderResolver providers,
    AutomationTriggers triggers,
    TimeProvider clock,
    ILogger<AudienceService> logger,
    IOptions<ExportOptions> exports)
{
    public const int ConfirmationValidDays = 7;

    // ---------- Workspaces ----------

    public async Task<IReadOnlyList<WorkspaceDto>> WorkspacesAsync(CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(null, ct);
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Status != ClientAccountStatus.Churned).OrderBy(c => c.Name)
            .Select(c => new WorkspaceDto(c.Id.ToString(), c.Id, c.Name, c.Slug, c.TimeZone, c.Currency)).ToListAsync(ct);
        return new[] { new WorkspaceDto(Workspace.AgencyKey, null, "Optimize All (agency)", null, "UTC", "USD") }.Concat(clients).ToList();
    }

    // ---------- Lists ----------

    public async Task<IReadOnlyList<EmailListDto>> ListsAsync(Guid? clientId, bool includeArchived, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        var key = Workspace.Key(clientId);
        var lists = await db.Set<EmailList>().AsNoTracking().Where(l => l.ScopeKey == key && (includeArchived || !l.IsArchived))
            .OrderBy(l => l.Name).ToListAsync(ct);
        return await ToDtosAsync(lists, ct);
    }

    public async Task<EmailListDto> GetListAsync(Guid id, CancellationToken ct)
    {
        var list = await LoadListAsync(id, ct);
        return (await ToDtosAsync(new[] { list }, ct))[0];
    }

    public async Task<EmailList> LoadListAsync(Guid id, CancellationToken ct) =>
        await access.LoadAsync(db.Set<EmailList>().Where(l => l.Id == id), l => l.ClientAccountId, "List", ct);

    public async Task<EmailListDto> CreateListAsync(EmailListRequest request, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(request.ClientAccountId, ct);
        var list = new EmailList
        {
            ClientAccountId = request.ClientAccountId,
            ScopeKey = Workspace.Key(request.ClientAccountId),
            PublicKey = NewPublicKey(),
        };
        Apply(list, request);
        db.Set<EmailList>().Add(list);
        audit.Record("email.list.created", nameof(EmailList), list.Id, after: Snapshot(list));
        await db.SaveChangesAsync(ct);
        return await GetListAsync(list.Id, ct);
    }

    public async Task<EmailListDto> UpdateListAsync(Guid id, EmailListRequest request, CancellationToken ct)
    {
        var list = await LoadListAsync(id, ct);
        ExpectStamp(list.ConcurrencyStamp, request.ConcurrencyStamp);
        var before = Snapshot(list);
        Apply(list, request);
        audit.Record("email.list.updated", nameof(EmailList), list.Id, before, Snapshot(list));
        await db.SaveChangesAsync(ct);
        return await GetListAsync(list.Id, ct);
    }

    public async Task ArchiveListAsync(Guid id, CancellationToken ct)
    {
        var list = await LoadListAsync(id, ct);
        if (list.IsArchived) return;
        list.IsArchived = true;
        audit.Record("email.list.archived", nameof(EmailList), list.Id, Snapshot(list));
        await db.SaveChangesAsync(ct);
    }

    public async Task<EmailListDto> RestoreListAsync(Guid id, CancellationToken ct)
    {
        var list = await LoadListAsync(id, ct);
        if (list.IsArchived)
        {
            list.IsArchived = false;
            audit.Record("email.list.restored", nameof(EmailList), list.Id, after: Snapshot(list));
            await db.SaveChangesAsync(ct);
        }
        return await GetListAsync(list.Id, ct);
    }

    private static void Apply(EmailList list, EmailListRequest r)
    {
        list.Name = r.Name.Trim();
        list.Description = Text.Clean(r.Description, 1000);
        list.DoubleOptIn = r.DoubleOptIn;
        list.ShowInPreferenceCenter = r.ShowInPreferenceCenter;
        if (Text.Clean(r.ConsentText, 1000) is { } text) list.ConsentText = text;
        if (Text.Clean(r.ConsentTextVersion, 40) is { } version) list.ConsentTextVersion = version;
    }

    private async Task<List<EmailListDto>> ToDtosAsync(IReadOnlyList<EmailList> lists, CancellationToken ct)
    {
        var ids = lists.Select(l => l.Id).ToList();
        var counts = await db.Set<ListMembership>().AsNoTracking().Where(m => ids.Contains(m.ListId))
            .GroupBy(m => new { m.ListId, m.Status }).Select(g => new { g.Key.ListId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        int Count(Guid listId, MembershipStatus status) => counts.Where(c => c.ListId == listId && c.Status == status).Sum(c => c.Count);
        return lists.Select(l => new EmailListDto(l.Id, l.ClientAccountId, l.Name, l.Description, l.DoubleOptIn, l.ShowInPreferenceCenter, l.PublicKey,
            urls.SignupForm(l.PublicKey), l.ConsentText, l.ConsentTextVersion, Count(l.Id, MembershipStatus.Subscribed), Count(l.Id, MembershipStatus.Pending),
            Count(l.Id, MembershipStatus.Unsubscribed), l.IsArchived, l.CreatedAt, l.ConcurrencyStamp)).ToList();
    }

    public static string NewPublicKey() => RandomNumberGenerator.GetString("abcdefghijkmnpqrstuvwxyz23456789", 16);

    // ---------- Subscribers ----------

    public async Task<PagedResult<SubscriberListItem>> SubscribersAsync(SubscriberQuery q, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(q.ClientAccountId, ct);
        var key = Workspace.Key(q.ClientAccountId);
        var query = db.Set<Subscriber>().AsNoTracking().Where(s => s.ScopeKey == key);
        if (q.ListId is { } listId)
            query = query.Where(s => db.Set<ListMembership>().Any(m => m.SubscriberId == s.Id && m.ListId == listId));
        if (q.Status is { } status) query = query.Where(s => s.Status == status);
        if (ContactRules.NormalizeTag(q.Tag) is { } tag)
            query = query.Where(s => db.Set<SubscriberTag>().Any(t => t.SubscriberId == s.Id && t.Tag == tag));
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = PagingExtensions.LikePattern(q.Search);
            query = query.Where(s => EF.Functions.Like(s.Email!, pattern, "\\") || EF.Functions.Like(s.FirstName!, pattern, "\\") ||
                                     EF.Functions.Like(s.LastName!, pattern, "\\") || EF.Functions.Like(s.Phone!, pattern, "\\"));
        }
        query = q.Sort switch
        {
            "email" => q.Desc ? query.OrderByDescending(s => s.Email).ThenByDescending(s => s.Id) : query.OrderBy(s => s.Email).ThenBy(s => s.Id),
            "lastOpenAt" => q.Desc ? query.OrderByDescending(s => s.LastOpenAt).ThenByDescending(s => s.Id) : query.OrderBy(s => s.LastOpenAt).ThenBy(s => s.Id),
            _ => q.Desc ? query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id) : query.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id),
        };
        var page = await query.ToPagedAsync(q, ct);
        var ids = page.Items.Select(s => s.Id).ToList();
        var tags = await db.Set<SubscriberTag>().AsNoTracking().Where(t => ids.Contains(t.SubscriberId)).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return new PagedResult<SubscriberListItem>(page.Items.Select(s => new SubscriberListItem(
            s.Id, s.Email, s.Phone, s.FirstName, s.LastName, s.Status, s.EmailConsent, s.SmsConsent, s.CountryCode, s.Source, s.CreatedAt,
            s.LastOpenAt, s.LastClickAt, tags.Where(t => t.SubscriberId == s.Id).Select(t => t.Tag).OrderBy(t => t).ToList(),
            EngagementTiers.Classify(now, s.CreatedAt, s.LastOpenAt, s.LastClickAt))).ToList(), page.Total, page.Page, page.PageSize);
    }

    public async Task<Subscriber> LoadSubscriberAsync(Guid id, CancellationToken ct) =>
        await access.LoadAsync(db.Set<Subscriber>().Where(s => s.Id == id), s => s.ClientAccountId, "Subscriber", ct);

    public async Task<SubscriberDetail> GetSubscriberAsync(Guid id, CancellationToken ct)
    {
        var s = await LoadSubscriberAsync(id, ct);
        return await DetailAsync(s, ct);
    }

    private async Task<SubscriberDetail> DetailAsync(Subscriber s, CancellationToken ct)
    {
        var tags = await db.Set<SubscriberTag>().AsNoTracking().Where(t => t.SubscriberId == s.Id).OrderBy(t => t.Tag).Select(t => t.Tag).ToListAsync(ct);
        var fields = await db.Set<SubscriberField>().AsNoTracking().Where(f => f.SubscriberId == s.Id).ToDictionaryAsync(f => f.Key, f => f.Value, ct);
        var lists = await (from m in db.Set<ListMembership>().AsNoTracking()
                           join l in db.Set<EmailList>() on m.ListId equals l.Id
                           where m.SubscriberId == s.Id
                           orderby l.Name
                           select new MembershipDto(l.Id, l.Name, m.Status, m.SubscribedAt, m.UnsubscribedAt, m.Source)).ToListAsync(ct);
        var consent = await db.Set<ConsentRecord>().AsNoTracking().Where(c => c.SubscriberId == s.Id).OrderByDescending(c => c.RecordedAt).Take(50)
            .Select(c => new ConsentRecordDto(c.Channel, c.Status, c.RecordedAt, c.Source, c.ConsentTextVersion, c.IpHash != null, c.Note)).ToListAsync(ct);
        var activity = await (from e in db.Set<EngagementEvent>().AsNoTracking()
                              where e.SubscriberId == s.Id
                              orderby e.OccurredAt descending
                              select new ActivityDto(e.Type, e.OccurredAt, e.IsMachine, e.Name ?? e.Detail, e.CampaignId,
                                  db.Set<EmailCampaign>().Where(c => c.Id == e.CampaignId).Select(c => c.Name).FirstOrDefault())).Take(50).ToListAsync(ct);
        var emailSuppressed = s.NormalizedEmail is not null && await IsSuppressedAsync(s.ScopeKey, MessageChannel.Email, s.NormalizedEmail, ct);
        var smsSuppressed = s.Phone is not null && await IsSuppressedAsync(s.ScopeKey, MessageChannel.Sms, s.Phone, ct);
        return new SubscriberDetail(s.Id, s.ClientAccountId, s.Email, s.Phone, s.FirstName, s.LastName, s.Language, s.CountryCode, s.TimeZone, s.Source,
            s.Status, s.EmailConsent, s.EmailConsentAt, s.SmsConsent, s.SmsConsentAt, s.WhatsAppConsent, s.WhatsAppConsentAt, s.Frequency, tags, fields,
            lists, consent, activity, emailSuppressed, smsSuppressed, EngagementTiers.Classify(clock.GetUtcNow().UtcDateTime, s.CreatedAt, s.LastOpenAt, s.LastClickAt),
            s.CreatedAt, s.ConcurrencyStamp);
    }

    /// <summary>Staff create (or update by email) with optional consent attestation and list subscriptions.</summary>
    public async Task<SubscriberDetail> CreateSubscriberAsync(SubscriberRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        var input = ParseInput(r, "manual");
        if ((r.AttestEmailConsent || r.AttestSmsConsent) && string.IsNullOrWhiteSpace(r.ConsentSource))
            throw new DomainException("email.consent_source_required", "Describe where and how the contact gave consent.");
        var ip = null as string;
        var emailConsent = r.AttestEmailConsent ? new ConsentGrant(ConsentStatus.Granted, "manual", ip, null, currentUser.IdOrNull, r.ConsentSource) : null;
        var smsConsent = r.AttestSmsConsent ? new ConsentGrant(ConsentStatus.Granted, "manual", ip, null, currentUser.IdOrNull, r.ConsentSource) : null;
        var (subscriber, created) = await UpsertContactAsync(r.ClientAccountId, input, emailConsent, smsConsent, allowResubscribe: false, ct);
        audit.Record(created ? "email.subscriber.created" : "email.subscriber.updated", nameof(Subscriber), subscriber.Id,
            after: new { subscriber.Email, subscriber.Phone, emailConsent = r.AttestEmailConsent, smsConsent = r.AttestSmsConsent, r.ConsentSource });
        await db.SaveChangesAsync(ct);

        foreach (var listId in r.ListIds ?? new List<Guid>())
        {
            var list = await db.Set<EmailList>().FirstOrDefaultAsync(l => l.Id == listId && l.ScopeKey == subscriber.ScopeKey, ct)
                       ?? throw DomainException.NotFound("List");
            await SubscribeAsync(list, subscriber, "manual", requireConfirmation: list.DoubleOptIn && !r.AttestEmailConsent, null, ct);
        }
        return await DetailAsync(await db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.Id == subscriber.Id, ct), ct);
    }

    public async Task<SubscriberDetail> UpdateSubscriberAsync(Guid id, SubscriberRequest r, CancellationToken ct)
    {
        var s = await LoadSubscriberAsync(id, ct);
        ExpectStamp(s.ConcurrencyStamp, r.ConcurrencyStamp);
        var input = ParseInput(r, s.Source);
        if (input.Email is not null && input.Email != s.NormalizedEmail &&
            await db.Set<Subscriber>().AnyAsync(x => x.ScopeKey == s.ScopeKey && x.NormalizedEmail == input.Email && x.Id != s.Id, ct))
            throw DomainException.Conflict("email.subscriber_duplicate", "Another contact in this workspace already uses that email address.");
        var before = new { s.Email, s.Phone, s.FirstName, s.LastName, s.CountryCode, s.Language, s.TimeZone };
        if (input.Email != s.NormalizedEmail)
        {
            // A changed address has not consented yet.
            s.Email = r.Email?.Trim();
            s.NormalizedEmail = input.Email;
            if (s.EmailConsent == ConsentStatus.Granted) await SetConsentAsync(s, MessageChannel.Email, ConsentStatus.Unknown, "address-changed", null, null, currentUser.IdOrNull, "Email address changed", ct);
            s.Status = SubscriberStatus.Subscribed;
        }
        if (input.Phone != s.Phone)
        {
            s.Phone = input.Phone;
            if (s.SmsConsent == ConsentStatus.Granted) await SetConsentAsync(s, MessageChannel.Sms, ConsentStatus.Unknown, "phone-changed", null, null, currentUser.IdOrNull, "Phone number changed", ct);
            if (s.WhatsAppConsent == ConsentStatus.Granted) await SetConsentAsync(s, MessageChannel.WhatsApp, ConsentStatus.Unknown, "phone-changed", null, null, currentUser.IdOrNull, "Phone number changed", ct);
        }
        s.FirstName = input.FirstName;
        s.LastName = input.LastName;
        s.Language = input.Language;
        s.CountryCode = input.CountryCode;
        s.TimeZone = input.TimeZone;
        if (input.CustomFields is not null) await ApplyFieldsAsync(s.Id, input.CustomFields, ct);
        audit.Record("email.subscriber.updated", nameof(Subscriber), s.Id, before, new { s.Email, s.Phone, s.FirstName, s.LastName, s.CountryCode, s.Language, s.TimeZone });
        await db.SaveChangesAsync(ct);
        if (input.Tags is not null)
        {
            var current = await db.Set<SubscriberTag>().Where(t => t.SubscriberId == s.Id).Select(t => t.Tag).ToListAsync(ct);
            await ChangeTagsAsync(s, input.Tags.Except(current).ToList(), current.Except(input.Tags).ToList(), ct);
        }
        return await DetailAsync(s, ct);
    }

    /// <summary>Erases a contact (GDPR right to erasure): the contact, memberships, tags, fields, consent history and events.</summary>
    public async Task EraseSubscriberAsync(Guid id, CancellationToken ct)
    {
        var s = await LoadSubscriberAsync(id, ct);
        audit.Record("email.subscriber.erased", nameof(Subscriber), s.Id, before: new { s.ScopeKey, s.Source, s.Status }, reason: "Erasure request");
        db.Set<Subscriber>().Remove(s);
        await db.SaveChangesAsync(ct);
    }

    public async Task<SubscriberDetail> ChangeTagsAsync(Guid id, TagChangeRequest r, CancellationToken ct)
    {
        var s = await LoadSubscriberAsync(id, ct);
        var add = NormalizeTags(r.Add);
        var remove = NormalizeTags(r.Remove);
        await ChangeTagsAsync(s, add, remove, ct);
        return await DetailAsync(s, ct);
    }

    /// <summary>Adds/removes tags (saved) and starts "tag added" journeys for new tags.</summary>
    public async Task ChangeTagsAsync(Subscriber s, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken ct)
    {
        var current = await db.Set<SubscriberTag>().Where(t => t.SubscriberId == s.Id).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var added = new List<string>();
        foreach (var tag in add.Distinct())
        {
            if (current.Any(t => t.Tag == tag)) continue;
            if (current.Count + added.Count >= ContactRules.MaxTags) throw new DomainException("email.too_many_tags", $"A contact can have at most {ContactRules.MaxTags} tags.");
            db.Set<SubscriberTag>().Add(new SubscriberTag { SubscriberId = s.Id, Tag = tag, AddedAt = now });
            added.Add(tag);
        }
        foreach (var t in current.Where(t => remove.Contains(t.Tag))) db.Set<SubscriberTag>().Remove(t);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            // Added concurrently by another request: the tag is present, which is what was asked.
            foreach (var entry in db.ChangeTracker.Entries<SubscriberTag>().ToList()) entry.State = EntityState.Detached;
        }
        foreach (var tag in added) await triggers.OnTagAddedAsync(s.ClientAccountId, tag, s.Id, ct);
    }

    public async Task<SubscriberDetail> ChangeConsentAsync(Guid id, ConsentChangeRequest r, CancellationToken ct)
    {
        var s = await LoadSubscriberAsync(id, ct);
        if (r.Status is ConsentStatus.Pending) throw new DomainException("email.consent_invalid", "Pending is set by double opt-in only.");
        if (r.Status == ConsentStatus.Granted)
        {
            var value = r.Channel == MessageChannel.Email ? s.NormalizedEmail : s.Phone;
            if (value is null) throw new DomainException("email.consent_no_address", "The contact has no address for this channel.");
            if (await IsSuppressedAsync(s.ScopeKey, r.Channel == MessageChannel.WhatsApp ? MessageChannel.WhatsApp : r.Channel, value, ct))
                throw DomainException.Conflict("email.consent_suppressed",
                    "This address is on the suppression list (unsubscribe, bounce, complaint or STOP). It can only be re-enabled by the contact opting in again.");
        }
        await SetConsentAsync(s, r.Channel, r.Status, "manual", null, null, currentUser.IdOrNull, $"{r.Source}{(r.Note is null ? "" : ": " + r.Note)}", ct);
        audit.Record("email.subscriber.consent_changed", nameof(Subscriber), s.Id, after: new { r.Channel, r.Status, r.Source }, reason: r.Note);
        await db.SaveChangesAsync(ct);
        return await DetailAsync(s, ct);
    }

    public async Task<SubscriberDetail> ChangeMembershipAsync(Guid id, MembershipChangeRequest r, CancellationToken ct)
    {
        var s = await LoadSubscriberAsync(id, ct);
        var list = await db.Set<EmailList>().FirstOrDefaultAsync(l => l.Id == r.ListId && l.ScopeKey == s.ScopeKey, ct) ?? throw DomainException.NotFound("List");
        if (r.Action == "unsubscribe") await UnsubscribeAsync(s, list.Id, "manual", null, null, ct);
        else if (r.Action == "subscribe")
            await SubscribeAsync(list, s, "manual", requireConfirmation: list.DoubleOptIn && s.EmailConsent != ConsentStatus.Granted, null, ct);
        else throw new DomainException("email.membership_action", "Action must be subscribe or unsubscribe.");
        audit.Record("email.subscriber.membership_changed", nameof(Subscriber), s.Id, after: new { list.Id, r.Action });
        await db.SaveChangesAsync(ct);
        return await DetailAsync(s, ct);
    }

    // ---------- Core contact operations (used by forms, imports, events, website) ----------

    public ContactInput ParseInput(SubscriberRequest r, string source)
    {
        var errors = new List<string>();
        string? email = null;
        if (!string.IsNullOrWhiteSpace(r.Email))
        {
            email = ContactRules.NormalizeEmail(r.Email);
            if (email is null) errors.Add("email is not a valid address.");
        }
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(r.Phone))
        {
            phone = ContactRules.NormalizePhone(r.Phone);
            if (phone is null) errors.Add("phone must be in international format, e.g. +14155550123.");
        }
        if (email is null && phone is null && errors.Count == 0) errors.Add("An email address or phone number is required.");
        var country = r.CountryCode?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(country) && !ContactRules.IsValidCountry(country)) errors.Add("countryCode must be ISO 3166-1 alpha-2.");
        var language = r.Language?.Trim();
        if (!string.IsNullOrEmpty(language) && !ContactRules.IsValidLanguage(language)) errors.Add("language must be a BCP 47 code such as en or en-GB.");
        if (!string.IsNullOrEmpty(r.TimeZone) && !SendTiming.IsValidZone(r.TimeZone)) errors.Add("timeZone must be an IANA time zone.");
        var fields = r.CustomFields is null ? null : ContactRules.ParseCustomFields(r.CustomFields, errors);
        var tags = r.Tags is null ? null : NormalizeTags(r.Tags, errors);
        if (errors.Count > 0) throw EmailProblem.Invalid("email.subscriber_invalid", "The contact is invalid.", errors);
        return new ContactInput(email, phone, Text.Clean(r.FirstName, 100), Text.Clean(r.LastName, 100), string.IsNullOrEmpty(language) ? null : language,
            string.IsNullOrEmpty(country) ? null : country, Text.Clean(r.TimeZone, 64), fields, tags, source);
    }

    /// <summary>
    /// Creates or updates a contact by email (else phone) and records consent. Saves. Existing unsubscribed, bounced or
    /// complained contacts are never re-subscribed unless <paramref name="allowResubscribe"/> (a confirmed opt-in by the
    /// contact themselves) and never when the address is on the suppression list for a bounce or complaint.
    /// </summary>
    public async Task<(Subscriber Subscriber, bool Created)> UpsertContactAsync(
        Guid? clientId, ContactInput input, ConsentGrant? emailConsent, ConsentGrant? smsConsent, bool allowResubscribe, CancellationToken ct)
    {
        var key = Workspace.Key(clientId);
        for (var attempt = 0; ; attempt++)
        {
            var s = input.Email is not null
                ? await db.Set<Subscriber>().FirstOrDefaultAsync(x => x.ScopeKey == key && x.NormalizedEmail == input.Email, ct)
                : await db.Set<Subscriber>().FirstOrDefaultAsync(x => x.ScopeKey == key && x.Phone == input.Phone, ct);
            var created = s is null;
            if (s is null)
            {
                s = new Subscriber { ClientAccountId = clientId, ScopeKey = key, Source = input.Source };
                db.Set<Subscriber>().Add(s);
            }
            if (input.Email is not null && s.NormalizedEmail is null) { s.Email = input.Email; s.NormalizedEmail = input.Email; }
            if (input.Phone is not null) s.Phone ??= input.Phone;
            s.FirstName = input.FirstName ?? s.FirstName;
            s.LastName = input.LastName ?? s.LastName;
            s.Language = input.Language ?? s.Language;
            s.CountryCode = input.CountryCode ?? s.CountryCode;
            s.TimeZone = input.TimeZone ?? s.TimeZone;

            if (emailConsent is not null && s.NormalizedEmail is not null)
            {
                var blocked = s.Status is SubscriberStatus.Bounced or SubscriberStatus.Complained ||
                              (s.Status is SubscriberStatus.Unsubscribed or SubscriberStatus.Cleaned && !allowResubscribe) ||
                              (s.EmailConsent == ConsentStatus.Withdrawn && !allowResubscribe);
                if (!blocked && s.EmailConsent != emailConsent.Status)
                    await SetConsentAsync(s, MessageChannel.Email, emailConsent.Status, emailConsent.Source, emailConsent.IpHash, emailConsent.TextVersion, emailConsent.UserId, emailConsent.Note, ct);
                if (!blocked && allowResubscribe && emailConsent.Status == ConsentStatus.Granted && s.Status is SubscriberStatus.Unsubscribed or SubscriberStatus.Cleaned)
                    s.Status = SubscriberStatus.Subscribed;
            }
            if (smsConsent is not null && s.Phone is not null && s.SmsConsent != smsConsent.Status && (s.SmsConsent != ConsentStatus.Withdrawn || allowResubscribe))
                await SetConsentAsync(s, MessageChannel.Sms, smsConsent.Status, smsConsent.Source, smsConsent.IpHash, smsConsent.TextVersion, smsConsent.UserId, smsConsent.Note, ct);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when ((DatabaseErrors.IsUniqueViolation(ex) || ex is DbUpdateConcurrencyException) && attempt < 2)
            {
                // Created (or updated) concurrently: detach what this attempt staged for the contact, then reload the current
                // row and retry as an update. Other tracked entities of the caller are left alone.
                var subscriberId = s.Id;
                foreach (var entry in db.ChangeTracker.Entries().Where(x =>
                             (x.Entity == s && created) || (x.Entity is ConsentRecord c && c.SubscriberId == subscriberId && x.State == EntityState.Added)).ToList())
                    entry.State = EntityState.Detached;
                if (!created) db.Entry(s).State = EntityState.Detached; // reload the current row (and its concurrency stamp)
                continue;
            }
            if (input.CustomFields is { Count: > 0 }) { await ApplyFieldsAsync(s.Id, input.CustomFields, ct); await db.SaveChangesAsync(ct); }
            if (input.Tags is { Count: > 0 }) await ChangeTagsAsync(s, input.Tags, Array.Empty<string>(), ct);
            return (s, created);
        }
    }

    private async Task ApplyFieldsAsync(Guid subscriberId, IReadOnlyDictionary<string, string?> fields, CancellationToken ct)
    {
        var existing = await db.Set<SubscriberField>().Where(f => f.SubscriberId == subscriberId).ToListAsync(ct);
        foreach (var (k, v) in fields)
        {
            var row = existing.FirstOrDefault(f => f.Key == k);
            if (string.IsNullOrEmpty(v)) { if (row is not null) db.Set<SubscriberField>().Remove(row); continue; }
            if (row is null)
            {
                if (existing.Count >= ContactRules.MaxCustomFields) throw new DomainException("email.too_many_fields", $"A contact can have at most {ContactRules.MaxCustomFields} custom fields.");
                db.Set<SubscriberField>().Add(new SubscriberField { SubscriberId = subscriberId, Key = k, Value = v });
                existing.Add(new SubscriberField { Key = k });
            }
            else row.Value = v;
        }
    }

    /// <summary>Records consent on a channel (updates the contact and appends history). Caller saves.</summary>
    public Task SetConsentAsync(Subscriber s, MessageChannel channel, ConsentStatus status, string source, string? ipHash, string? version,
        Guid? userId, string? note, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        switch (channel)
        {
            case MessageChannel.Email: s.EmailConsent = status; s.EmailConsentAt = now; break;
            case MessageChannel.Sms: s.SmsConsent = status; s.SmsConsentAt = now; break;
            case MessageChannel.WhatsApp: s.WhatsAppConsent = status; s.WhatsAppConsentAt = now; break;
        }
        db.Set<ConsentRecord>().Add(new ConsentRecord
        {
            SubscriberId = s.Id, ClientAccountId = s.ClientAccountId, Channel = channel, Status = status, RecordedAt = now,
            Source = Text.Truncate(source, 40), IpHash = ipHash, ConsentTextVersion = version, RecordedByUserId = userId, Note = Text.Clean(note, 1000),
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Subscribes a contact to a list. With confirmation required, the membership stays Pending and a double opt-in email
    /// is sent; the contact receives nothing else from the list until they confirm. Saves.
    /// </summary>
    public async Task<SubscribeOutcome> SubscribeAsync(EmailList list, Subscriber s, string source, bool requireConfirmation, string? ipHash, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var membership = await db.Set<ListMembership>().FirstOrDefaultAsync(m => m.ListId == list.Id && m.SubscriberId == s.Id, ct);
        if (membership?.Status == MembershipStatus.Subscribed) return SubscribeOutcome.AlreadySubscribed;
        if (membership is null)
        {
            membership = new ListMembership { ListId = list.Id, SubscriberId = s.Id, CreatedAt = now, Source = Text.Truncate(source, 40) };
            db.Set<ListMembership>().Add(membership);
        }
        if (requireConfirmation)
        {
            membership.Status = MembershipStatus.Pending;
            if (s.EmailConsent is ConsentStatus.Unknown)
                await SetConsentAsync(s, MessageChannel.Email, ConsentStatus.Pending, source, ipHash, list.ConsentTextVersion, currentUser.IdOrNull, null, ct);
            if (db.Entry(s).State == EntityState.Detached) db.Attach(s).State = EntityState.Modified;
            await SaveMembershipAsync(ct);
            if (s.NormalizedEmail is not null && await IsSuppressedForBounceOrComplaintAsync(s, ct)) return SubscribeOutcome.Suppressed;
            if (membership.ConfirmationSentAt is { } sentAt && sentAt > now.AddMinutes(-10)) return SubscribeOutcome.ConfirmationPending;
            var sent = await SendConfirmationAsync(list, s, membership, ct);
            return sent ? SubscribeOutcome.ConfirmationSent : SubscribeOutcome.ConfirmationPending;
        }

        membership.Status = MembershipStatus.Subscribed;
        membership.SubscribedAt = now;
        membership.UnsubscribedAt = null;
        await SaveMembershipAsync(ct);
        await triggers.OnListSubscribedAsync(s.ClientAccountId, list.Id, s.Id, ct);
        return SubscribeOutcome.Subscribed;
    }

    private async Task SaveMembershipAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            foreach (var entry in db.ChangeTracker.Entries<ListMembership>().Where(e => e.State == EntityState.Added).ToList()) entry.State = EntityState.Detached;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Sends the double opt-in email (transactional; no marketing consent needed, bounces/complaints still respected).</summary>
    public async Task<bool> SendConfirmationAsync(EmailList list, Subscriber s, ListMembership membership, CancellationToken ct)
    {
        if (s.NormalizedEmail is null) return false;
        var settings = await settingsStore.GetAsync(s.ClientAccountId, ct);
        var sender = await db.Set<SenderProfile>().AsNoTracking()
            .Where(p => p.ScopeKey == s.ScopeKey && p.VerifiedAt != null).OrderByDescending(p => p.IsDefault).FirstOrDefaultAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var token = tokens.Create(TokenPurpose.Confirm, TokenSource.Membership, membership.Id, TrackingTokens.ExpiryGuid(now.AddDays(ConfirmationValidDays)));
        var link = urls.ConfirmSubscription(token);
        var org = string.IsNullOrWhiteSpace(settings.OrganizationName) ? "us" : settings.OrganizationName;
        var design = new EmailDesign
        {
            Blocks =
            {
                new DesignBlock { Type = "text", Html = $"<h2>Please confirm your subscription</h2><p>Hi {{{{first_name|there}}}},</p><p>You asked to join <strong>{System.Net.WebUtility.HtmlEncode(list.Name)}</strong> from {System.Net.WebUtility.HtmlEncode(org)}. Confirm your email address to start receiving our emails.</p>" },
                new DesignBlock { Type = "button", Text = "Yes, subscribe me", Href = link, Align = "center" },
                new DesignBlock { Type = "text", Html = $"<p>This link expires in {ConfirmationValidDays} days. If you didn't ask to subscribe, ignore this email and you won't hear from us again.</p>" },
            },
        };
        var values = new Dictionary<string, string?> { ["first_name"] = s.FirstName };
        var rendered = EmailRenderer.Render(design, new RenderOptions { Subject = $"Confirm your subscription to {list.Name}", Values = values });
        var provider = await providers.ForWorkspaceAsync(s.ClientAccountId, ct);
        var result = await provider.SendAsync(new OutboundEmail(s.ClientAccountId, s.Email!, s.FirstName,
            sender?.FromEmail ?? "no-reply@optimizeall.app", sender?.FromName ?? settings.OrganizationName, sender?.ReplyTo,
            $"Confirm your subscription to {list.Name}", rendered.Html, rendered.Text, new Dictionary<string, string>(),
            new Dictionary<string, string> { ["oa_kind"] = "confirm" }), ct);
        if (result.Outcome != ProviderOutcome.Accepted)
        {
            logger.LogWarning("Double opt-in email for membership {Membership} not sent: {Error}", membership.Id, result.Error);
            return false;
        }
        await db.Set<ListMembership>().Where(m => m.Id == membership.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.ConfirmationSentAt, now), ct);
        return true;
    }

    /// <summary>Completes double opt-in. Returns the list name, or null when the token is expired/unknown.</summary>
    public async Task<(string ListName, string Workspace)?> ConfirmAsync(TrackingToken token, string? ipHash, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (token.Source != TokenSource.Membership || TrackingTokens.ExpiryFrom(token.Extra) < now) return null;
        var membership = await db.Set<ListMembership>().FirstOrDefaultAsync(m => m.Id == token.MessageId, ct);
        if (membership is null) return null;
        var list = await db.Set<EmailList>().FirstAsync(l => l.Id == membership.ListId, ct);
        var s = await db.Set<Subscriber>().FirstAsync(x => x.Id == membership.SubscriberId, ct);
        var workspace = await access.WorkspaceNameAsync(s.ClientAccountId, ct);
        if (membership.Status == MembershipStatus.Subscribed) return (list.Name, workspace);

        // The contact proved control of the address: consent granted, a previous unsubscribe is lifted (bounces and
        // complaints are not).
        if (s.EmailConsent != ConsentStatus.Granted)
            await SetConsentAsync(s, MessageChannel.Email, ConsentStatus.Granted, "double-opt-in", ipHash, list.ConsentTextVersion, null, null, ct);
        if (s.Status is SubscriberStatus.Unsubscribed or SubscriberStatus.Cleaned) s.Status = SubscriberStatus.Subscribed;
        if (s.NormalizedEmail is not null)
            await db.Set<Suppression>().Where(x => x.ScopeKey == s.ScopeKey && x.Channel == MessageChannel.Email && x.Value == s.NormalizedEmail &&
                                                   x.Reason == SuppressionReason.Unsubscribed).ExecuteDeleteAsync(ct);
        membership.Status = MembershipStatus.Subscribed;
        membership.ConfirmedAt = now;
        membership.SubscribedAt = now;
        membership.UnsubscribedAt = null;
        await db.SaveChangesAsync(ct);
        await triggers.OnListSubscribedAsync(s.ClientAccountId, list.Id, s.Id, ct);
        return (list.Name, workspace);
    }

    /// <summary>
    /// Unsubscribes from one list, or from everything in the workspace (<paramref name="listId"/> null): email consent
    /// withdrawn and the address added to the suppression list. Saves. Idempotent.
    /// </summary>
    public async Task UnsubscribeAsync(Subscriber s, Guid? listId, string source, string? ipHash, CampaignRecipient? recipient, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (listId is { } lid)
        {
            await db.Set<ListMembership>().Where(m => m.SubscriberId == s.Id && m.ListId == lid && m.Status != MembershipStatus.Unsubscribed)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, MembershipStatus.Unsubscribed).SetProperty(m => m.UnsubscribedAt, now), ct);
            return;
        }
        var tracked = db.Entry(s).State == EntityState.Detached ? await db.Set<Subscriber>().FirstAsync(x => x.Id == s.Id, ct) : s;
        var alreadyDone = tracked.Status == SubscriberStatus.Unsubscribed && tracked.EmailConsent == ConsentStatus.Withdrawn;
        if (!alreadyDone)
        {
            if (tracked.Status == SubscriberStatus.Subscribed) tracked.Status = SubscriberStatus.Unsubscribed;
            await SetConsentAsync(tracked, MessageChannel.Email, ConsentStatus.Withdrawn, source, ipHash, null, null, null, ct);
            db.Set<EngagementEvent>().Add(new EngagementEvent
            {
                ClientAccountId = tracked.ClientAccountId, SubscriberId = tracked.Id, CampaignId = recipient?.CampaignId, RecipientId = recipient?.Id,
                Type = EngagementType.Unsubscribe, OccurredAt = now, IpHash = ipHash, Detail = source,
            });
            await db.SaveChangesAsync(ct);
            await db.Set<ListMembership>().Where(m => m.SubscriberId == s.Id && m.Status != MembershipStatus.Unsubscribed)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, MembershipStatus.Unsubscribed).SetProperty(m => m.UnsubscribedAt, now), ct);
        }
        if (recipient is not null)
            await db.Set<CampaignRecipient>().Where(r => r.Id == recipient.Id && r.UnsubscribedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.UnsubscribedAt, now), ct);
        if (tracked.NormalizedEmail is not null)
            await AddSuppressionAsync(tracked.ClientAccountId, MessageChannel.Email, tracked.NormalizedEmail, SuppressionReason.Unsubscribed, source, null, null, ct);
    }

    // ---------- Suppression & deliverability ----------

    public Task<bool> IsSuppressedAsync(string scopeKey, MessageChannel channel, string value, CancellationToken ct) =>
        db.Set<Suppression>().AnyAsync(x => x.ScopeKey == scopeKey && x.Channel == channel && x.Value == value, ct);

    private Task<bool> IsSuppressedForBounceOrComplaintAsync(Subscriber s, CancellationToken ct) =>
        db.Set<Suppression>().AnyAsync(x => x.ScopeKey == s.ScopeKey && x.Channel == MessageChannel.Email && x.Value == s.NormalizedEmail &&
                                            (x.Reason == SuppressionReason.HardBounce || x.Reason == SuppressionReason.Complaint), ct);

    /// <summary>Adds a suppression (idempotent: an existing entry for the address is kept). Returns true when added.</summary>
    public async Task<bool> AddSuppressionAsync(Guid? clientId, MessageChannel channel, string value, SuppressionReason reason, string source,
        string? note, Guid? userId, CancellationToken ct)
    {
        var key = Workspace.Key(clientId);
        if (await IsSuppressedAsync(key, channel, value, ct)) return false;
        var entry = new Suppression
        {
            ClientAccountId = clientId, ScopeKey = key, Channel = channel, Value = value, Reason = reason, Source = Text.Truncate(source, 60),
            Note = Text.Clean(note, 500), CreatedAt = clock.GetUtcNow().UtcDateTime, CreatedByUserId = userId,
        };
        db.Set<Suppression>().Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.Entry(entry).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>Applies a bounce or complaint reported by a provider webhook or a manual import. Saves.</summary>
    public async Task ApplyDeliveryFailureAsync(Guid? clientId, string email, BounceType? bounce, bool complaint, string source, CancellationToken ct)
    {
        var normalized = ContactRules.NormalizeEmail(email);
        if (normalized is null) return;
        var key = Workspace.Key(clientId);
        var s = await db.Set<Subscriber>().FirstOrDefaultAsync(x => x.ScopeKey == key && x.NormalizedEmail == normalized, ct);
        if (complaint)
        {
            if (s is not null)
            {
                s.Status = SubscriberStatus.Complained;
                if (s.EmailConsent != ConsentStatus.Withdrawn)
                    await SetConsentAsync(s, MessageChannel.Email, ConsentStatus.Withdrawn, "complaint", null, null, null, $"Spam complaint ({source})", ct);
                await db.SaveChangesAsync(ct);
            }
            await AddSuppressionAsync(clientId, MessageChannel.Email, normalized, SuppressionReason.Complaint, source, null, null, ct);
            return;
        }
        if (bounce == BounceType.Hard)
        {
            if (s is not null && s.Status is SubscriberStatus.Subscribed or SubscriberStatus.Unsubscribed) { s.Status = SubscriberStatus.Bounced; await db.SaveChangesAsync(ct); }
            await AddSuppressionAsync(clientId, MessageChannel.Email, normalized, SuppressionReason.HardBounce, source, null, null, ct);
            return;
        }
        if (s is not null)
        {
            s.SoftBounceCount++;
            // List hygiene: three soft bounces in a row clean the address (no suppression; it can be re-activated).
            if (s.SoftBounceCount >= 3 && s.Status == SubscriberStatus.Subscribed) s.Status = SubscriberStatus.Cleaned;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<PagedResult<SuppressionDto>> SuppressionsAsync(SuppressionQuery q, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(q.ClientAccountId, ct);
        var key = Workspace.Key(q.ClientAccountId);
        var query = db.Set<Suppression>().AsNoTracking().Where(x => x.ScopeKey == key);
        if (q.Channel is { } channel) query = query.Where(x => x.Channel == channel);
        if (q.Reason is { } reason) query = query.Where(x => x.Reason == reason);
        if (!string.IsNullOrWhiteSpace(q.Search)) { var p = PagingExtensions.LikePattern(q.Search.ToLowerInvariant()); query = query.Where(x => EF.Functions.Like(x.Value, p, "\\")); }
        return await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new SuppressionDto(x.Id, x.Channel, x.Value, x.Reason, x.Source, x.Note, x.CreatedAt)).ToPagedAsync(q, ct);
    }

    public async Task<SuppressionDto> AddManualSuppressionAsync(SuppressionRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        var value = r.Channel == MessageChannel.Email ? ContactRules.NormalizeEmail(r.Value) : ContactRules.NormalizePhone(r.Value);
        if (value is null) throw new DomainException("email.suppression_invalid", "Enter a valid email address or E.164 phone number.");
        await AddSuppressionAsync(r.ClientAccountId, r.Channel, value, r.Reason, "manual", r.Note, currentUser.IdOrNull, ct);
        var key = Workspace.Key(r.ClientAccountId);
        var row = await db.Set<Suppression>().AsNoTracking().FirstAsync(x => x.ScopeKey == key && x.Channel == r.Channel && x.Value == value, ct);
        audit.Record("email.suppression.added", nameof(Suppression), row.Id, after: new { r.Channel, value, r.Reason }, reason: r.Note);
        await db.SaveChangesAsync(ct);
        return new SuppressionDto(row.Id, row.Channel, row.Value, row.Reason, row.Source, row.Note, row.CreatedAt);
    }

    public async Task RemoveSuppressionAsync(Guid id, string reason, CancellationToken ct)
    {
        var row = await access.LoadAsync(db.Set<Suppression>().Where(x => x.Id == id), x => x.ClientAccountId, "Suppression", ct);
        if (row.Reason is SuppressionReason.StopKeyword or SuppressionReason.Complaint)
            throw DomainException.Conflict("email.suppression_locked",
                "STOP opt-outs and spam complaints can only be lifted by the contact opting in again (START or a confirmed sign-up).");
        audit.Record("email.suppression.removed", nameof(Suppression), row.Id, before: new { row.Channel, row.Value, row.Reason }, reason: reason);
        db.Set<Suppression>().Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<BounceImportResult> ImportBouncesAsync(BounceImportRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        List<string[]> rows;
        try { rows = CsvParser.Parse(r.Content); }
        catch (FormatException ex) { throw new DomainException("email.import_invalid_csv", ex.Message); }
        if (rows.Count > 50_000) throw new DomainException("email.import_too_large", "Import at most 50,000 addresses at a time.");
        var header = rows.FirstOrDefault()?.Select(h => h.Trim().ToLowerInvariant()).ToArray() ?? Array.Empty<string>();
        var emailCol = Array.IndexOf(header, "email");
        var typeCol = Array.IndexOf(header, "type");
        var data = emailCol >= 0 ? rows.Skip(1) : rows;
        if (emailCol < 0) emailCol = 0;
        int hard = 0, soft = 0, complaints = 0, invalid = 0;
        foreach (var row in data)
        {
            var email = row.Length > emailCol ? ContactRules.NormalizeEmail(row[emailCol]) : null;
            if (email is null) { invalid++; continue; }
            var type = typeCol >= 0 && row.Length > typeCol ? row[typeCol].Trim().ToLowerInvariant() : r.DefaultType == BounceType.Hard ? "hard" : "soft";
            switch (type)
            {
                case "complaint" or "spam": complaints++; await ApplyDeliveryFailureAsync(r.ClientAccountId, email, null, true, "bounce-import", ct); break;
                case "soft": soft++; await ApplyDeliveryFailureAsync(r.ClientAccountId, email, BounceType.Soft, false, "bounce-import", ct); break;
                default: hard++; await ApplyDeliveryFailureAsync(r.ClientAccountId, email, BounceType.Hard, false, "bounce-import", ct); break;
            }
        }
        audit.Record("email.bounces.imported", "EmailWorkspace", Workspace.Key(r.ClientAccountId), after: new { hard, soft, complaints, invalid });
        await db.SaveChangesAsync(ct);
        return new BounceImportResult(hard, soft, complaints, invalid);
    }

    // ---------- Export ----------

    public async Task<(string FileName, IReadOnlyList<string> Header, IReadOnlyList<object?[]> Rows)> ExportListAsync(Guid listId, CancellationToken ct)
    {
        var list = await LoadListAsync(listId, ct);
        await ExportLimit.EnsureAsync(db.Set<ListMembership>().Where(m => m.ListId == listId), exports.Value.EmailList, ct);
        var rows = await (from m in db.Set<ListMembership>().AsNoTracking()
                          join s in db.Set<Subscriber>() on m.SubscriberId equals s.Id
                          where m.ListId == listId
                          orderby s.CreatedAt, s.Id
                          select new { s, m }).ToListAsync(ct);
        var ids = rows.Select(r => r.s.Id).ToList();
        var tags = new List<SubscriberTag>();
        var fields = new List<SubscriberField>();
        foreach (var chunk in ids.Chunk(2000))
        {
            tags.AddRange(await db.Set<SubscriberTag>().AsNoTracking().Where(t => chunk.Contains(t.SubscriberId)).ToListAsync(ct));
            fields.AddRange(await db.Set<SubscriberField>().AsNoTracking().Where(f => chunk.Contains(f.SubscriberId)).ToListAsync(ct));
        }
        var fieldKeys = fields.Select(f => f.Key).Distinct().OrderBy(k => k).ToList();
        var tagLookup = tags.ToLookup(t => t.SubscriberId);
        var fieldLookup = fields.ToLookup(f => f.SubscriberId);
        var header = new List<string> { "email", "phone", "first_name", "last_name", "country", "language", "status", "list_status", "email_consent", "email_consent_at", "sms_consent", "source", "subscribed_at", "tags" };
        header.AddRange(fieldKeys.Select(k => "custom." + k));
        var output = rows.Select(r =>
        {
            var values = new List<object?> { r.s.Email, r.s.Phone, r.s.FirstName, r.s.LastName, r.s.CountryCode, r.s.Language, r.s.Status.ToString(), r.m.Status.ToString(),
                r.s.EmailConsent.ToString(), r.s.EmailConsentAt, r.s.SmsConsent.ToString(), r.s.Source, r.m.SubscribedAt,
                string.Join(';', tagLookup[r.s.Id].Select(t => t.Tag).OrderBy(t => t)) };
            values.AddRange(fieldKeys.Select(k => (object?)fieldLookup[r.s.Id].FirstOrDefault(f => f.Key == k)?.Value));
            return values.ToArray();
        }).ToList();
        audit.Record("email.list.exported", nameof(EmailList), list.Id, after: new { rows = output.Count });
        await db.SaveChangesAsync(ct);
        var safeName = new string(list.Name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
        return ($"{(safeName.Length == 0 ? "list" : safeName)}-{clock.GetUtcNow():yyyyMMdd}.csv", header, output);
    }

    // ---------- helpers ----------

    public static List<string> NormalizeTags(IEnumerable<string>? tags, List<string>? errors = null)
    {
        var result = new List<string>();
        foreach (var raw in tags ?? Enumerable.Empty<string>())
        {
            var tag = ContactRules.NormalizeTag(raw);
            if (tag is null) { (errors ?? throw new DomainException("email.tag_invalid", $"'{Text.Truncate(raw, 50)}' is not a valid tag (letters, digits, space, _ : -; max 50).")).Add($"'{Text.Truncate(raw, 50)}' is not a valid tag."); continue; }
            if (!result.Contains(tag)) result.Add(tag);
        }
        return result;
    }

    public static void ExpectStamp(Guid current, Guid? expected)
    {
        if (expected is { } stamp && stamp != current)
            throw DomainException.Conflict("concurrency.conflict", "This record was changed by someone else. Reload and try again.");
    }

    private static object Snapshot(EmailList l) => new { l.Name, l.DoubleOptIn, l.ShowInPreferenceCenter, l.ConsentTextVersion, l.IsArchived };
}
