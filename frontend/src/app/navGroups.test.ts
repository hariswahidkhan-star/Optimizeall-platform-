import { groupNav } from './navGroups';
import { getPortal, portals } from './portals';

describe('sidebar nav groups', () => {
  it.each(portals.map((p) => [p.id, p] as const))(
    '%s: groups keep the nav order and never repeat',
    (_, portal) => {
      const sections = groupNav(portal.id, portal.nav);
      // Flattening the sections gives back exactly the nav, in order.
      expect(sections.flatMap((s) => s.items)).toEqual(portal.nav);
      // Each label names one contiguous run (a label never appears twice).
      const labels = sections.map((s) => s.label).filter(Boolean);
      expect(new Set(labels).size).toBe(labels.length);
      // The portal home is never grouped.
      const home = sections.find((s) => s.items.some((i) => i.to === ''));
      if (home) expect(home.label).toBeUndefined();
    },
  );

  it('labels agency areas', () => {
    const sections = groupNav('agency', getPortal('agency').nav);
    expect(sections.map((s) => s.label)).toEqual([
      undefined,
      'Delivery',
      'Sales',
      'Billing',
      'Messaging',
      'Social',
      'Ads',
      'Search & pages',
      'Website',
      'Workspace',
    ]);
  });
});
