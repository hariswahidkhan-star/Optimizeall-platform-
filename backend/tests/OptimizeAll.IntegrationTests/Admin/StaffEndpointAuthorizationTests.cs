using System.Net;
using System.Net.Http.Json;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Admin;

/// <summary>Every staff endpoint of the accounts/social/content/notifications/support/admin modules rejects participants.</summary>
public sealed class StaffEndpointAuthorizationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly string Id = Guid.NewGuid().ToString();

    public static IEnumerable<object[]> StaffEndpoints() => new[]
    {
        // Social review
        new object[] { "GET", "/api/v1/review/social-accounts" },
        new object[] { "GET", $"/api/v1/review/social-accounts/{Id}" },
        new object[] { "POST", $"/api/v1/review/social-accounts/{Id}/decision" },
        // Content CMS
        new object[] { "GET", "/api/v1/admin/content/banners" },
        new object[] { "GET", $"/api/v1/admin/content/banners/{Id}" },
        new object[] { "POST", "/api/v1/admin/content/banners" },
        new object[] { "PUT", $"/api/v1/admin/content/banners/{Id}" },
        new object[] { "DELETE", $"/api/v1/admin/content/banners/{Id}" },
        new object[] { "POST", "/api/v1/admin/content/banners/reorder" },
        new object[] { "GET", "/api/v1/admin/content/announcements" },
        new object[] { "GET", $"/api/v1/admin/content/announcements/{Id}" },
        new object[] { "POST", "/api/v1/admin/content/announcements" },
        new object[] { "PUT", $"/api/v1/admin/content/announcements/{Id}" },
        new object[] { "DELETE", $"/api/v1/admin/content/announcements/{Id}" },
        new object[] { "GET", "/api/v1/admin/content/faqs" },
        new object[] { "GET", $"/api/v1/admin/content/faqs/{Id}" },
        new object[] { "POST", "/api/v1/admin/content/faqs" },
        new object[] { "PUT", $"/api/v1/admin/content/faqs/{Id}" },
        new object[] { "DELETE", $"/api/v1/admin/content/faqs/{Id}" },
        new object[] { "POST", "/api/v1/admin/content/faqs/reorder" },
        new object[] { "GET", "/api/v1/admin/content/onboarding-steps" },
        new object[] { "GET", $"/api/v1/admin/content/onboarding-steps/{Id}" },
        new object[] { "POST", "/api/v1/admin/content/onboarding-steps" },
        new object[] { "PUT", $"/api/v1/admin/content/onboarding-steps/{Id}" },
        new object[] { "DELETE", $"/api/v1/admin/content/onboarding-steps/{Id}" },
        new object[] { "POST", "/api/v1/admin/content/onboarding-steps/reorder" },
        // Notifications outbox
        new object[] { "GET", "/api/v1/admin/notifications/deliveries" },
        new object[] { "POST", $"/api/v1/admin/notifications/deliveries/{Id}/retry" },
        // Support
        new object[] { "GET", "/api/v1/admin/support/tickets" },
        new object[] { "GET", $"/api/v1/admin/support/tickets/{Id}" },
        new object[] { "POST", $"/api/v1/admin/support/tickets/{Id}/messages" },
        new object[] { "PUT", $"/api/v1/admin/support/tickets/{Id}" },
        // Users
        new object[] { "GET", "/api/v1/admin/users" },
        new object[] { "GET", "/api/v1/admin/users/export.csv" },
        new object[] { "GET", $"/api/v1/admin/users/{Id}" },
        new object[] { "POST", $"/api/v1/admin/users/{Id}/suspend" },
        new object[] { "POST", $"/api/v1/admin/users/{Id}/reactivate" },
        new object[] { "PUT", $"/api/v1/admin/users/{Id}/roles" },
        new object[] { "PUT", $"/api/v1/admin/users/{Id}/tier" },
        new object[] { "POST", "/api/v1/admin/users/staff" },
        // Settings, audit, jobs
        new object[] { "GET", "/api/v1/admin/settings" },
        new object[] { "PUT", "/api/v1/admin/settings/eligibility.minAccountAgeDays" },
        new object[] { "GET", "/api/v1/admin/audit-logs" },
        new object[] { "GET", "/api/v1/admin/audit-logs/export.csv" },
        new object[] { "GET", "/api/v1/admin/jobs" },
        new object[] { "GET", "/api/v1/admin/jobs/runs" },
        new object[] { "POST", "/api/v1/admin/jobs/NotificationDispatchJob/run" },
    };

    private HttpClient? _participant;

    private async Task<HttpClient> ParticipantAsync() => _participant ??= (await api.CreateClientAsync(Role.Participant)).Client;

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), path)
    {
        // A syntactically valid body so the request reaches authorization rather than failing on content type.
        Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null,
    };

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task Participants_get_403(string method, string path)
    {
        var client = await ParticipantAsync();
        var response = await client.SendAsync(Request(method, path));
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"{method} {path} returned {(int)response.StatusCode} for a participant: {await response.Content.ReadAsStringAsync()}");
    }

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task Anonymous_callers_get_401(string method, string path)
    {
        var response = await api.CreateClient().SendAsync(Request(method, path));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
