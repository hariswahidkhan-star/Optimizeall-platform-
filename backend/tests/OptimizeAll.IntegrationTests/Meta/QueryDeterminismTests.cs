using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Api.Modules.Website.Blog;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Meta;

/// <summary>
/// Skip/Take without OrderBy (EF warning 10102) returns arbitrary rows; in Development and Testing it throws, so every
/// test exercising such a query fails. These jobs used to log the warning in the background after startup.
/// </summary>
public sealed class QueryDeterminismTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Row_limiting_without_OrderBy_throws_in_Testing()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.WithDbAsync(db => db.Set<User>().AsNoTracking().Take(1).ToListAsync()));
        Assert.Contains("RowLimitingOperationWithoutOrderByWarning", ex.Message);
        await api.WithDbAsync(db => db.Set<User>().AsNoTracking().OrderBy(u => u.Id).Take(1).ToListAsync());
    }

    [Theory]
    [InlineData(typeof(SocialEvergreenJob))]
    [InlineData(typeof(SocialPublishingJob))]
    [InlineData(typeof(BlogSchedulerJob))]
    public async Task Background_jobs_use_deterministic_ordering(Type job)
    {
        var run = await api.Services.GetRequiredService<JobRunner>().RunAsync(job);
        Assert.NotNull(run);
        Assert.True(run!.Status == JobRunStatus.Succeeded, run.Error);
    }
}
