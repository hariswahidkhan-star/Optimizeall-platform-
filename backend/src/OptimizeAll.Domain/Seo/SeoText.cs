using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.Seo;

/// <summary>Text statistics used by the on-page analyzer and the audit (pure functions).</summary>
public static partial class SeoText
{
    /// <summary>Words: runs of letters/digits (apostrophes and hyphens inside a word are kept).</summary>
    public static IReadOnlyList<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : WordRegex().Matches(text).Select(m => m.Value).ToArray();

    public static int WordCount(string? text) => Words(text).Count;

    /// <summary>Sentences: split on . ! ? and line breaks (headings and list items count as sentences); at least 1 for non-empty text.</summary>
    public static int SentenceCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var count = SentenceSplitRegex().Split(text).Count(s => WordRegex().IsMatch(s));
        return Math.Max(1, count);
    }

    /// <summary>
    /// English syllable estimate: vowel groups (a, e, i, o, u, y), minus a silent final "e" (but not "-le"), "-es"/"-ed"
    /// endings that do not form a syllable; minimum 1 per word. Accurate to about ±1 syllable per word, which is the
    /// usual tolerance of Flesch implementations.
    /// </summary>
    public static int Syllables(string word)
    {
        var w = new string(word.ToLowerInvariant().Where(char.IsAsciiLetter).ToArray());
        if (w.Length == 0) return 0;
        if (w.Length <= 3) return 1;

        if (w.EndsWith("es", StringComparison.Ordinal) && !EndsWithSibilant(w[..^2])) w = w[..^2];
        else if (w.EndsWith("ed", StringComparison.Ordinal) && w.Length > 3 && w[^3] is not ('t' or 'd')) w = w[..^2];
        else if (w.EndsWith('e') && !w.EndsWith("le", StringComparison.Ordinal) && !w.EndsWith("ee", StringComparison.Ordinal)) w = w[..^1];

        var count = 0;
        var previousVowel = false;
        foreach (var c in w)
        {
            var vowel = "aeiouy".Contains(c);
            if (vowel && !previousVowel) count++;
            previousVowel = vowel;
        }
        return Math.Max(1, count);
    }

    private static bool EndsWithSibilant(string stem) =>
        stem.EndsWith('s') || stem.EndsWith('x') || stem.EndsWith('z') || stem.EndsWith("ch", StringComparison.Ordinal) ||
        stem.EndsWith("sh", StringComparison.Ordinal) || stem.EndsWith("ce", StringComparison.Ordinal) || stem.EndsWith("ge", StringComparison.Ordinal);

    /// <summary>
    /// Flesch reading ease = 206.835 − 1.015 × (words / sentences) − 84.6 × (syllables / words). Designed for English;
    /// scores for other languages are not comparable. Null for empty text.
    /// </summary>
    public static double? FleschReadingEase(string? text)
    {
        var words = Words(text);
        if (words.Count == 0) return null;
        var sentences = SentenceCount(text);
        var syllables = words.Sum(Syllables);
        return Math.Round(206.835 - 1.015 * ((double)words.Count / sentences) - 84.6 * ((double)syllables / words.Count), 1);
    }

    public static string FleschLabel(double score) => score switch
    {
        >= 90 => "Very easy",
        >= 80 => "Easy",
        >= 70 => "Fairly easy",
        >= 60 => "Plain English",
        >= 50 => "Fairly difficult",
        >= 30 => "Difficult",
        _ => "Very difficult",
    };

    /// <summary>Lower-case, trimmed, single-spaced (keyword identity).</summary>
    public static string NormalizeKeyword(string keyword) =>
        WhitespaceRegex().Replace(keyword.Trim().ToLowerInvariant(), " ");

    /// <summary>Non-overlapping occurrences of the keyword phrase (whole words, case-insensitive).</summary>
    public static int CountPhrase(string? text, string keyword)
    {
        var phrase = NormalizeKeyword(keyword);
        if (string.IsNullOrEmpty(text) || phrase.Length == 0) return 0;
        var words = Words(text).Select(w => w.ToLowerInvariant()).ToArray();
        var target = Words(phrase).Select(w => w.ToLowerInvariant()).ToArray();
        if (target.Length == 0) return 0;
        var count = 0;
        for (var i = 0; i + target.Length <= words.Length;)
        {
            var match = true;
            for (var j = 0; j < target.Length; j++)
                if (words[i + j] != target[j]) { match = false; break; }
            if (match) { count++; i += target.Length; }
            else i++;
        }
        return count;
    }

    public static bool ContainsPhrase(string? text, string keyword) => CountPhrase(text, keyword) > 0;

    /// <summary>
    /// 64-bit SimHash over word 3-shingles (SHA-256 based feature hashes). Near-duplicate documents have a small Hamming
    /// distance between their hashes; identical normalized text yields identical hashes.
    /// </summary>
    public static ulong SimHash(string? text)
    {
        var words = Words(text).Select(w => w.ToLowerInvariant()).ToArray();
        if (words.Length == 0) return 0;
        Span<int> vector = stackalloc int[64];
        var shingleSize = Math.Min(3, words.Length);
        for (var i = 0; i + shingleSize <= words.Length; i++)
        {
            var shingle = string.Join(' ', words, i, shingleSize);
            var hash = BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(shingle)).AsSpan(0, 8));
            for (var bit = 0; bit < 64; bit++)
                vector[bit] += ((hash >> bit) & 1) == 1 ? 1 : -1;
        }
        ulong result = 0;
        for (var bit = 0; bit < 64; bit++)
            if (vector[bit] > 0) result |= 1UL << bit;
        return result;
    }

    public static int HammingDistance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"[.!?]+(?=\s|$)|\n", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}

/// <summary>
/// Registrable domain ("eTLD+1") used to keep the crawler on one site: the last two labels, or the last three when the
/// second-level label is a common public suffix ("co.uk", "com.pk", "com.au", …). IP literals and single-label hosts
/// are their own registrable domain. A compact built-in list, not the full Public Suffix List (documented limitation).
/// </summary>
public static class RegistrableDomain
{
    private static readonly HashSet<string> MultiPartSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk", "ltd.uk", "plc.uk", "net.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au",
        "co.nz", "org.nz", "net.nz",
        "com.pk", "net.pk", "org.pk", "edu.pk", "gov.pk",
        "co.ae", "ae.org", "net.ae", "org.ae", "gov.ae",
        "com.sa", "net.sa", "org.sa", "com.eg", "com.tr", "com.br", "com.mx", "com.ar", "com.cn", "com.hk", "com.sg",
        "co.in", "net.in", "org.in", "firm.in", "gen.in", "ind.in",
        "co.za", "org.za", "co.jp", "ne.jp", "or.jp", "co.kr", "or.kr", "co.id", "or.id", "com.my", "com.ph", "com.ng",
        "com.qa", "com.kw", "com.bh", "com.om", "co.il", "org.il",
        "github.io", "herokuapp.com", "vercel.app", "netlify.app", "pages.dev", "blogspot.com", "azurewebsites.net",
        "cloudfront.net", "appspot.com", "onrender.com",
    };

    public static string Of(string host)
    {
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.StartsWith('[') || System.Net.IPAddress.TryParse(host, out _)) return host;
        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length <= 2) return host;
        var lastTwo = $"{labels[^2]}.{labels[^1]}";
        return MultiPartSuffixes.Contains(lastTwo) ? $"{labels[^3]}.{lastTwo}" : lastTwo;
    }

    public static bool SameSite(string hostA, string hostB) => Of(hostA) == Of(hostB);
}
