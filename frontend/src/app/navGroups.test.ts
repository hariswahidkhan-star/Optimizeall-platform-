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

  it("keeps the manager portal's rate cards and groups in the Campaigns section", () => {
    const sections = groupNav('manager', getPortal('manager').nav);
    const campaigns = sections.find((s) => s.label === 'Campaigns');
    expect(campaigns?.items.map((i) => i.to)).toEqual(
      expect.arrayContaining(['campaigns', 'rate-cards', 'rate-groups']),
    );
  });

  it.each([
    ['participant', 'codes', 'Money'],
    ['manager', 'codes', 'Campaigns'],
    ['reviewer', 'code-sales', 'Review'],
    ['finance', 'code-sales', 'Controls'],
  ] as const)('puts %s → %s (discount codes) in the %s section', (portal, to, label) => {
    const sections = groupNav(portal, getPortal(portal).nav);
    const section = sections.find((s) => s.items.some((i) => i.to === to));
    expect(section?.label).toBe(label);
  });
});
