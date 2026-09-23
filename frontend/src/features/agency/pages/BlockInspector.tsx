import { useMutation } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, Plus, Trash2, Upload } from 'lucide-react';
import { useId, useRef, type ReactNode } from 'react';
import { Button, Checkbox, FormField, IconButton, Input, Select, Textarea, useToast } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { Block, BlockPropsMap, BlockType, FormListItem } from './api';
import { blockLabels, move } from './blockDefaults';

type Errors = Record<string, string[]>;

interface InspectorProps {
  block: Block;
  onChange: (block: Block) => void;
  forms: FormListItem[];
  /** Server errors keyed by lower-cased path; `prefix` is this block's path, e.g. "variants[0].blocks[2].props". */
  errors: Errors;
  prefix: string;
}

/** Edits the props of the selected block. Every field maps 1:1 to the server-side block schema. */
export function BlockInspector({ block, onChange, forms, errors, prefix }: InspectorProps) {
  const err = (field: string) => errors[`${prefix}.${field}`.toLowerCase()]?.[0] ?? errors[prefix.toLowerCase()]?.[0];
  function update<K extends BlockType>(b: Extract<Block, { type: K }>, patch: Partial<BlockPropsMap[K]>) {
    onChange({ ...b, props: { ...b.props, ...patch } } as Block);
  }
  const text = (label: string, value: string | null | undefined, onValue: (v: string) => void, field: string, opts: { multiline?: boolean; required?: boolean; hint?: string } = {}) => (
    <FormField label={label} required={opts.required} error={err(field)} hint={opts.hint}>
      {opts.multiline ? (
        <Textarea rows={4} value={value ?? ''} onChange={(e) => onValue(e.target.value)} />
      ) : (
        <Input value={value ?? ''} onChange={(e) => onValue(e.target.value)} />
      )}
    </FormField>
  );

  return (
    <div className="stack pb-inspector">
      <h2 className="pb-panel-title">{blockLabels[block.type]}</h2>
      {(() => {
        switch (block.type) {
          case 'hero': {
            const p = block.props;
            return (
              <>
                {text('Headline', p.headline, (v) => update(block, { headline: v }), 'headline', { required: true })}
                {text('Subheadline', p.subheadline, (v) => update(block, { subheadline: v || null }), 'subheadline', { multiline: true })}
                <div className="pb-grid-2">
                  {text('Button label', p.ctaLabel, (v) => update(block, { ctaLabel: v || null }), 'ctaLabel')}
                  {text('Button link', p.ctaHref, (v) => update(block, { ctaHref: v || null }), 'ctaHref', { hint: 'https://…, /path or #form' })}
                </div>
                <ImageField label="Image" value={p.imageUrl} onValue={(v) => update(block, { imageUrl: v || null })} error={err('imageUrl')} />
                {text('Image alt text', p.imageAlt, (v) => update(block, { imageAlt: v || null }), 'imageAlt')}
                <div className="pb-grid-2">
                  <FormField label="Alignment">
                    <Select value={p.align} onChange={(e) => update(block, { align: e.target.value as 'left' | 'center' })} options={[{ value: 'center', label: 'Centered' }, { value: 'left', label: 'Left' }]} />
                  </FormField>
                  <FormField label="Theme">
                    <Select value={p.theme} onChange={(e) => update(block, { theme: e.target.value as 'light' | 'dark' | 'brand' })} options={[{ value: 'brand', label: 'Brand' }, { value: 'light', label: 'Light' }, { value: 'dark', label: 'Dark' }]} />
                  </FormField>
                </div>
              </>
            );
          }
          case 'text':
            return (
              <>
                {text('Heading', block.props.heading, (v) => update(block, { heading: v || null }), 'heading')}
                {text('Body', block.props.body, (v) => update(block, { body: v }), 'body', { multiline: true, required: true, hint: 'Plain text; blank lines start new paragraphs.' })}
              </>
            );
          case 'image': {
            const p = block.props;
            return (
              <>
                <ImageField label="Image" value={p.url} onValue={(v) => update(block, { url: v })} error={err('url')} required />
                <Checkbox label="Decorative (no alt text needed)" checked={!!p.decorative} onChange={(e) => update(block, { decorative: e.target.checked })} />
                {!p.decorative && text('Alt text', p.alt, (v) => update(block, { alt: v || null }), 'alt', { required: true })}
                {text('Caption', p.caption, (v) => update(block, { caption: v || null }), 'caption')}
                {text('Link', p.linkHref, (v) => update(block, { linkHref: v || null }), 'linkHref')}
              </>
            );
          }
          case 'video': {
            const p = block.props;
            return (
              <>
                {text('YouTube or Vimeo link', p.url ?? (p.provider && p.videoId ? (p.provider === 'vimeo' ? `https://vimeo.com/${p.videoId}` : `https://youtu.be/${p.videoId}`) : ''), (v) => update(block, { url: v, provider: null, videoId: null }), 'url', {
                  required: true,
                  hint: 'Only YouTube and Vimeo are allowed; the video loads from a privacy-enhanced player.',
                })}
                {text('Title (for screen readers)', p.title, (v) => update(block, { title: v }), 'title', { required: true })}
              </>
            );
          }
          case 'features':
            return (
              <>
                {text('Heading', block.props.heading, (v) => update(block, { heading: v || null }), 'heading')}
                {text('Intro', block.props.intro, (v) => update(block, { intro: v || null }), 'intro', { multiline: true })}
                <ListEditor
                  label="Feature"
                  items={block.props.items}
                  onChange={(items) => update(block, { items })}
                  create={() => ({ title: 'New benefit', body: '' })}
                  error={err('items')}
                  render={(item, set, i) => (
                    <>
                      {text(`Feature ${i + 1} title`, item.title, (v) => set({ ...item, title: v }), `items[${i}].title`, { required: true })}
                      {text(`Feature ${i + 1} text`, item.body, (v) => set({ ...item, body: v || null }), `items[${i}].body`, { multiline: true })}
                    </>
                  )}
                />
              </>
            );
          case 'testimonials':
            return (
              <>
                {text('Heading', block.props.heading, (v) => update(block, { heading: v || null }), 'heading')}
                <ListEditor
                  label="Testimonial"
                  items={block.props.items}
                  onChange={(items) => update(block, { items })}
                  create={() => ({ quote: 'Quote', author: 'Name', role: null, rating: 5 })}
                  error={err('items')}
                  render={(item, set, i) => (
                    <>
                      {text(`Quote ${i + 1}`, item.quote, (v) => set({ ...item, quote: v }), `items[${i}].quote`, { multiline: true, required: true })}
                      <div className="pb-grid-2">
                        {text('Author', item.author, (v) => set({ ...item, author: v }), `items[${i}].author`, { required: true })}
                        {text('Role', item.role, (v) => set({ ...item, role: v || null }), `items[${i}].role`)}
                      </div>
                      <FormField label="Rating">
                        <Select value={String(item.rating ?? '')} onChange={(e) => set({ ...item, rating: e.target.value ? Number(e.target.value) : null })} options={[{ value: '', label: 'No rating' }, ...[5, 4, 3, 2, 1].map((r) => ({ value: String(r), label: `${r} stars` }))]} />
                      </FormField>
                    </>
                  )}
                />
              </>
            );
          case 'pricing':
            return (
              <>
                {text('Heading', block.props.heading, (v) => update(block, { heading: v || null }), 'heading')}
                <ListEditor
                  label="Plan"
                  items={block.props.plans}
                  onChange={(plans) => update(block, { plans })}
                  create={() => ({ name: 'Plan', price: '$0', period: 'per month', features: ['Feature'], ctaLabel: 'Choose', ctaHref: '#form', highlighted: false })}
                  error={err('plans')}
                  render={(plan, set, i) => (
                    <>
                      <div className="pb-grid-2">
                        {text('Plan name', plan.name, (v) => set({ ...plan, name: v }), `plans[${i}].name`, { required: true })}
                        {text('Price', plan.price, (v) => set({ ...plan, price: v }), `plans[${i}].price`, { required: true })}
                      </div>
                      {text('Period', plan.period, (v) => set({ ...plan, period: v || null }), `plans[${i}].period`)}
                      <FormField label="Features" hint="One per line." error={err(`plans[${i}].features`)}>
                        <Textarea rows={3} value={plan.features.join('\n')} onChange={(e) => set({ ...plan, features: e.target.value.split('\n').filter((l) => l.trim()) })} />
                      </FormField>
                      <div className="pb-grid-2">
                        {text('Button label', plan.ctaLabel, (v) => set({ ...plan, ctaLabel: v || null }), `plans[${i}].ctaLabel`)}
                        {text('Button link', plan.ctaHref, (v) => set({ ...plan, ctaHref: v || null }), `plans[${i}].ctaHref`)}
                      </div>
                      <Checkbox label="Highlight this plan" checked={!!plan.highlighted} onChange={(e) => set({ ...plan, highlighted: e.target.checked })} />
                    </>
                  )}
                />
                {text('Footnote', block.props.footnote, (v) => update(block, { footnote: v || null }), 'footnote')}
              </>
            );
          case 'faq':
            return (
              <>
                {text('Heading', block.props.heading, (v) => update(block, { heading: v || null }), 'heading')}
                <ListEditor
                  label="Question"
                  items={block.props.items}
                  onChange={(items) => update(block, { items })}
                  create={() => ({ question: 'Question?', answer: 'Answer.' })}
                  error={err('items')}
                  render={(item, set, i) => (
                    <>
                      {text(`Question ${i + 1}`, item.question, (v) => set({ ...item, question: v }), `items[${i}].question`, { required: true })}
                      {text(`Answer ${i + 1}`, item.answer, (v) => set({ ...item, answer: v }), `items[${i}].answer`, { multiline: true, required: true })}
                    </>
                  )}
                />
              </>
            );
          case 'countdown': {
            const p = block.props;
            const local = p.endsAt ? new Date(p.endsAt) : null;
            const value = local && !Number.isNaN(local.getTime()) ? new Date(local.getTime() - local.getTimezoneOffset() * 60000).toISOString().slice(0, 16) : '';
            return (
              <>
                {text('Heading', p.heading, (v) => update(block, { heading: v || null }), 'heading')}
                <FormField label="Ends at" required hint="Your local time." error={err('endsAt')}>
                  <Input type="datetime-local" value={value} onChange={(e) => update(block, { endsAt: e.target.value ? new Date(e.target.value).toISOString() : '' })} />
                </FormField>
                {text('Text after it ends', p.expiredText, (v) => update(block, { expiredText: v || null }), 'expiredText')}
              </>
            );
          }
          case 'form':
            return (
              <>
                <FormField label="Form" required error={err('formId')} hint="Only active forms of this client can be placed.">
                  <Select value={block.props.formId} placeholder="Choose a form" onChange={(e) => update(block, { formId: e.target.value })} options={forms.map((f) => ({ value: f.id, label: f.name }))} />
                </FormField>
                {text('Heading', block.props.heading, (v) => update(block, { heading: v || null }), 'heading')}
                {text('Description', block.props.description, (v) => update(block, { description: v || null }), 'description', { multiline: true })}
              </>
            );
          case 'cta':
            return (
              <>
                {text('Heading', block.props.heading, (v) => update(block, { heading: v }), 'heading', { required: true })}
                {text('Text', block.props.body, (v) => update(block, { body: v || null }), 'body', { multiline: true })}
                <div className="pb-grid-2">
                  {text('Button label', block.props.buttonLabel, (v) => update(block, { buttonLabel: v }), 'buttonLabel', { required: true })}
                  {text('Button link', block.props.buttonHref, (v) => update(block, { buttonHref: v }), 'buttonHref', { required: true, hint: 'https://…, /path, #anchor, mailto: or tel:' })}
                </div>
                <FormField label="Style">
                  <Select value={block.props.style} onChange={(e) => update(block, { style: e.target.value as 'primary' | 'highlight' | 'secondary' })} options={[{ value: 'primary', label: 'Primary' }, { value: 'highlight', label: 'Highlight' }, { value: 'secondary', label: 'Secondary' }]} />
                </FormField>
              </>
            );
          case 'logos':
            return (
              <>
                {text('Heading', block.props.heading, (v) => update(block, { heading: v || null }), 'heading')}
                <ListEditor
                  label="Logo"
                  items={block.props.items}
                  onChange={(items) => update(block, { items })}
                  create={() => ({ name: 'Company', imageUrl: '' })}
                  error={err('items')}
                  render={(item, set, i) => (
                    <>
                      {text(`Logo ${i + 1} name`, item.name, (v) => set({ ...item, name: v }), `items[${i}].name`, { required: true })}
                      <ImageField label={`Logo ${i + 1} image`} value={item.imageUrl} onValue={(v) => set({ ...item, imageUrl: v })} error={err(`items[${i}].imageUrl`)} required />
                    </>
                  )}
                />
              </>
            );
          case 'spacer':
            return (
              <FormField label="Size">
                <Select value={block.props.size} onChange={(e) => update(block, { size: e.target.value as 'sm' | 'md' | 'lg' | 'xl' })} options={['sm', 'md', 'lg', 'xl'].map((s) => ({ value: s, label: s.toUpperCase() }))} />
              </FormField>
            );
        }
      })()}
    </div>
  );
}

