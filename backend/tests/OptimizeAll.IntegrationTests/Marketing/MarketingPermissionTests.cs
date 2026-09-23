using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Marketing;

public sealed class MarketingPermissionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly string Id = Guid.NewGuid().ToString();

    /// <summary>Every staff marketing/analytics endpoint (method, path).</summary>
    public static TheoryData<string, string> StaffEndpoints() => new()
    {
        { "GET", "/api/v1/marketing/referrals" },
        { "POST", $"/api/v1/marketing/referrals/{Id}/reject" },
        { "GET", "/api/v1/marketing/invitations" },
        { "POST", "/api/v1/marketing/invitations" },
        { "GET", $"/api/v1/marketing/invitations/{Id}" },
        { "PUT", $"/api/v1/marketing/invitations/{Id}" },
        { "DELETE", $"/api/v1/marketing/invitations/{Id}" },
        { "GET", "/api/v1/marketing/tracking/summary" },
        { "GET", "/api/v1/marketing/experiments" },
        { "POST", "/api/v1/marketing/experiments" },
        { "GET", $"/api/v1/marketing/experiments/{Id}" },
        { "PUT", $"/api/v1/marketing/experiments/{Id}" },
        { "DELETE", $"/api/v1/marketing/experiments/{Id}" },
        { "POST", $"/api/v1/marketing/experiments/{Id}/start" },
        { "POST", $"/api/v1/marketing/experiments/{Id}/pause" },
        { "POST", $"/api/v1/marketing/experiments/{Id}/resume" },
        { "POST", $"/api/v1/marketing/experiments/{Id}/complete" },
        { "GET", $"/api/v1/marketing/experiments/{Id}/results" },
        { "GET", "/api/v1/marketing/templates" },
        { "POST", "/api/v1/marketing/templates" },
        { "GET", $"/api/v1/marketing/templates/{Id}" },
        { "PUT", $"/api/v1/marketing/templates/{Id}" },
        { "DELETE", $"/api/v1/marketing/templates/{Id}" },
        { "GET", "/api/v1/marketing/calendar" },
        { "POST", "/api/v1/marketing/calendar" },
        { "GET", $"/api/v1/marketing/calendar/{Id}" },
        { "PUT", $"/api/v1/marketing/calendar/{Id}" },
        { "DELETE", $"/api/v1/marketing/calendar/{Id}" },
        { "GET", "/api/v1/marketing/achievements" },
        { "POST", "/api/v1/marketing/achievements" },
        { "GET", $"/api/v1/marketing/achievements/{Id}" },
        { "PUT", $"/api/v1/marketing/achievements/{Id}" },
        { "DELETE", $"/api/v1/marketing/achievements/{Id}" },
        { "GET", "/api/v1/marketing/retention/summary" },
        { "GET", "/api/v1/marketing/retention/log" },
        { "GET", "/api/v1/analytics/overview" },
        { "GET", $"/api/v1/analytics/campaigns/{Id}" },
        { "GET", "/api/v1/analytics/overview/export.csv" },
    };

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), path)
    {
        Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null,
    };

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task Participants_are_forbidden(string method, string path)
    {
        var (_, participant) = await api.CreateClientAsync(Role.Participant);
        Assert.Equal(HttpStatusCode.Forbidden, (await participant.SendAsync(Request(method, path))).StatusCode);
    }

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task Anonymous_callers_are_unauthorized(string method, string path)
    {
        var anonymous = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(method, path))).StatusCode);
    }

    [Theory]
    [InlineData(Role.Reviewer, "/api/v1/marketing/templates", HttpStatusCode.Forbidden)]
    [InlineData(Role.Reviewer, "/api/v1/analytics/overview", HttpStatusCode.Forbidden)]
    [InlineData(Role.Finance, "/api/v1/marketing/templates", HttpStatusCode.Forbidden)]
    [InlineData(Role.Finance, "/api/v1/analytics/overview", HttpStatusCode.OK)]
    [InlineData(Role.CampaignManager, "/api/v1/marketing/templates", HttpStatusCode.OK)]
    [InlineData(Role.CampaignManager, "/api/v1/analytics/overview", HttpStatusCode.OK)]
    [InlineData(Role.Admin, "/api/v1/marketing/retention/summary", HttpStatusCode.OK)]
    public async Task Staff_roles_follow_the_permission_map(Role role, string path, HttpStatusCode expected)
    {
        var (_, client) = await api.CreateClientAsync(role);
        Assert.Equal(expected, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v1/me/referrals")]
    [InlineData("GET", "/api/v1/me/achievements")]
    [InlineData("GET", "/api/v1/me/tracking-links")]
    public async Task Participant_endpoints_require_the_participant_portal(string method, string path)
    {
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.SendAsync(Request(method, path))).StatusCode);
        var (_, participant) = await api.CreateClientAsync(Role.Participant);
        Assert.Equal(HttpStatusCode.OK, (await participant.SendAsync(Request(method, path))).StatusCode);
    }
}
