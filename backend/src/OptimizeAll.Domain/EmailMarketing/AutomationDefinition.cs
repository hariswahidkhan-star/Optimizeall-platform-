using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.EmailMarketing;

/// <summary>Trigger settings (only the fields relevant to the trigger are used).</summary>
public sealed class TriggerConfig
{
    public Guid? ListId { get; set; }
    public string? Tag { get; set; }
    public Guid? FormId { get; set; }
    /// <summary>Custom date field key (yyyy-MM-dd values) for anniversaries.</summary>
    public string? DateField { get; set; }
    public string? EventName { get; set; }
}

/// <summary>Journey goal: the contact exits as soon as it is met.</summary>
public sealed class GoalConfig
{
    /// <summary>tag_added, event, purchased.</summary>
    public string Kind { get; set; } = "purchased";
    public string? Tag { get; set; }
    public string? EventName { get; set; }
}

/// <summary>Step settings (only the fields relevant to the step type are used).</summary>
public sealed class StepConfig
{
    // SendEmail
    public Guid? TemplateId { get; set; }
    public string? Subject { get; set; }
    // SendSms
    public string? Body { get; set; }
    // Wait
    public int? Minutes { get; set; }
    public int? Hours { get; set; }
    public int? Days { get; set; }
    /// <summary>Wait until this local time of day ("HH:mm", contact's time zone), after the duration (if any).</summary>
    public string? UntilTime { get; set; }
    // Condition: opened, clicked (an email of this journey, optionally a specific step), tag, field, event.
    public string? Check { get; set; }
    public string? StepKey { get; set; }
    public string? Tag { get; set; }
    public string? Field { get; set; }
    public string? Value { get; set; }
    public string? EventName { get; set; }
    // NotifyStaff
    public List<Guid>? UserIds { get; set; }
    public string? Message { get; set; }
}

public sealed class StepDefinition
{
    public string Key { get; set; } = string.Empty;
    public AutomationStepType Type { get; set; }
    public StepConfig Config { get; set; } = new();
    public string? Next { get; set; }
    /// <summary>"No" branch (conditions only).</summary>
    public string? AltNext { get; set; }
}