function ListEditor<T>({
  label,
  items,
  onChange,
  create,
  render,
  error,
}: {
  label: string;
  items: T[];
  onChange: (items: T[]) => void;
  create: () => T;
  render: (item: T, set: (item: T) => void, index: number) => ReactNode;
  error?: string;
}) {
  return (
    <fieldset className="pb-list">
      <legend className="ui-field__label">{label}s</legend>
      {error && <p className="ui-field__error">{error}</p>}
      {items.map((item, i) => (
        <div key={i} className="pb-list__item">
          {render(item, (next) => onChange(items.map((x, j) => (j === i ? next : x))), i)}
          <div className="pb-list__actions">
            <IconButton size="sm" variant="ghost" label={`Move ${label.toLowerCase()} ${i + 1} up`} icon={<ArrowUp />} disabled={i === 0} onClick={() => onChange(move(items, i, i - 1))} />
            <IconButton size="sm" variant="ghost" label={`Move ${label.toLowerCase()} ${i + 1} down`} icon={<ArrowDown />} disabled={i === items.length - 1} onClick={() => onChange(move(items, i, i + 1))} />
            <IconButton size="sm" variant="ghost" label={`Remove ${label.toLowerCase()} ${i + 1}`} icon={<Trash2 />} onClick={() => onChange(items.filter((_, j) => j !== i))} />
          </div>
        </div>
      ))}
      <Button size="sm" variant="secondary" leadingIcon={<Plus />} onClick={() => onChange([...items, create()])}>
        Add {label.toLowerCase()}
      </Button>
    </fieldset>
  );
}

/** URL field with an upload button (POST /agency/pages/images → public /api/v1/files/{id}). */
function ImageField({ label, value, onValue, error, required }: { label: string; value: string | null | undefined; onValue: (v: string) => void; error?: string; required?: boolean }) {
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const id = useId();
  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append('file', file);
      return api.upload<{ url: string }>('/agency/pages/images', form);
    },
    onSuccess: (r) => onValue(r.url),
    onError: (err) => toast.error('Upload failed', errorMessage(err)),
  });
  return (
    <FormField label={label} required={required} error={error} hint="Upload an image or use an https URL on an allowed image host.">
      <div className="pb-image-field">
        <Input value={value ?? ''} onChange={(e) => onValue(e.target.value)} />
        <input
          ref={input}
          id={`${id}-file`}
          type="file"
          accept="image/png,image/jpeg,image/webp"
          className="visually-hidden"
          tabIndex={-1}
          aria-label={`Upload ${label.toLowerCase()}`}
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) upload.mutate(file);
          }}
        />
        <Button type="button" variant="secondary" size="sm" leadingIcon={<Upload />} loading={upload.isPending} onClick={() => input.current?.click()}>
          Upload
        </Button>
      </div>
    </FormField>
  );
}
