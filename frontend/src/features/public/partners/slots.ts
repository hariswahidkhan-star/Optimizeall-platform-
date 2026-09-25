/**
 * The named partner placements of the public site. Mirrors `PartnerSlots` in
 * backend/src/OptimizeAll.Domain/Website/WebsitePartners.cs (same names, same order; a backend unit test compares them).
 *
 * - `list` slots show every eligible partner (home strip, footer line).
 * - `unit` slots show at most one ad unit, always labelled "Sponsored".
 * - `page` slots are the partner pages themselves (clicks only).
 *
 * The academy (/learn) uses the `learn.*` slots: `<PartnerSlot slot="learn.course" categories={[course.category]} />`.
 */
export type PartnerSlotKind = 'List' | 'Unit' | 'Page';

export interface PartnerSlotInfo {
  name: string;
  kind: PartnerSlotKind;
  label: string;
}

export const PARTNER_SLOTS = [
  { name: 'home.partners', kind: 'List', label: 'Home page — partner strip' },
  { name: 'footer.partners', kind: 'List', label: 'Footer — partner line' },
  { name: 'blog.inline', kind: 'Unit', label: 'Blog post — inline' },
  { name: 'blog.end', kind: 'Unit', label: 'Blog post — end of article' },
  { name: 'service.detail', kind: 'Unit', label: 'Service pages' },
  { name: 'case-study.detail', kind: 'Unit', label: 'Case studies' },
  { name: 'careers.index', kind: 'Unit', label: 'Careers page' },
  { name: 'learn.course', kind: 'Unit', label: 'Academy — course page' },
  { name: 'learn.lesson', kind: 'Unit', label: 'Academy — lesson page' },
  { name: 'learn.exam', kind: 'Unit', label: 'Academy — exam page' },
  { name: 'learn.certificate', kind: 'Unit', label: 'Academy — certificate pages' },
  { name: 'learn.dashboard', kind: 'Unit', label: 'Academy — My learning' },
  { name: 'partners.profile', kind: 'Page', label: 'Partner profile page' },
  { name: 'partners.directory', kind: 'Page', label: 'Partners page' },
] as const satisfies readonly PartnerSlotInfo[];

export type PartnerSlotName = (typeof PARTNER_SLOTS)[number]['name'];
export type PartnerUnitSlot = Extract<(typeof PARTNER_SLOTS)[number], { kind: 'Unit' }>['name'];
export type PartnerListSlot = Extract<(typeof PARTNER_SLOTS)[number], { kind: 'List' }>['name'];

export function slotKind(name: string): PartnerSlotKind | null {
  return PARTNER_SLOTS.find((s) => s.name === name)?.kind ?? null;
}
