using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.EmailMarketing;

/// <summary>Validation and normalization of contact data (email, phone, tags, custom fields).</summary>
public static partial class ContactRules
{
    public const int MaxTags = 50;
    public const int MaxTagLength = 50;
    public const int MaxCustomFields = 40;
    public const int MaxCustomValueLength = 500;

    /// <summary>Reserved names that cannot be used as custom field keys (they are built-in fields).</summary>
    public static readonly IReadOnlySet<string> BuiltInFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "email", "phone", "first_name", "last_name", "full_name", "language", "country", "time_zone", "source", "status", "tags",
    };

    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var trimmed = email.Trim();
        return IsValidEmail(trimmed) ? trimmed.ToLowerInvariant() : null;
    }

    /// <summary>Pragmatic address syntax check (RFC 5321 lengths, one @, dotted domain with a TLD, no spaces or quotes).</summary>
    public static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= 254 && EmailRegex().IsMatch(email) &&
        email.IndexOf('@') is > 0 and <= 64;

    /// <summary>Normalizes a phone number to E.164 (+ then 8–15 digits). Spaces, dashes, dots and brackets are ignored; "00" prefix becomes "+".</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var cleaned = PhoneNoiseRegex().Replace(phone.Trim(), string.Empty);
        if (cleaned.StartsWith("00", StringComparison.Ordinal)) cleaned = "+" + cleaned[2..];
        return E164Regex().IsMatch(cleaned) ? cleaned : null;
    }

    public static bool IsE164(string? phone) => phone is not null && E164Regex().IsMatch(phone);

    public static string? NormalizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim().ToLowerInvariant();
        return t.Length <= MaxTagLength && TagRegex().IsMatch(t) ? t : null;
    }

    public static bool IsValidFieldKey(string? key) =>
        key is not null && FieldKeyRegex().IsMatch(key) && !BuiltInFields.Contains(key);

    public static bool IsValidCountry(string? code) => code is { Length: 2 } && code.All(char.IsAsciiLetterUpper);

    public static bool IsValidLanguage(string? code) => code is not null && LanguageRegex().IsMatch(code);

    /// <summary>
    /// Validates a JSON object of custom fields: keys match <c>^[a-z][a-z0-9_]{0,39}$</c>, values are strings, numbers,
    /// booleans or null (null removes the field); at most <see cref="MaxCustomFields"/> entries. Returns normalized text
    /// values (dates stay as given; numbers invariant; booleans "true"/"false").
    /// </summary>
    public static Dictionary<string, string?> ParseCustomFields(JsonElement? json, List<string> errors)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (json is null || json.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return result;
        if (json.Value.ValueKind != JsonValueKind.Object)
        {
            errors.Add("customFields must be a JSON object.");
            return result;
        }
        foreach (var property in json.Value.EnumerateObject())
        {
            if (!IsValidFieldKey(property.Name))
            {
                errors.Add($"Custom field key '{Truncate(property.Name, 40)}' is invalid (lower-case letters, digits and _; max 40; not a built-in field).");
                continue;
            }
            string? value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => "\u0000invalid",
            };
            if (value == "\u0000invalid")
            {
                errors.Add($"Custom field '{property.Name}' must be a string, number, boolean or null.");
                continue;
            }
            if (value is { Length: > MaxCustomValueLength })
            {
                errors.Add($"Custom field '{property.Name}' is longer than {MaxCustomValueLength} characters.");
                continue;
            }
            result[property.Name] = value?.Trim();
        }
        if (result.Count > MaxCustomFields) errors.Add($"At most {MaxCustomFields} custom fields are allowed.");
        return result;
    }

    /// <summary>Parses a date custom field value (yyyy-MM-dd or --MM-dd).</summary>
    public static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    [GeneratedRegex(@"^[A-Za-z0-9.!#$%&*+/=?^_`{|}~-]+(\.[A-Za-z0-9!#$%&*+/=?^_`{|}~-]+)*@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*\.[A-Za-z]{2,24}$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"[\s\-.()]")]
    private static partial Regex PhoneNoiseRegex();

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex E164Regex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9 _:\-]*$")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,39}$")]
    private static partial Regex FieldKeyRegex();

    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Za-z0-9]{2,8})?$")]
    private static partial Regex LanguageRegex();
}
