import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Filter, Plus, Save } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { SkeletonText } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { EMAIL_API, emailKeys, useCampaigns, useLists, useSegment, useSegments } from '../api/queries';
import type { Segment, SegmentDefinition, SegmentPreview } from '../api/types';
import { useEmailWorkspace } from '../shared/workspace';
import { SegmentBuilder, emptyDefinition } from './SegmentBuilder';

export function SegmentsPage() {
  const { clientId } = useEmailWorkspace();
  const segments = useSegments(clientId);
  return (
    <>
      <PageHeader
        title="Segments"
        description="Saved audience rules evaluated on the server whenever a campaign is sent."
        actions={
          <ButtonLink to="new" leadingIcon={<Plus />}>
            New segment
          </ButtonLink>
        }
      />
      <Card>
        <CardBody>
          {segments.isError ? (
            <ErrorState error={segments.error} onRetry={() => void segments.refetch()} />
          ) : (
            <DataTable
              caption="Segments"
              rows={segments.data ?? []}
              getRowId={(s) => s.id}
              loading={segments.isPending}
              columns={[
                { id: 'name', header: 'Segment', primary: true, cell: (s) => <Link className="ui-link" to={s.id}>{s.name}</Link> },
                { id: 'count', header: 'Contacts (last count)', align: 'right', cell: (s) => (s.lastCount === null ? '—' : formatNumber(s.lastCount)) },
                { id: 'counted', header: 'Counted', hideOnMobile: true, cell: (s) => (s.lastCountedAt ? <DateTime value={s.lastCountedAt} format="relative" /> : '—') },
              ]}
              emptyState={<EmptyState icon={<Filter />} headingLevel={2} title="No segments yet" description="Target contacts by field, tag, engagement or purchases." />}
            />
          )}
        </CardBody>
      </Card>
    </>
  );
}

/** True while a list condition (e.g. "country is any of") has no values yet — the API refuses to count those (400). */
export function awaitsValues(definition: SegmentDefinition): boolean {
  return (
    definition.conditions.some((c) => c.values !== undefined && c.values.length === 0) ||
    definition.groups.some(awaitsValues)
  );
}

/** Live count of contacts matching unsaved rules (debounced). */
export function SegmentCount({ clientId, definition }: { clientId: string | null; definition: SegmentDefinition }) {
  const debounced = useDebouncedValue(definition, 400);
  const incomplete = awaitsValues(debounced);
  const preview = useQuery({
    queryKey: ['email', 'segment-preview', clientId, debounced],
    queryFn: ({ signal }) => api.post<SegmentPreview>(`${EMAIL_API}/segments/preview`, { clientAccountId: clientId, definition: debounced }, { signal }),
    placeholderData: (prev) => prev,
    retry: false,
    // A new segment starts with an empty "country is any of" rule: count once it has values, not before.
    enabled: !incomplete,
  });
  return (
    <div aria-live="polite" className="stack">
      {incomplete ? (
        <p className="email-muted">Choose at least one value for each rule to count matching contacts.</p>
      ) : preview.isError ? (
        <Alert tone="warning" title="Rules incomplete">
          {isApiError(preview.error) && preview.error.errors ? Object.values(preview.error.errors).flat().join(' ') : errorMessage(preview.error)}
        </Alert>
      ) : preview.data ? (
        <>
          <p className="segment-count">
            <strong className="tabular">{formatNumber(preview.data.count)}</strong>
            <span className="email-muted">of {formatNumber(preview.data.total)} contacts match</span>
          </p>
          {preview.data.sample.length > 0 && (
            <ul className="email-muted">
              {preview.data.sample.map((s) => (
                <li key={s.id}>
                  {[s.firstName, s.lastName].filter(Boolean).join(' ') || '—'} · {s.email ?? 'no email'}
                </li>
              ))}
            </ul>
          )}
        </>
      ) : (
        <p className="email-muted">Counting…</p>
      )}
    </div>
  );
}

export function SegmentEditorPage() {
  const { id } = useParams();
  const isNew = !id || id === 'new';
  const segment = useSegment(isNew ? undefined : id);
  if (!isNew && segment.isPending) return <SkeletonText lines={6} />;
  if (!isNew && segment.isError) return <ErrorState error={segment.error} onRetry={() => void segment.refetch()} />;
  return <SegmentForm key={segment.data?.concurrencyStamp ?? 'new'} segment={isNew ? null : segment.data!} />;
}

function SegmentForm({ segment }: { segment: Segment | null }) {
  const { clientId, key } = useEmailWorkspace();
  const workspaceId = segment ? segment.clientAccountId : clientId;
  const [name, setName] = useState(segment?.name ?? '');
  const [definition, setDefinition] = useState<SegmentDefinition>(segment?.definition ?? emptyDefinition());
  const lists = useLists(workspaceId);
  const campaigns = useCampaigns(workspaceId, 'email', { page: 1, pageSize: 100 });
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const toast = useToast();
  const save = useMutation({
    mutationFn: () => {
      const body = { clientAccountId: workspaceId, name, definition, concurrencyStamp: segment?.concurrencyStamp };
      return segment ? api.put<Segment>(`${EMAIL_API}/segments/${segment.id}`, body) : api.post<Segment>(`${EMAIL_API}/segments`, body);
    },
    onSuccess: (saved) => {
      toast.success('Segment saved', `${formatNumber(saved.lastCount ?? 0)} contacts match.`);
      void queryClient.invalidateQueries({ queryKey: emailKeys.segments(key) });
      queryClient.setQueryData(emailKeys.segment(saved.id), saved);
      if (!segment) navigate(`../${saved.id}`, { relative: 'path' });
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };
  return (
    <>
      <PageHeader
        title={segment ? segment.name : 'New segment'}
        breadcrumbs={[{ label: 'Segments', to: '/agency/email/segments' }, { label: segment ? segment.name : 'New' }]}
        actions={
          <Button type="submit" form="segment-form" leadingIcon={<Save />} loading={save.isPending}>
            Save segment
          </Button>
        }
      />
      {save.isError && <Alert tone="danger" title="Could not save">{errorMessage(save.error)}</Alert>}
      <div className="email-two-col">
        <form id="segment-form" onSubmit={submit} className="stack" aria-label="Segment editor">
          <FormField label="Segment name" required>
            <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={150} />
          </FormField>
          <SegmentBuilder
            definition={definition}
            onChange={setDefinition}
            lists={(lists.data ?? []).map((l) => ({ value: l.id, label: l.name }))}
            campaigns={(campaigns.data?.items ?? []).map((c) => ({ value: c.id, label: c.name }))}
          />
        </form>
        <Card>
          <CardHeader title="Live count" description="Only contacts in this workspace. Campaigns still skip contacts without consent or on the suppression list." />
          <CardBody>
            <SegmentCount clientId={workspaceId} definition={definition} />
          </CardBody>
        </Card>
      </div>
    </>
  );
}
