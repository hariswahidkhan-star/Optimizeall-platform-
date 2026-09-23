import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Save, Send } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody } from '@/components/ui/Card';
import { ErrorState } from '@/components/ui/ErrorState';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { Select } from '@/components/ui/Select';
import { SkeletonText } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { EMAIL_API, emailKeys, useTemplate } from '../api/queries';
import type { EmailDesign, Template } from '../api/types';
import { useEmailWorkspace } from '../shared/workspace';
import { BlockEditor, MERGE_TAG_HELP, emptyDesign } from './BlockEditor';
import { EmailPreview } from './EmailPreview';
import { TestSendDialog } from './TestSendDialog';

const CATEGORIES = ['welcome', 'newsletter', 'promo', 'abandoned-cart', 're-engagement', 'event', 'nps', 'transactional', 'other'];

export function TemplateEditorPage() {
  const { id } = useParams();
  const isNew = !id || id === 'new';
  const template = useTemplate(isNew ? undefined : id);
  if (!isNew && template.isPending) return <SkeletonText lines={8} />;
  if (!isNew && template.isError) return <ErrorState error={template.error} onRetry={() => void template.refetch()} />;
  return <TemplateForm key={template.data?.concurrencyStamp ?? 'new'} template={isNew ? null : template.data!} />;
}

function TemplateForm({ template }: { template: Template | null }) {
  const { clientId, key } = useEmailWorkspace();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [name, setName] = useState(template?.name ?? '');
  const [category, setCategory] = useState(template?.category ?? 'newsletter');
  const [subject, setSubject] = useState(template?.subject ?? '');
  const [previewText, setPreviewText] = useState(template?.previewText ?? '');
  const [design, setDesign] = useState<EmailDesign>(template?.design?.blocks ? template.design : emptyDesign());
  const [testOpen, setTestOpen] = useState(false);
  const workspaceId = template ? template.clientAccountId : clientId;

  useEffect(() => {
    if (template) document.title = `${template.name} · Templates`;
  }, [template]);

  const save = useMutation({
    mutationFn: () => {
      const body = { clientAccountId: workspaceId, name, category, subject, previewText: previewText || null, design, concurrencyStamp: template?.concurrencyStamp };
      return template ? api.put<Template>(`${EMAIL_API}/templates/${template.id}`, body) : api.post<Template>(`${EMAIL_API}/templates`, body);
    },
    onSuccess: (saved) => {
      toast.success('Template saved');
      void queryClient.invalidateQueries({ queryKey: emailKeys.templates(key) });
      queryClient.setQueryData(emailKeys.template(saved.id), saved);
      if (!template) navigate(`../${saved.id}`, { relative: 'path' });
    },
  });

  const errors = isApiError(save.error) ? Object.values(save.error.errors ?? {}).flat() : [];
  const submit = (event: FormEvent) => {
    event.preventDefault();
    save.mutate();
  };

  return (
    <>
      <PageHeader
        title={template ? template.name : 'New template'}
        breadcrumbs={[{ label: 'Templates', to: '/agency/email/templates' }, { label: template ? template.name : 'New' }]}
        actions={
          <>
            {template && (
              <Button variant="secondary" leadingIcon={<Send />} onClick={() => setTestOpen(true)}>
                Send test
              </Button>
            )}
            <Button type="submit" form="template-form" leadingIcon={<Save />} loading={save.isPending}>
              Save template
            </Button>
          </>
        }
      />
      {template?.isGlobal && clientId && (
        <Alert tone="info" title="Agency library template">
          Changes affect every workspace that starts from it. Copy it into this workspace to customize it for one client.
        </Alert>
      )}
      {save.isError && (
        <Alert tone="danger" title="Could not save the template">
          {errors.length > 0 ? (
            <ul>
              {errors.map((e) => (
                <li key={e}>{e}</li>
              ))}
            </ul>
          ) : (
            errorMessage(save.error)
          )}
        </Alert>
      )}
      <div className="email-editor">
        <form id="template-form" onSubmit={submit} className="stack" aria-label="Template editor">
          <Card>
            <CardBody className="stack">
              <FormField label="Template name" required>
                <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={150} />
              </FormField>
              <FormField label="Category">
                <Select value={category} onChange={(e) => setCategory(e.target.value)} options={CATEGORIES.map((c) => ({ value: c, label: c }))} />
              </FormField>
              <FormField label="Subject line" required hint={MERGE_TAG_HELP}>
                <Input value={subject} onChange={(e) => setSubject(e.target.value)} required maxLength={200} />
              </FormField>
              <FormField label="Preview text" optional hint="Shown after the subject in most inboxes.">
                <Input value={previewText} onChange={(e) => setPreviewText(e.target.value)} maxLength={200} />
              </FormField>
            </CardBody>
          </Card>
          <BlockEditor design={design} onChange={setDesign} />
        </form>
        <EmailPreview clientId={workspaceId} subject={subject} previewText={previewText} design={design} />
      </div>
      {template && (
        <TestSendDialog open={testOpen} onClose={() => setTestOpen(false)} path={`${EMAIL_API}/templates/${template.id}/test`} clientId={clientId} />
      )}
    </>
  );
}
