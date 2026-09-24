import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { RotateCcw, Save, Search } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  EmptyState,
  FormField,
  Input,
  Select,
  SkeletonText,
  Textarea,
  useToast,
} from '@/components/ui';
import { UnsavedChangesGuard } from '@/features/campaigns/shared/UnsavedChangesGuard';
import { COPY_QUERY_KEY, splitLines, splitPairs } from '@/features/public/site/copy';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import { QueryError } from '../shared/common';
import { adminErrorMessage, isConflict } from '../shared/errors';

/** Server shape of `GET /agency/website/copy` and `GET /admin/content/copy`. */
export type CopyEntryType = 'Text' | 'Textarea' | 'List' | 'Pairs';

export interface CopyEntry {
  key: string;
  label: string;
  type: CopyEntryType;
  default: string;
  value: string;
  isCustomized: boolean;
  placeholders: string[];
  maxLength: number;
  updatedAt: string | null;
  concurrencyStamp: string | null;
}

export interface CopyGroup {
  id: string;
  label: string;
  scope: 'Website' | 'Portal';
  entries: CopyEntry[];
}

export interface CopyCatalog {
  groups: CopyGroup[];
}

interface CopyChange {
  key: string;
  value: string | null;
  concurrencyStamp: string | null;
}

const TYPE_HINT: Record<CopyEntryType, string> = {
  Text: 'One line.',
  Textarea: 'A paragraph; line breaks are kept.',
  List: 'One item per line.',
  Pairs: 'One item per line, written as “Title | Text”.',
};

function hintFor(entry: CopyEntry) {
  const placeholders = entry.placeholders.length
    ? ` Placeholders: ${entry.placeholders.map((p) => `{${p}}`).join(', ')} (filled in by the page).`
    : '';
  return TYPE_HINT[entry.type] + placeholders;
}

/** Parsed view of list and pair values, so editors see exactly how the page will split them. */
function ListPreview({ entry, value }: { entry: CopyEntry; value: string }) {
  if (entry.type === 'List') {
    const items = splitLines(value);
    return (
      <ul className="admin-copy__preview" aria-label={`${entry.label}: preview`}>
        {items.map((item, i) => (
          <li key={`${i}-${item}`}>{item}</li>
        ))}
      </ul>
    );
  }
  if (entry.type === 'Pairs') {
    const items = splitPairs(value);
    return (
      <dl className="admin-copy__preview" aria-label={`${entry.label}: preview`}>
        {items.map((item, i) => (
          <div key={`${i}-${item.title}`}>
            <dt>{item.title}</dt>
            <dd>{item.text || <em>Missing text after “|”</em>}</dd>
          </div>
        ))}
      </dl>
    );
  }
  return null;
}

export interface CopyEditorProps {
  /** API path of the catalog, e.g. `/agency/website/copy`. */
  endpoint: string;
  title: string;
  description: string;
}

/**
 * Editor for editable page copy: pick a page, edit its texts (lists and pairs show a parsed preview), reset any text
 * to its shipped default, then save every change of the page in one request. A stale edit (someone else saved the
 * same text meanwhile) is rejected with a reload prompt instead of overwriting their work.
 */
