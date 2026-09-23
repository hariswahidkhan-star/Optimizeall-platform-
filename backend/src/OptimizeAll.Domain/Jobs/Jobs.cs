using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Jobs;

public enum JobRunStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// Execution log of a background job. <see cref="RunKey"/> identifies the logical unit of work
/// (e.g. "payout-prepare:2026-09-20"); a Succeeded row for a key means retries are no-ops.
/// </summary>
public class JobRun : Entity
{
    public string JobName { get; set; } = string.Empty;
    public string RunKey { get; set; } = string.Empty;
    public JobRunStatus Status { get; set; }
    public int Attempt { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Summary { get; set; }
    public string? Error { get; set; }
    public string? InstanceId { get; set; }
}

/// <summary>Distributed lease so only one API instance runs a given job at a time.</summary>
public class JobLease
{
    public string Name { get; set; } = string.Empty;
    public string Holder { get; set; } = string.Empty;
    public DateTime LeasedUntil { get; set; }
}
