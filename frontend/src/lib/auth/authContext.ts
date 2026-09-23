import { createContext } from 'react';
import type { RegisterRequest, SessionUser } from '@/lib/api/types';

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous';

export interface AuthContextValue {
  status: AuthStatus;
  user: SessionUser | null;
  permissions: readonly string[];
  /** ISO timestamp at which the current access token expires. */
  expiresAt: string | null;
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (permissions: readonly string[]) => boolean;
  login: (email: string, password: string) => Promise<SessionUser>;
  logout: () => Promise<void>;
  register: (request: Omit<RegisterRequest, 'deviceId'>) => Promise<string>;
  /** Re-reads the signed-in user (e.g. after verifying email). */
  refreshUser: () => Promise<SessionUser | null>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
