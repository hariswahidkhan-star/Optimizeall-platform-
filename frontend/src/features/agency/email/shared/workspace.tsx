import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { safeStorage } from '@/lib/hooks/storage';

const STORAGE_KEY = 'oa.email.workspace';

export interface EmailWorkspaceValue {
  /** Client account id, or null for the agency's own marketing. */
  clientId: string | null;
  /** "agency" or the client id (query keys, URLs). */
  key: string;
  setClientId: (clientId: string | null) => void;
}

const EmailWorkspaceContext = createContext<EmailWorkspaceValue | null>(null);

/** Holds the selected workspace (remembered per browser) for every email page. */
export function EmailWorkspaceProvider({ children, initial }: { children: ReactNode; initial?: string | null }) {
  const [clientId, setClientIdState] = useState<string | null>(() => {
    if (initial !== undefined) return initial;
    const stored = safeStorage.get(STORAGE_KEY);
    return stored && stored !== 'agency' ? stored : null;
  });
  const value = useMemo<EmailWorkspaceValue>(
    () => ({
      clientId,
      key: clientId ?? 'agency',
      setClientId: (next) => {
        setClientIdState(next);
        safeStorage.set(STORAGE_KEY, next ?? 'agency');
      },
    }),
    [clientId],
  );
  return <EmailWorkspaceContext.Provider value={value}>{children}</EmailWorkspaceContext.Provider>;
}

export function useEmailWorkspace(): EmailWorkspaceValue {
  const value = useContext(EmailWorkspaceContext);
  // Pages rendered outside the layout (tests, deep links) fall back to the agency workspace.
  return value ?? { clientId: null, key: 'agency', setClientId: () => undefined };
}
