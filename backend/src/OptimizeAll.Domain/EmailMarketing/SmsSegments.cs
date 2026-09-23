namespace OptimizeAll.Domain.EmailMarketing;

public enum SmsEncoding
{
    Gsm7,
    Ucs2,
}

/// <summary>Result of SMS segmentation: encoding, billable units (septets or UTF-16 code units) and segment count.</summary>
public sealed record SmsSegmentInfo(SmsEncoding Encoding, int Units, int Segments, int PerSegment, int Remaining);

/// <summary>
/// GSM 03.38 segment counting. GSM-7 messages hold 160 septets (153 per part when concatenated, the UDH takes 7);
/// extension-table characters (e.g. € [ ] { } ~ ^ | \) take two septets. Any character outside the GSM-7 alphabet
/// switches the whole message to UCS-2: 70 UTF-16 code units (67 per part); emoji and other astral characters use two.
/// Carriers never split an escape sequence or a surrogate pair across parts, which the part packing below respects.
/// </summary>
public static class SmsSegments
{
    private const string Basic =
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

    private const string Extension = "\f^{}\\[~]|€";

    private static readonly HashSet<char> BasicSet = new(Basic);
    private static readonly HashSet<char> ExtensionSet = new(Extension);

    public static bool IsGsm7(string text) => text.All(c => BasicSet.Contains(c) || ExtensionSet.Contains(c));

    public static SmsSegmentInfo Calculate(string? text)
    {
        text ??= string.Empty;
        if (IsGsm7(text))
        {
            var units = text.Sum(c => ExtensionSet.Contains(c) ? 2 : 1);
            if (units <= 160) return new SmsSegmentInfo(SmsEncoding.Gsm7, units, units == 0 ? 0 : 1, 160, 160 - units);
            // Pack into 153-septet parts without splitting an escape pair.
            var parts = 1;
            var used = 0;
            foreach (var c in text)
            {
                var size = ExtensionSet.Contains(c) ? 2 : 1;
                if (used + size > 153) { parts++; used = 0; }
                used += size;
            }
            return new SmsSegmentInfo(SmsEncoding.Gsm7, units, parts, 153, 153 - used);
        }

        var codeUnits = text.Length;
        if (codeUnits <= 70) return new SmsSegmentInfo(SmsEncoding.Ucs2, codeUnits, 1, 70, 70 - codeUnits);
        var ucsParts = 1;
        var inPart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var size = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            if (inPart + size > 67) { ucsParts++; inPart = 0; }
            inPart += size;
            if (size == 2) i++;
        }
        return new SmsSegmentInfo(SmsEncoding.Ucs2, codeUnits, ucsParts, 67, 67 - inPart);
    }

    /// <summary>Estimated cost of sending <paramref name="text"/> to <paramref name="recipients"/> contacts.</summary>
    public static decimal EstimateCost(string? text, int recipients, decimal costPerSegment) =>
        Calculate(text).Segments * (decimal)Math.Max(0, recipients) * costPerSegment;

    /// <summary>Keywords that opt a number out (CTIA / carrier standard set).</summary>
    public static readonly IReadOnlySet<string> StopKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "STOP", "STOPALL", "UNSUBSCRIBE", "CANCEL", "END", "QUIT", "OPTOUT", "OPT-OUT", "REVOKE", "STOP ALL",
    };

    /// <summary>Keywords that opt a number back in.</summary>
    public static readonly IReadOnlySet<string> StartKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "START", "UNSTOP", "YES", "SUBSCRIBE",
    };

    public static string NormalizeKeyword(string? body) =>
        (body ?? string.Empty).Trim().Trim('.', '!', '"', '\'').Trim();

    public static bool IsStop(string? body) => StopKeywords.Contains(NormalizeKeyword(body));

    public static bool IsStart(string? body) => StartKeywords.Contains(NormalizeKeyword(body));

    /// <summary>True when the body tells the recipient how to opt out.</summary>
    public static bool HasOptOutInstruction(string? body) =>
        body is not null && body.Contains("STOP", StringComparison.OrdinalIgnoreCase);
}
