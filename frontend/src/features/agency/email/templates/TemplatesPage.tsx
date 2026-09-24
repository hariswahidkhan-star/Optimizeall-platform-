import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Archive, ArchiveRestore, Copy, LayoutTemplate, Pencil, Plus } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Checkbox } from '@/components/ui/Checkbox';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { Card, CardBody } from '@/components/ui/Card';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { PageHeader } from '@/components/ui/PageHeader';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { humanize } from '@/lib/format/text';
import { EMAIL_API, emailKeys, useTemplates } from '../api/queries';
import type { Template, TemplateListItem } from '../api/types';
import { useEmailWorkspace } from '../shared/workspace';

export function TemplatesPage() {
  const { clientId, key } = useEmailWorkspace();
  const [showArchived, setShowArchived] = useState(false);
  const active = useTemplates(clientId);
  const all = useQuery({
    queryKey: [...emailKeys.templates(key), 'all'],
    queryFn: ({ signal }) => api.get<TemplateListItem[]>(`${EMAIL_API}/templates/all`, { query: clientId ? { clientId } : {}, signal }),
    enabled: showArchived,
  });
  const templates = showArchived ? all : active;
  const [archiving, setArchiving] = useState<TemplateListItem | null>(null);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const toast = useToast();
  const duplicate = useMutation({
    mutationFn: (id: string) => api.post<Template>(`${EMAIL_API}/templates/${id}/duplicate`, undefined, { query: clientId ? { clientId } : {} }),
    onSuccess: (t) => {
      void queryClient.invalidateQueries({ queryKey: emailKeys.templates(key) });
      navigate(t.id);
    },
    onError: (e) => toast.error('Could not copy the template', errorMessage(e)),
  });
  const restore = useMutation({
    mutationFn: (t: TemplateListItem) => api.post<Template>(`${EMAIL_API}/templates/${t.id}/restore`),
    onSuccess: (t) => {
      toast.success('Template restored', t.name);
      void queryClient.invalidateQueries({ queryKey: emailKeys.templates(key) });
    },
    onError: (e) => toast.error('Could not restore the template', errorMessage(e)),
  });

  return (
    <>
      <PageHeader
        title="Templates"
        description="Block-based, responsive templates. Agency starter templates can be copied into any client workspace."
        actions={
          <ButtonLink to="new" leadingIcon={<Plus />}>
            New template
          </ButtonLink>
        }
      />
      <Checkbox label="Show archived templates" checked={showArchived} onChange={(e) => setShowArchived(e.target.checked)} />
      <Card>
        <CardBody>
          {templates.isError ? (
            <ErrorState error={templates.error} onRetry={() => void templates.refetch()} />
          ) : (
            <DataTable
              caption="Email templates"
              rows={templates.data ?? []}
              getRowId={(t) => t.id}
              rowLabel={(t) => t.name}
              loading={templates.isPending}
              rowActions={(t) => [
                { id: 'edit', label: 'Edit', icon: <Pencil />, to: t.id },
                { id: 'duplicate', label: t.isGlobal && clientId ? 'Copy to this workspace' : 'Duplicate', icon: <Copy />, onSelect: () => duplicate.mutate(t.id) },
                t.isArchived
                  ? { id: 'restore', label: 'Restore', icon: <ArchiveRestore />, onSelect: () => restore.mutate(t) }
                  : {
                      id: 'archive',
                      label: 'Archive',
                      icon: <Archive />,
                      danger: true,
                      disabled: t.isGlobal && !!clientId,
                      description: t.isGlobal && clientId ? 'Agency library templates are archived from the agency workspace.' : undefined,
                      onSelect: () => setArchiving(t),
                    },
              ]}
              columns={[
                {
                  id: 'name',
                  header: 'Template',
                  primary: true,
                  cell: (t) => (
                    <span className="email-cell-stack">
                      <Link className="ui-link" to={t.id}>
                        {t.name}
                      </Link>
                      <span className="email-muted">{t.subject}</span>
                      {t.isArchived && <Badge size="sm">Archived</Badge>}
                    </span>
                  ),
                },
                { id: 'category', header: 'Category', cell: (t) => humanize(t.category) },
                {
                  id: 'scope',
                  header: 'Library',
                  cell: (t) => (t.isGlobal ? <Badge tone="brand" size="sm">Agency library</Badge> : <Badge size="sm">This workspace</Badge>),
                },
                { id: 'updated', header: 'Updated', hideOnMobile: true, cell: (t) => <DateTime value={t.updatedAt} format="relative" /> },
                {
                  id: 'actions',
                  header: <span className="visually-hidden">Actions</span>,
                  cell: (t) =>
                    t.isGlobal && clientId ? (
                      <Button size="sm" variant="secondary" loading={duplicate.isPending && duplicate.variables === t.id} onClick={() => duplicate.mutate(t.id)}>
                        Copy to workspace
                      </Button>
                    ) : null,
                },
              ]}
              emptyState={<EmptyState icon={<LayoutTemplate />} headingLevel={2} title="No templates" description="Create your first template." />}
            />
          )}
        </CardBody>
      </Card>
      <ConfirmDialog
        open={archiving !== null}
        onClose={() => setArchiving(null)}
        tone="danger"
        title="Archive this template?"
        description="It is hidden from the template picker. Campaigns and journeys that already use it keep working; you can restore it."
        confirmLabel="Archive"
        onConfirm={async () => {
          if (!archiving) return;
          await api.delete(`${EMAIL_API}/templates/${archiving.id}`);
          toast.success('Template archived');
          void queryClient.invalidateQueries({ queryKey: emailKeys.templates(key) });
        }}
      />
    </>
  );
}
