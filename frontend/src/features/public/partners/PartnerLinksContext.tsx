import { createContext, useContext, type ReactNode } from 'react';
import { usePartners } from './api';
import type { PartnerLinkRule } from './partnerLinks';

const PartnerLinksContext = createContext<readonly PartnerLinkRule[]>([]);

/**
 * Makes the active partners' link rules available to the Markdown renderer, which then renders links to partner
 * websites in editorial content (blog posts, CMS pages, courses) with `rel="sponsored noopener"`, a new tab and UTM
 * tags. Mounted by the public site chrome; other public surfaces rendering Markdown (e.g. the academy) wrap their
 * content in it too.
 */
export function PartnerLinksProvider({ children }: { children: ReactNode }) {
  const { data } = usePartners();
  return <PartnerLinksContext.Provider value={data?.linkRules ?? EMPTY}>{children}</PartnerLinksContext.Provider>;
}

const EMPTY: readonly PartnerLinkRule[] = [];

/** Test and preview helper: provide fixed rules without fetching. */
export function StaticPartnerLinks({ rules, children }: { rules: readonly PartnerLinkRule[]; children: ReactNode }) {
  return <PartnerLinksContext.Provider value={rules}>{children}</PartnerLinksContext.Provider>;
}

export function usePartnerLinkRules(): readonly PartnerLinkRule[] {
  return useContext(PartnerLinksContext);
}
