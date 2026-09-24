import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { ErrorState } from '@/components/ui/ErrorState';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { KeyValueList } from '@/components/ui/KeyValueList';
import { PageHeader } from '@/components/ui/PageHeader';
import { SkeletonText } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { humanize } from '@/lib/format/text';
import { EMAIL_API, emailKeys, useSubscriber } from '../api/queries';
import type { SubscriberDetail } from '../api/types';
import { ConsentBadge, SubscriberStatusBadge, TierBadge } from '../shared/ui';

/** One contact: consent per channel with history, lists, tags, custom fields and activity. */
export function SubscriberPage() {
  const { id } = useParams();
  const subscriber = useSubscriber(id);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const toast = useToast();
  const [tag, setTag] = useState('');
  const [withdrawOpen, setWithdrawOpen] = useState(false);
  const [eraseOpen, setEraseOpen] = useState(false);
  const refresh = (s: SubscriberDetail) => queryClient.setQueryData(emailKeys.subscriber(s.id), s);

  const tags = useMutation({
    mutationFn: (body: { add?: string[]; remove?: string[] }) => api.post<SubscriberDetail>(`${EMAIL_API}/subscribers/${id}/tags`, body),
    onSuccess: (s) => {
      refresh(s);
      setTag('');
    },
    onError: (e) => toast.error('Could not change tags', errorMessage(e)),
  });

  if (subscriber.isPending) return <SkeletonText lines={8} />;
  if (subscriber.isError) return <ErrorState error={subscriber.error} onRetry={() => void subscriber.refetch()} />;
  const s = subscriber.data;
  const name = [s.firstName, s.lastName].filter(Boolean).join(' ') || s.email || s.phone || 'Contact';

  return (
    <>
      <PageHeader
        title={name}
        breadcrumbs={[{ label: 'Audience', to: '/agency/email/lists' }, { label: name }]}
        meta={
          <span className="cluster">
            <SubscriberStatusBadge status={s.status} />
            <TierBadge tier={s.tier} />
            {s.emailSuppressed && <Badge tone="danger" size="sm">Email suppressed</Badge>}
            {s.smsSuppressed && <Badge tone="danger" size="sm">SMS suppressed</Badge>}
          </span>
        }
        actions={
          <>
            <Button variant="secondary" onClick={() => setWithdrawOpen(true)} disabled={s.emailConsent === 'Withdrawn'}>
              Record consent withdrawal
            </Button>
            <Button variant="danger" onClick={() => setEraseOpen(true)}>
              Erase contact
            </Button>
          </>
        }
      />
      <div className="email-two-col">
        <div className="stack">
          <Card>
            <CardHeader title="Details" />
            <CardBody>
              <KeyValueList
                items={[
                  { label: 'Email', value: s.email ?? '—' },
                  { label: 'Phone', value: s.phone ?? '—' },
                  { label: 'Country', value: s.countryCode ?? '—' },
                  { label: 'Language', value: s.language ?? '—' },
                  { label: 'Time zone', value: s.timeZone ?? 'Workspace default' },
                  { label: 'Source', value: humanize(s.source) },
                  { label: 'Email frequency', value: s.frequency },
                  { label: 'Added', value: <DateTime value={s.createdAt} format="datetime" /> },
                  ...Object.entries(s.customFields).map(([k, v]) => ({ label: `custom.${k}`, value: v })),
                ]}
              />
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Consent" />
            <CardBody className="stack">
              <div className="cluster">
                <ConsentBadge status={s.emailConsent} channel="Email" />
                <ConsentBadge status={s.smsConsent} channel="SMS" />
                <ConsentBadge status={s.whatsAppConsent} channel="WhatsApp" />
              </div>
              <DataTable
                caption="Consent history"
                rows={s.consentHistory}
                getRowId={(c) => `${c.recordedAt}-${c.channel}-${c.status}`}
                columns={[
                  { id: 'when', header: 'When', primary: true, cell: (c) => <DateTime value={c.recordedAt} format="datetime" /> },
                  { id: 'channel', header: 'Channel', cell: (c) => c.channel },
                  { id: 'status', header: 'Status', cell: (c) => <ConsentBadge status={c.status} /> },
                  { id: 'source', header: 'Source', cell: (c) => humanize(c.source) },
                  { id: 'evidence', header: 'Evidence', hideOnMobile: true, cell: (c) => [c.consentTextVersion && `text ${c.consentTextVersion}`, c.hasIpHash && 'IP (hashed)', c.note].filter(Boolean).join(' · ') || '—' },
                ]}
              />
            </CardBody>
          </Card>
        </div>
        <div className="stack">
          <Card>
            <CardHeader title="Lists" />
            <CardBody>
              <ul>
                {s.lists.map((l) => (
                  <li key={l.listId}>
                    {l.listName} — {l.status}
                  </li>
                ))}
                {s.lists.length === 0 && <li>Not on any list.</li>}
              </ul>
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Tags" />
            <CardBody className="stack">
              <div className="cluster">
                {s.tags.map((t) => (
                  <Button key={t} size="sm" variant="secondary" onClick={() => tags.mutate({ remove: [t] })} aria-label={`Remove tag ${t}`}>
                    {t} ×
                  </Button>
                ))}
                {s.tags.length === 0 && <span className="email-muted">No tags.</span>}
              </div>
              <form
                className="cluster"
                onSubmit={(e) => {
                  e.preventDefault();
                  if (tag.trim()) tags.mutate({ add: [tag.trim()] });
                }}
              >
                <FormField label="Add tag">
                  <Input value={tag} onChange={(e) => setTag(e.target.value)} maxLength={50} />
                </FormField>
                <Button type="submit" size="sm" loading={tags.isPending}>
                  Add
                </Button>
              </form>
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Activity" />
            <CardBody>
              {s.activity.length === 0 ? (
                <p className="email-muted">No activity yet.</p>
              ) : (
                <ul className="stack">
                  {s.activity.map((a, i) => (
                    <li key={i}>
                      <strong>{humanize(a.type)}</strong>
                      {a.isMachine && ' (machine)'} {a.campaignName && `· ${a.campaignName}`} · <DateTime value={a.occurredAt} format="relative" />
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </div>
      </div>
      {s.emailSuppressed && <Alert tone="warning">This address is on the suppression list; it will not receive any email.</Alert>}
      <ConfirmDialog
        open={withdrawOpen}
        onClose={() => setWithdrawOpen(false)}
        title="Record consent withdrawal?"
        description="The contact will stop receiving marketing email. Record where the request came from."
        requireReason
        reasonLabel="Source of the request"
        onConfirm={async ({ reason }) => {
          refresh(await api.post<SubscriberDetail>(`${EMAIL_API}/subscribers/${id}/consent`, { channel: 'Email', status: 'Withdrawn', source: reason }));
        }}
      />
      <ConfirmDialog
        open={eraseOpen}
        onClose={() => setEraseOpen(false)}
        tone="danger"
        title="Erase this contact?"
        description="Deletes the contact, consent history and activity (GDPR erasure). This cannot be undone."
        confirmLabel="Erase"
        confirmText="ERASE"
        onConfirm={async () => {
          await api.delete(`${EMAIL_API}/subscribers/${id}`);
          navigate('/agency/email/lists');
          // The erased contact is gone: refetching its own (still mounted) query would only produce a 404.
          const erased = emailKeys.subscriber(id ?? '').join('/');
          void queryClient.invalidateQueries({
            queryKey: emailKeys.all,
            predicate: (query) => query.queryKey.join('/') !== erased,
          });
        }}
      />
    </>
  );
}
