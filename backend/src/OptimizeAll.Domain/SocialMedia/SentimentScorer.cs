using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.SocialMedia;

public sealed record SentimentEstimate(Sentiment Sentiment, decimal Score, int MatchedTerms);

/// <summary>
/// A small lexicon-based sentiment scorer for mentions and inbox messages. It is an <b>automatic estimate</b> (always
/// labelled as such): it counts positive and negative terms, flips a term after a negator ("not good"), weights
/// intensifiers ("very") and emoji, and returns a score in [-1, 1]. Sarcasm, context and other languages are not
/// understood; people can always override it with a manual tag.
/// </summary>
public static partial class SentimentScorer
{
    public const decimal Threshold = 0.2m;

    private static readonly Dictionary<string, int> Lexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        // positive
        ["love"] = 3, ["loved"] = 3, ["loving"] = 3, ["amazing"] = 3, ["awesome"] = 3, ["excellent"] = 3, ["fantastic"] = 3,
        ["perfect"] = 3, ["wonderful"] = 3, ["best"] = 3, ["great"] = 2, ["good"] = 2, ["nice"] = 2, ["happy"] = 2,
        ["recommend"] = 2, ["recommended"] = 2, ["thanks"] = 1, ["thank"] = 1, ["helpful"] = 2, ["delicious"] = 3,
        ["beautiful"] = 2, ["easy"] = 1, ["fast"] = 1, ["glad"] = 2, ["enjoy"] = 2, ["enjoyed"] = 2, ["impressed"] = 2,
        ["friendly"] = 2, ["fresh"] = 1, ["worth"] = 1, ["favourite"] = 2, ["favorite"] = 2, ["brilliant"] = 3, ["cool"] = 1,
        // negative
        ["hate"] = -3, ["hated"] = -3, ["terrible"] = -3, ["awful"] = -3, ["worst"] = -3, ["horrible"] = -3, ["disgusting"] = -3,
        ["scam"] = -3, ["bad"] = -2, ["poor"] = -2, ["slow"] = -1, ["broken"] = -2, ["refund"] = -1, ["disappointed"] = -2,
        ["disappointing"] = -2, ["rude"] = -2, ["cold"] = -1, ["expensive"] = -1, ["overpriced"] = -2, ["late"] = -1,
        ["never"] = -1, ["problem"] = -1, ["issue"] = -1, ["bug"] = -1, ["crash"] = -2, ["crashes"] = -2, ["waste"] = -2,
        ["angry"] = -2, ["useless"] = -3, ["annoying"] = -2, ["cancel"] = -1, ["cancelled"] = -1, ["dirty"] = -2,
    };

    private static readonly HashSet<string> Negators = new(StringComparer.OrdinalIgnoreCase)
    {
        "not", "no", "never", "isn't", "wasn't", "don't", "doesn't", "didn't", "can't", "won't", "hardly", "without",
    };

    private static readonly HashSet<string> Intensifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "very", "really", "so", "super", "extremely", "totally", "absolutely",
    };

    private static readonly Dictionary<string, int> Emoji = new()
    {
        ["😍"] = 3, ["❤️"] = 3, ["❤"] = 3, ["😊"] = 2, ["👍"] = 2, ["🙌"] = 2, ["🔥"] = 2, ["😀"] = 2, ["😂"] = 1,
        ["😡"] = -3, ["😠"] = -3, ["👎"] = -2, ["😞"] = -2, ["😢"] = -2, ["🤮"] = -3,
    };

    public static SentimentEstimate Score(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new SentimentEstimate(Sentiment.Neutral, 0m, 0);

        var tokens = WordRegex().Matches(text).Select(m => m.Value).ToList();
        var sum = 0m;
        var matched = 0;
        var maxAbs = 0m;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!Lexicon.TryGetValue(tokens[i], out var weight)) continue;
            // "never" is only negative on its own; as a negator it is handled below.
            if (Negators.Contains(tokens[i]) && i + 1 < tokens.Count && Lexicon.ContainsKey(tokens[i + 1])) continue;
            decimal value = weight;
            var window = tokens.Skip(Math.Max(0, i - 3)).Take(i - Math.Max(0, i - 3)).ToList();
            if (window.Any(w => Negators.Contains(w))) value = -value * 0.75m;
            if (i > 0 && Intensifiers.Contains(tokens[i - 1])) value *= 1.5m;
            sum += value;
            maxAbs += Math.Abs(value);
            matched++;
        }
        foreach (var (emoji, weight) in Emoji)
        {
            var count = CountOccurrences(text, emoji);
            if (count == 0) continue;
            sum += weight * count;
            maxAbs += Math.Abs(weight) * count;
            matched += count;
        }
        if (matched == 0 || maxAbs == 0) return new SentimentEstimate(Sentiment.Neutral, 0m, 0);

        var score = Math.Round(Math.Clamp(sum / maxAbs, -1m, 1m), 3);
        var label = score >= Threshold ? Sentiment.Positive : score <= -Threshold ? Sentiment.Negative : Sentiment.Neutral;
        return new SentimentEstimate(label, score, matched);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    [GeneratedRegex(@"[\p{L}']+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
