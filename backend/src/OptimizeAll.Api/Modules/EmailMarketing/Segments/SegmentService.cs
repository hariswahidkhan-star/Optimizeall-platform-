using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Segments;

public sealed record SegmentDto(Guid Id, Guid? ClientAccountId, string Name, JsonElement Definition, int? LastCount, DateTime? LastCountedAt,
    DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class SegmentRequest
{
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required] public JsonElement Definition { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class SegmentPreviewRequest
{
    public Guid? ClientAccountId { get; set; }
    public Guid? ListId { get; set; }
    [Required] public JsonElement Definition { get; set; }
}

public sealed record SegmentPreview(int Count, int Total, IReadOnlyList<SegmentSample> Sample);

public sealed record SegmentSample(Guid Id, string? Email, string? FirstName, string? LastName, string? CountryCode);

/// <summary>
/// Builds a server-side query from segment rules. Every condition becomes a translatable LINQ predicate (EXISTS
/// sub-queries for tags, lists, fields and engagement), combined with AND/OR per group, so the same rules produce the
/// same audience in the live count, in campaign expansion and on both database providers. Date boundaries are computed
/// in C# (no SQL date arithmetic).
/// </summary>
public sealed class SegmentQueryBuilder(AppDbContext db, TimeProvider clock)
{
    public IQueryable<Subscriber> Apply(IQueryable<Subscriber> query, SegmentDefinition definition) =>
        query.Where(Build(definition));

    public Expression<Func<Subscriber, bool>> Build(SegmentDefinition definition)
    {
        var p = Expression.Parameter(typeof(Subscriber), "s");
        var body = Group(definition, p);
        return Expression.Lambda<Func<Subscriber, bool>>(body, p);
    }

    private Expression Group(SegmentDefinition group, ParameterExpression p)
    {
        var parts = group.Conditions.Select(c => Inline(Condition(c), p)).Concat(group.Groups.Select(g => Group(g, p))).ToList();
        if (parts.Count == 0) return Expression.Constant(true);
        return group.Match == "any" ? parts.Aggregate(Expression.OrElse) : parts.Aggregate(Expression.AndAlso);
    }

    private static Expression Inline(Expression<Func<Subscriber, bool>> predicate, ParameterExpression p) =>
        new Replace(predicate.Parameters[0], p).Visit(predicate.Body)!;

    private Expression<Func<Subscriber, bool>> Condition(SegmentCondition c)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var since = now.AddDays(-(c.WithinDays ?? 36500));
        var events = db.Set<EngagementEvent>();
        var recipients = db.Set<CampaignRecipient>();
        switch (c.Kind)
        {
            case "field":
                return Field(c, now);
            case "custom":
            {
                var key = c.Field!;
                var fields = db.Set<SubscriberField>();
                var value = c.Value ?? string.Empty;
                return c.Op switch
                {
                    "equals" => s => fields.Any(f => f.SubscriberId == s.Id && f.Key == key && f.Value == value),
                    "not_equals" => s => !fields.Any(f => f.SubscriberId == s.Id && f.Key == key && f.Value == value),
                    "contains" => s => fields.Any(f => f.SubscriberId == s.Id && f.Key == key && f.Value.Contains(value)),
                    "is_empty" => s => !fields.Any(f => f.SubscriberId == s.Id && f.Key == key),
                    _ => s => fields.Any(f => f.SubscriberId == s.Id && f.Key == key),
                };
            }
            case "tag":
            {
                var tag = ContactRules.NormalizeTag(c.Value)!;
                var tags = db.Set<SubscriberTag>();
                return c.Op == "has_not"
                    ? s => !tags.Any(t => t.SubscriberId == s.Id && t.Tag == tag)
                    : s => tags.Any(t => t.SubscriberId == s.Id && t.Tag == tag);
            }
            case "list":
            {
                var listId = Guid.Parse(c.Value!);
                var memberships = db.Set<ListMembership>();
                return c.Op == "not_in"
                    ? s => !memberships.Any(m => m.SubscriberId == s.Id && m.ListId == listId && m.Status == MembershipStatus.Subscribed)
                    : s => memberships.Any(m => m.SubscriberId == s.Id && m.ListId == listId && m.Status == MembershipStatus.Subscribed);
            }
            case "engagement":
            {
                var campaignId = c.CampaignId;
                return c.Event switch
                {
                    "opened" => s => events.Any(e => e.SubscriberId == s.Id && e.Type == EngagementType.Open && !e.IsMachine && e.OccurredAt >= since &&
                                                     (campaignId == null || e.CampaignId == campaignId)),
                    "not_opened" => s => !events.Any(e => e.SubscriberId == s.Id && e.Type == EngagementType.Open && !e.IsMachine && e.OccurredAt >= since &&
                                                          (campaignId == null || e.CampaignId == campaignId)),
                    "clicked" => s => events.Any(e => e.SubscriberId == s.Id && e.Type == EngagementType.Click && !e.IsMachine && e.OccurredAt >= since &&
                                                      (campaignId == null || e.CampaignId == campaignId)),
                    "not_clicked" => s => !events.Any(e => e.SubscriberId == s.Id && e.Type == EngagementType.Click && !e.IsMachine && e.OccurredAt >= since &&
                                                           (campaignId == null || e.CampaignId == campaignId)),
                    _ => s => recipients.Any(r => r.SubscriberId == s.Id && r.Status == RecipientStatus.Sent && r.SentAt >= since &&
                                                  (campaignId == null || r.CampaignId == campaignId)),
                };
            }
            case "purchase":
                return c.Op == "not_purchased"
                    ? s => !events.Any(e => e.SubscriberId == s.Id && e.Type == EngagementType.Conversion && e.OccurredAt >= since)
                    : s => events.Any(e => e.SubscriberId == s.Id && e.Type == EngagementType.Conversion && e.OccurredAt >= since);
            case "event":
            {
                var name = c.Value!;
                return c.Op == "not_occurred"
                    ? s => !events.Any(e => e.SubscriberId == s.Id && e.Type == EngagementType.Custom && e.Name == name && e.OccurredAt >= since)
                    : s => events.Any(e => e.SubscriberId == s.Id && e.Type == EngagementType.Custom && e.Name == name && e.OccurredAt >= since);
            }
            case "consent":
            {
                var granted = c.Op != "not_granted";
                return c.Channel switch
                {
                    "sms" => granted ? s => s.SmsConsent == ConsentStatus.Granted : s => s.SmsConsent != ConsentStatus.Granted,
                    "whatsapp" => granted ? s => s.WhatsAppConsent == ConsentStatus.Granted : s => s.WhatsAppConsent != ConsentStatus.Granted,
                    _ => granted ? s => s.EmailConsent == ConsentStatus.Granted : s => s.EmailConsent != ConsentStatus.Granted,
                };
            }
            default:
                throw new DomainException("email.segment_invalid", $"Unknown condition kind '{c.Kind}'.");
        }
    }

    private static Expression<Func<Subscriber, bool>> Field(SegmentCondition c, DateTime now)
    {
        if (c.Field == "created_at")
        {
            var boundary = now.AddDays(-(c.WithinDays ?? 0));
            return c.Op == "before_days_ago" ? s => s.CreatedAt < boundary : s => s.CreatedAt >= boundary;
        }
        if (c.Field == "status")
        {
            var statuses = (c.Values ?? new List<string> { c.Value ?? string.Empty })
                .Select(v => Enum.TryParse<SubscriberStatus>(v, out var st) ? (SubscriberStatus?)st : null).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return c.Op is "not_equals" or "not_in" ? s => !statuses.Contains(s.Status) : s => statuses.Contains(s.Status);
        }

        Expression<Func<Subscriber, string?>> selector = c.Field switch
        {
            "email" => s => s.NormalizedEmail,
            "first_name" => s => s.FirstName,
            "last_name" => s => s.LastName,
            "country" => s => s.CountryCode,
            "language" => s => s.Language,
            "source" => s => s.Source,
            "phone" => s => s.Phone,
            _ => throw new DomainException("email.segment_invalid", $"Unknown field '{c.Field}'."),
        };
        var normalize = c.Field is "country" ? (Func<string, string>)(v => v.Trim().ToUpperInvariant())
            : c.Field is "email" ? v => v.Trim().ToLowerInvariant() : v => v.Trim();
        var value = normalize(c.Value ?? string.Empty);
        var values = (c.Values ?? new List<string>()).Select(normalize).ToList();

        Expression<Func<string?, bool>> test = c.Op switch
        {
            "equals" => v => v == value,
            "not_equals" => v => v == null || v != value,
            "contains" => v => v != null && v.Contains(value),
            "starts_with" => v => v != null && v.StartsWith(value),
            "in" => v => v != null && values.Contains(v),
            "not_in" => v => v == null || !values.Contains(v),
            "is_empty" => v => v == null || v == "",
            "is_not_empty" => v => v != null && v != "",
            _ => throw new DomainException("email.segment_invalid", $"Unknown operator '{c.Op}'."),
        };
        var p = Expression.Parameter(typeof(Subscriber), "s");
        var selected = new Replace(selector.Parameters[0], p).Visit(selector.Body)!;
        var body = new Replace(test.Parameters[0], selected).Visit(test.Body)!;
        return Expression.Lambda<Func<Subscriber, bool>>(body, p);
    }

    private sealed class Replace(Expression from, Expression to) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node) => node == from ? to : base.Visit(node);
    }
}

