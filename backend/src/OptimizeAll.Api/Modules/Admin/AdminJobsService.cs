using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Admin;

public sealed class AdminJobsService(
    AppDbContext db,
    IEnumerable<JobDescriptor> descriptors,
    IServiceScopeFactory scopes,
    JobRunner runner,
    IAuditLogger audit)
{
    private sealed record JobInfo(JobDescriptor Descriptor, string JobName);

    /// <summary>Resolves each registered job once (in a throwaway scope) to read its run-log name.</summary>
    private List<JobInfo> Jobs()
    {
        using var scope = scopes.CreateScope();
        return descriptors
            .GroupBy(d => d.JobType).Select(g => g.Last())
            .Select(d => new JobInfo(d, ((IJob)scope.ServiceProvider.GetRequiredService(d.JobType)).Name))
            .OrderBy(j => j.Descriptor.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<JobDto>> ListAsync(CancellationToken ct)
    {
        var result = new List<JobDto>();
        foreach (var job in Jobs())
        {
            var last = await db.Set<JobRun>().AsNoTracking().Where(r => r.JobName == job.JobName)
                .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            result.Add(new JobDto(job.Descriptor.Name, job.JobName, (int)job.Descriptor.Interval.TotalSeconds, last is null ? null : ToDto(last)));
        }
        return result;
    }

    public async Task<PagedResult<JobRunDto>> RunsAsync(JobRunQuery query, CancellationToken ct)
    {
        var q = db.Set<JobRun>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.JobName))
        {
            // Accept either the descriptor (class) name or the run-log name.
            var name = query.JobName.Trim();
            var mapped = Jobs().FirstOrDefault(j => string.Equals(j.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase))?.JobName ?? name;
            q = q.Where(r => r.JobName == mapped);
        }
        if (query.Status is { } status) q = q.Where(r => r.Status == status);
        var page = await q.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).ToPagedAsync(query, ct);
        return new PagedResult<JobRunDto>(page.Items.Select(ToDto).ToList(), page.Total, page.Page, page.PageSize);
    }

    public async Task<JobRunDto> RunNowAsync(string name, CancellationToken ct)
    {
        var job = Jobs().FirstOrDefault(j => string.Equals(j.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(j.JobName, name, StringComparison.OrdinalIgnoreCase))
                  ?? throw DomainException.NotFound("Job");

        audit.Record("admin.job_run_requested", "Job", job.Descriptor.Name);
        await db.SaveChangesAsync(ct);

        var run = await runner.RunAsync(job.Descriptor.JobType, ct)
                  ?? throw DomainException.Conflict("jobs.lease_held", "This job is already running on another instance. Try again shortly.");
        return ToDto(run);
    }

    private static JobRunDto ToDto(JobRun r) =>
        new(r.Id, r.JobName, r.RunKey, r.Status, r.Attempt, r.StartedAt, r.FinishedAt, r.Summary, r.Error, r.InstanceId);
}
