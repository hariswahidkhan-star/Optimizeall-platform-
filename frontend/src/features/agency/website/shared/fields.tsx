import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Button, Checkbox, FileDrop, FormField, IconButton, Input, Select, Switch, Tabs, Textarea, useToast } from '@/components/ui';
import type { SelectOption, SelectOptionGroup } from '@/components/ui';
import { Markdown } from '@/features/public/site/Markdown';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { Seo } from '../api';

/** Form building blocks for the website CMS editors. Server field errors are passed in as `error`. */

export type Errors = Record<string, string | undefined>;

export function TextField({
  label,
  value,
  onChange,
  error,
  hint,
  required,
  maxLength,
  type = 'text',
  placeholder,
}: {
  label: string;
  value: string | null | undefined;
  onChange: (value: string) => void;
  error?: string;
  hint?: ReactNode;
  required?: boolean;
  maxLength?: number;
  type?: string;
  placeholder?: string;
}) {
  return (
    <FormField label={label} error={error} hint={hint} required={required} optional={!required}>
      <Input type={type} value={value ?? ''} maxLength={maxLength} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} />
    </FormField>
  );
}

export function AreaField({
  label,
  value,
  onChange,
  error,
  hint,
  required,
  rows = 3,
  maxLength,
}: {
  label: string;
  value: string | null | undefined;
  onChange: (value: string) => void;
  error?: string;
  hint?: ReactNode;
  required?: boolean;
  rows?: number;
  maxLength?: number;
}) {
  return (
    <FormField label={label} error={error} hint={hint} required={required} optional={!required}>
      <Textarea rows={rows} maxLength={maxLength} value={value ?? ''} onChange={(e) => onChange(e.target.value)} />
    </FormField>
  );
}

/** A list of short strings edited as one item per line. */
export function LinesField({ label, value, onChange, error, hint }: { label: string; value: string[]; onChange: (value: string[]) => void; error?: string; hint?: ReactNode }) {
  const [text, setText] = useState(value.join('\n'));
  return (
    <FormField label={label} error={error} hint={hint ?? 'One item per line.'} optional>
      <Textarea
        rows={Math.min(10, Math.max(3, value.length + 1))}
        value={text}
        onChange={(e) => {
          setText(e.target.value);
          onChange(e.target.value.split('\n').map((l) => l.trim()).filter(Boolean));
        }}
      />
    </FormField>
  );
}

/** Markdown editor with a live preview rendered by the public site's safe renderer. */
export function MarkdownField({
  label,
  value,
  onChange,
  error,
  required,
  rows = 12,
}: {
  label: string;
  value: string | null | undefined;
  onChange: (value: string) => void;
  error?: string;
  required?: boolean;
  rows?: number;
}) {
  return (
    <Tabs
      label={`${label} editor`}
      tabs={[
        {
          id: 'write',
          label: 'Write',
          content: (
            <FormField label={label} error={error} required={required} optional={!required} hint="Markdown: ## headings, **bold**, *italic*, - lists, [links](https://…). Raw HTML is removed.">
              <Textarea rows={rows} value={value ?? ''} onChange={(e) => onChange(e.target.value)} className="cms-mono" />
            </FormField>
          ),
        },
        {
          id: 'preview',
          label: 'Preview',
          content: (
            <div className="cms-preview" aria-label={`${label} preview`}>
              {value?.trim() ? <Markdown source={value} /> : <p className="text-muted">Nothing to preview yet.</p>}
            </div>
          ),
        },
      ]}
    />
  );
}

export function SwitchField({ label, checked, onChange, description }: { label: string; checked: boolean; onChange: (v: boolean) => void; description?: ReactNode }) {
  return <Switch label={label} checked={checked} onCheckedChange={onChange} description={description} />;
}

export function SelectField({
  label,
  value,
  onChange,
  options,
  error,
  required,
  placeholder,
  hint,
}: {
  label: string;
  value: string | null | undefined;
  onChange: (value: string) => void;
  options: (SelectOption | SelectOptionGroup)[];
  error?: string;
  required?: boolean;
  placeholder?: string;
  hint?: ReactNode;
}) {
  return (
    <FormField label={label} error={error} required={required} optional={!required} hint={hint}>
      <Select value={value ?? ''} onChange={(e) => onChange(e.target.value)} options={options} placeholder={placeholder} />
    </FormField>
  );
}

export function MultiCheck({
  legend,
  options,
  value,
  onChange,
  error,
}: {
  legend: string;
  options: { value: string; label: string }[];
  value: string[];
  onChange: (value: string[]) => void;
  error?: string;
}) {
  return (
    <fieldset className="cms-multicheck">
      <legend>{legend}</legend>
      <div className="cms-multicheck__grid">
        {options.map((o) => (
          <Checkbox
            key={o.value}
            label={o.label}
            checked={value.includes(o.value)}
            onChange={(e) => onChange(e.target.checked ? [...value, o.value] : value.filter((v) => v !== o.value))}
          />
        ))}
      </div>
      {error && (
        <p className="site-field-error" role="alert">
          {error}
        </p>
      )}
    </fieldset>
  );
}

