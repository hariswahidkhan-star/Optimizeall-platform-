import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Eye, RotateCcw, Save } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  DataTable,
  type DataTableColumn,
  DateTime,
  EmptyState,
  FormField,
  Input,
  Select,
  SkeletonText,
  Textarea,
  useToast,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import { QueryError } from '../shared/common';
import { adminErrorMessage, isConflict, toDisplayError } from '../shared/errors';

export interface EmailTemplateSummary {
  key: string;
  group: string;
  name: string;
  description: string;
  subject: string;
  isCustomized: boolean;
  updatedAt: string | null;
}

export interface EmailVariable {
  name: string;
  description: string;
  sample: string;
  required: boolean;
}

export interface EmailTemplate {
  key: string;
  group: string;
  name: string;
  description: string;
  subject: string;
  body: string;
  actionLabel: string | null;
  defaultSubject: string;
  defaultBody: string;
  defaultActionLabel: string | null;
  hasActionLabel: boolean;
  hasHtml: boolean;
  variables: EmailVariable[];
  isCustomized: boolean;
  updatedAt: string | null;
  concurrencyStamp: string | null;
}

export interface EmailPreview {
  subject: string;
  text: string;
  html: string | null;
}

const BASE = '/admin/email-templates';
const KEY = ['admin', 'email-templates'] as const;

interface Draft {
  subject: string;
  body: string;
  actionLabel: string;
}

function toDraft(t: EmailTemplate): Draft {
  return { subject: t.subject, body: t.body, actionLabel: t.actionLabel ?? '' };
}

