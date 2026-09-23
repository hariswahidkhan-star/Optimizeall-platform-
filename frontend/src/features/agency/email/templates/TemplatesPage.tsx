import { useMutation, useQueryClient } from '@tanstack/react-query';
import { LayoutTemplate, Plus } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
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
import type { Template } from '../api/types';
import { useEmailWorkspace } from '../shared/workspace';

export function TemplatesPage() {
  const { clientId, key } = useEmailWorkspace();
  const templates = useTemplates(clientId);
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
      <Card>
        <CardBody>
          {templates.isError ? (
            <ErrorState error={templates.error} onRetry={() => void templates.refetch()} />
          ) : (
            <DataTable
              caption="Email templates"
              rows={templates.data ?? []}
              getRowId={(t) => t.id}
              loading={templates.isPending}
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
    </>
  );
}