/** Image URL field plus an uploader (`POST /agency/website/images`; stored as a public content image). */
export function ImageField({ label, value, onChange, error }: { label: string; value: string | null | undefined; onChange: (value: string) => void; error?: string }) {
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const upload = async () => {
    if (!file) return;
    setBusy(true);
    setUploadError(null);
    try {
      const form = new FormData();
      form.append('file', file);
      const stored = await api.upload<{ url: string }>('/agency/website/images', form);
      onChange(stored.url);
      setFile(null);
      toast.success('Image uploaded');
    } catch (e) {
      setUploadError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="cms-image">
      <TextField label={label} value={value} onChange={onChange} error={error} hint="An uploaded image (/api/v1/files/…) or an https URL on an allowed image host." />
      {value && value.startsWith('/api/v1/files/') && <img src={value} alt="" className="cms-image__preview" width={160} height={90} />}
      <FileDrop label={`Upload ${label.toLowerCase()}`} value={file} onChange={setFile} maxSizeBytes={10 * 1024 * 1024} error={uploadError} />
      {file && (
        <div>
          <Button size="sm" onClick={upload} loading={busy}>
            Upload image
          </Button>
        </div>
      )}
    </div>
  );
}

export function SeoFields({ value, onChange, errors }: { value: Seo; onChange: (value: Seo) => void; errors: Errors }) {
  const set = <K extends keyof Seo>(key: K, v: Seo[K]) => onChange({ ...value, [key]: v });
  return (
    <fieldset className="cms-fieldset">
      <legend>Search &amp; social</legend>
      <TextField label="SEO title" value={value.title} onChange={(v) => set('title', v)} maxLength={70} error={errors['seo.title']} hint={`${(value.title ?? '').length}/70 — defaults to the name.`} />
      <AreaField label="Meta description" value={value.description} onChange={(v) => set('description', v)} maxLength={200} rows={2} error={errors['seo.description']} hint={`${(value.description ?? '').length}/200`} />
      <ImageField label="Social image" value={value.ogImageUrl} onChange={(v) => set('ogImageUrl', v)} error={errors['seo.ogImageUrl']} />
      <TextField label="Canonical URL" value={value.canonicalUrl} onChange={(v) => set('canonicalUrl', v)} error={errors['seo.canonicalUrl']} hint="Only when this content is published elsewhere first." />
      <SwitchField label="Hide from search engines (noindex)" checked={value.noIndex} onChange={(v) => set('noIndex', v)} description="Also removes the page from the sitemap." />
    </fieldset>
  );
}

export const EMPTY_SEO: Seo = { title: null, description: null, ogImageUrl: null, canonicalUrl: null, noIndex: false };

/** Generic editor for a list of small records (FAQs, process steps, metrics, links). */
export function ListEditor<T>({
  legend,
  items,
  onChange,
  empty,
  render,
  error,
  addLabel = 'Add',
}: {
  legend: string;
  items: T[];
  onChange: (items: T[]) => void;
  empty: T;
  render: (item: T, update: (item: T) => void, index: number) => ReactNode;
  error?: string;
  addLabel?: string;
}) {
  const move = (from: number, to: number) => {
    const next = [...items];
    const [x] = next.splice(from, 1);
    next.splice(to, 0, x);
    onChange(next);
  };
  return (
    <fieldset className="cms-fieldset">
      <legend>{legend}</legend>
      {items.length === 0 && <p className="text-small text-muted">None yet.</p>}
      <ol className="cms-list">
        {items.map((item, i) => (
          <li key={i} className="cms-list__item">
            <div className="cms-list__body">{render(item, (value) => onChange(items.map((x, j) => (j === i ? value : x))), i)}</div>
            <div className="cms-list__tools">
              <IconButton size="sm" variant="ghost" label={`Move ${legend} item ${i + 1} up`} icon={<ArrowUp />} disabled={i === 0} onClick={() => move(i, i - 1)} />
              <IconButton size="sm" variant="ghost" label={`Move ${legend} item ${i + 1} down`} icon={<ArrowDown />} disabled={i === items.length - 1} onClick={() => move(i, i + 1)} />
              <IconButton size="sm" variant="ghost" label={`Remove ${legend} item ${i + 1}`} icon={<Trash2 />} onClick={() => onChange(items.filter((_, j) => j !== i))} />
            </div>
          </li>
        ))}
      </ol>
      {error && (
        <p className="site-field-error" role="alert">
          {error}
        </p>
      )}
      <div>
        <Button size="sm" variant="secondary" leadingIcon={<Plus />} onClick={() => onChange([...items, empty])}>
          {addLabel}
        </Button>
      </div>
    </fieldset>
  );
}

/** First error for a field (exact key or any nested key such as `metrics[0].measurement`). */
export function errorFor(errors: Errors, field: string): string | undefined {
  if (errors[field]) return errors[field];
  const nested = Object.keys(errors).find((k) => k.startsWith(`${field}[`) || k.startsWith(`${field}.`));
  return nested ? errors[nested] : undefined;
}

export function toErrors(record: Record<string, string[]> | undefined): Errors {
  const out: Errors = {};
  for (const [k, v] of Object.entries(record ?? {})) out[k] = v?.[0];
  return out;
}
