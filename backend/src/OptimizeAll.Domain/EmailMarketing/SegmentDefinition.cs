using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptimizeAll.Domain.EmailMarketing;

/// <summary>
/// Segment rules: a root group combining conditions and nested groups with AND ("all") or OR ("any").
/// <code>
/// { "match": "all", "conditions": [ { "kind": "field", "field": "country", "op": "in", "values": ["US","GB"] },
///                                   { "kind": "engagement", "event": "opened", "withinDays": 30 } ],
///   "groups": [ { "match": "any", "conditions": [ ... ] } ] }
/// </code>
/// </summary>
public sealed class SegmentDefinition
{
    /// <summary>"all" (AND) or "any" (OR).</summary>
    public string Match { get; set; } = "all";
    public List<SegmentCondition> Conditions { get; set; } = new();
    public List<SegmentDefinition> Groups { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 12,
    };

    public static SegmentDefinition Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SegmentDefinition();
        try
        {
            return JsonSerializer.Deserialize<SegmentDefinition>(json, JsonOptions) ?? new SegmentDefinition();
        }
        catch (JsonException ex)
        {
            throw new FormatException("The segment definition is not valid JSON: " + ex.Message, ex);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

/// <summary>
/// One rule. <see cref="Kind"/>:
/// <list type="bullet">
/// <item><c>field</c>: built-in field (<c>email, first_name, last_name, country, language, source, status, phone, created_at</c>)
///   with <c>op</c> equals, not_equals, contains, starts_with, in, not_in, is_empty, is_not_empty, before_days_ago, within_days.</item>
/// <item><c>custom</c>: custom field <c>field</c> with equals, not_equals, contains, is_empty, is_not_empty.</item>
/// <item><c>tag</c>: <c>op</c> has / has_not, <c>value</c> tag.</item>
/// <item><c>list</c>: <c>op</c> in / not_in, <c>value</c> list id (subscribed membership).</item>
/// <item><c>engagement</c>: <c>event</c> opened | clicked | received | not_opened | not_clicked, optional <c>withinDays</c> and <c>campaignId</c>.</item>
/// <item><c>purchase</c>: <c>op</c> purchased | not_purchased, optional <c>withinDays</c>.</item>
/// <item><c>consent</c>: <c>channel</c> email | sms | whatsapp, <c>op</c> granted | not_granted.</item>
/// <item><c>event</c>: custom event <c>value</c> (name), <c>op</c> occurred | not_occurred, optional <c>withinDays</c>.</item>
/// </list>
/// </summary>
public sealed class SegmentCondition
{
    public string Kind { get; set; } = "field";
    public string? Field { get; set; }
    public string? Op { get; set; }
    public string? Value { get; set; }
    public List<string>? Values { get; set; }
    public string? Event { get; set; }
    public int? WithinDays { get; set; }
    public Guid? CampaignId { get; set; }
    public string? Channel { get; set; }
}

public static class SegmentRules
{
    public const int MaxConditions = 40;
    public const int MaxDepth = 3;

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        "email", "first_name", "last_name", "country", "language", "source", "status", "phone", "created_at",
    };

    public static readonly IReadOnlySet<string> FieldOps = new HashSet<string>(StringComparer.Ordinal)
    {
        "equals", "not_equals", "contains", "starts_with", "in", "not_in", "is_empty", "is_not_empty", "before_days_ago", "within_days",
    };

    public static readonly IReadOnlySet<string> EngagementEvents = new HashSet<string>(StringComparer.Ordinal)
    {
        "opened", "clicked", "received", "not_opened", "not_clicked",
    };

    /// <summary>Validation errors (empty when valid).</summary>
    public static List<string> Validate(SegmentDefinition definition)
    {
        var errors = new List<string>();
        var count = 0;
        Validate(definition, "root", 1, errors, ref count);
        if (count > MaxConditions) errors.Add($"A segment may have at most {MaxConditions} conditions.");
        return errors;
    }

    private static void Validate(SegmentDefinition group, string path, int depth, List<string> errors, ref int count)
    {
        if (group.Match is not ("all" or "any")) errors.Add($"{path}.match must be 'all' or 'any'.");
        if (depth > MaxDepth) { errors.Add($"Groups can be nested at most {MaxDepth} levels deep."); return; }
        for (var i = 0; i < group.Conditions.Count; i++)
        {
            count++;
            var c = group.Conditions[i];
            var p = $"{path}.conditions[{i}]";
            switch (c.Kind)
            {
                case "field":
                    if (c.Field is null || !Fields.Contains(c.Field)) errors.Add($"{p}: unknown field '{c.Field}'.");
                    if (c.Op is null || !FieldOps.Contains(c.Op)) { errors.Add($"{p}: unknown operator '{c.Op}'."); break; }
                    if (c.Op is "in" or "not_in" && (c.Values is null || c.Values.Count == 0 || c.Values.Count > 200))
                        errors.Add($"{p}: provide 1–200 values.");
                    if (c.Op is "equals" or "not_equals" or "contains" or "starts_with" && string.IsNullOrWhiteSpace(c.Value))
                        errors.Add($"{p}: a value is required.");
                    if (c.Op is "before_days_ago" or "within_days" && (c.WithinDays is null or < 0 or > 3650 || c.Field != "created_at"))
                        errors.Add($"{p}: date operators need created_at and withinDays between 0 and 3650.");
                    if (c.Field == "status" && c.Value is not null && !Enum.TryParse<SubscriberStatus>(c.Value, out _))
                        errors.Add($"{p}: unknown status '{c.Value}'.");
                    break;
                case "custom":
                    if (!ContactRules.IsValidFieldKey(c.Field)) errors.Add($"{p}: invalid custom field key.");
                    if (c.Op is not ("equals" or "not_equals" or "contains" or "is_empty" or "is_not_empty")) errors.Add($"{p}: unknown operator '{c.Op}'.");
                    else if (c.Op is "equals" or "not_equals" or "contains" && c.Value is null) errors.Add($"{p}: a value is required.");
                    break;
                case "tag":
                    if (c.Op is not ("has" or "has_not")) errors.Add($"{p}: tag operator must be has or has_not.");
                    if (ContactRules.NormalizeTag(c.Value) is null) errors.Add($"{p}: invalid tag.");
                    break;
                case "list":
                    if (c.Op is not ("in" or "not_in")) errors.Add($"{p}: list operator must be in or not_in.");
                    if (!Guid.TryParse(c.Value, out _)) errors.Add($"{p}: value must be a list id.");
                    break;
                case "engagement":
                    if (c.Event is null || !EngagementEvents.Contains(c.Event)) errors.Add($"{p}: unknown engagement event '{c.Event}'.");
                    if (c.WithinDays is < 1 or > 3650) errors.Add($"{p}: withinDays must be between 1 and 3650.");
                    break;
                case "purchase":
                    if (c.Op is not ("purchased" or "not_purchased")) errors.Add($"{p}: purchase operator must be purchased or not_purchased.");
                    if (c.WithinDays is < 1 or > 3650) errors.Add($"{p}: withinDays must be between 1 and 3650.");
                    break;
                case "consent":
                    if (c.Channel is not ("email" or "sms" or "whatsapp")) errors.Add($"{p}: channel must be email, sms or whatsapp.");
                    if (c.Op is not ("granted" or "not_granted")) errors.Add($"{p}: consent operator must be granted or not_granted.");
                    break;
                case "event":
                    if (c.Op is not ("occurred" or "not_occurred")) errors.Add($"{p}: event operator must be occurred or not_occurred.");
                    if (string.IsNullOrWhiteSpace(c.Value) || c.Value.Length > 64) errors.Add($"{p}: event name is required (max 64).");
                    if (c.WithinDays is < 1 or > 3650) errors.Add($"{p}: withinDays must be between 1 and 3650.");
                    break;
                default:
                    errors.Add($"{p}: unknown condition kind '{c.Kind}'.");
                    break;
            }
        }
        for (var g = 0; g < group.Groups.Count; g++)
            Validate(group.Groups[g], $"{path}.groups[{g}]", depth + 1, errors, ref count);
    }
}
