import type { PortalId, PortalNavItem } from './portalTypes';

/**
 * Section labels of the portal sidebar. Groups only label runs of consecutive nav items — they never reorder them
 * (the nav order is part of each portal's contract). An item keeps an explicit `group` when its feature declares one;
 * otherwise the first rule whose prefix matches its path (`to`) names it. The portal home ('') is never grouped.
 */
const RULES: Record<PortalId, [prefix: string, group: string][]> = {
  participant: [
    ['campaigns', 'Earn'],
    ['submissions', 'Earn'],
    ['earnings', 'Money'],
    ['payouts', 'Money'],
    ['social-accounts', 'Grow'],
    ['referrals', 'Grow'],
    ['achievements', 'Grow'],
    ['learning', 'Learn'],
    ['notifications', 'Account'],
    ['support', 'Account'],
    ['profile', 'Account'],
  ],
  reviewer: [['', 'Review']],
  manager: [
    ['campaigns', 'Campaigns'],
    ['templates', 'Campaigns'],
    ['calendar', 'Campaigns'],
    ['invitations', 'Growth'],
    ['experiments', 'Growth'],
    ['referrals', 'Growth'],
    ['analytics', 'Insights'],
    ['achievements', 'Insights'],
  ],
  finance: [
    ['payments', 'Payouts'],
    ['batches', 'Payouts'],
    ['ledger', 'Payouts'],
    ['approvals', 'Controls'],
    ['holds', 'Controls'],
    ['exchange-rates', 'Configuration'],
    ['schedule', 'Configuration'],
  ],
  admin: [
    ['users', 'People'],
    ['roles', 'People'],
    ['settings', 'Platform'],
    ['content', 'Platform'],
    ['categories', 'Platform'],
    ['support', 'Operations'],
    ['learning', 'Operations'],
    ['audit', 'Operations'],
    ['jobs', 'Operations'],
    ['analytics', 'Operations'],
  ],
  agency: [
    ['crm', 'Sales'],
    ['proposals', 'Sales'],
    ['contracts', 'Sales'],
    ['billing', 'Billing'],
    ['email', 'Messaging'],
    ['sms', 'Messaging'],
    ['social', 'Social'],
    ['ads', 'Ads'],
    ['seo', 'Search & pages'],
    ['pages', 'Search & pages'],
    ['website', 'Website'],
    ['integrations', 'Workspace'],
    ['', 'Delivery'],
  ],
  client: [
    ['social', 'Marketing'],
    ['email', 'Marketing'],
    ['seo', 'Marketing'],
    ['billing', 'Account'],
    ['', 'Your work'],
  ],
};

function matches(to: string, prefix: string): boolean {
  return prefix === '' || to === prefix || to.startsWith(`${prefix}/`);
}

export function navGroupFor(portal: PortalId, item: PortalNavItem): string | undefined {
  if (item.to === '') return undefined;
  if (item.group) return item.group;
  return RULES[portal].find(([prefix]) => matches(item.to, prefix))?.[1];
}

export interface NavSection {
  /** undefined for the leading, unlabelled run (the portal home). */
  label: string | undefined;
  items: PortalNavItem[];
}

/** Splits nav items into runs of the same group, keeping their order. */
export function groupNav(portal: PortalId, items: PortalNavItem[]): NavSection[] {
  const sections: NavSection[] = [];
  for (const item of items) {
    const label = navGroupFor(portal, item);
    const last = sections[sections.length - 1];
    if (last && last.label === label) last.items.push(item);
    else sections.push({ label, items: [item] });
  }
  return sections;
}
