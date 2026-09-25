using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.UnitTests.Admin;

/// <summary>
/// Guards <see cref="DeniedWhileImpersonatingAttribute"/> coverage by reflection over every controller action, so a new
/// endpoint that moves money, changes credentials, identity, roles, integrations or payout destinations, or exports
/// decrypted data fails this test until it is either denied while impersonating or consciously allow-listed here.
/// </summary>
public sealed class ImpersonationCoverageTests
{
    /// <summary>Any write guarded by one of these permissions must be denied while impersonating.</summary>
    private static readonly HashSet<string> SensitivePermissions = new(StringComparer.Ordinal)
    {
        // money
        Permissions.LedgerAdjust, Permissions.PayoutsPrepare, Permissions.PayoutsFinalize, Permissions.PayoutsRecordPayment,
        Permissions.PayoutsHold, Permissions.PayoutSettingsEdit, Permissions.RewardsEdit, Permissions.RewardsApproveBonus,
        Permissions.SubmissionsReverse, Permissions.BillingManage, Permissions.BillingSettings, Permissions.ContractsManage,
        // credentials / integrations
        Permissions.IntegrationsManage,
        // identity, accounts and roles
        Permissions.UsersManage, Permissions.UsersSuspend, Permissions.RolesAssign, Permissions.RolesManage,
        Permissions.UsersImpersonate,
    };

    /// <summary>
    /// Writes that are sensitive for what they do rather than for the permission guarding them (maintained deny list):
    /// credentials, identity, client-portal access, money created or committed as a side effect, platform settings.
    /// </summary>
    private static readonly string[] MustDenyWrites =
    {
        "AuthController.ChangePassword",
        "GoogleAuthController.StartLink",
        "GoogleAuthController.Unlink",
        "ProfileController.Update",
        "PayoutProfileController.Update",
        "AdminSettingsController.Update",
        "AdminSettingsController.Reset",
        "WebsiteContentController.UpdateSettings",
        "WebsiteRedirectsController.Create",
        "WebsiteRedirectsController.Delete",
        "AdminJobsController.Run",
        "ReviewController.Decide",
        "ReviewController.ResolveAppeal",
        "ReferralsController.Reject",
        "SocialProfilesController.ConnectStart",
        "SocialProfilesController.ConnectCallback",
        "SocialProfilesController.Disconnect",
        "AgencyClientsController.Invite",
        "AgencyClientsController.ChangeRole",
        "AgencyClientsController.RemoveMember",
        "AgencyClientsController.OnBehalfOfClient",
        "AgencyClientsController.RemoveAsset",
        "AgencyTasksController.Detach",
        "ClientPortalAccountController.Invite",
        "ClientPortalAccountController.ChangeRole",
        "ClientPortalAccountController.Remove",
        "ClientProposalsController.Accept",
        "ClientProposalsController.Decline",
        "ClientBillingController.Pay",
        "ClientPaymentsController.Claim",
        "ClientPaymentsController.UploadProof",
        "AgencyTimeController.SaveRate",
        "AgencyTimeController.DeleteRate",
        "AdminTestUsersController.Create",
        "AdminTestUsersController.Delete",
        "AdminImpersonationController.Start",
        // Outbound messages to a client's audience (campaigns, journeys) or the client's own consent to one. Test sends
        // (to staff addresses), pause/unschedule/cancel (they stop sending) and drafting stay available.
        "EmailCampaignsController.Send",
        "EmailCampaignsController.Resume",
        "SmsCampaignsController.Send",
        "SmsCampaignsController.Resume",
        "EmailAutomationsController.Activate",
        "EmailAutomationsController.Enroll",
        "ClientEmailController.Decide",
    };

    /// <summary>Reads that must be denied too (they decrypt data).</summary>
    private static readonly string[] MustDenyReads =
    {
        "PayoutBatchesController.PaymentInstructions",
    };

    /// <summary>
    /// Writes guarded by a sensitive permission that are deliberately allowed while impersonating, with the reason.
    /// Keep this list short: every entry is a conscious decision.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedSensitiveWrites = new(StringComparer.Ordinal)
    {
    };

    /// <summary>
    /// The way back must stay reachable while impersonating, and so must the actions that stop outbound messages or
    /// only reach staff (a campaign test send goes to the addresses typed in, normally the team's own).
    /// </summary>
    private static readonly string[] MustAllow =
    {
        "AuthController.ExitImpersonation", "AuthController.Logout", "AuthController.Refresh", "AuthController.Me",
        "EmailCampaignsController.Test", "EmailCampaignsController.Pause", "EmailCampaignsController.Cancel",
        "EmailCampaignsController.Unschedule", "EmailAutomationsController.Pause",
    };

