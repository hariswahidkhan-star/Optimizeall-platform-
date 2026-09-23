using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OptimizeAll.Domain.Files;

namespace OptimizeAll.Domain.LandingPages;

public sealed class FormOption
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class FieldValidation
{
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }

    /// <summary>Regular expression the value must fully match (evaluated with a timeout).</summary>
    public string? Pattern { get; set; }
    public string? PatternMessage { get; set; }

    /// <summary>File fields: allowed kinds ("pdf", "image").</summary>
    public List<string>? Accept { get; set; }
    public int? MaxSizeMb { get; set; }

    /// <summary>Multiselect: minimum/maximum number of choices.</summary>
    public int? MinChoices { get; set; }
    public int? MaxChoices { get; set; }
}

/// <summary>Show this field only when the condition on another (earlier) field holds.</summary>
public sealed class FieldCondition
{
    public string Field { get; set; } = string.Empty;

    /// <summary>equals | notEquals | contains | in | isEmpty | isNotEmpty | greaterThan | lessThan</summary>
    public string Operator { get; set; } = "equals";
    public string? Value { get; set; }
    public List<string>? Values { get; set; }
}

public sealed class FormField
{
    public string Key { get; set; } = string.Empty;

    /// <summary>text | email | phone | number | select | multiselect | checkbox | radio | date | textarea | file | hidden | consent</summary>
    public string Type { get; set; } = "text";
    public string Label { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public List<FormOption>? Options { get; set; }
    public FieldValidation? Validation { get; set; }
    public FieldCondition? ShowIf { get; set; }

    /// <summary>Hidden fields: URL parameter to capture (utm_source, utm_medium, utm_campaign, utm_term, utm_content, gclid, fbclid, ref).</summary>
    public string? UrlParam { get; set; }
    public string? DefaultValue { get; set; }

    /// <summary>Layout hint: "full" (default) or "half".</summary>
    public string? Width { get; set; }
}

public sealed class FormStep
{
    public string Id { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public List<FormField> Fields { get; set; } = new();
}

public sealed class FormSchema
{
    public List<FormStep> Steps { get; set; } = new();

    [JsonIgnore]
    public IEnumerable<FormField> AllFields => Steps.SelectMany(s => s.Fields);
}

/// <summary>An uploaded file offered for a file field (bytes already read, bounded by the request limit).</summary>
public sealed record UploadedFile(string FieldKey, string FileName, byte[] Content);

public sealed record AcceptedFile(string FieldKey, string FileName, string ContentType, string Extension, byte[] Content);

public sealed record FormValidationResult(
    IReadOnlyDictionary<string, string> Values, IReadOnlyList<AcceptedFile> Files, bool ConsentGiven,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Form definitions: schema validation (when a form is saved) and submission validation with conditional logic (when a
/// visitor submits). The web app mirrors <see cref="IsVisible"/> for instant feedback, but the server is authoritative:
/// hidden fields are never required and their submitted values are dropped.
/// </summary>
public static partial class FormSchemas
{
    public static readonly IReadOnlyList<string> FieldTypes = new[]
    {
        "text", "email", "phone", "number", "select", "multiselect", "checkbox", "radio", "date", "textarea", "file", "hidden", "consent",
    };

    public static readonly IReadOnlyList<string> Operators = new[]
    {
        "equals", "notEquals", "contains", "in", "isEmpty", "isNotEmpty", "greaterThan", "lessThan",
    };

    public static readonly IReadOnlyList<string> UrlParams = new[]
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "gclid", "fbclid", "ref",
    };

    public const int MaxFields = 60;
    public const int MaxSteps = 10;
    public const int DefaultTextMax = 500;
    public const int DefaultTextareaMax = 5000;
    public const int DefaultFileMaxMb = 5;
    public const int AbsoluteFileMaxMb = 10;
    public const int MaxFilesPerSubmission = 5;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static FormSchema Deserialize(string json) =>
        JsonSerializer.Deserialize<FormSchema>(json, Json) ?? new FormSchema();

    public static string Serialize(FormSchema schema) => JsonSerializer.Serialize(schema, Json);

    /// <summary>Validates a form definition; returns field-path → errors (empty when valid).</summary>
    public static IReadOnlyDictionary<string, string[]> ValidateSchema(FormSchema schema)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var l)) errors[key] = l = new List<string>();
            l.Add(message);
        }

