using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Seo;

namespace OptimizeAll.IntegrationTests.LandingPages;

/// <summary>Records every FormSubmitted event published in the test host.</summary>
public sealed class FormSubmittedRecorder : IEventHandler<FormSubmitted>
{
    public static ConcurrentQueue<FormSubmitted> Events { get; } = new();

    public Task HandleAsync(FormSubmitted domainEvent, CancellationToken cancellationToken)
    {
        Events.Enqueue(domainEvent);
        return Task.CompletedTask;
    }
}

/// <summary>An API host (on the ApiFactory's database) with the FormSubmitted recorder and the test remote-IP middleware.</summary>
public sealed class LandingPagesFixture : IAsyncLifetime
{
    public ApiFactory Api { get; } = new();
    public WebApplicationFactory<Program> Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Api.InitializeAsync();
        Host = Api.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddScoped<IEventHandler<FormSubmitted>, FormSubmittedRecorder>();
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
        }));
        await Host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        await Api.DisposeAsync();
    }

    public async Task<HttpClient> StaffAsync(Role role = Role.Designer) => await Host.LoginAsync(await Api.CreateUserAsync(new[] { role }));

    public HttpClient Anonymous(string ip = "203.0.113.10", string? visitorId = null, string userAgent = "Mozilla/5.0 (Windows NT 10.0) Chrome/126")
    {
        var client = Host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(TestRemoteIpStartupFilter.Header, ip);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        if (visitorId is not null) client.DefaultRequestHeaders.Add("X-Visitor-Id", visitorId);
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Host.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
