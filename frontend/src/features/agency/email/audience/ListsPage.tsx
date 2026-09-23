import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus, Users } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody } from '@/components/ui/Card';
import { DataTable } from '@/components/ui/DataTable';
import { Dialog } from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { Switch } from '@/components/ui/Switch';
import { Textarea } from '@/components/ui/Textarea';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import { EMAIL_API, emailKeys, useLists } from '../api/queries';
import type { EmailList } from '../api/types';
import { useEmailWorkspace } from '../shared/workspace';

export function ListFormDialog({ open, onClose, list }: { open: boolean; onClose: () => void; list?: EmailList }) {
  const { clientId, key } = useEmailWorkspace();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [name, setName] = useState(list?.name ?? '');
  const [description, setDescription] = useState(list?.description ?? '');
  const [doubleOptIn, setDoubleOptIn] = useState(list?.doubleOptIn ?? true);
  const [topic, setTopic] = useState(list?.showInPreferenceCenter ?? true);
  const [consentText, setConsentText] = useState(list?.consentText ?? 'Yes, send me news and offers by email. I can unsubscribe at any time.');
  const [version, setVersion] = useState(list?.consentTextVersion ?? 'v1');
  const save = useMutation({
    mutationFn: () => {
      const body = {
        clientAccountId: list ? list.clientAccountId : clientId,
        name,
        description,
        doubleOptIn,
        showInPreferenceCenter: topic,
        consentText,
        consentTextVersion: version,
        concurrencyStamp: list?.concurrencyStamp,
      };
      return list ? api.put<EmailList>(`${EMAIL_API}/lists/${list.id}`, body) : api.post<EmailList>(`${EMAIL_API}/lists`, body);
    },
    onSuccess: () => {
      toast.success(list ? 'List updated' : 'List created');
      void queryClient.invalidateQueries({ queryKey: emailKeys.lists(key) });
      if (list) void queryClient.invalidateQueries({ queryKey: emailKeys.list(list.id) });
      onClose();
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };
  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={list ? 'Edit list' : 'New list'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="list-form" loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form id="list-form" className="stack" onSubmit={submit}>
        <FormField label="Name" required>
          <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={150} />
        </FormField>
        <FormField label="Description" optional>
          <Textarea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} maxLength={1000} />
        </FormField>
        <Switch
          checked={doubleOptIn}
          onCheckedChange={setDoubleOptIn}
          label="Double opt-in"
          description="New sign-ups must confirm by email before they receive anything (recommended; required in some countries)."
        />
        <Switch checked={topic} onCheckedChange={setTopic} label="Show as a topic in the preference center" />
        <FormField label="Consent wording on the sign-up form" hint="Stored with every consent record as proof.">
          <Textarea rows={2} value={consentText} onChange={(e) => setConsentText(e.target.value)} maxLength={1000} />
        </FormField>
        <FormField label="Consent text version">
          <Input value={version} onChange={(e) => setVersion(e.target.value)} maxLength={40} />
        </FormField>
        {save.isError && <Alert tone="danger">{errorMessage(save.error)}</Alert>}
      </form>
    </Dialog>
  );
}

export function ListsPage() {
  const { clientId } = useEmailWorkspace();
  const lists = useLists(clientId);
  const [open, setOpen] = useState(false);
  return (
    <>
      <PageHeader
        title="Audience"
        description="Lists of contacts who gave consent. The workspace suppression list always wins over any list."
        actions={
          <Button leadingIcon={<Plus />} onClick={() => setOpen(true)}>
            New list
          </Button>
        }
      />
      <Card>
        <CardBody>
          {lists.isError ? (
            <ErrorState error={lists.error} onRetry={() => void lists.refetch()} />
          ) : (
            <DataTable
              caption="Lists"
              rows={lists.data ?? []}
              getRowId={(l) => l.id}
              loading={lists.isPending}
              columns={[
                {
                  id: 'name',
                  header: 'List',
                  primary: true,
                  cell: (l) => (
                    <span className="email-cell-stack">
                      <Link className="ui-link" to={l.id}>
                        {l.name}
                      </Link>
                      {l.description && <span className="email-muted">{l.description}</span>}
                    </span>
                  ),
                },
                { id: 'subscribed', header: 'Subscribed', align: 'right', cell: (l) => formatNumber(l.subscribed) },
                { id: 'pending', header: 'Awaiting confirmation', align: 'right', hideOnMobile: true, cell: (l) => formatNumber(l.pending) },
                { id: 'unsubscribed', header: 'Unsubscribed', align: 'right', hideOnMobile: true, cell: (l) => formatNumber(l.unsubscribed) },
                { id: 'doi', header: 'Opt-in', cell: (l) => (l.doubleOptIn ? <Badge tone="success" size="sm">Double</Badge> : <Badge size="sm">Single</Badge>) },
              ]}
              emptyState={<EmptyState icon={<Users />} headingLevel={2} title="No lists yet" description="Create a list, then import contacts or share the sign-up form." />}
            />
          )}
        </CardBody>
      </Card>
      {open && <ListFormDialog open onClose={() => setOpen(false)} />}
    </>
  );
}
