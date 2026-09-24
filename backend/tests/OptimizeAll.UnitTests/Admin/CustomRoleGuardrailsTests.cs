using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Admin.Roles;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using Xunit;

namespace OptimizeAll.UnitTests.Admin;

public sealed class CustomRoleGuardrailsTests
{
    private static IReadOnlySet<string> Set(params string[] p) => p.ToHashSet();

    [Fact]
    public void Permissions_are_trimmed_deduplicated_and_sorted()
    {
        var result = CustomRoleGuardrails.NormalizePermissions(new[] { " crm.view", Permissions.AnalyticsView, "crm.view" });
        Assert.Equal(new[] { Permissions.AnalyticsView, Permissions.CrmView }, result);
    }

    [Fact]
    public void Unknown_or_empty_permission_sets_are_rejected()
    {
        var unknown = Assert.Throws<DomainException>(() => CustomRoleGuardrails.NormalizePermissions(new[] { "crm.view", "root.everything" }));
        Assert.Equal("roles.unknown_permission", unknown.Code);
        Assert.Contains("root.everything", unknown.Message);
        Assert.Equal("roles.permissions_required", Assert.Throws<DomainException>(() => CustomRoleGuardrails.NormalizePermissions(Array.Empty<string>())).Code);
        Assert.Equal("roles.permissions_required", Assert.Throws<DomainException>(() => CustomRoleGuardrails.NormalizePermissions(null)).Code);
    }

    [Theory]
    [InlineData(Permissions.CrmView)]
    [InlineData(Permissions.SupportManage)]
    [InlineData(Permissions.ClientsView)]
    public void Client_portal_cannot_be_combined_with_staff_permissions(string staff)
    {
        var ex = Assert.Throws<DomainException>(() => CustomRoleGuardrails.NormalizePermissions(new[] { Permissions.ClientPortal, staff }));
        Assert.Equal("roles.client_portal_mixed", ex.Code);
        Assert.Equal(new[] { Permissions.ClientPortal }, CustomRoleGuardrails.NormalizePermissions(new[] { Permissions.ClientPortal }));
        Assert.False(CustomRoleGuardrails.MixesClientAndStaff(new[] { Permissions.ClientPortal, Permissions.ParticipantPortal }));
        Assert.True(CustomRoleGuardrails.MixesClientAndStaff(new[] { Permissions.ClientPortal, staff }));
    }

    [Fact]
    public void Nobody_can_grant_a_permission_they_do_not_hold()
    {
        var actor = Set(Permissions.RolesManage, Permissions.CrmView, Permissions.CrmManage);
        CustomRoleGuardrails.EnsureCanGrant(actor, actorIsAdmin: false, new[] { Permissions.CrmView });
        var ex = Assert.Throws<DomainException>(() =>
            CustomRoleGuardrails.EnsureCanGrant(actor, actorIsAdmin: false, new[] { Permissions.CrmView, Permissions.PayoutsFinalize }));
        Assert.Equal("roles.cannot_grant_unheld", ex.Code);
        Assert.Equal(DomainErrorKind.Forbidden, ex.Kind);
        Assert.Contains(Permissions.PayoutsFinalize, ex.Message);
        Assert.False(CustomRoleGuardrails.CanGrant(actor, false, new[] { Permissions.PayoutsFinalize }));
        Assert.True(CustomRoleGuardrails.CanGrant(actor, false, new[] { Permissions.CrmManage }));
    }

    [Theory]
    [InlineData(Permissions.RolesManage)]
    [InlineData(Permissions.SettingsManage)]
    [InlineData("users.impersonate")]
    public void Admin_only_permissions_need_the_built_in_admin_role_even_when_held(string permission)
    {
        var actor = Set(permission, Permissions.RolesManage);
        var ex = Assert.Throws<DomainException>(() => CustomRoleGuardrails.EnsureCanGrant(actor, actorIsAdmin: false, new[] { permission }));
        Assert.Equal("roles.admin_only_permission", ex.Code);
        Assert.False(CustomRoleGuardrails.CanGrant(actor, false, new[] { permission }));
        CustomRoleGuardrails.EnsureCanGrant(actor, actorIsAdmin: true, new[] { permission });
        Assert.True(CustomRoleGuardrails.CanGrant(actor, true, new[] { permission }));
    }

    [Fact]
    public void Admins_still_cannot_grant_what_they_do_not_hold()
    {
        var admin = RolePermissions.For(new[] { Role.Admin });
        Assert.DoesNotContain(Permissions.ClientPortal, admin);
        Assert.Equal("roles.cannot_grant_unheld",
            Assert.Throws<DomainException>(() => CustomRoleGuardrails.EnsureCanGrant(admin, true, new[] { Permissions.ClientPortal })).Code);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("Account Manager")]
    [InlineData("accountmanager")]
    public void Built_in_role_names_are_reserved(string name) =>
        Assert.Equal("roles.name_reserved", Assert.Throws<DomainException>(() => CustomRoleGuardrails.ValidateName(name)).Code);

    [Theory]
    [InlineData("")]
    [InlineData(" x ")]
    public void Names_need_two_to_eighty_characters(string name)
    {
        Assert.Equal("roles.invalid_name", Assert.Throws<DomainException>(() => CustomRoleGuardrails.ValidateName(name)).Code);
        Assert.Equal("roles.invalid_name", Assert.Throws<DomainException>(() => CustomRoleGuardrails.ValidateName(new string('a', 81))).Code);
        Assert.Equal("CRM viewer", CustomRoleGuardrails.ValidateName("  CRM viewer "));
    }

    [Fact]
    public void Only_admin_holds_roles_manage_among_built_in_roles()
    {
        foreach (var role in Enum.GetValues<Role>())
            Assert.Equal(role == Role.Admin, RolePermissions.For(role).Contains(Permissions.RolesManage));
    }

    [Fact]
    public void Every_permission_has_a_catalog_entry_and_appears_in_exactly_one_area()
    {
        foreach (var permission in Permissions.All)
        {
            var info = PermissionCatalog.Find(permission);
            Assert.True(info is not null, $"{permission} has no PermissionCatalog entry");
            Assert.False(string.IsNullOrWhiteSpace(info!.Label));
            Assert.False(string.IsNullOrWhiteSpace(info.Description));
            Assert.Contains(info.Area, PermissionCatalog.AreaOrder);
        }
        var listed = PermissionCatalog.Areas().SelectMany(a => a.Permissions).Select(p => p.Key).ToList();
        Assert.Equal(Permissions.All.OrderBy(p => p), listed.OrderBy(p => p));
        Assert.Equal(PermissionCatalog.Areas().Select(a => a.Area), PermissionCatalog.AreaOrder.Where(a => PermissionCatalog.Areas().Any(x => x.Area == a)));
    }

    [Fact]
    public void Portal_markers_are_not_staff_permissions()
    {
        Assert.False(PermissionCatalog.IsStaffPermission(Permissions.ClientPortal));
        Assert.False(PermissionCatalog.IsStaffPermission(Permissions.ParticipantPortal));
        Assert.True(PermissionCatalog.IsStaffPermission(Permissions.CrmView));
    }
}
