import { useQuery } from '@tanstack/react-query';
import { FlaskConical } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { defaultLandingPath } from '@/app/portals';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { AuthResponse } from '@/lib/api/types';
import { useAuth } from '@/lib/auth/useAuth';

export interface DevTestAccount {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  isTestAccount: boolean;
}

function roleText(roles: string[]): string {
  return roles.map((r) => r.replace(/([a-z])([A-Z])/g, '$1 $2')).join(', ') || 'No role';
}

/**
 * Non-production helper on the sign-in page: one-click sign-in as a test or demo account. The API answers 404 unless
 * DevTools:TestLoginEnabled is on outside Production, in which case this renders nothing.
 */
export function TestAccountsPanel() {
  const { startSession } = useAuth();
  const navigate = useNavigate();
  const [pendingId, setPendingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const accounts = useQuery({
    queryKey: ['dev', 'test-accounts'],
    queryFn: ({ signal }) => api.get<DevTestAccount[]>('/dev/test-accounts', { signal, skipRefresh: true }),
    retry: false,
    staleTime: 60_000,
  });

  if (!accounts.data || accounts.data.length === 0) return null;

  const signIn = async (account: DevTestAccount) => {
    setPendingId(account.id);
    setError(null);
    try {
      const session = await api.post<AuthResponse>(
        '/dev/test-login',
        { userId: account.id },
        { skipRefresh: true },
      );
      const user = startSession(session);
      navigate(defaultLandingPath(user.permissions), { replace: true });
    } catch (e) {
      setError(errorMessage(e));
      setPendingId(null);
    }
  };

  const groups = [
    { key: 'test', title: 'Test accounts', items: accounts.data.filter((a) => a.isTestAccount) },
    { key: 'demo', title: 'Demo accounts', items: accounts.data.filter((a) => !a.isTestAccount) },
  ].filter((g) => g.items.length > 0);

  return (
    <section className="auth-test-accounts stack" aria-labelledby="test-accounts-title">
      <h2 id="test-accounts-title" className="auth-test-accounts__title">
        <FlaskConical aria-hidden="true" /> Test accounts
        <Badge size="sm" tone="warning">
          Not production
        </Badge>
      </h2>
      <p className="text-small text-muted">Sign in as any test or demo account with one click.</p>
      {error && (
        <Alert tone="danger" role="alert" title="Couldn’t sign in">
          {error}
        </Alert>
      )}
      {groups.map((group) => (
        <div key={group.key} className="stack">
          <h3 className="text-small">{group.title}</h3>
          <ul className="auth-test-accounts__list">
            {group.items.map((account) => (
              <li key={account.id}>
                <Button
                  variant="secondary"
                  size="sm"
                  fullWidth
                  loading={pendingId === account.id}
                  disabled={pendingId !== null && pendingId !== account.id}
                  aria-label={`Sign in as ${account.displayName} (${roleText(account.roles)}), ${account.email}`}
                  onClick={() => void signIn(account)}
                >
                  <span className="auth-test-accounts__name">{account.displayName}</span>
                  <span className="text-small text-muted">{roleText(account.roles)}</span>
                </Button>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </section>
  );
}