    private sealed record ActionInfo(string Name, bool IsWrite, bool IsRead, IReadOnlySet<string> Permissions,
        bool DeniesWrites, bool DeniesReads);

    private static List<ActionInfo> Actions()
    {
        var assembly = typeof(DeniedWhileImpersonatingAttribute).Assembly;
        var result = new List<ActionInfo>();
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t)))
        {
            var classDenial = type.GetCustomAttribute<DeniedWhileImpersonatingAttribute>(inherit: true);
            var classPermissions = type.GetCustomAttributes<HasPermissionAttribute>(inherit: true).Select(PermissionOf);
            // Actions inherited from an abstract base controller of the app (e.g. CampaignActions) belong to each concrete
            // controller, so scan the whole hierarchy up to ControllerBase.
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => m.DeclaringType is { } d && d.Assembly == assembly && typeof(ControllerBase).IsAssignableFrom(d)))
            {
                var verbs = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).SelectMany(a => a.HttpMethods).ToList();
                if (verbs.Count == 0) continue;
                var isRead = verbs.Any(v => v is "GET" or "HEAD");
                var isWrite = verbs.Any(v => v is not ("GET" or "HEAD"));
                var methodDenial = method.GetCustomAttribute<DeniedWhileImpersonatingAttribute>(inherit: true);
                var permissions = method.GetCustomAttributes<HasPermissionAttribute>(inherit: true).Select(PermissionOf)
                    .Concat(classPermissions).ToHashSet(StringComparer.Ordinal);
                var deniesWrites = methodDenial is not null || classDenial is not null;
                var deniesReads = (methodDenial is not null && !methodDenial.WritesOnly) || (classDenial is not null && !classDenial.WritesOnly);
                result.Add(new ActionInfo($"{type.Name}.{method.Name}", isWrite, isRead, permissions, deniesWrites, deniesReads));
            }
        }
        return result;
    }

    private static string PermissionOf(HasPermissionAttribute a) => a.Policy![HasPermissionAttribute.PolicyPrefix.Length..];

    [Fact]
    public void The_scan_finds_the_controllers()
    {
        var actions = Actions();
        Assert.True(actions.Count > 300, $"Only {actions.Count} actions found: the reflection scan is broken.");
        Assert.Contains(actions, a => a.Name == "PayoutBatchesController.Finalize");
        // Actions declared on an abstract base controller are found on each concrete controller.
        Assert.Contains(actions, a => a.Name == "SmsCampaignsController.Send");
    }

    [Fact]
    public void Every_write_guarded_by_a_sensitive_permission_is_denied_while_impersonating()
    {
        var missing = Actions()
            .Where(a => a.IsWrite && a.Permissions.Overlaps(SensitivePermissions) && !a.DeniesWrites && !AllowedSensitiveWrites.ContainsKey(a.Name))
            .Select(a => $"{a.Name} [{string.Join(", ", a.Permissions.Intersect(SensitivePermissions))}]")
            .OrderBy(n => n).ToList();
        Assert.True(missing.Count == 0,
            "Add [DeniedWhileImpersonating] (or allow-list with a reason in ImpersonationCoverageTests): " + string.Join("; ", missing));
    }

    [Fact]
    public void The_maintained_deny_lists_are_enforced_and_current()
    {
        var byName = Actions().GroupBy(a => a.Name).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var name in MustDenyWrites)
        {
            Assert.True(byName.TryGetValue(name, out var list), $"{name} no longer exists: update the deny list.");
            Assert.All(list!.Where(a => a.IsWrite), a => Assert.True(a.DeniesWrites, $"{name} must be denied while impersonating."));
        }
        foreach (var name in MustDenyReads)
        {
            Assert.True(byName.TryGetValue(name, out var list), $"{name} no longer exists: update the deny list.");
            Assert.All(list!, a => Assert.True(a.DeniesReads, $"{name} (a read) must be denied while impersonating."));
        }
        foreach (var name in AllowedSensitiveWrites.Keys)
            Assert.True(byName.ContainsKey(name), $"{name} no longer exists: remove it from the allow list.");
        foreach (var name in MustAllow)
        {
            Assert.True(byName.TryGetValue(name, out var list), $"{name} no longer exists.");
            Assert.All(list!, a => Assert.False(a.DeniesWrites || a.DeniesReads, $"{name} must stay reachable while impersonating."));
        }
    }
}
