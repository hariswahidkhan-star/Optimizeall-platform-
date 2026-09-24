import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Pencil, Tags, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DataTable } from '@/components/ui/DataTable';
import { Dialog } from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import { EMAIL_API, emailKeys, useAudienceKeys, type AudienceKey } from '../api/queries';
import { useEmailWorkspace } from '../shared/workspace';

type Kind = 'tags' | 'fields';
const LABEL: Record<Kind, { one: string; many: string }> = {
  tags: { one: 'tag', many: 'Tags' },
  fields: { one: 'custom field', many: 'Custom fields' },
};

/** Workspace tags and custom fields: usage, rename (merging into an existing key) and removal from every contact. */
export function TagsFieldsPage() {
  return (
    <>
      <PageHeader
        title="Tags & custom fields"
        description="Rename or remove a tag or custom field for every contact of this workspace. Segments and journeys that mention it are counted so you can update them."
        breadcrumbs={[{ label: 'Audience', to: '/agency/email/lists' }, { label: 'Tags & fields' }]}
      />
      <div className="stack">
        <KeyCard kind="tags" />
        <KeyCard kind="fields" />
      </div>
    </>
  );
}

function KeyCard({ kind }: { kind: Kind }) {
  const { clientId, key } = useEmailWorkspace();
  const keys = useAudienceKeys(kind, clientId);
  const queryClient = useQueryClient();
  const toast = useToast();
  const [renaming, setRenaming] = useState<AudienceKey | null>(null);
  const [deleting, setDeleting] = useState<AudienceKey | null>(null);
  const refresh = () => {
    void queryClient.invalidateQueries({
      queryKey: kind === 'tags' ? emailKeys.tags(key) : emailKeys.fields(key),
    });
    void queryClient.invalidateQueries({ queryKey: ['email', key, 'subscribers'] });
  };
  const label = LABEL[kind];
  return (
    <Card as="section" aria-labelledby={`email-${kind}`}>
      <CardHeader title={label.many} titleId={`email-${kind}`} />
      <CardBody>
        {keys.isError ? (
          <ErrorState error={keys.error} onRetry={() => void keys.refetch()} />
        ) : (
          <DataTable
            caption={label.many}
            rows={keys.data ?? []}
            getRowId={(k) => k.key}
            rowLabel={(k) => k.key}
            loading={keys.isPending}
            columns={[
              {
                id: 'key',
                header: kind === 'tags' ? 'Tag' : 'Field key',
                primary: true,
                cell: (k) => <code>{k.key}</code>,
              },
              {
                id: 'contacts',
                header: 'Contacts',
                align: 'right',
                sortable: true,
                sortValue: (k) => k.contacts,
                cell: (k) => formatNumber(k.contacts),
              },
              {
                id: 'refs',
                header: 'Used by segments / journeys',
                align: 'right',
                cell: (k) => (k.referencedBy > 0 ? formatNumber(k.referencedBy) : '—'),
              },
            ]}
            rowActions={(k) => [
              { id: 'rename', label: 'Rename / merge', icon: <Pencil />, onSelect: () => setRenaming(k) },
              {
                id: 'delete',
                label: `Remove from all contacts`,
                icon: <Trash2 />,
                danger: true,
                onSelect: () => setDeleting(k),
              },
            ]}
            emptyState={
              <EmptyState
                icon={<Tags />}
                compact
                headingLevel={3}
                title={`No ${label.many.toLowerCase()} yet`}
              />
            }
          />
        )}
      </CardBody>
      {renaming && (
        <RenameKeyDialog kind={kind} item={renaming} onClose={() => setRenaming(null)} onSaved={refresh} />
      )}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title={`Remove this ${label.one} from every contact?`}
        description={
          deleting
            ? `“${deleting.key}” is removed from ${formatNumber(deleting.contacts)} contact(s).${
                deleting.referencedBy > 0
                  ? ` ${deleting.referencedBy} segment(s)/journey(s) still mention it — update them too.`
                  : ''
              }`
            : undefined
        }
        confirmText={deleting?.key}
        confirmLabel="Remove"
        onConfirm={async () => {
          if (!deleting) return;
          const query = {
            ...(clientId ? { clientId } : {}),
            [kind === 'tags' ? 'tag' : 'key']: deleting.key,
          };
          await api.delete(`${EMAIL_API}/${kind}`, undefined, { query });
          toast.success(`${kind === 'tags' ? 'Tag' : 'Field'} removed`);
          refresh();
        }}
      />
    </Card>
  );
}

export function RenameKeyDialog({
  kind,
  item,
  onClose,
  onSaved,
}: {
  kind: Kind;
  item: AudienceKey;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { clientId } = useEmailWorkspace();
  const toast = useToast();
  const [to, setTo] = useState(item.key);
  const save = useMutation({
    mutationFn: () =>
      api.post<{ changed: number; merged: number; referencedBy: number }>(`${EMAIL_API}/${kind}/rename`, {
        clientAccountId: clientId,
        from: item.key,
        to: to.trim(),
      }),
    onSuccess: (r) => {
      toast.success(
        `Renamed on ${formatNumber(r.changed + r.merged)} contact(s)`,
        r.referencedBy > 0
          ? `${r.referencedBy} segment(s)/journey(s) still use the old name — update them.`
          : undefined,
      );
      onSaved();
      onClose();
    },
  });
  const fieldError = isApiError(save.error) ? (save.error.fieldError('to') ?? undefined) : undefined;
  return (
    <Dialog
      open
      onClose={onClose}
      title={`Rename “${item.key}”`}
      description="If the new name already exists, the two are merged (contacts keep the existing value)."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={`rename-${kind}`}
            loading={save.isPending}
            disabled={!to.trim() || to.trim() === item.key}
          >
            Rename
          </Button>
        </>
      }
    >
      <form
        id={`rename-${kind}`}
        className="stack"
        onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}
      >
        {save.isError && !fieldError && <Alert tone="danger">{errorMessage(save.error)}</Alert>}
        <FormField
          label="New name"
          error={fieldError}
          hint={
            kind === 'tags'
              ? 'Letters, digits, spaces, dashes or underscores (lower case).'
              : 'Lower-case letters, digits and underscores.'
          }
        >
          <Input value={to} maxLength={100} onChange={(e) => setTo(e.target.value)} />
        </FormField>
        {item.referencedBy > 0 && (
          <Alert tone="warning">
            {item.referencedBy} segment(s) or journey(s) use “{item.key}”. They are not changed automatically
            — edit them after renaming.
          </Alert>
        )}
      </form>
    </Dialog>
  );
}
