import { useContext } from 'react';
import { AuthContext, type AuthContextValue } from './authContext';

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error('useAuth must be used inside <AuthProvider>.');
  return value;
}

/** Like useAuth, but returns null outside an AuthProvider (for presentational components such as DateTime). */
export function useOptionalAuth(): AuthContextValue | null {
  return useContext(AuthContext);
}
