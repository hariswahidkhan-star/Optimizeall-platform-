import { createContext } from 'react';
import type { AuthResponse, RegisterRequest, SessionUser } from '@/lib/api/types';

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous';

export interface AuthContextValue {
  status: AuthStatus;
  user: SessionUser | null;
  permissions: readonly string[];
  /** ISO timestamp at which the current access token expires. */
  expiresAt: string | null;
  /**
   * True from a forced sign-out (the session could not be renewed, e.g. after a suspension or password change) until
   * the sign-in page is reached, so guards that redirect to /login keep the "session expired" notice (`expired=1`).
   */
  sessionExpired: boolean;
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (permissions: readonly string[]) => boolean;
  login: (email: string, password: string) => Promise<SessionUser>;
  /** Adopts a session established by another sign-in flow (e.g. Google); returns the signed-in user. */
  startSession: (session: AuthResponse) => SessionUser;
  logout: () => Promise<void>;
  register: (request: Omit<RegisterRequest, 'deviceId'>) => Promise<string>;
  /** Re-reads the signed-in user (e.g. after verifying email). */
  refreshUser: () => Promise<SessionUser | null>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
