using System.Net;
using System.Text;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Billing;

/// <summary>Permission matrix of the CRM, proposal, contract and billing APIs (403 = the role lacks the permission).</summary>
public sealed class CrmBillingPermissionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly string Id = Guid.NewGuid().ToString();

    /// <summary>(method, path, roles allowed past authorization).</summary>
    private static readonly (string Method, string Path, Role[] Allowed)[] Matrix =
    {
        ("GET", "/api/v1/agency/crm/dashboard", new[] { Role.Admin, Role.AccountManager, Role.SalesRep, Role.Strategist }),
        ("GET", "/api/v1/agency/crm/contacts", new[] { Role.Admin, Role.AccountManager, Role.SalesRep, Role.Strategist }),
        ("POST", "/api/v1/agency/crm/deals", new[] { Role.Admin, Role.AccountManager, Role.SalesRep }),
        ("PUT", "/api/v1/agency/crm/stages", new[] { Role.Admin, Role.AccountManager, Role.SalesRep }),
        ("POST", "/api/v1/agency/crm/scoring/rules", new[] { Role.Admin, Role.AccountManager, Role.SalesRep }),
        ("GET", "/api/v1/agency/proposals", new[] { Role.Admin, Role.AccountManager, Role.SalesRep }),
        ("POST", "/api/v1/agency/proposals/preview", new[] { Role.Admin, Role.AccountManager, Role.SalesRep }),
        ("GET", "/api/v1/agency/contracts", new[] { Role.Admin, Role.AccountManager }),
        ("POST", "/api/v1/agency/contracts", new[] { Role.Admin, Role.AccountManager }),
        ("GET", "/api/v1/agency/billing/invoices", new[] { Role.Admin, Role.AccountManager, Role.SalesRep }),
        ("GET", "/api/v1/agency/billing/reports/aging", new[] { Role.Admin, Role.AccountManager, Role.SalesRep }),
        ("POST", "/api/v1/agency/billing/invoices", new[] { Role.Admin }),
        ("POST", $"/api/v1/agency/billing/invoices/{Id}/payments", new[] { Role.Admin }),
        ("POST", $"/api/v1/agency/billing/invoices/{Id}/void", new[] { Role.Admin }),
        ("POST", "/api/v1/agency/billing/credit-notes", new[] { Role.Admin }),
        ("PUT", "/api/v1/agency/billing/settings", new[] { Role.Admin }),
        ("POST", "/api/v1/agency/billing/tax-rates", new[] { Role.Admin }),
        ("GET", $"/api/v1/client/billing/invoices/{Id}", new[] { Role.Client }),
        ("GET", $"/api/v1/client/billing/proposals/{Id}", new[] { Role.Client }),
    };

    public static IEnumerable<object[]> Roles() => new[]
    {
        Role.Admin, Role.AccountManager, Role.SalesRep, Role.Strategist, Role.ContentCreator, Role.Finance, Role.Client, Role.Participant,
    }.Select(r => new object[] { r });

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task Each_role_reaches_exactly_the_endpoints_its_permissions_allow(Role role)
    {
        var (_, client) = await api.CreateClientAsync(role);
        foreach (var (method, path, allowed) in Matrix)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method is "POST" or "PUT") request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request);
            if (allowed.Contains(role))
                Assert.True(response.StatusCode != HttpStatusCode.Forbidden, $"{role} should reach {method} {path} but got 403");
            else
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{role} should be denied {method} {path} but got {(int)response.StatusCode}");
        }
    }
}
