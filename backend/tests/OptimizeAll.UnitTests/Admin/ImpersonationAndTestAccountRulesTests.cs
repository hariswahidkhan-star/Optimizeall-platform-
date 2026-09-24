using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Api.Common.Hosting;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Admin;
using OptimizeAll.Api.Modules.Auth;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Api.Modules.Integrations;
using OptimizeAll.Api.Modules.Ledger;
using OptimizeAll.Api.Modules.Payouts;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Payouts;

namespace OptimizeAll.UnitTests.Admin;

public sealed class ImpersonationAndTestAccountRulesTests
{
    [Fact]
    public void Test_accounts_are_excluded_from_payout_plans_before_any_other_rule()
    {
        var test = Guid.NewGuid();
        var real = Guid.NewGuid();
        var plan = PayoutPlanner.Plan(
            new[] { new PlannerEarning(Guid.NewGuid(), test, 100m), new PlannerEarning(Guid.NewGuid(), real, 100m) },
            new Dictionary<Guid, PlannerParticipant>
            {
                // Also on hold: the test-account reason wins.
                [test] = new(test, IsActive: true, HasActiveHold: true, HasPayoutProfile: true, IsTestAccount: true),
                [real] = new(real, IsActive: true, HasActiveHold: false, HasPayoutProfile: true),
            },
            10m, "USD");
        Assert.Equal(real, Assert.Single(plan.Items).UserId);
        var exclusion = Assert.Single(plan.Exclusions);
        Assert.Equal(test, exclusion.UserId);
        Assert.Equal(PayoutExclusionReason.TestAccount, exclusion.Reason);
    }

    [Theory]
    [InlineData(true, "Development", true)]
    [InlineData(true, "Staging", true)]
    [InlineData(true, "Testing", true)]
    [InlineData(true, "Production", false)]
    [InlineData(true, "production", false)]
    [InlineData(false, "Development", false)]
    [InlineData(false, "Staging", false)]
    public void Test_login_needs_the_flag_and_a_non_production_environment(bool flag, string environment, bool expected) =>
        Assert.Equal(expected, TestAccounts.TestLoginAllowed(flag, environment));

    [Theory]
    [InlineData("admin@demo.optimizeall.app", true)]
    [InlineData("owner@nimbus.demo.optimizeall.app", true)]
    [InlineData("Sara.Participant@DEMO.optimizeall.app", true)]
    [InlineData("someone@optimizeall.app", false)]
    [InlineData("x@evil-demo.optimizeall.app", false)]
    [InlineData("x@demo.optimizeall.app.evil.com", false)]
    [InlineData("not-an-email", false)]
    public void Demo_accounts_are_recognised_by_their_domain(string email, bool expected) =>
        Assert.Equal(expected, TestAccounts.IsDemoEmail(email));

    [Fact]
    public void Generated_test_emails_and_passwords()
    {
        var email = TestAccounts.GenerateEmail("QA  Jane_Doe!", "Test.Example.com");
        Assert.Matches("^test\\+qa-jane-doe-[a-z0-9]{6}@test\\.example\\.com$", email);
        Assert.NotEqual(email, TestAccounts.GenerateEmail("QA  Jane_Doe!", "test.example.com"));
        Assert.Matches("^test\\+[a-z0-9]{6}@x\\.test$", TestAccounts.GenerateEmail("!!!", "x.test"));

        var password = TestAccounts.GeneratePassword();
        Assert.Equal(20, password.Length);
        PasswordPolicy.Validate(password, email); // does not throw
        Assert.NotEqual(password, TestAccounts.GeneratePassword());
    }

