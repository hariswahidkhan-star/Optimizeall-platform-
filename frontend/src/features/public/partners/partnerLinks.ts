import { isExternalHref } from '@/lib/safeHref';

/**
 * Google's link-spam policy: every paid or partnership link carries `rel="sponsored"`. This mirrors
 * `PartnerLinkPolicy` in backend/src/OptimizeAll.Domain/Website/PartnerLinkPolicy.cs (same rules, same test cases):
 * a link matches a partner when its host is the partner's website host or a subdomain of it ("www." ignored); it is then
 * rendered with `rel="sponsored noopener"`, `target="_blank"` and the partner's UTM tags (existing utm_* tags win).
 *
 * The rules come from `GET /public/partners` → `linkRules` (active partners with a website only), provided to the
 * Markdown renderer by `PartnerLinksProvider` (mounted in the public site chrome).
 */
export interface PartnerLinkRule {
  slug: string;
  host: string;
  utmSource: string;
  utmMedium: string;
  utmCampaign: string | null;
}

export interface PartnerLinkAttributes {
  href: string;
  rel: string;
  target: '_blank';
  partnerSlug: string;
}

export const SPONSORED_REL = 'sponsored noopener';
export const EDITORIAL_CAMPAIGN = 'editorial';

/** The host partner links are matched on (lower case, "www." removed), or null for anything but an http(s) URL. */
export function partnerHostOf(href: string | null | undefined): string | null {
  if (!isExternalHref(href)) return null;
  const host = new URL(href).hostname.toLowerCase().replace(/\.$/, '');
  return host.startsWith('www.') ? host.slice(4) : host;
}

export function matchPartnerLink(href: string | null | undefined, rules: readonly PartnerLinkRule[]): PartnerLinkRule | null {
  const host = partnerHostOf(href);
  if (!host) return null;
  return (
    rules
      .filter((r) => host === r.host || host.endsWith(`.${r.host}`))
      .sort((a, b) => b.host.length - a.host.length)[0] ?? null
  );
}

/** The href with the partner's UTM tags; utm_* parameters already on the link are kept. */
export function withPartnerUtm(href: string, rule: PartnerLinkRule, campaign = EDITORIAL_CAMPAIGN): string {
  const url = new URL(href);
  const existing = new Set([...url.searchParams.keys()].map((k) => k.toLowerCase()));
  const tags: [string, string][] = [
    ['utm_source', rule.utmSource],
    ['utm_medium', rule.utmMedium],
    ['utm_campaign', rule.utmCampaign ?? campaign],
  ];
  for (const [key, value] of tags) if (!existing.has(key) && value) url.searchParams.append(key, value);
  return url.toString();
}

/** How to render a link: partner links get sponsored attributes and UTM tags; anything else returns null. */
export function applyPartnerLink(
  href: string | null | undefined,
  rules: readonly PartnerLinkRule[],
  campaign = EDITORIAL_CAMPAIGN,
): PartnerLinkAttributes | null {
  const rule = matchPartnerLink(href, rules);
  if (!rule || !href) return null;
  return { href: withPartnerUtm(href, rule, campaign), rel: SPONSORED_REL, target: '_blank', partnerSlug: rule.slug };
}

/** The click-counting address of a partner card for one placement (null while the partner has no website). */
export function visitHref(visitUrl: string | null | undefined, slot: string, pagePath: string): string | null {
  if (!visitUrl || !visitUrl.startsWith('/api/v1/public/partners/')) return null;
  const params = new URLSearchParams({ slot, path: pagePath });
  return `${visitUrl}?${params.toString()}`;
}
