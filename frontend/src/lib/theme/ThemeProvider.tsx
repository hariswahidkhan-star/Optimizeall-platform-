import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { safeStorage } from '@/lib/hooks/storage';
import { useMediaQuery } from '@/lib/hooks/useMediaQuery';
import { ThemeContext, type ThemeContextValue, type ThemePreference } from './themeContext';

export const THEME_STORAGE_KEY = 'oa.theme';

function readPreference(): ThemePreference {
  const stored = safeStorage.get(THEME_STORAGE_KEY);
  return stored === 'light' || stored === 'dark' ? stored : 'system';
}

/**
 * Light/dark theme. 'system' follows prefers-color-scheme; an explicit choice is persisted and applied through
 * <html data-theme>, which switches `color-scheme` and therefore every light-dark() token.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreferenceState] = useState<ThemePreference>(readPreference);
  const systemDark = useMediaQuery('(prefers-color-scheme: dark)');
  const resolved = preference === 'system' ? (systemDark ? 'dark' : 'light') : preference;

  useEffect(() => {
    const root = document.documentElement;
    if (preference === 'system') root.removeAttribute('data-theme');
    else root.setAttribute('data-theme', preference);
  }, [preference]);

  const setPreference = useCallback((next: ThemePreference) => {
    setPreferenceState(next);
    if (next === 'system') safeStorage.remove(THEME_STORAGE_KEY);
    else safeStorage.set(THEME_STORAGE_KEY, next);
  }, []);

  const value = useMemo<ThemeContextValue>(
    () => ({ preference, resolved, setPreference }),
    [preference, resolved, setPreference],
  );

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}
