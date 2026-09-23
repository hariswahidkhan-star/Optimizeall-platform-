import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-react';
import { useId, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { FormField } from '@/components/ui/FormField';
import { IconButton } from '@/components/ui/IconButton';
import { Input } from '@/components/ui/Input';
import { Select } from '@/components/ui/Select';
import { Switch } from '@/components/ui/Switch';
import { Textarea } from '@/components/ui/Textarea';
import type { BlockType, DesignBlock, EmailDesign } from '../api/types';

export const BLOCK_LABELS: Record<BlockType, string> = {
  header: 'Header',
  text: 'Text',
  image: 'Image',
  button: 'Button',
  divider: 'Divider',
  spacer: 'Spacer',
  columns: 'Columns',
  social: 'Social links',
  footer: 'Footer (address + unsubscribe)',
};

const NESTED_TYPES: BlockType[] = ['text', 'image', 'button', 'divider', 'spacer', 'social'];

export const MERGE_TAG_HELP =
  'Merge tags: {{first_name|fallback}}, {{last_name}}, {{email}}, {{country}}, {{org_name}}, {{custom.your_field}}. Unknown tags are rejected.';

/** A sensible starting block of each type. */
export function newBlock(type: BlockType): DesignBlock {
  switch (type) {
    case 'header':
      return { type, title: 'Your headline', align: 'center' };
    case 'text':
      return { type, html: '<p>Hi {{first_name|there}},</p><p>Write your message here.</p>' };
    case 'image':
      return { type, src: 'https://', alt: '' };
    case 'button':
      return { type, text: 'Learn more', href: 'https://', align: 'center', color: '#1F2659', textColor: '#ffffff' };
    case 'divider':
      return { type, height: 16 };
    case 'spacer':
      return { type, height: 24 };
    case 'columns':
      return { type, columns: [{ blocks: [newBlock('text')] }, { blocks: [newBlock('text')] }] };
    case 'social':
      return { type, links: [{ network: 'instagram', url: 'https://' }] };
    case 'footer':
      return { type, showPreferencesLink: true };
  }
}

const ALIGN_OPTIONS = [
  { value: 'left', label: 'Left' },
  { value: 'center', label: 'Center' },
  { value: 'right', label: 'Right' },
];

const NETWORKS = ['instagram', 'facebook', 'x', 'linkedin', 'tiktok', 'youtube', 'pinterest', 'website'].map((n) => ({ value: n, label: n }));

function num(value: string): number | undefined {
  const n = Number(value);
  return value === '' || Number.isNaN(n) ? undefined : n;
}

function BlockFields({ block, onChange, path }: { block: DesignBlock; onChange: (b: DesignBlock) => void; path: string }) {
  const set = (patch: Partial<DesignBlock>) => onChange({ ...block, ...patch });
  switch (block.type) {
    case 'header':
      return (
        <>
          <FormField label="Title">
            <Input value={block.title ?? ''} onChange={(e) => set({ title: e.target.value })} />
          </FormField>
          <FormField label="Subtitle" optional>
            <Input value={block.subtitle ?? ''} onChange={(e) => set({ subtitle: e.target.value })} />
          </FormField>
          <FormField label="Logo URL (https)" optional>
            <Input value={block.logoUrl ?? ''} onChange={(e) => set({ logoUrl: e.target.value || undefined })} inputMode="url" />
          </FormField>
          <FormField label="Alignment">
            <Select value={block.align ?? 'center'} options={ALIGN_OPTIONS} onChange={(e) => set({ align: e.target.value as DesignBlock['align'] })} />
          </FormField>
        </>
      );
    case 'text':
      return (
        <FormField label="Content" hint={`Basic HTML (p, strong, em, a, ul, li, h2). Scripts, styles and event handlers are removed. ${MERGE_TAG_HELP}`}>
          <Textarea rows={6} value={block.html ?? ''} onChange={(e) => set({ html: e.target.value })} />
        </FormField>
      );
    case 'image':
      return (
        <>
          <FormField label="Image URL (https)">
            <Input value={block.src ?? ''} onChange={(e) => set({ src: e.target.value })} inputMode="url" />
          </FormField>
          <FormField label="Alt text" hint="Shown when images are blocked and read by screen readers.">
            <Input value={block.alt ?? ''} onChange={(e) => set({ alt: e.target.value })} />
          </FormField>
          <FormField label="Link" optional>
            <Input value={block.href ?? ''} onChange={(e) => set({ href: e.target.value || undefined })} inputMode="url" />
          </FormField>
          <FormField label="Width (px)" optional>
            <Input type="number" min={20} max={800} value={block.width ?? ''} onChange={(e) => set({ width: num(e.target.value) })} />
          </FormField>
        </>
      );
    case 'button':
      return (
        <>
          <FormField label="Button text">
            <Input value={block.text ?? ''} onChange={(e) => set({ text: e.target.value })} />
          </FormField>
          <FormField label="Link">
            <Input value={block.href ?? ''} onChange={(e) => set({ href: e.target.value })} inputMode="url" />
          </FormField>
          <FormField label="Button color">
            <Input type="color" value={block.color ?? '#1F2659'} onChange={(e) => set({ color: e.target.value })} />
          </FormField>
          <FormField label="Text color">
            <Input type="color" value={block.textColor ?? '#ffffff'} onChange={(e) => set({ textColor: e.target.value })} />
          </FormField>
          <FormField label="Alignment">
            <Select value={block.align ?? 'center'} options={ALIGN_OPTIONS} onChange={(e) => set({ align: e.target.value as DesignBlock['align'] })} />
          </FormField>
        </>
      );
    case 'divider':
    case 'spacer':
      return (
        <FormField label="Height (px)">
          <Input type="number" min={0} max={200} value={block.height ?? ''} onChange={(e) => set({ height: num(e.target.value) })} />
        </FormField>
      );
    case 'social':
      return (
        <div className="stack">
          {(block.links ?? []).map((link, i) => (
            <div key={i} className="cluster">
              <FormField label={`Network ${i + 1}`}>
                <Select
                  value={link.network}
                  options={NETWORKS}
                  onChange={(e) => set({ links: (block.links ?? []).map((l, j) => (j === i ? { ...l, network: e.target.value } : l)) })}
                />
              </FormField>
              <FormField label={`URL ${i + 1}`}>
                <Input value={link.url} onChange={(e) => set({ links: (block.links ?? []).map((l, j) => (j === i ? { ...l, url: e.target.value } : l)) })} />
              </FormField>
              <IconButton
                label={`Remove social link ${i + 1}`}
                icon={<Trash2 />}
                variant="ghost"
                onClick={() => set({ links: (block.links ?? []).filter((_, j) => j !== i) })}
              />
            </div>
          ))}
          <Button variant="secondary" size="sm" onClick={() => set({ links: [...(block.links ?? []), { network: 'website', url: 'https://' }] })}>
            Add social link
          </Button>
        </div>
      );
    case 'columns':
      return (
        <div className="stack">
          {(block.columns ?? []).map((column, ci) => (
            <fieldset key={ci} className="segment-group">
              <legend>Column {ci + 1}</legend>
              <BlockList
                blocks={column.blocks}
                allowed={NESTED_TYPES}
                path={`${path}-c${ci}`}
                onChange={(blocks) => set({ columns: (block.columns ?? []).map((c, j) => (j === ci ? { blocks } : c)) })}
              />
            </fieldset>
          ))}
          {(block.columns ?? []).length < 3 && (
            <Button variant="secondary" size="sm" onClick={() => set({ columns: [...(block.columns ?? []), { blocks: [newBlock('text')] }] })}>
              Add column
            </Button>
          )}
        </div>
      );
    case 'footer':
      return (
        <>
          <p className="email-muted">
            The footer always shows your organization name, physical postal address and an unsubscribe link. Sending is blocked without it.
          </p>
          <FormField label="Extra footer text" optional>
            <Textarea rows={2} value={block.html ?? ''} onChange={(e) => set({ html: e.target.value || undefined })} />
          </FormField>
          <Switch
            checked={block.showPreferencesLink !== false}
            onCheckedChange={(checked) => set({ showPreferencesLink: checked })}
            label="Show “Manage preferences” link"
          />
        </>
      );
  }
}

function BlockList({
  blocks,
  onChange,
  allowed,
  path,
}: {
  blocks: DesignBlock[];
  onChange: (blocks: DesignBlock[]) => void;
  allowed: BlockType[];
  path: string;
}) {
  const [adding, setAdding] = useState<BlockType>(allowed[0] ?? 'text');
  const selectId = useId();
  const move = (i: number, delta: number) => {
    const next = [...blocks];
    const [item] = next.splice(i, 1);
    next.splice(i + delta, 0, item!);
    onChange(next);
  };
  return (
    <div className="stack">
      <ol className="email-blocks">
        {blocks.map((block, i) => (
          <li key={`${path}-${i}`} className="email-block">
            <div className="email-block__head">
              <h3 className="email-block__title">
                {i + 1}. {BLOCK_LABELS[block.type] ?? block.type}
              </h3>
              <div className="email-block__actions">
                <IconButton label={`Move ${BLOCK_LABELS[block.type]} block ${i + 1} up`} icon={<ArrowUp />} variant="ghost" size="sm" disabled={i === 0} onClick={() => move(i, -1)} />
                <IconButton
                  label={`Move ${BLOCK_LABELS[block.type]} block ${i + 1} down`}
                  icon={<ArrowDown />}
                  variant="ghost"
                  size="sm"
                  disabled={i === blocks.length - 1}
                  onClick={() => move(i, 1)}
                />
                <IconButton
                  label={`Remove ${BLOCK_LABELS[block.type]} block ${i + 1}`}
                  icon={<Trash2 />}
                  variant="ghost"
                  size="sm"
                  onClick={() => onChange(blocks.filter((_, j) => j !== i))}
                />
              </div>
            </div>
            <BlockFields block={block} path={`${path}-${i}`} onChange={(b) => onChange(blocks.map((x, j) => (j === i ? b : x)))} />
          </li>
        ))}
      </ol>
      <div className="cluster">
        <label className="visually-hidden" htmlFor={selectId}>
          Block type to add
        </label>
        <Select
          id={selectId}
          size="sm"
          value={adding}
          options={allowed.map((t) => ({ value: t, label: BLOCK_LABELS[t] }))}
          onChange={(e) => setAdding(e.target.value as BlockType)}
        />
        <Button variant="secondary" size="sm" leadingIcon={<Plus />} onClick={() => onChange([...blocks, newBlock(adding)])}>
          Add block
        </Button>
      </div>
    </div>
  );
}

/** Block-based email editor (header, text, image, button, divider, spacer, columns, social, footer). */
export function BlockEditor({ design, onChange }: { design: EmailDesign; onChange: (design: EmailDesign) => void }) {
  const hasFooter = design.blocks.some((b) => b.type === 'footer');
  const allowed = (Object.keys(BLOCK_LABELS) as BlockType[]).filter((t) => t !== 'footer' || !hasFooter);
  const titleId = useId();
  return (
    <section className="stack" aria-labelledby={titleId}>
      <h2 id={titleId} className="email-section-title">
        Content blocks
      </h2>
      <BlockList blocks={design.blocks} allowed={allowed} path="b" onChange={(blocks) => onChange({ ...design, blocks })} />
    </section>
  );
}

export function emptyDesign(): EmailDesign {
  return { blocks: [newBlock('header'), newBlock('text'), newBlock('button'), newBlock('footer')] };
}