        if (schema.Steps.Count is 0 or > MaxSteps) Add("steps", $"A form needs 1–{MaxSteps} steps.");
        var fields = schema.AllFields.ToList();
        if (fields.Count == 0) Add("steps", "Add at least one field.");
        if (fields.Count > MaxFields) Add("steps", $"A form may have at most {MaxFields} fields.");
        if (!fields.Any(f => f.Type != "hidden")) Add("steps", "Add at least one visible field.");

        var stepIds = new HashSet<string>();
        var seen = new List<string>();
        for (var s = 0; s < schema.Steps.Count; s++)
        {
            var step = schema.Steps[s];
            var sp = $"steps[{s}]";
            if (!KeyRegex().IsMatch(step.Id)) Add($"{sp}.id", "Step ids are 1–40 letters, digits, '-' or '_'.");
            else if (!stepIds.Add(step.Id)) Add($"{sp}.id", "Step ids must be unique.");
            if (step.Title is { Length: > 100 }) Add($"{sp}.title", "At most 100 characters.");
            if (step.Description is { Length: > 500 }) Add($"{sp}.description", "At most 500 characters.");
            if (step.Fields.Count == 0) Add($"{sp}.fields", "Each step needs at least one field.");

            for (var i = 0; i < step.Fields.Count; i++)
            {
                var f = step.Fields[i];
                var p = $"{sp}.fields[{i}]";
                if (!KeyRegex().IsMatch(f.Key)) Add($"{p}.key", "Field keys are 1–40 letters, digits, '-' or '_'.");
                else if (seen.Contains(f.Key)) Add($"{p}.key", "Field keys must be unique across the form.");
                if (!FieldTypes.Contains(f.Type)) { Add($"{p}.type", $"Unknown field type '{f.Type}'."); seen.Add(f.Key); continue; }
                if (f.Type != "hidden" && (string.IsNullOrWhiteSpace(f.Label) || f.Label.Length > 200)) Add($"{p}.label", "A label (max 200 characters) is required.");
                if (f.Placeholder is { Length: > 150 }) Add($"{p}.placeholder", "At most 150 characters.");
                if (f.HelpText is { Length: > 500 }) Add($"{p}.helpText", "At most 500 characters.");
                if (f.Width is not (null or "full" or "half")) Add($"{p}.width", "Width is 'full' or 'half'.");

                if (f.Type is "select" or "multiselect" or "radio")
                {
                    if (f.Options is null || f.Options.Count is < 1 or > 100) Add($"{p}.options", "Add 1–100 options.");
                    else
                    {
                        if (f.Options.Any(o => string.IsNullOrWhiteSpace(o.Value) || o.Value.Length > 100 || string.IsNullOrWhiteSpace(o.Label) || o.Label.Length > 200))
                            Add($"{p}.options", "Every option needs a value (max 100) and a label (max 200).");
                        if (f.Options.Select(o => o.Value).Distinct().Count() != f.Options.Count) Add($"{p}.options", "Option values must be unique.");
                    }
                }
                else if (f.Options is { Count: > 0 }) Add($"{p}.options", "Only select, multiselect and radio fields have options.");

                if (f.Type == "hidden")
                {
                    if (f.UrlParam is not null && !UrlParams.Contains(f.UrlParam)) Add($"{p}.urlParam", $"Allowed parameters: {string.Join(", ", UrlParams)}.");
                    if (f.Required) Add($"{p}.required", "Hidden fields cannot be required.");
                }
                else if (f.UrlParam is not null) Add($"{p}.urlParam", "Only hidden fields capture URL parameters.");
                if (f.DefaultValue is { Length: > 500 }) Add($"{p}.defaultValue", "At most 500 characters.");

                if (f.Validation is { } v)
                {
                    if (v.MinLength is < 0 || v.MaxLength is < 1 or > 20000 || (v.MinLength > v.MaxLength)) Add($"{p}.validation", "Length limits are invalid.");
                    if (v.Min > v.Max) Add($"{p}.validation", "Minimum must not exceed maximum.");
                    if (v.Pattern is { } pattern)
                    {
                        if (pattern.Length > 300) Add($"{p}.validation.pattern", "At most 300 characters.");
                        else
                        {
                            try { _ = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout); }
                            catch (ArgumentException) { Add($"{p}.validation.pattern", "Not a valid regular expression."); }
                        }
                    }
                    if (v.PatternMessage is { Length: > 200 }) Add($"{p}.validation.patternMessage", "At most 200 characters.");
                    if (v.Accept is { } accept && (accept.Count == 0 || accept.Any(a => a is not ("pdf" or "image"))))
                        Add($"{p}.validation.accept", "Files may be 'pdf' and/or 'image'.");
                    if (v.MaxSizeMb is < 1 or > AbsoluteFileMaxMb) Add($"{p}.validation.maxSizeMb", $"Maximum file size is 1–{AbsoluteFileMaxMb} MB.");
                    if (v.MinChoices is < 0 || v.MaxChoices is < 1 || v.MinChoices > v.MaxChoices) Add($"{p}.validation", "Choice limits are invalid.");
                }

                if (f.ShowIf is { } cond)
                {
                    if (!seen.Contains(cond.Field)) Add($"{p}.showIf.field", "Conditions must refer to an earlier field.");
                    if (!Operators.Contains(cond.Operator)) Add($"{p}.showIf.operator", $"Allowed operators: {string.Join(", ", Operators)}.");
                    if (cond.Operator is "equals" or "notEquals" or "contains" or "greaterThan" or "lessThan" && cond.Value is null)
                        Add($"{p}.showIf.value", "A comparison value is required.");
                    if (cond.Operator == "in" && (cond.Values is null || cond.Values.Count == 0)) Add($"{p}.showIf.values", "List at least one value.");
                    if (f.Type == "consent") Add($"{p}.showIf", "Consent fields cannot be conditional.");
                }
                seen.Add(f.Key);
            }
        }
        if (fields.Count(f => f.Type == "consent") > 1) Add("steps", "A form can have only one consent field.");
        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    /// <summary>
    /// Whether a field is shown given the current (raw) values. A condition on a field that is itself hidden treats that
    /// field as empty, so chains of conditions collapse correctly.
    /// </summary>
    public static bool IsVisible(FormField field, IReadOnlyDictionary<string, IReadOnlyList<string>> values, FormSchema schema)
        => IsVisible(field, values, schema, 0);

    private static bool IsVisible(FormField field, IReadOnlyDictionary<string, IReadOnlyList<string>> values, FormSchema schema, int depth)
    {
        if (field.ShowIf is not { } cond) return true;
        if (depth > MaxFields) return false;
        var controller = schema.AllFields.FirstOrDefault(f => f.Key == cond.Field);
        var controllerVisible = controller is not null && IsVisible(controller, values, schema, depth + 1);
        var current = controllerVisible && values.TryGetValue(cond.Field, out var v) ? v.Where(x => x.Length > 0).ToList() : new List<string>();
        return Evaluate(cond, current);
    }

    public static bool Evaluate(FieldCondition cond, IReadOnlyList<string> current)
    {
        var single = current.Count > 0 ? current[0] : string.Empty;
        return cond.Operator switch
        {
            "equals" => current.Any(c => string.Equals(c, cond.Value, StringComparison.OrdinalIgnoreCase)),
            "notEquals" => !current.Any(c => string.Equals(c, cond.Value, StringComparison.OrdinalIgnoreCase)),
            "contains" => cond.Value is not null && current.Any(c => c.Contains(cond.Value, StringComparison.OrdinalIgnoreCase)),
            "in" => cond.Values is not null && current.Any(c => cond.Values.Contains(c, StringComparer.OrdinalIgnoreCase)),
            "isEmpty" => current.Count == 0,
            "isNotEmpty" => current.Count > 0,
            "greaterThan" => TryNumber(single, out var a) && TryNumber(cond.Value, out var b) && a > b,
            "lessThan" => TryNumber(single, out var c1) && TryNumber(cond.Value, out var d) && c1 < d,
            _ => false,
        };
    }

    /// <summary>
    /// Validates a submission against the schema. <paramref name="raw"/> maps field keys to submitted values (one value, or
    /// several for multiselect). Unknown keys are ignored; hidden (conditional) fields are dropped.
    /// </summary>
    public static FormValidationResult ValidateSubmission(
        FormSchema schema, IReadOnlyDictionary<string, IReadOnlyList<string>> raw, IReadOnlyList<UploadedFile> files)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var l)) errors[key] = l = new List<string>();
            l.Add(message);
        }

        var values = new Dictionary<string, string>();
        var accepted = new List<AcceptedFile>();
        var consent = false;
        if (files.Count > MaxFilesPerSubmission) Add("files", $"At most {MaxFilesPerSubmission} files per submission.");

        foreach (var field in schema.AllFields)
        {
            var submitted = raw.TryGetValue(field.Key, out var list)
                ? list.Select(x => x?.Trim() ?? string.Empty).Where(x => x.Length > 0).ToList()
                : new List<string>();
            var fieldFiles = files.Where(f => f.FieldKey == field.Key).ToList();

            if (!IsVisible(field, raw, schema)) continue;

            if (field.Type == "file")
            {
                if (fieldFiles.Count == 0)
                {
                    if (field.Required) Add(field.Key, $"{field.Label} is required.");
                    continue;
                }
                if (fieldFiles.Count > 1) { Add(field.Key, "Upload one file."); continue; }
                var file = fieldFiles[0];
                var maxMb = Math.Min(field.Validation?.MaxSizeMb ?? DefaultFileMaxMb, AbsoluteFileMaxMb);
                if (file.Content.Length == 0) { Add(field.Key, "The file is empty."); continue; }
                if (file.Content.Length > maxMb * 1024L * 1024L) { Add(field.Key, $"The file must be at most {maxMb} MB."); continue; }
                var accept = field.Validation?.Accept ?? new List<string> { "pdf", "image" };
                var kind = DetectFile(file.Content);
                if (kind is null || !accept.Contains(kind.Value.Kind))
                {
                    Add(field.Key, $"Upload a {string.Join(" or ", accept.Select(a => a == "pdf" ? "PDF" : "PNG/JPEG/WebP image"))} file.");
                    continue;
                }
                accepted.Add(new AcceptedFile(field.Key, SafeFileName(file.FileName, kind.Value.Extension), kind.Value.ContentType, kind.Value.Extension, file.Content));
                values[field.Key] = SafeFileName(file.FileName, kind.Value.Extension);
                continue;
            }

            if (field.Type == "hidden")
            {
                var hiddenValue = submitted.FirstOrDefault() ?? field.DefaultValue;
                if (!string.IsNullOrEmpty(hiddenValue)) values[field.Key] = Truncate(hiddenValue, 500);
                continue;
            }

            if (submitted.Count == 0)
            {
                if (field.Type == "consent" && field.Required) Add(field.Key, "Please accept to continue.");
                else if (field.Type == "checkbox" && field.Required) Add(field.Key, $"{field.Label} must be checked.");
                else if (field.Required) Add(field.Key, $"{field.Label} is required.");
                continue;
            }
            if (field.Type != "multiselect" && submitted.Count > 1) { Add(field.Key, "Only one value is allowed."); continue; }
            var value = submitted[0];
            var v = field.Validation;

            switch (field.Type)
            {
                case "text":
                case "textarea":
                    var max = v?.MaxLength ?? (field.Type == "textarea" ? DefaultTextareaMax : DefaultTextMax);
                    if (value.Length > max) Add(field.Key, $"At most {max} characters.");
                    if (v?.MinLength is { } min && value.Length < min) Add(field.Key, $"At least {min} characters.");
                    break;
                case "email":
                    if (value.Length > 254 || !EmailRegex().IsMatch(value)) Add(field.Key, "Enter a valid email address.");
                    break;
                case "phone":
                    var digits = value.Count(char.IsAsciiDigit);
                    if (!PhoneRegex().IsMatch(value) || digits is < 7 or > 15) Add(field.Key, "Enter a valid phone number.");
                    break;
                case "number":
                    if (!TryNumber(value, out var number)) Add(field.Key, "Enter a number.");
                    else
                    {
                        if (v?.Min is { } nmin && number < nmin) Add(field.Key, $"Must be at least {nmin.ToString(CultureInfo.InvariantCulture)}.");
                        if (v?.Max is { } nmax && number > nmax) Add(field.Key, $"Must be at most {nmax.ToString(CultureInfo.InvariantCulture)}.");
                        value = number.ToString(CultureInfo.InvariantCulture);
                    }
                    break;
                case "date":
                    if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        Add(field.Key, "Enter a date (YYYY-MM-DD).");
                    break;
                case "select":
                case "radio":
                    if (field.Options is null || field.Options.All(o => o.Value != value)) Add(field.Key, "Choose one of the options.");
                    break;
                case "multiselect":
                    if (field.Options is null || submitted.Any(s => field.Options.All(o => o.Value != s))) Add(field.Key, "Choose from the listed options.");
                    else if (submitted.Distinct().Count() != submitted.Count) Add(field.Key, "Each option can be chosen once.");
                    if (v?.MinChoices is { } minC && submitted.Count < minC) Add(field.Key, $"Choose at least {minC}.");
                    if (v?.MaxChoices is { } maxC && submitted.Count > maxC) Add(field.Key, $"Choose at most {maxC}.");
                    value = string.Join(", ", submitted);
                    break;
                case "checkbox":
                case "consent":
                    if (!IsTruthy(value))
                    {
                        if (field.Required) Add(field.Key, field.Type == "consent" ? "Please accept to continue." : $"{field.Label} must be checked.");
                        continue;
                    }
                    value = "yes";
                    if (field.Type == "consent") consent = true;
                    break;
            }

            if (v?.Pattern is { } pattern && field.Type is "text" or "textarea" or "phone" or "email")
            {
                try
                {
                    if (!Regex.IsMatch(value, $"^(?:{pattern})$", RegexOptions.CultureInvariant, RegexTimeout))
                        Add(field.Key, v.PatternMessage ?? "The value has an invalid format.");
                }
                catch (RegexMatchTimeoutException)
                {
                    Add(field.Key, v.PatternMessage ?? "The value has an invalid format.");
                }
            }
            if (!errors.ContainsKey(field.Key)) values[field.Key] = value;
        }

        return new FormValidationResult(values, accepted, consent, errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }

    /// <summary>Identifies PDFs and PNG/JPEG/WebP images from their magic bytes (never from the name or MIME type).</summary>
    public static (string Kind, string ContentType, string Extension)? DetectFile(byte[] content)
    {
        if (content.Length >= 5 && content[0] == '%' && content[1] == 'P' && content[2] == 'D' && content[3] == 'F' && content[4] == '-')
            return ("pdf", "application/pdf", ".pdf");
        var image = ImageInspector.Inspect(content);
        return image is null ? null : ("image", image.ContentType, image.Extension);
    }

    /// <summary>Keeps a display name only (no path), limited characters, with the detected extension.</summary>
    public static string SafeFileName(string fileName, string extension)
    {
        var name = Path.GetFileNameWithoutExtension(fileName.Replace('\\', '/').Split('/').Last());
        name = SafeNameRegex().Replace(name, "-").Trim('-', '.');
        if (name.Length == 0) name = "upload";
        if (name.Length > 80) name = name[..80];
        return name + extension;
    }

    private static bool IsTruthy(string value) => value.ToLowerInvariant() is "true" or "yes" or "on" or "1";

    private static bool TryNumber(string? value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out number);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,39}$")]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"^[^\s@<>()\[\]\\,;:""]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^\+?[0-9 ().\-]{7,25}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex SafeNameRegex();
}
