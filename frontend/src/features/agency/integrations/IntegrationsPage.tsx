import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AlertTriangle, PlugZap } from 'lucide-react';
import { useState } from 'react';
import { Badge, Button, ConfirmDialog, DateTime, ErrorState, FormField, PageHeader, Select, Skeleton, useToast } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useClientOptions } from '../seo/common';
import { categoryLabels, integrationKeys, statusTone, type Connection, type IntegrationCategory, type Provider } from './api';
import { ConnectionDialog } from './ConnectionDialog';
import './integrations.css';

const AGENCY = 'agency';

/** Provider catalogue with connection status for the chosen scope (agency-wide or one client). */
export function IntegrationsPage() {
  const [scope, setScope] = useState<string>(AGENCY);
  const [editing, setEditing] = useState<{ provider: Provider; connection?: Connection } | null>(null);
  const [confirm, setConfirm] = useState<{ kind: 'disconnect' | 'delete'; connection: Connection } | null>(null);
  const toast = useToast();
  const queryClient = useQueryClient();
  const clients = useClientOptions('integrations');
  const providers = useQuery({ queryKey: integrationKeys.providers, queryFn: () => api.get<Provider[]>('/agency/integrations/providers'), staleTime: 30 * 60_000 });
  const connections = useQuery({
    queryKey: integrationKeys.connections(scope),
    queryFn: () =>
      api.get<Connection[]>('/agency/integrations/connections', { query: scope === AGENCY ? { scope: 'agency' } : { clientId: scope } }),
  });

  const test = useMutation({
    mutationFn: (c: Connection) => api.post<Connection>(`/agency/integrations/connections/${c.id}/test`),
    onSuccess: (c) => {
      if (c.status === 'Connected') toast.success(`${c.providerName} is working`, c.statusMessage ?? undefined);
      else toast.error(`${c.providerName} check failed`, c.statusMessage ?? undefined);
      void queryClient.invalidateQueries({ queryKey: integrationKeys.all });
    },
    onError: (err) => toast.error('Test failed', errorMessage(err)),
  });

  const agencyScope = scope === AGENCY;
  const available = (providers.data ?? []).filter((p) => (agencyScope ? p.agencyWide : p.perClient));
  const byCategory = new Map<IntegrationCategory, Provider[]>();
  for (const p of available) byCategory.set(p.category, [...(byCategory.get(p.category) ?? []), p]);
  const connectionFor = (p: Provider) => (connections.data ?? []).find((c) => c.provider === p.key);
  const attention = (connections.data ?? []).filter((c) => c.status === 'Error' || c.expiringSoon);

  return (
    <>
      <PageHeader
        title="Integrations"
        description="Connect social, ads, messaging, email and SEO providers. Credentials are encrypted and write-only."
      />
      <div className="stack">
        <div className="int-scope">
          <FormField label="Connections for">
            <Select
              value={scope}
              onChange={(e) => setScope(e.target.value)}
              options={[{ value: AGENCY, label: 'The agency (shared)' }, ...(clients.data ?? []).map((c) => ({ value: c.id, label: c.name }))]}
            />
          </FormField>
          <p className="text-small text-muted">
            {agencyScope
              ? 'Agency-wide connections are shared by every client (email delivery, SMS, SEO data, payments).'
              : 'Client connections publish to and read from this client’s own accounts.'}
          </p>
        </div>
        {attention.length > 0 && (
          <div className="int-attention" role="status">
            <AlertTriangle aria-hidden="true" />
            <span>
              {attention.length} connection{attention.length === 1 ? ' needs' : 's need'} attention:{' '}
              {attention.map((c) => `${c.providerName} (${c.status === 'Error' ? 'error' : 'token expiring'})`).join(', ')}.
            </span>
          </div>
        )}
        {providers.isError || connections.isError ? (
          <ErrorState error={providers.error ?? connections.error} onRetry={() => (void providers.refetch(), void connections.refetch())} />
        ) : !providers.data || connections.isLoading ? (
          <Skeleton height="16rem" />
        ) : (
          [...byCategory.entries()].map(([category, list]) => (
            <section key={category} className="stack" aria-labelledby={`int-cat-${category}`}>
              <h2 id={`int-cat-${category}`} className="int-category">
                {categoryLabels[category]}
              </h2>
              <ul className="int-grid">
                {list.map((p) => {
                  const c = connectionFor(p);
                  const connected = c && c.status !== 'Disconnected';
                  return (
                    <li key={p.key}>
                      <article className="int-card" aria-labelledby={`int-${p.key}`}>
                        <header className="int-card__head">
                          <h3 id={`int-${p.key}`}>{p.name}</h3>
                          {c ? <Badge tone={statusTone[c.status]}>{c.status}</Badge> : <Badge tone="neutral">Not connected</Badge>}
                        </header>
                        <p className="int-card__desc">{p.description}</p>
                        {c && (
                          <dl className="int-card__meta">
                            {c.statusMessage && (
                              <>
                                <dt>Status</dt>
                                <dd>{c.statusMessage}</dd>
                              </>
                            )}
                            {c.lastVerifiedAt && (
                              <>
                                <dt>Last checked</dt>
                                <dd>
                                  <DateTime value={c.lastVerifiedAt} format="relative" />
                                </dd>
                              </>
                            )}
                            {c.expiresAt && (
                              <>
                                <dt>Token expires</dt>
                                <dd>
                                  <DateTime value={c.expiresAt} format="date" /> {c.expiringSoon && <Badge tone="warning">Soon</Badge>}
                                </dd>
                              </>
                            )}
                            {connected && c.secrets.length > 0 && (
                              <>
                                <dt>Credentials</dt>
                                <dd>{c.secrets.map((s) => `${s.label}: ${s.saved ? 'saved' : 'not set'}`).join(' · ')}</dd>
                              </>
                            )}
                          </dl>
                        )}
                        <div className="int-card__actions">
                          {!c || c.status === 'Disconnected' ? (
                            <Button size="sm" leadingIcon={<PlugZap />} onClick={() => setEditing({ provider: p, connection: c })} aria-label={`${c ? 'Reconnect' : 'Connect'} ${p.name}`}>
                              {c ? 'Reconnect' : 'Connect'}
                            </Button>
                          ) : (
                            <>
                              <Button size="sm" variant="secondary" onClick={() => setEditing({ provider: p, connection: c })} aria-label={`Edit ${p.name}`}>
                                Edit
                              </Button>
                              {p.supportsVerification && (
                                <Button
                                  size="sm"
                                  variant="secondary"
                                  loading={test.isPending && test.variables?.id === c.id}
                                  onClick={() => test.mutate(c)}
                                  aria-label={`Test ${p.name}`}
                                >
                                  Test
                                </Button>
                              )}
                              <Button size="sm" variant="ghost" onClick={() => setConfirm({ kind: 'disconnect', connection: c })} aria-label={`Disconnect ${p.name}`}>
                                Disconnect
                              </Button>
                            </>
                          )}
                          {c?.status === 'Disconnected' && (
                            <Button size="sm" variant="ghost" onClick={() => setConfirm({ kind: 'delete', connection: c })} aria-label={`Delete ${p.name} connection`}>
                              Delete
                            </Button>
                          )}
                        </div>
                      </article>
                    </li>
                  );
                })}
              </ul>
            </section>
          ))
        )}
      </div>
      {editing && (
        <ConnectionDialog
          provider={editing.provider}
          connection={editing.connection}
          clientAccountId={agencyScope ? null : scope}
          onClose={() => setEditing(null)}
        />
      )}
      {confirm && (
        <ConfirmDialog
          open
          onClose={() => setConfirm(null)}
          tone="danger"
          title={confirm.kind === 'disconnect' ? `Disconnect ${confirm.connection.providerName}?` : `Delete the ${confirm.connection.providerName} connection?`}
          description={
            confirm.kind === 'disconnect'
              ? 'Saved credentials are erased immediately and features using this provider stop working until you reconnect.'
              : 'The connection record is removed permanently.'
          }
          confirmLabel={confirm.kind === 'disconnect' ? 'Disconnect' : 'Delete'}
          onConfirm={async () => {
            const c = confirm.connection;
            if (confirm.kind === 'disconnect') await api.post(`/agency/integrations/connections/${c.id}/disconnect`);
            else await api.delete(`/agency/integrations/connections/${c.id}`);
            toast.success(confirm.kind === 'disconnect' ? `${c.providerName} disconnected` : 'Connection deleted');
            await queryClient.invalidateQueries({ queryKey: integrationKeys.all });
            setConfirm(null);
          }}
        />
      )}
    </>
  );
}