/** Edit, preview and reset one template. */
function TemplateEditor({ templateKey, onClose }: { templateKey: string; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const bodyRef = useRef<HTMLTextAreaElement>(null);
  const detail = useQuery({
    queryKey: [...KEY, templateKey],
    queryFn: ({ signal }) => api.get<EmailTemplate>(`${BASE}/${encodeURIComponent(templateKey)}`, { signal }),
  });
  const [draft, setDraft] = useState<Draft | null>(null);
  const [resetting, setResetting] = useState(false);

  useEffect(() => {
    if (detail.data) setDraft(toDraft(detail.data));
  }, [detail.data]);

  const payload = (d: Draft) => ({ subject: d.subject, body: d.body, actionLabel: d.actionLabel || null });

  const preview = useMutation({
    mutationFn: (d: Draft) => api.post<EmailPreview>(`${BASE}/${encodeURIComponent(templateKey)}/preview`, payload(d)),
  });
  const save = useMutation({
    mutationFn: (d: Draft) =>
      api.put<EmailTemplate>(`${BASE}/${encodeURIComponent(templateKey)}`, {
        ...payload(d),
        concurrencyStamp: detail.data?.concurrencyStamp ?? null,
      }),
    onSuccess: (data) => {
      client.setQueryData([...KEY, templateKey], data);
      void client.invalidateQueries({ queryKey: [...KEY, 'list'] });
      toast.success('Template saved', 'New emails use this wording from now on.');
    },
  });

  // Show the current wording straight away.
  const previewMutate = preview.mutate;
  useEffect(() => {
    if (detail.data) previewMutate(toDraft(detail.data));
  }, [detail.data, previewMutate]);

  if (detail.isError) return <QueryError error={detail.error} onRetry={() => void detail.refetch()} />;
  if (detail.isPending || !draft) return <SkeletonText lines={8} />;
  const t = detail.data;
  const dirty = draft.subject !== t.subject || draft.body !== t.body || (draft.actionLabel || null) !== (t.actionLabel ?? null);
  const fieldError = (field: string) => (isApiError(save.error) ? save.error.errors?.[field] : undefined) ??
    (isApiError(preview.error) ? preview.error.errors?.[field] : undefined);

  const insertVariable = (name: string) => {
    const token = `{{${name}}}`;
    const el = bodyRef.current;
    if (!el) {
      setDraft({ ...draft, body: draft.body + token });
      return;
    }
    const start = el.selectionStart ?? draft.body.length;
    const end = el.selectionEnd ?? start;
    const body = draft.body.slice(0, start) + token + draft.body.slice(end);
    setDraft({ ...draft, body });
    requestAnimationFrame(() => {
      el.focus();
      el.setSelectionRange(start + token.length, start + token.length);
    });
  };

  return (
    <Card>
      <CardHeader
        headingLevel={2}
        title={t.name}
        description={t.description}
        actions={
          <Button variant="ghost" leadingIcon={<ArrowLeft />} onClick={onClose}>
            All templates
          </Button>
        }
      />
      <CardBody className="admin-email">
        <form
          className="stack"
          aria-label={`Edit ${t.name}`}
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate(draft);
          }}
        >
          <div className="cluster">
            {t.isCustomized ? <Badge tone="info">Customized</Badge> : <Badge>Default wording</Badge>}
            {t.updatedAt && (
              <span className="text-small text-muted">
                Last saved <DateTime value={t.updatedAt} format="relative" />
              </span>
            )}
          </div>
          <FormField label="Subject" required error={fieldError('subject')}>
            <Input value={draft.subject} maxLength={300} onChange={(e) => setDraft({ ...draft, subject: e.target.value })} />
          </FormField>
          <FormField label="Email text" required hint="Plain text. Blank lines start a new paragraph." error={fieldError('body')}>
            <Textarea ref={bodyRef} rows={12} value={draft.body} onChange={(e) => setDraft({ ...draft, body: e.target.value })} />
          </FormField>
          {t.hasActionLabel && (
            <FormField label="Button label" required error={fieldError('actionLabel')}>
              <Input value={draft.actionLabel} maxLength={80} onChange={(e) => setDraft({ ...draft, actionLabel: e.target.value })} />
            </FormField>
          )}
          <fieldset className="stack">
            <legend className="text-small">Variables (select one to insert it at the cursor)</legend>
            <ul className="admin-email__vars">
              {t.variables.map((v) => (
                <li key={v.name}>
                  <Button size="sm" variant="secondary" type="button" title={v.description} onClick={() => insertVariable(v.name)}>
                    {`{{${v.name}}}`}
                    {v.required && <span className="visually-hidden"> (required)</span>}
                  </Button>
                </li>
              ))}
            </ul>
            <ul className="text-small text-muted">
              {t.variables.map((v) => (
                <li key={v.name}>
                  <code>{`{{${v.name}}}`}</code> — {v.description}
                  {v.required && ' Required.'}
                </li>
              ))}
            </ul>
          </fieldset>
          {save.isError &&
            (isConflict(save.error) ? (
              <Alert
                tone="warning"
                title="Someone else changed this template"
                actions={
                  <Button
                    variant="secondary"
                    type="button"
                    onClick={() => {
                      save.reset();
                      void detail.refetch();
                    }}
                  >
                    Reload latest version
                  </Button>
                }
              >
                Reloading replaces your unsaved text with theirs.
              </Alert>
            ) : (
              <Alert tone="danger" title="Template not saved">
                {adminErrorMessage(save.error)}
              </Alert>
            ))}
          <div className="cluster">
            <Button type="submit" leadingIcon={<Save />} disabled={!dirty} loading={save.isPending}>
              Save template
            </Button>
            <Button type="button" variant="secondary" leadingIcon={<Eye />} loading={preview.isPending} onClick={() => preview.mutate(draft)}>
              Preview
            </Button>
            {t.isCustomized && (
              <Button type="button" variant="ghost" leadingIcon={<RotateCcw />} onClick={() => setResetting(true)}>
                Reset to default
              </Button>
            )}
            {dirty && (
              <Button type="button" variant="ghost" onClick={() => setDraft(toDraft(t))}>
                Discard changes
              </Button>
            )}
          </div>
        </form>
        <section className="stack" aria-labelledby="email-preview-title" aria-live="polite">
          <h3 id="email-preview-title">Preview with sample values</h3>
          {preview.isError && !fieldError('body') && !fieldError('subject') && (
            <Alert tone="danger" title="Preview unavailable">
              {adminErrorMessage(preview.error)}
            </Alert>
          )}
          {preview.data ? (
            <>
              <p>
                <strong>Subject:</strong> {preview.data.subject}
              </p>
              {preview.data.html && (
                <iframe
                  className="admin-email__frame"
                  title={`HTML preview of ${t.name}`}
                  sandbox=""
                  srcDoc={preview.data.html}
                />
              )}
              <details open={!preview.data.html}>
                <summary>Plain-text version</summary>
                <pre className="admin-email__text">{preview.data.text}</pre>
              </details>
            </>
          ) : (
            <SkeletonText lines={5} />
          )}
        </section>
      </CardBody>
      <ConfirmDialog
        open={resetting}
        onClose={() => setResetting(false)}
        title="Reset to the default wording?"
        description="Your custom subject and text are removed and emails use the platform's default wording again."
        confirmLabel="Reset template"
        tone="danger"
        onConfirm={async () => {
          try {
            const stamp = t.concurrencyStamp ? `?concurrencyStamp=${encodeURIComponent(t.concurrencyStamp)}` : '';
            await api.delete(`${BASE}/${encodeURIComponent(templateKey)}${stamp}`);
          } catch (error) {
            throw toDisplayError(error);
          }
          toast.success('Template reset', 'Emails use the default wording again.');
          await client.invalidateQueries({ queryKey: KEY });
        }}
      />
    </Card>
  );
}

