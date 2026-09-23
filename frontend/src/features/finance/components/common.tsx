import { Download, Lock } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Alert, Button, ErrorState, useToast, type ButtonProps } from '@/components/ui';
import { api, type QueryParams } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import { financeErrorMessage } from '../api/errors';
import type { UserRef } from '../api/types';

/** Name + email of a participant, optionally linking to their balance. */
export function PersonCell({ user, link }: { user: UserRef | null | undefined; link?: boolean }) {
  if (!user) return <span className="text-muted">System</span>;
  const name = user.displayName || user.email || user.id;
  return (
    <span className="fin-person">
      {link ? (
        <Link className="ui-link fin-person__name" to={`/finance/ledger/users/${user.id}`}>
          {name}
        </Link>
      ) : (
        <span className="fin-person__name">{name}</span>
      )}
      {user.email && user.email !== name && <span className="fin-person__email">{user.email}</span>}
    </span>
  );
}

/** Button that downloads a CSV with the current session, with a pending state and an error toast. */
export function DownloadButton({
  path,
  fileName,
  query,
  children,
  variant = 'secondary',
  size = 'sm',
}: {
  path: string;
  fileName: string;
  query?: QueryParams;
  children: ReactNode;
  variant?: ButtonProps['variant'];
  size?: ButtonProps['size'];
}) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  return (
    <Button
      variant={variant}
      size={size}
      leadingIcon={<Download />}
      loading={busy}
      onClick={async () => {
        setBusy(true);
        try {
          await api.download(path, fileName, { query });
        } catch (error) {
          toast.error('Download failed', financeErrorMessage(error));
        } finally {
          setBusy(false);
        }
      }}
    >
      {children}
    </Button>
  );
}

/** Error panel for a failed query; 403 gets a permission explanation instead of a generic failure. */
export function QueryError({
  error,
  onRetry,
  title,
  compact = true,
}: {
  error: unknown;
  onRetry?: () => void;
  title?: string;
  compact?: boolean;
}) {
  if (isApiError(error) && error.status === 403) {
    return (
      <Alert tone="warning" icon={<Lock />} title="No access">
        {financeErrorMessage(error)}
      </Alert>
    );
  }
  return <ErrorState compact={compact} headingLevel={3} error={error} title={title} onRetry={onRetry} />;
}

/** "3 of 10" style muted count label. */
export function Muted({ children }: { children: ReactNode }) {
  return <span className="text-muted text-small">{children}</span>;
}
