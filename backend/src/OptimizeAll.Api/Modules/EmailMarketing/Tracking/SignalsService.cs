using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.EmailMarketing.Automations;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Tracking;

/// <summary>An order (or other revenue event) reported by the client's store, attributed to the last email click.</summary>
public sealed class ConversionRequest
{
    /// <summary>Client workspace id; omit for the agency workspace.</summary>
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(254)] public string Email { get; set; } = string.Empty;
    /// <summary>Order id; conversions are idempotent per workspace + reference.</summary>
    [Required, MaxLength(150)] public string ExternalReference { get; set; } = string.Empty;
    [Range(0, 1_000_000_000)] public decimal? Value { get; set; }
    [MaxLength(3)] public string? Currency { get; set; }
    public DateTime? OccurredAt { get; set; }
}

public sealed record ConversionResult(Guid Id, bool Duplicate, Guid? CampaignId, Guid? AutomationId);

/// <summary>A custom event for journeys ("cart_abandoned", "order_completed" …) about an existing contact.</summary>
public sealed class CustomEventRequest
{
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(254)] public string Email { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string Name { get; set; } = string.Empty;
    /// <summary>Flat object of properties (available to merge tags as {{event.key}}).</summary>
    public JsonElement? Properties { get; set; }
    /// <summary>Optional idempotency key.</summary>
    [MaxLength(100)] public string? EventId { get; set; }
    public DateTime? OccurredAt { get; set; }
}

public sealed record CustomEventResult(bool Accepted, bool Duplicate, int Enrolled, string? Reason);

/// <summary>
/// Conversion attribution (last human click within <see cref="AttributionWindow"/>) and custom events that trigger journeys.
/// Events never create contacts: without a known contact there is no consent to act on.
/// </summary>
public sealed class SignalsService(AppDbContext db, AutomationTriggers triggers, IDatabaseDialect dialect, TimeProvider clock)
{
    public static readonly TimeSpan AttributionWindow = TimeSpan.FromDays(7);

    public async Task<ConversionResult> RecordConversionAsync(ConversionRequest r, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var errors = new List<string>();
        var email = ContactRules.NormalizeEmail(r.Email);
        if (email is null) errors.Add("email is invalid.");
        if (r.Value is not null && (r.Currency is null || !Money.IsSupported(r.Currency.Trim()))) errors.Add("currency is required with value and must be supported.");
        var occurredAt = r.OccurredAt?.ToUniversalTime() ?? now;
        if (occurredAt > now.AddDays(1)) errors.Add("occurredAt is in the future.");
        if (errors.Count > 0) throw EmailProblem.Invalid("email.conversion_invalid", "The conversion is invalid.", errors);

        var key = Workspace.Key(r.ClientAccountId);
        var dedup = $"conv:{key}:{r.ExternalReference.Trim()}";
        if (await db.Set<EngagementEvent>().AsNoTracking().Where(e => e.DedupKey == dedup).Select(e => new { e.Id, e.CampaignId, e.AutomationId }).FirstOrDefaultAsync(ct) is { } existing)
            return new ConversionResult(existing.Id, true, existing.CampaignId, existing.AutomationId);

        var subscriber = await db.Set<Subscriber>().AsNoTracking().Where(s => s.ScopeKey == key && s.NormalizedEmail == email).Select(s => new { s.Id, s.ClientAccountId })
            .FirstOrDefaultAsync(ct) ?? throw new DomainException("email.contact_not_found", "No contact with that email in this workspace.", DomainErrorKind.NotFound);

        var since = occurredAt - AttributionWindow;
        var click = await db.Set<EngagementEvent>().AsNoTracking()
            .Where(e => e.SubscriberId == subscriber.Id && e.Type == EngagementType.Click && !e.IsMachine && e.OccurredAt >= since && e.OccurredAt <= occurredAt)
            .OrderByDescending(e => e.OccurredAt).Select(e => new { e.CampaignId, e.RecipientId, e.AutomationId }).FirstOrDefaultAsync(ct);
        var currency = r.Currency is null ? null : Money.Normalize(r.Currency);
        var value = r.Value is { } v && currency is not null ? Money.Round(v, currency) : r.Value;
        var conversion = new EngagementEvent
        {
            ClientAccountId = subscriber.ClientAccountId, SubscriberId = subscriber.Id, CampaignId = click?.CampaignId, RecipientId = click?.RecipientId,
            AutomationId = click?.AutomationId, Type = EngagementType.Conversion, OccurredAt = occurredAt, Value = value, Currency = currency,
            ExternalReference = Text.Truncate(r.ExternalReference.Trim(), 150), DedupKey = dedup,
        };
        db.Set<EngagementEvent>().Add(conversion);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            var winner = await db.Set<EngagementEvent>().AsNoTracking().Where(e => e.DedupKey == dedup).Select(e => new { e.Id, e.CampaignId, e.AutomationId }).FirstAsync(ct);
            return new ConversionResult(winner.Id, true, winner.CampaignId, winner.AutomationId);
        }
        if (click?.RecipientId is { } recipientId)
        {
            await db.Set<CampaignRecipient>().Where(x => x.Id == recipientId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.ConvertedAt, x => x.ConvertedAt ?? occurredAt), ct);
        }
        return new ConversionResult(conversion.Id, false, click?.CampaignId, click?.AutomationId);
    }

    public async Task<CustomEventResult> RecordEventAsync(CustomEventRequest r, CancellationToken ct)
    {
        if (!AutomationRules.IsEventName(r.Name)) throw new DomainException("email.event_invalid", "Event names use letters, digits, _ . - (max 64).");
        var email = ContactRules.NormalizeEmail(r.Email) ?? throw new DomainException("email.event_invalid", "email is invalid.");
        string? properties = null;
        if (r.Properties is { ValueKind: JsonValueKind.Object } p)
        {
            properties = p.GetRawText();
            if (properties.Length > 4000) throw new DomainException("email.event_invalid", "Event properties are limited to 4000 characters.");
        }
        var key = Workspace.Key(r.ClientAccountId);
        var subscriber = await db.Set<Subscriber>().AsNoTracking().Where(s => s.ScopeKey == key && s.NormalizedEmail == email)
            .Select(s => new { s.Id, s.ClientAccountId }).FirstOrDefaultAsync(ct);
        if (subscriber is null) return new CustomEventResult(false, false, 0, "No contact with that email in this workspace.");

        var now = clock.GetUtcNow().UtcDateTime;
        var dedup = string.IsNullOrWhiteSpace(r.EventId) ? null : $"evt:{key}:{r.EventId.Trim()}";
        var ev = new EngagementEvent
        {
            ClientAccountId = subscriber.ClientAccountId, SubscriberId = subscriber.Id, Type = EngagementType.Custom, Name = r.Name,
            OccurredAt = r.OccurredAt?.ToUniversalTime() is { } at && at <= now ? at : now, Detail = properties is null ? null : Text.Truncate(properties, 2000),
            DedupKey = dedup,
        };
        db.Set<EngagementEvent>().Add(ev);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return new CustomEventResult(true, true, 0, null);
        }
        var enrolled = await triggers.OnCustomEventAsync(subscriber.ClientAccountId, r.Name, subscriber.Id, properties, ct);
        return new CustomEventResult(true, false, enrolled, null);
    }
}