/** Admin → Content → Email templates: every transactional email the platform sends, editable with preview. */
export function EmailTemplatesTab() {
  const [selected, setSelected] = useState<string | null>(null);
  const [group, setGroup] = useState('');
  const [search, setSearch] = useState('');
  const list = useQuery({
    queryKey: [...KEY, 'list'],
    queryFn: ({ signal }) => api.get<EmailTemplateSummary[]>(BASE, { signal }),
  });

  const groups = useMemo(() => [...new Set((list.data ?? []).map((t) => t.group))], [list.data]);
  const term = search.trim().toLowerCase();
  const rows = (list.data ?? []).filter(
    (t) =>
      (!group || t.group === group) &&
      (!term || [t.name, t.description, t.subject, t.key].some((v) => v.toLowerCase().includes(term))),
  );

  if (selected) return <TemplateEditor templateKey={selected} onClose={() => setSelected(null)} />;

  const columns: DataTableColumn<EmailTemplateSummary>[] = [
    {
      id: 'name',
      header: 'Email',
      cell: (t) => (
        <div>
          <Button variant="link" onClick={() => setSelected(t.key)}>
            {t.name}
          </Button>
          <div className="text-small text-muted">{t.description}</div>
        </div>
      ),
    },
    { id: 'group', header: 'Group', cell: (t) => t.group },
    { id: 'subject', header: 'Subject', cell: (t) => <span className="text-small">{t.subject}</span> },
    {
      id: 'status',
      header: 'Wording',
      cell: (t) => (t.isCustomized ? <Badge tone="info">Customized</Badge> : <Badge>Default</Badge>),
    },
    { id: 'updated', header: 'Last saved', cell: (t) => (t.updatedAt ? <DateTime value={t.updatedAt} format="relative" /> : '—') },
  ];

  return (
    <Card>
      <CardHeader
        headingLevel={2}
        title="Email templates"
        description="The wording of every automatic email: the layout around notification emails, each notification, and the website's newsletter and consultation emails. Variables in {{double braces}} are filled in when the email is sent."
      />
      <CardBody className="stack">
        <div className="admin-copy__toolbar">
          <FormField label="Group">
            <Select value={group} onChange={(e) => setGroup(e.target.value)} placeholder="All groups" options={groups.map((g) => ({ value: g, label: g }))} />
          </FormField>
          <FormField label="Search templates">
            <Input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Name, subject or key" />
          </FormField>
        </div>
        {list.isPending ? (
          <SkeletonText lines={6} />
        ) : list.isError ? (
          <QueryError error={list.error} onRetry={() => void list.refetch()} />
        ) : rows.length === 0 ? (
          <EmptyState compact headingLevel={3} title="No templates match" description="Try another group or search term." />
        ) : (
          <DataTable caption="Email templates" columns={columns} rows={rows} getRowId={(t) => t.key} />
        )}
      </CardBody>
    </Card>
  );
}
