namespace OptimizeAll.Domain.Learning;

/// <summary>A question drawn for an attempt: which pool question, and the order its options are shown in.</summary>
/// <param name="QuestionId">Pool question id.</param>
/// <param name="OptionOrder">Shown position → original option index (e.g. [2,0,3,1] shows option 2 first).</param>
public sealed record DrawnQuestion(string QuestionId, int[] OptionOrder);

public sealed record GradedQuestion(string QuestionId, int[] Selected, bool IsCorrect);

public sealed record ExamGrade(int CorrectCount, int QuestionCount, int Score, bool Passed, IReadOnlyList<GradedQuestion> Questions);

/// <summary>
/// Pure final-exam rules: the randomised draw, grading, the server-side timer and the daily attempt budget. The API only
/// persists and exposes what these functions decide (unit-tested in isolation).
/// </summary>
public static class ExamEngine
{
    /// <summary>Submissions are accepted this long after the deadline (network latency / the final autosave).</summary>
    public static readonly TimeSpan SubmitGrace = TimeSpan.FromSeconds(30);

    /// <summary>The rolling window of <see cref="PackExam.MaxAttemptsPerDay"/>.</summary>
    public static readonly TimeSpan AttemptWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Draws <paramref name="count"/> distinct questions at random from the pool with every question's options shuffled.
    /// When <paramref name="count"/> is at least the number of modules, every module the pool covers gets at least one
    /// question (one random question per module first, the rest at random from the remainder); the result is shuffled.
    /// </summary>
    public static IReadOnlyList<DrawnQuestion> Draw(IReadOnlyList<PackQuestion> pool, int count, Random random)
    {
        if (pool.Count == 0) throw new ArgumentException("The pool is empty.", nameof(pool));
        count = Math.Clamp(count, 1, pool.Count);
        var remaining = pool.ToList();
        var chosen = new List<PackQuestion>(count);

        var modules = pool.Select(q => q.Module).Distinct(StringComparer.Ordinal).ToList();
        if (count >= modules.Count)
        {
            foreach (var module in modules)
            {
                var candidates = remaining.Where(q => q.Module == module).ToList();
                var pick = candidates[random.Next(candidates.Count)];
                chosen.Add(pick);
                remaining.Remove(pick);
            }
        }
        while (chosen.Count < count)
        {
            var index = random.Next(remaining.Count);
            chosen.Add(remaining[index]);
            remaining.RemoveAt(index);
        }
        Shuffle(chosen, random);
        return chosen.Select(q =>
        {
            var order = Enumerable.Range(0, q.Options?.Count ?? 0).ToArray();
            Shuffle(order, random);
            return new DrawnQuestion(q.Id, order);
        }).ToList();
    }

    private static void Shuffle<T>(IList<T> items, Random random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>Maps the shown positions a learner selected to original option indices (null when a position is invalid).</summary>
    public static int[]? ToOriginal(DrawnQuestion drawn, IReadOnlyCollection<int> shownPositions)
    {
        if (shownPositions.Any(p => p < 0 || p >= drawn.OptionOrder.Length)) return null;
        if (shownPositions.Distinct().Count() != shownPositions.Count) return null;
        return shownPositions.Select(p => drawn.OptionOrder[p]).OrderBy(i => i).ToArray();
    }

    /// <summary>Maps original option indices back to the positions shown in this attempt.</summary>
    public static int[] ToShown(DrawnQuestion drawn, IEnumerable<int> originalIndices) =>
        originalIndices.Select(i => Array.IndexOf(drawn.OptionOrder, i)).Where(p => p >= 0).OrderBy(p => p).ToArray();

    /// <summary>
    /// Grades an attempt: a question is correct only when the selected set equals the correct set exactly (no partial
    /// credit for "choose all that apply"). Unanswered questions are wrong. Score = floor(correct × 100 / questions).
    /// </summary>
    public static ExamGrade Grade(
        IReadOnlyList<DrawnQuestion> drawn,
        IReadOnlyDictionary<string, int[]> answersOriginal,
        IReadOnlyDictionary<string, PackQuestion> pool,
        int passingScore)
    {
        var graded = new List<GradedQuestion>(drawn.Count);
        foreach (var d in drawn)
        {
            var selected = answersOriginal.TryGetValue(d.QuestionId, out var s) ? s.Distinct().OrderBy(i => i).ToArray() : Array.Empty<int>();
            var correct = pool.TryGetValue(d.QuestionId, out var q) ? (q.Correct ?? new List<int>()).Distinct().OrderBy(i => i).ToArray() : Array.Empty<int>();
            graded.Add(new GradedQuestion(d.QuestionId, selected, correct.Length > 0 && selected.SequenceEqual(correct)));
        }
        var correctCount = graded.Count(g => g.IsCorrect);
        var score = drawn.Count == 0 ? 0 : correctCount * 100 / drawn.Count;
        return new ExamGrade(correctCount, drawn.Count, score, score >= passingScore, graded);
    }

    public static DateTime Deadline(DateTime startedAt, int timeLimitMinutes) => startedAt.AddMinutes(timeLimitMinutes);

    /// <summary>Whether answers may still be saved/submitted (deadline plus <see cref="SubmitGrace"/>).</summary>
    public static bool AcceptsAnswers(DateTime nowUtc, DateTime deadlineUtc) => nowUtc <= deadlineUtc + SubmitGrace;

    /// <summary>Seconds left on the clock (never negative).</summary>
    public static int SecondsRemaining(DateTime nowUtc, DateTime deadlineUtc) =>
        (int)Math.Max(0, Math.Ceiling((deadlineUtc - nowUtc).TotalSeconds));

    /// <summary>
    /// Attempts left in the rolling 24-hour window, and when the next one frees up when none are left.
    /// </summary>
    public static (int Remaining, DateTime? NextAttemptAt) AttemptBudget(IEnumerable<DateTime> attemptStarts, DateTime nowUtc, int maxPerDay)
    {
        var inWindow = attemptStarts.Where(t => t > nowUtc - AttemptWindow).OrderBy(t => t).ToList();
        var remaining = Math.Max(0, maxPerDay - inWindow.Count);
        DateTime? next = remaining > 0 || inWindow.Count == 0 ? null : inWindow[inWindow.Count - maxPerDay] + AttemptWindow;
        return (remaining, next);
    }
}