/// <summary>Saved segments and live count previews.</summary>
public sealed class SegmentService(AppDbContext db, EmailAccess access, IAuditLogger audit, SegmentQueryBuilder builder, TimeProvider clock)
{
    public async Task<IReadOnlyList<SegmentDto>> ListAsync(Guid? clientId, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        var key = Workspace.Key(clientId);
        var rows = await db.Set<Segment>().AsNoTracking().Where(s => s.ScopeKey == key).OrderBy(s => s.Name).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<SegmentDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await LoadAsync(id, ct));

    public Task<Segment> LoadAsync(Guid id, CancellationToken ct) =>
        access.LoadAsync(db.Set<Segment>().Where(s => s.Id == id), s => s.ClientAccountId, "Segment", ct);

    public async Task<SegmentDto> CreateAsync(SegmentRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        var definition = await ParseAsync(r.Definition, r.ClientAccountId, ct);
        var segment = new Segment { ClientAccountId = r.ClientAccountId, ScopeKey = Workspace.Key(r.ClientAccountId), Name = r.Name.Trim(), DefinitionJson = definition.ToJson() };
        segment.LastCount = await CountAsync(r.ClientAccountId, null, definition, ct);
        segment.LastCountedAt = clock.GetUtcNow().UtcDateTime;
        db.Set<Segment>().Add(segment);
        audit.Record("email.segment.created", nameof(Segment), segment.Id, after: new { segment.Name, segment.DefinitionJson });
        await db.SaveChangesAsync(ct);
        return ToDto(segment);
    }