export function CopyEditor({ endpoint, title, description }: CopyEditorProps) {
  const toast = useToast();
  const client = useQueryClient();
  const searchId = useId();
  const query = useQuery({ queryKey: ['copy-editor', endpoint], queryFn: ({ signal }) => api.get<CopyCatalog>(endpoint, { signal }) });
  const [groupId, setGroupId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [drafts, setDrafts] = useState<Record<string, string>>({});

  const groups = useMemo(() => query.data?.groups ?? [], [query.data]);
  const entries = useMemo(() => groups.flatMap((g) => g.entries), [groups]);
  const byKey = useMemo(() => new Map(entries.map((e) => [e.key, e])), [entries]);
  const active = groups.find((g) => g.id === groupId) ?? groups[0];
  const term = search.trim().toLowerCase();
  const visible = term
    ? entries.filter((e) => [e.label, e.key, e.value].some((v) => v.toLowerCase().includes(term)))
    : (active?.entries ?? []);

  const changes: CopyChange[] = Object.entries(drafts)
    .filter(([key, value]) => byKey.get(key)?.value !== value)
    .map(([key, value]) => {
      const entry = byKey.get(key)!;
      return {
        key,
        // Saving the default text removes the override, so later updates to the default reach the site.
        value: value === entry.default ? null : value,
        concurrencyStamp: entry.concurrencyStamp,
      };
    });
  const dirty = changes.length > 0;

  const save = useMutation({
    mutationFn: (body: CopyChange[]) => api.put<CopyCatalog>(endpoint, { changes: body }),
    onSuccess: (data) => {
      client.setQueryData(['copy-editor', endpoint], data);
      setDrafts({});
      void client.invalidateQueries({ queryKey: COPY_QUERY_KEY });
      toast.success('Texts saved', 'The site shows the new wording straight away.');
    },
  });

  const fieldErrors = (key: string): string[] | undefined => {
    if (!isApiError(save.error)) return undefined;
    return save.error.errors?.[key];
  };

  if (query.isPending) return <SkeletonText lines={6} />;
  if (query.isError) return <QueryError error={query.error} onRetry={() => void query.refetch()} />;

  const setValue = (key: string, value: string) => setDrafts((d) => ({ ...d, [key]: value }));
  const valueOf = (e: CopyEntry) => drafts[e.key] ?? e.value;

  return (
    <div className="stack">
      <UnsavedChangesGuard when={dirty} />
      <Card>
        <CardHeader
          headingLevel={2}
          title={title}
          description={description}
          actions={
            <div className="cluster">
              <Button variant="secondary" disabled={!dirty || save.isPending} onClick={() => setDrafts({})}>
                Discard changes
              </Button>
              <Button leadingIcon={<Save />} disabled={!dirty} loading={save.isPending} onClick={() => save.mutate(changes)}>
                {dirty ? `Save ${changes.length} ${changes.length === 1 ? 'change' : 'changes'}` : 'Save changes'}
              </Button>
            </div>
          }
        />
        <CardBody className="stack">
          <div className="admin-copy__toolbar">
            <FormField label="Page">
              <Select
                value={active?.id ?? ''}
                onChange={(e) => {
                  setGroupId(e.target.value);
                  setSearch('');
                }}
                options={groups.map((g) => ({
                  value: g.id,
                  label: `${g.label} (${g.entries.filter((e) => e.isCustomized).length} of ${g.entries.length} customized)`,
                }))}
                disabled={!!term}
              />
            </FormField>
            <FormField label="Search all texts" id={searchId}>
              <Input
                type="search"
                leading={<Search />}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Label, key or wording"
              />
            </FormField>
          </div>
          {save.isError &&
            (isConflict(save.error) ? (
              <Alert
                tone="warning"
                title="Someone else changed these texts"
                actions={
                  <Button
                    variant="secondary"
                    onClick={() => {
                      save.reset();
                      void query.refetch();
                    }}
                  >
                    Reload latest texts
                  </Button>
                }
              >
                Your edits are still here. Reload to see their version, then re-apply your changes.
              </Alert>
            ) : (
              <Alert tone="danger" title="Texts not saved">
                {adminErrorMessage(save.error)}
              </Alert>
            ))}
          {visible.length === 0 ? (
            <EmptyState compact headingLevel={3} title="No texts match your search" description="Try another word." />
          ) : (
            <ul className="admin-copy__list">
              {visible.map((entry) => {
                const value = valueOf(entry);
                const changed = value !== entry.value;
                const isDefault = value === entry.default;
                const multiline = entry.type !== 'Text';
                return (
                  <li key={entry.key} className="admin-copy__item">
                    <FormField
                      label={entry.label}
                      hint={hintFor(entry)}
                      error={fieldErrors(entry.key)}
                      labelAside={
                        <span className="cluster">
                          {changed && <Badge tone="warning">Unsaved</Badge>}
                          {!changed && entry.isCustomized && <Badge tone="info">Customized</Badge>}
                          <code className="text-small text-muted">{entry.key}</code>
                        </span>
                      }
                    >
                      {multiline ? (
                        <Textarea
                          rows={entry.type === 'Textarea' ? 3 : Math.min(8, Math.max(3, value.split('\n').length + 1))}
                          maxLength={entry.maxLength}
                          value={value}
                          onChange={(e) => setValue(entry.key, e.target.value)}
                        />
                      ) : (
                        <Input maxLength={entry.maxLength} value={value} onChange={(e) => setValue(entry.key, e.target.value)} />
                      )}
                    </FormField>
                    <ListPreview entry={entry} value={value} />
                    <div className="cluster admin-copy__actions">
                      {!isDefault && (
                        <Button size="sm" variant="ghost" leadingIcon={<RotateCcw />} onClick={() => setValue(entry.key, entry.default)}>
                          Reset to default
                        </Button>
                      )}
                      {!isDefault && (
                        <details className="admin-copy__default">
                          <summary>Show default text</summary>
                          <p className="text-small">{entry.default}</p>
                        </details>
                      )}
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </CardBody>
      </Card>
    </div>
  );
}
