import { createContext, useContext } from 'react';
import type { PortalDefinition } from './portalTypes';

/** The portal whose layout is currently rendered (set by PortalLayout). */
export const PortalContext = createContext<PortalDefinition | null>(null);

export function useCurrentPortal(): PortalDefinition {
  const portal = useContext(PortalContext);
  if (!portal) throw new Error('useCurrentPortal must be used inside a portal layout.');
  return portal;
}