    [Fact]
    public void Admins_and_impersonators_are_protected_targets()
    {
        Assert.True(ImpersonationService.IsProtectedTarget(new[] { Role.Admin }));
        Assert.True(ImpersonationService.IsProtectedTarget(new[] { Role.Participant, Role.Admin }));
        Assert.False(ImpersonationService.IsProtectedTarget(new[] { Role.Participant }));
        Assert.False(ImpersonationService.IsProtectedTarget(new[] { Role.Finance }));
        Assert.False(ImpersonationService.IsProtectedTarget(new[] { Role.Client }));
        Assert.Contains(Permissions.UsersImpersonate, RolePermissions.For(new[] { Role.Admin }));
        foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Admin))
            Assert.DoesNotContain(Permissions.UsersImpersonate, RolePermissions.For(role));
    }

    private static AuthorizationFilterContext FilterContext(string method, bool impersonating)
    {
        var claims = new List<Claim> { new(AppClaims.UserId, Guid.NewGuid().ToString()) };
        if (impersonating)
        {
            claims.Add(new Claim(ImpersonationClaims.SessionId, Guid.NewGuid().ToString()));
            claims.Add(new Claim(ImpersonationClaims.ActorId, Guid.NewGuid().ToString()));
        }
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };
        http.Request.Method = method;
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
    }

    [Fact]
    public void The_filter_refuses_impersonated_requests_with_the_dedicated_code_and_lets_reads_through_when_writes_only()
    {
        var always = new DeniedWhileImpersonatingAttribute();
        var writesOnly = new DeniedWhileImpersonatingAttribute { WritesOnly = true };

        var ex = Assert.Throws<DomainException>(() => always.OnAuthorization(FilterContext("GET", impersonating: true)));
        Assert.Equal(Impersonation.ForbiddenActionCode, ex.Code);
        Assert.Equal(DomainErrorKind.Forbidden, ex.Kind);
        Assert.Throws<DomainException>(() => writesOnly.OnAuthorization(FilterContext("POST", impersonating: true)));
        Assert.Throws<DomainException>(() => writesOnly.OnAuthorization(FilterContext("DELETE", impersonating: true)));
        writesOnly.OnAuthorization(FilterContext("GET", impersonating: true));
        always.OnAuthorization(FilterContext("POST", impersonating: false));
        writesOnly.OnAuthorization(FilterContext("PUT", impersonating: false));
    }

    private static DeniedWhileImpersonatingAttribute? Denial(Type controller, string? action = null)
    {
        if (action is null) return controller.GetCustomAttribute<DeniedWhileImpersonatingAttribute>();
        var method = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(m => m.Name == action);
        return method.GetCustomAttribute<DeniedWhileImpersonatingAttribute>();
    }

    [Fact]
    public void High_risk_endpoints_are_denied_while_impersonating()
    {
        // Whole action (reads included).
        Assert.NotNull(Denial(typeof(AuthController), nameof(AuthController.ChangePassword)));
        Assert.NotNull(Denial(typeof(AdminImpersonationController), nameof(AdminImpersonationController.Start)));
        Assert.NotNull(Denial(typeof(PayoutBatchesController), nameof(PayoutBatchesController.PaymentInstructions)));
        Assert.NotNull(Denial(typeof(AgencyInvoicesController), nameof(AgencyInvoicesController.RecordPayment)));
        Assert.NotNull(Denial(typeof(ClientBillingController), nameof(ClientBillingController.Pay)));

        // Every write of these controllers (payout destination, payouts, holds, schedule, ledger adjustments and
        // approvals, exchange rates, payments, credit notes, integration credentials, user/role administration, test users).
        foreach (var controller in new[]
                 {
                     typeof(PayoutProfileController), typeof(PayoutBatchesController), typeof(PayoutScheduleController),
                     typeof(PayoutHoldsController), typeof(FinanceLedgerController), typeof(PendingEarningsController),
                     typeof(ExchangeRatesController), typeof(AgencyPaymentsController), typeof(AgencyCreditNotesController),
                     typeof(IntegrationsController), typeof(AdminUsersController), typeof(AdminTestUsersController),
                 })
        {
            var attribute = Denial(controller);
            Assert.True(attribute is { WritesOnly: true }, $"{controller.Name} must deny writes while impersonating.");
        }

        // Exit must stay reachable.
        Assert.Null(Denial(typeof(AuthController), nameof(AuthController.ExitImpersonation)));
        Assert.Null(Denial(typeof(AuthController)));
    }
}