public static partial class AutomationRules
{
    public const int MaxSteps = 50;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Parse<T>(string? json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T(); }
        catch (JsonException ex) { throw new FormatException("Invalid automation configuration: " + ex.Message, ex); }
    }

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <summary>Total wait duration of a Wait step.</summary>
    public static TimeSpan WaitDuration(StepConfig c) =>
        TimeSpan.FromMinutes(Math.Max(0, c.Minutes ?? 0)) + TimeSpan.FromHours(Math.Max(0, c.Hours ?? 0)) + TimeSpan.FromDays(Math.Max(0, c.Days ?? 0));

    /// <summary>When a Wait step releases the contact.</summary>
    public static DateTime WaitUntil(StepConfig c, DateTime nowUtc, TimeZoneInfo zone)
    {
        var at = nowUtc + WaitDuration(c);
        if (c.UntilTime is { } t && TimeOnly.TryParseExact(t, "HH:mm", out var time))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(at, zone);
            var candidate = local.Date + time.ToTimeSpan();
            if (candidate < local) candidate = candidate.AddDays(1);
            at = SendTiming.LocalToUtc(candidate, zone);
        }
        return at;
    }

    /// <summary>Validates the step graph: unique keys, known references, acyclic (so each step runs at most once per enrollment).</summary>
    public static List<string> Validate(AutomationTrigger trigger, TriggerConfig triggerConfig, IReadOnlyList<StepDefinition> steps, string? entryKey)
    {
        var errors = new List<string>();
        switch (trigger)
        {
            case AutomationTrigger.ListSubscribed when triggerConfig.ListId is null:
                errors.Add("Choose the list that starts this journey."); break;
            case AutomationTrigger.TagAdded when ContactRules.NormalizeTag(triggerConfig.Tag) is null:
                errors.Add("Enter the tag that starts this journey."); break;
            case AutomationTrigger.DateAnniversary when !ContactRules.IsValidFieldKey(triggerConfig.DateField):
                errors.Add("Enter the custom date field (e.g. birthday)."); break;
            case AutomationTrigger.CustomEvent when !IsEventName(triggerConfig.EventName):
                errors.Add("Enter the event name (letters, digits, _ . -; max 64)."); break;
        }

        if (steps.Count == 0) { errors.Add("Add at least one step."); return errors; }
        if (steps.Count > MaxSteps) errors.Add($"A journey may have at most {MaxSteps} steps.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            if (!StepKeyRegex().IsMatch(step.Key)) errors.Add($"Step key '{step.Key}' is invalid (letters, digits, - and _; max 20).");
            else if (!keys.Add(step.Key)) errors.Add($"Step key '{step.Key}' is used twice.");
        }
        if (entryKey is null || !keys.Contains(entryKey)) errors.Add("The first step is missing.");

        foreach (var step in steps)
        {
            if (step.Next is not null && !keys.Contains(step.Next)) errors.Add($"Step '{step.Key}' points to unknown step '{step.Next}'.");
            if (step.AltNext is not null && !keys.Contains(step.AltNext)) errors.Add($"Step '{step.Key}' points to unknown step '{step.AltNext}'.");
            if (step.Type != AutomationStepType.Condition && step.AltNext is not null) errors.Add($"Only conditions have a 'no' branch ('{step.Key}').");
            var c = step.Config;
            switch (step.Type)
            {
                case AutomationStepType.SendEmail when c.TemplateId is null:
                    errors.Add($"Step '{step.Key}': choose an email template."); break;
                case AutomationStepType.SendSms when string.IsNullOrWhiteSpace(c.Body) || c.Body.Length > 1600:
                    errors.Add($"Step '{step.Key}': the SMS text is required (max 1600 characters)."); break;
                case AutomationStepType.SendSms when MergeTags.Unknown(c.Body).Count > 0:
                    errors.Add($"Step '{step.Key}': unknown merge tags {string.Join(", ", MergeTags.Unknown(c.Body))}."); break;
                case AutomationStepType.Wait:
                    var duration = WaitDuration(c);
                    if (duration <= TimeSpan.Zero && c.UntilTime is null) errors.Add($"Step '{step.Key}': set a wait duration or time of day.");
                    if (duration > TimeSpan.FromDays(365)) errors.Add($"Step '{step.Key}': waits are limited to 365 days.");
                    if (c.UntilTime is not null && !TimeOnly.TryParseExact(c.UntilTime, "HH:mm", out _)) errors.Add($"Step '{step.Key}': time of day must be HH:mm.");
                    break;
                case AutomationStepType.Condition:
                    if (c.Check is not ("opened" or "clicked" or "tag" or "field" or "event"))
                        errors.Add($"Step '{step.Key}': condition must check opened, clicked, tag, field or event.");
                    if (c.Check == "tag" && ContactRules.NormalizeTag(c.Tag) is null) errors.Add($"Step '{step.Key}': enter a tag.");
                    if (c.Check == "field" && string.IsNullOrWhiteSpace(c.Field)) errors.Add($"Step '{step.Key}': choose a field.");
                    if (c.Check == "event" && !IsEventName(c.EventName)) errors.Add($"Step '{step.Key}': enter an event name.");
                    if (c.StepKey is not null && !keys.Contains(c.StepKey)) errors.Add($"Step '{step.Key}': unknown email step '{c.StepKey}'.");
                    break;
                case AutomationStepType.AddTag or AutomationStepType.RemoveTag when ContactRules.NormalizeTag(c.Tag) is null:
                    errors.Add($"Step '{step.Key}': enter a valid tag."); break;
                case AutomationStepType.NotifyStaff when string.IsNullOrWhiteSpace(c.Message) || c.Message.Length > 500:
                    errors.Add($"Step '{step.Key}': the staff message is required (max 500 characters)."); break;
            }
        }

        if (errors.Count == 0 && HasCycle(steps, entryKey!)) errors.Add("Journeys cannot loop back to an earlier step.");
        return errors;
    }

    public static bool IsEventName(string? name) => name is not null && EventNameRegex().IsMatch(name);

    private static bool HasCycle(IReadOnlyList<StepDefinition> steps, string entry)
    {
        var byKey = steps.ToDictionary(s => s.Key);
        var state = new Dictionary<string, int>();
        bool Visit(string key)
        {
            if (state.TryGetValue(key, out var s)) return s == 1;
            state[key] = 1;
            var step = byKey[key];
            foreach (var next in new[] { step.Next, step.AltNext })
                if (next is not null && Visit(next)) return true;
            state[key] = 2;
            return false;
        }
        return steps.Any(s => !state.ContainsKey(s.Key) && Visit(s.Key)) || Visit(entry);
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,20}$")]
    private static partial Regex StepKeyRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,64}$")]
    private static partial Regex EventNameRegex();
}