    public async Task<SegmentDto> UpdateAsync(Guid id, SegmentRequest r, CancellationToken ct)
    {
        var segment = await LoadAsync(id, ct);
        AudienceService.ExpectStamp(segment.ConcurrencyStamp, r.ConcurrencyStamp);
        var definition = await ParseAsync(r.Definition, segment.ClientAccountId, ct);
        var before = new { segment.Name, segment.DefinitionJson };
        segment.Name = r.Name.Trim();
        segment.DefinitionJson = definition.ToJson();
        segment.LastCount = await CountAsync(segment.ClientAccountId, null, definition, ct);
        segment.LastCountedAt = clock.GetUtcNow().UtcDateTime;
        audit.Record("email.segment.updated", nameof(Segment), segment.Id, before, new { segment.Name, segment.DefinitionJson });
        await db.SaveChangesAsync(ct);
        return ToDto(segment);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var segment = await LoadAsync(id, ct);
        if (await db.Set<EmailCampaign>().AnyAsync(c => c.SegmentId == id && c.Status != CampaignStatus.Sent && c.Status != CampaignStatus.Cancelled, ct))
            throw DomainException.Conflict("email.segment_in_use", "A draft or scheduled campaign uses this segment.");
        audit.Record("email.segment.deleted", nameof(Segment), segment.Id, before: new { segment.Name, segment.DefinitionJson });
        db.Set<Segment>().Remove(segment);
        await db.SaveChangesAsync(ct);
    }

