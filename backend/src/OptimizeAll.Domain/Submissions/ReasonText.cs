namespace OptimizeAll.Domain.Submissions;

/// <summary>Length limits for review reasons and safe truncation of composed reason strings.</summary>
public static class ReasonText
{
    /// <summary>Maximum length of a reviewer-entered reason or note (request DTOs).</summary>
    public const int MaxInputLength = 900;

    /// <summary>Minimum length of a required reason after trimming.</summary>
    public const int MinLength = 5;

    /// <summary>Column size of SubmissionEvent.Reason, Submission.DecisionReason, AuditLog.Reason and EarningEntry.Reason.</summary>
    public const int MaxStoredLength = 1000;

    /// <summary>Column size of Appeal.ResolutionNote.</summary>
    public const int MaxResolutionNoteLength = 2000;

    private const char Ellipsis = '…';

    /// <summary>
    /// Returns <paramref name="text"/> unchanged when it fits in <paramref name="max"/> characters (UTF-16 code units,
    /// which never exceed MySQL's character count); otherwise cuts it and appends an ellipsis, never splitting a
    /// surrogate pair. Null stays null.
    /// </summary>
    public static string? Fit(string? text, int max = MaxStoredLength)
    {
        if (text is null || text.Length <= max) return text;
        if (max < 1) return string.Empty;
        var cut = max - 1;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut].TrimEnd() + Ellipsis;
    }
}
