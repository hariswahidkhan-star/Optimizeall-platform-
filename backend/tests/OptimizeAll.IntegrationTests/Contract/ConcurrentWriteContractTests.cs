using System.Text;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>
/// Simultaneous writes to one record (a child row insert plus a parent update in one transaction) can deadlock on InnoDB;
/// the losing transaction is rolled back and the request must answer a retryable 409 problem, never a 500. Found by the
/// hostile-input matrix on MySQL (careers application notes, subscriber consent).
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class ConcurrentWriteContractTests(ContractFixture fx)
{
    [Fact]
    public async Task Simultaneous_writes_to_one_record_succeed_or_answer_409_never_500()
    {
        var (application, subscriber) = await fx.WithDbAsync(async db => (
            await db.Set<JobApplication>().OrderBy(a => a.Id).Select(a => a.Id).FirstAsync(),
            await db.Set<Subscriber>().OrderBy(s => s.Id).Select(s => s.Id).FirstAsync()));

        var requests = Enumerable.Range(0, 12).SelectMany(i => new Func<HttpRequestMessage>[]
        {
            () => new HttpRequestMessage(HttpMethod.Post, $"/api/v1/agency/website/careers/applications/{application}/notes")
            {
                Content = new StringContent($$"""{"body":"Concurrent note {{i}}"}""", Encoding.UTF8, "application/json"),
            },
            () => new HttpRequestMessage(HttpMethod.Post, $"/api/v1/agency/email/subscribers/{subscriber}/consent")
            {
                Content = new StringContent($$"""{"channel":"Email","status":"{{(i % 2 == 0 ? "Withdrawn" : "Unknown")}}","source":"contract {{i}}"}""",
                    Encoding.UTF8, "application/json"),
            },
        }).ToList();

        var outcomes = await Task.WhenAll(requests.Select(async build => await Outcome.ReadAsync(await fx.SendAsync(fx.Admin, build))));
        var unexpected = outcomes.Where(o => !(o.IsSuccess || o.Status == 409 && o.Code == "concurrency.conflict" || o.Status == 400))
            .Select(o => o.Short).ToList();
        Assert.True(unexpected.Count == 0, "unexpected answers to simultaneous writes:\n" + string.Join('\n', unexpected));
        Assert.Contains(outcomes, o => o.IsSuccess);
    }
}
