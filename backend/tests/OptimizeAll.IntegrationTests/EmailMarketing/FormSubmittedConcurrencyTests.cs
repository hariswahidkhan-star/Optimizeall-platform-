using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Events;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Seo;

namespace OptimizeAll.IntegrationTests.EmailMarketing;

public sealed class FormSubmittedConcurrencyFixture : IAsyncLifetime
{
    public ApiFactory Api { get; } = new();
    public LogCapture Logs { get; } = new();
    public WebApplicationFactory<Program> Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Api.InitializeAsync();
        Host = Api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddLogging(l => l.AddProvider(Logs))));
        _ = Host.Services;
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        await Api.DisposeAsync();
    }
}

/// <summary>The same person submitting landing-page forms several times at once becomes exactly one contact.</summary>
public sealed class FormSubmittedConcurrencyTests(FormSubmittedConcurrencyFixture fx) : IClassFixture<FormSubmittedConcurrencyFixture>
{
    [Fact]
    public async Task Concurrent_submissions_with_the_same_email_create_one_subscriber_without_errors()
    {
        var client = (await fx.Api.CreateClientAccountAsync()).Id;
        var publisher = fx.Host.Services.GetRequiredService<IEventPublisher>();
        var now = fx.Api.Clock.GetUtcNow().UtcDateTime;
        var formId = Guid.NewGuid();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => publisher.PublishAsync(new FormSubmitted(
            Guid.NewGuid(), formId, client, i % 2 == 0 ? "Same.Person@Example.com" : "same.person@example.com", $"Sam Person{i}", null,
            new Dictionary<string, string> { ["newsletter"] = "yes" }, null, null, null, now)))));

        var key = Workspace.Key(client);
        var subscribers = await fx.Api.WithDbAsync(db => db.Set<Subscriber>().AsNoTracking()
            .Where(s => s.ScopeKey == key && s.NormalizedEmail == "same.person@example.com").ToListAsync());
        var subscriber = Assert.Single(subscribers);
        Assert.Equal(ConsentStatus.Granted, subscriber.EmailConsent);
        Assert.Equal("Sam", subscriber.FirstName);
        // No failed handler and no unique-index violation on email_subscribers (EF logs failed saves as errors even when
        // the caller recovers).
        Assert.Empty(fx.Logs.AtLeast(LogLevel.Error).Where(l =>
            l.Category.EndsWith(nameof(EventPublisher), StringComparison.Ordinal) ||
            (l.Message + l.Exception).Contains("email_subscribers", StringComparison.OrdinalIgnoreCase)));
    }
}
