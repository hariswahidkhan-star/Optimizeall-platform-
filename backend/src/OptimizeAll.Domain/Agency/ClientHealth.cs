namespace OptimizeAll.Domain.Agency;

public enum HealthLevel
{
    Green,
    Amber,
    Red,
}

/// <summary>One reason contributing to a client's health (e.g. "4 overdue tasks").</summary>
public sealed record HealthReason(string Code, HealthLevel Level, string Message, int Penalty);

/// <summary>Raw signals gathered for a client (all counts as of "now").</summary>
public sealed record HealthSignals(
    int OverdueTasks,
    int PendingApprovals,
    double OldestPendingApprovalDays,
    double? DaysSinceLastActivity,
    double? AverageCsat,
    int CsatResponses,
    int? LatestNps,
    IReadOnlyList<HealthReason> External);

public sealed record HealthResult(int Score, HealthLevel Level, IReadOnlyList<HealthReason> Reasons);

/// <summary>
/// Client health (pure): start at 100 and subtract a penalty per reason. Red when any reason is red or the score is
/// below 50; amber when any reason is amber or the score is below 75; otherwise green.
/// </summary>
public static class ClientHealthCalculator
{
    public static HealthResult Compute(HealthSignals s)
    {
        var reasons = new List<HealthReason>();

        if (s.OverdueTasks >= 5)
            reasons.Add(new("overdue_tasks", HealthLevel.Red, $"{s.OverdueTasks} overdue tasks", 30));
        else if (s.OverdueTasks > 0)
            reasons.Add(new("overdue_tasks", HealthLevel.Amber, $"{s.OverdueTasks} overdue task{(s.OverdueTasks == 1 ? "" : "s")}", 10 + 3 * s.OverdueTasks));

        if (s.PendingApprovals > 0 && s.OldestPendingApprovalDays >= 7)
            reasons.Add(new("approvals_stale", HealthLevel.Red,
                $"{s.PendingApprovals} deliverable{(s.PendingApprovals == 1 ? "" : "s")} waiting on the client, oldest {Math.Floor(s.OldestPendingApprovalDays)} days", 25));
        else if (s.PendingApprovals > 0 && s.OldestPendingApprovalDays >= 3)
            reasons.Add(new("approvals_waiting", HealthLevel.Amber,
                $"{s.PendingApprovals} deliverable{(s.PendingApprovals == 1 ? "" : "s")} waiting on the client, oldest {Math.Floor(s.OldestPendingApprovalDays)} days", 10));

        if (s.DaysSinceLastActivity is null)
            reasons.Add(new("no_activity", HealthLevel.Amber, "No delivery activity recorded yet", 10));
        else if (s.DaysSinceLastActivity >= 30)
            reasons.Add(new("inactive", HealthLevel.Red, $"No activity for {Math.Floor(s.DaysSinceLastActivity.Value)} days", 25));
        else if (s.DaysSinceLastActivity >= 14)
            reasons.Add(new("quiet", HealthLevel.Amber, $"No activity for {Math.Floor(s.DaysSinceLastActivity.Value)} days", 10));

        if (s.CsatResponses > 0 && s.AverageCsat is { } csat)
        {
            if (csat < 2.5)
                reasons.Add(new("csat_low", HealthLevel.Red, $"Average CSAT {csat:0.0}/5 over the last 90 days", 25));
            else if (csat < 3.5)
                reasons.Add(new("csat_mixed", HealthLevel.Amber, $"Average CSAT {csat:0.0}/5 over the last 90 days", 10));
        }

        if (s.LatestNps is { } nps)
        {
            if (nps <= 4)
                reasons.Add(new("nps_detractor", HealthLevel.Red, $"Latest NPS response is {nps}/10 (detractor)", 20));
            else if (nps <= 6)
                reasons.Add(new("nps_detractor", HealthLevel.Amber, $"Latest NPS response is {nps}/10 (detractor)", 10));
        }

        reasons.AddRange(s.External);

        var score = Math.Clamp(100 - reasons.Sum(r => r.Penalty), 0, 100);
        var level = reasons.Any(r => r.Level == HealthLevel.Red) || score < 50 ? HealthLevel.Red
            : reasons.Any(r => r.Level == HealthLevel.Amber) || score < 75 ? HealthLevel.Amber
            : HealthLevel.Green;
        return new HealthResult(score, level, reasons.OrderByDescending(r => r.Level).ThenByDescending(r => r.Penalty).ToList());
    }
}
