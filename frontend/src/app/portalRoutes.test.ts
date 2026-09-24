import type { RouteObject } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { meetsRequirement, type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { accessiblePortals, canOpenPath, defaultLandingPath, getPortal, portals } from './portals';
import type { PortalRouteHandle } from './portalTypes';

const requiresOf = (route: RouteObject) => (route.handle as PortalRouteHandle | undefined)?.requires;

/** Every route with its full path relative to the portal base. */
function walk(routes: RouteObject[], parent = ''): { path: string; route: RouteObject }[] {
  return routes.flatMap((route) => {
    const path = route.index ? parent : [parent, route.path].filter(Boolean).join('/');
    return [{ path, route }, ...walk(route.children ?? [], path)];
  });
}

/** The smallest permission sets that satisfy a requirement (all of `allOf` plus one of `anyOf`). */
function minimalGrants(req: PermissionRequirement): string[][] {
  const base = [...(req.allOf ?? [])];
  return req.anyOf && req.anyOf.length > 0 ? req.anyOf.map((p) => [...base, p]) : [base];
}

describe('portal route permissions', () => {
  for (const portal of portals) {
    describe(portal.id, () => {
      it('every route requirement is covered by the portal entry requirement', () => {
        for (const { path, route } of walk(portal.routes)) {
          const requires = requiresOf(route);
          if (!requires) continue;
          for (const grant of minimalGrants(requires)) {
            expect(
              meetsRequirement(grant, portal.requires),
              `${portal.basePath}/${path}: [${grant.join(', ')}] meets the route but not the portal entry`,
            ).toBe(true);
          }
        }
      });

      it('nav items declare the same requirement as their route', () => {
        for (const item of portal.nav) {
          const route = portal.routes.find((r) => (item.to === '' ? r.index : r.path === item.to));
          expect(route, `${portal.basePath}/${item.to} has no route`).toBeDefined();
          expect(requiresOf(route!), `${portal.basePath}/${item.to}`).toEqual(item.requires);
        }
      });

      it('detail routes are guarded like their list', () => {
        for (const route of portal.routes) {
          const [section, ...rest] = (route.path ?? '').split('/');
          if (!section || rest.length === 0) continue;
          const list = portal.routes.find((r) => r.path === section);
          if (list) expect(requiresOf(route), `${portal.basePath}/${route.path}`).toEqual(requiresOf(list));
        }
      });
    });
  }

  it('lets campaign managers reach finance approvals only', () => {
    const manager = [
      Permissions.CampaignsView,
      Permissions.CampaignsManage,
      Permissions.RewardsApproveBonus,
      Permissions.MarketingManage,
      Permissions.AnalyticsView,
      Permissions.UsersView,
    ];
    expect(canOpenPath(manager, '/finance/approvals')).toBe(true);
    expect(canOpenPath(manager, '/finance')).toBe(true);
    expect(canOpenPath(manager, '/finance/batches')).toBe(false);
    expect(canOpenPath(manager, '/finance/holds')).toBe(false);
    expect(canOpenPath(manager, '/admin/categories')).toBe(true);
    expect(canOpenPath(manager, '/admin/settings')).toBe(false);
  });

  it('guards detail pages and deep paths with the section permission', () => {
    expect(canOpenPath([Permissions.PayoutsView], '/finance/batches/b1/reconciliation')).toBe(true);
    expect(canOpenPath([Permissions.PayoutsView], '/finance/ledger/users/u1')).toBe(false);
    expect(canOpenPath([Permissions.PayoutsView], '/finance/holds')).toBe(false);
    expect(canOpenPath([Permissions.PayoutsHold], '/finance/holds')).toBe(true);
    expect(canOpenPath([Permissions.SupportManage], '/admin/support/t1')).toBe(true);
    expect(canOpenPath([Permissions.SupportManage], '/admin/users/u1')).toBe(false);
    expect(canOpenPath([Permissions.JobsView], '/admin/jobs')).toBe(true);
    expect(canOpenPath([Permissions.SubmissionsReview], '/review/appeals/a1')).toBe(false);
  });
  it('decides portal access from session permissions only (custom roles): crm.view alone lands in the agency CRM', () => {
    // A user whose only access comes from a custom role (no built-in role at all).
    const crmOnly = [Permissions.CrmView];
    expect(defaultLandingPath(crmOnly)).toBe('/agency');
    expect(accessiblePortals(crmOnly).map((p) => p.id)).toEqual(['agency']);
    const agency = getPortal('agency');
    const visible = agency.nav
      .filter((item) => item.to !== '' && (!item.requires || meetsRequirement(crmOnly, item.requires)))
      .map((item) => item.to);
    // The CRM sections plus the website leads views that sales staff (crm.view) work from; nothing else.
    expect(visible).toEqual(['crm', 'crm/deals', 'crm/tasks', 'website/overview', 'website/inquiries']);
    expect(canOpenPath(crmOnly, '/agency/crm/deals')).toBe(true);
    expect(canOpenPath(crmOnly, '/agency/clients')).toBe(false);
    expect(canOpenPath(crmOnly, '/agency/proposals')).toBe(false);
    expect(canOpenPath(crmOnly, '/admin/users')).toBe(false);
    expect(canOpenPath(crmOnly, '/app')).toBe(false);
  });

  it('opens the admin Roles & permissions section with roles.manage only', () => {
    expect(canOpenPath([Permissions.RolesManage], '/admin/roles')).toBe(true);
    expect(accessiblePortals([Permissions.RolesManage]).map((p) => p.id)).toEqual(['admin']);
    expect(canOpenPath([Permissions.UsersView, Permissions.RolesAssign], '/admin/roles')).toBe(false);
  });
});
