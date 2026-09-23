using OptimizeAll.Domain.Events;

namespace OptimizeAll.Domain.Crm;

/// <summary>Attributes of a contact (and its company) that fit rules evaluate, plus engagement counts per event type.</summary>
public sealed record ScoringFacts(
    string? Industry,
    string? CompanySize,
    string? BudgetRange,
    string? CountryCode,
    string? Source,
    string? LifecycleStage,
    IReadOnlyDictionary<string, int> EngagementCounts);

public sealed record ScoreLine(string Rule, ScoringCategory Category, int Points);

public sealed record ScoreResult(int Score, IReadOnlyList<ScoreLine> Lines);

/// <summary>Rule-based lead scoring (pure). The score is recomputed from scratch whenever a contact or its engagement changes.</summary>
public static class LeadScoring
{
    public const int MaxScore = 1000;

    public static readonly IReadOnlyList<string> FitFields = new[] { "industry", "companySize", "budgetRange", "country", "source", "lifecycleStage" };

    /// <summary>Engagement event types recorded today (other modules may add more through <see cref="ContactEngagementRecorded"/>).</summary>
    public static readonly IReadOnlyList<string> KnownEngagementTypes = new[]
    {
        "website_inquiry", "form_submitted", "email_opened", "email_clicked", "proposal_viewed", "meeting_booked", "newsletter_subscribed",
    };

    public static ScoreResult Evaluate(IEnumerable<LeadScoringRule> rules, ScoringFacts facts)
    {
        var lines = new List<ScoreLine>();
        foreach (var rule in rules.Where(r => r.IsActive))
        {
            if (rule.Category == ScoringCategory.Fit)
            {
                var value = FitValue(rule.Field, facts);
                if (Matches(rule.MatchValue, value)) lines.Add(new ScoreLine(rule.Name, rule.Category, rule.Points));
            }
            else if (facts.EngagementCounts.TryGetValue(rule.Field, out var count) && count > 0)
            {
                var times = rule.MaxOccurrences is { } max ? Math.Min(count, Math.Max(0, max)) : count;
                if (times > 0) lines.Add(new ScoreLine(rule.Name, rule.Category, rule.Points * times));
            }
        }
        var score = Math.Clamp(lines.Sum(l => l.Points), 0, MaxScore);
        return new ScoreResult(score, lines);
    }

    public static string? FitValue(string field, ScoringFacts facts) => field switch
    {
        "industry" => facts.Industry,
        "companySize" => facts.CompanySize,
        "budgetRange" => facts.BudgetRange,
        "country" => facts.CountryCode,
        "source" => facts.Source,
        "lifecycleStage" => facts.LifecycleStage,
        _ => null,
    };

    /// <summary>Case-insensitive match against a comma-separated list; "*" matches any non-empty value.</summary>
    public static bool Matches(string? pattern, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(pattern)) return false;
        var v = value.Trim();
        return pattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(p => p == "*" || string.Equals(p, v, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsValidField(ScoringCategory category, string field) =>
        category == ScoringCategory.Fit
            ? FitFields.Contains(field)
            : field.Length is > 0 and <= 60 && field.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '.');
}

/// <summary>
/// An engagement signal for a CRM contact, published by any module (email marketing clicks, landing pages, meeting booking).
/// The CRM module records it once per <see cref="SourceKey"/> and recomputes the contact's lead score.
/// </summary>
public sealed record ContactEngagementRecorded(string Email, string Type, string SourceKey, DateTime OccurredAt) : IDomainEvent;