    public async Task<SegmentPreview> PreviewAsync(SegmentPreviewRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        var definition = await ParseAsync(r.Definition, r.ClientAccountId, ct);
        var key = Workspace.Key(r.ClientAccountId);
        var baseQuery = Base(key, r.ListId);
        var total = await baseQuery.CountAsync(ct);
        var matching = builder.Apply(baseQuery, definition);
        var count = await matching.CountAsync(ct);
        var sample = await matching.OrderByDescending(s => s.CreatedAt).Take(5)
            .Select(s => new SegmentSample(s.Id, s.Email, s.FirstName, s.LastName, s.CountryCode)).ToListAsync(ct);
        return new SegmentPreview(count, total, sample);
    }

    public async Task<int> CountAsync(Guid? clientId, Guid? listId, SegmentDefinition definition, CancellationToken ct) =>
        await builder.Apply(Base(Workspace.Key(clientId), listId), definition).CountAsync(ct);

    private IQueryable<Subscriber> Base(string scopeKey, Guid? listId)
    {
        var q = db.Set<Subscriber>().AsNoTracking().Where(s => s.ScopeKey == scopeKey);
        if (listId is { } id) q = q.Where(s => db.Set<ListMembership>().Any(m => m.SubscriberId == s.Id && m.ListId == id && m.Status == MembershipStatus.Subscribed));
        return q;
    }

    /// <summary>Parses and validates a definition (lists and campaigns referenced must belong to the workspace).</summary>
    public async Task<SegmentDefinition> ParseAsync(JsonElement json, Guid? clientId, CancellationToken ct)
    {
        SegmentDefinition definition;
        try { definition = SegmentDefinition.Parse(json.GetRawText()); }
        catch (FormatException ex) { throw new DomainException("email.segment_invalid", ex.Message); }
        var errors = SegmentRules.Validate(definition);
        if (errors.Count > 0) throw EmailProblem.Invalid("email.segment_invalid", "The segment rules are invalid.", errors, "definition");
        var key = Workspace.Key(clientId);
        var all = Flatten(definition).ToList();
        var listIds = all.Where(c => c.Kind == "list").Select(c => Guid.Parse(c.Value!)).Distinct().ToList();
        if (listIds.Count > 0 && await db.Set<EmailList>().CountAsync(l => listIds.Contains(l.Id) && l.ScopeKey == key, ct) != listIds.Count)
            throw new DomainException("email.segment_invalid", "A list in the rules does not belong to this workspace.");
        var campaignIds = all.Where(c => c.CampaignId is not null).Select(c => c.CampaignId!.Value).Distinct().ToList();
        if (campaignIds.Count > 0 && await db.Set<EmailCampaign>().CountAsync(x => campaignIds.Contains(x.Id) && x.ScopeKey == key, ct) != campaignIds.Count)
            throw new DomainException("email.segment_invalid", "A campaign in the rules does not belong to this workspace.");
        return definition;
    }

    private static IEnumerable<SegmentCondition> Flatten(SegmentDefinition d) => d.Conditions.Concat(d.Groups.SelectMany(Flatten));

    private static SegmentDto ToDto(Segment s) =>
        new(s.Id, s.ClientAccountId, s.Name, JsonDocument.Parse(s.DefinitionJson).RootElement.Clone(), s.LastCount, s.LastCountedAt, s.UpdatedAt, s.ConcurrencyStamp);
}
