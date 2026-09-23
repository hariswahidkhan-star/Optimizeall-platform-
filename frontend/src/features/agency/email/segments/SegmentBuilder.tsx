import { useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/Button';
import { FormField } from '@/components/ui/FormField';
import { IconButton } from '@/components/ui/IconButton';
import { Input, type InputProps } from '@/components/ui/Input';
import { Select, type SelectOption } from '@/components/ui/Select';
import type { SegmentCondition, SegmentConditionKind, SegmentDefinition } from '../api/types';

export const KIND_OPTIONS: SelectOption[] = [
  { value: 'field', label: 'Contact field' },
  { value: 'custom', label: 'Custom field' },
  { value: 'tag', label: 'Tag' },
  { value: 'list', label: 'List membership' },
  { value: 'engagement', label: 'Email engagement' },
  { value: 'purchase', label: 'Purchase' },
  { value: 'consent', label: 'Consent' },
  { value: 'event', label: 'Custom event' },
];

const FIELD_OPTIONS: SelectOption[] = [
  { value: 'email', label: 'Email' },
  { value: 'first_name', label: 'First name' },
  { value: 'last_name', label: 'Last name' },
  { value: 'country', label: 'Country (ISO code)' },
  { value: 'language', label: 'Language' },
  { value: 'source', label: 'Source' },
  { value: 'status', label: 'Status' },
  { value: 'phone', label: 'Phone' },
  { value: 'created_at', label: 'Date added' },
];

const TEXT_OPS: SelectOption[] = [
  { value: 'equals', label: 'is' },
  { value: 'not_equals', label: 'is not' },
  { value: 'contains', label: 'contains' },
  { value: 'starts_with', label: 'starts with' },
  { value: 'in', label: 'is one of' },
  { value: 'not_in', label: 'is none of' },
  { value: 'is_empty', label: 'is empty' },
  { value: 'is_not_empty', label: 'is not empty' },
];

const DATE_OPS: SelectOption[] = [
  { value: 'within_days', label: 'in the last N days' },
  { value: 'before_days_ago', label: 'more than N days ago' },
];

const ENGAGEMENT_EVENTS: SelectOption[] = [
  { value: 'opened', label: 'opened an email' },
  { value: 'clicked', label: 'clicked an email' },
  { value: 'received', label: 'received an email' },
  { value: 'not_opened', label: 'did not open' },
  { value: 'not_clicked', label: 'did not click' },
];

export function emptyDefinition(): SegmentDefinition {
  return { match: 'all', conditions: [{ kind: 'field', field: 'country', op: 'in', values: [] }], groups: [] };
}

function defaultsFor(kind: SegmentConditionKind): SegmentCondition {
  switch (kind) {
    case 'field':
      return { kind, field: 'country', op: 'equals', value: '' };
    case 'custom':
      return { kind, field: '', op: 'equals', value: '' };
    case 'tag':
      return { kind, op: 'has', value: '' };
    case 'list':
      return { kind, op: 'in', value: '' };
    case 'engagement':
      return { kind, event: 'opened', withinDays: 30 };
    case 'purchase':
      return { kind, op: 'purchased', withinDays: 90 };
    case 'consent':
      return { kind, channel: 'email', op: 'granted' };
    case 'event':
      return { kind, op: 'occurred', value: '', withinDays: 30 };
  }
}

/** Comma-separated values; keeps the raw text while typing so a trailing comma is not swallowed. */
function ListInput({ values, onChange, ...rest }: { values: string[]; onChange: (values: string[]) => void } & Omit<InputProps, 'value' | 'onChange'>) {
  const [text, setText] = useState(() => values.join(', '));
  const parse = (raw: string) => raw.split(',').map((v) => v.trim()).filter(Boolean);
  const current = parse(text).join('\u0000') === values.join('\u0000') ? text : values.join(', ');
  return (
    <Input
      {...rest}
      value={current}
      onChange={(e) => {
        setText(e.target.value);
        onChange(parse(e.target.value));
      }}
    />
  );
}

interface Lookups {
  lists: SelectOption[];
  campaigns: SelectOption[];
}

function ConditionEditor({
  condition,
  onChange,
  onRemove,
  label,
  lookups,
}: {
  condition: SegmentCondition;
  onChange: (c: SegmentCondition) => void;
  onRemove: () => void;
  label: string;
  lookups: Lookups;
}) {
  const set = (patch: Partial<SegmentCondition>) => onChange({ ...condition, ...patch });
  const days = (value: string) => (value === '' ? undefined : Number(value));
  const k = condition.kind;
  return (
    <div className="segment-condition" role="group" aria-label={label}>
      <FormField label="Rule type">
        <Select value={k} options={KIND_OPTIONS} onChange={(e) => onChange(defaultsFor(e.target.value as SegmentConditionKind))} />
      </FormField>

      {k === 'field' && (
        <>
          <FormField label="Field">
            <Select
              value={condition.field ?? 'country'}
              options={FIELD_OPTIONS}
              onChange={(e) =>
                set(e.target.value === 'created_at' ? { field: e.target.value, op: 'within_days', withinDays: 30 } : { field: e.target.value, op: 'equals' })
              }
            />
          </FormField>
          <FormField label="Operator">
            <Select
              value={condition.op ?? 'equals'}
              options={condition.field === 'created_at' ? DATE_OPS : TEXT_OPS}
              onChange={(e) => set({ op: e.target.value })}
            />
          </FormField>
          {condition.field === 'created_at' ? (
            <FormField label="Days">
              <Input type="number" min={0} value={condition.withinDays ?? ''} onChange={(e) => set({ withinDays: days(e.target.value) })} />
            </FormField>
          ) : condition.op === 'in' || condition.op === 'not_in' ? (
            <FormField label="Values" hint="Comma-separated">
              <ListInput values={condition.values ?? []} onChange={(values) => set({ values })} />
            </FormField>
          ) : condition.op === 'is_empty' || condition.op === 'is_not_empty' ? null : (
            <FormField label="Value">
              <Input value={condition.value ?? ''} onChange={(e) => set({ value: e.target.value })} />
            </FormField>
          )}
        </>
      )}

      {k === 'custom' && (
        <>
          <FormField label="Custom field key">
            <Input value={condition.field ?? ''} onChange={(e) => set({ field: e.target.value })} placeholder="plan" />
          </FormField>
          <FormField label="Operator">
            <Select value={condition.op ?? 'equals'} options={TEXT_OPS.filter((o) => !['in', 'not_in', 'starts_with'].includes(o.value))} onChange={(e) => set({ op: e.target.value })} />
          </FormField>
          {condition.op !== 'is_empty' && condition.op !== 'is_not_empty' && (
            <FormField label="Value">
              <Input value={condition.value ?? ''} onChange={(e) => set({ value: e.target.value })} />
            </FormField>
          )}
        </>
      )}

      {k === 'tag' && (
        <>
          <FormField label="Operator">
            <Select value={condition.op ?? 'has'} options={[{ value: 'has', label: 'has tag' }, { value: 'has_not', label: 'does not have tag' }]} onChange={(e) => set({ op: e.target.value })} />
          </FormField>
          <FormField label="Tag">
            <Input value={condition.value ?? ''} onChange={(e) => set({ value: e.target.value })} />
          </FormField>
        </>
      )}

      {k === 'list' && (
        <>
          <FormField label="Operator">
            <Select value={condition.op ?? 'in'} options={[{ value: 'in', label: 'is subscribed to' }, { value: 'not_in', label: 'is not subscribed to' }]} onChange={(e) => set({ op: e.target.value })} />
          </FormField>
          <FormField label="List">
            <Select value={condition.value ?? ''} placeholder="Choose a list" options={lookups.lists} onChange={(e) => set({ value: e.target.value })} />
          </FormField>
        </>
      )}

      {k === 'engagement' && (
        <>
          <FormField label="Behaviour">
            <Select value={condition.event ?? 'opened'} options={ENGAGEMENT_EVENTS} onChange={(e) => set({ event: e.target.value })} />
          </FormField>
          <FormField label="In the last (days)">
            <Input type="number" min={1} value={condition.withinDays ?? ''} onChange={(e) => set({ withinDays: days(e.target.value) })} />
          </FormField>
          <FormField label="Campaign" optional>
            <Select
              value={condition.campaignId ?? ''}
              placeholder="Any campaign"
              options={lookups.campaigns}
              onChange={(e) => set({ campaignId: e.target.value || undefined })}
            />
          </FormField>
        </>
      )}

      {k === 'purchase' && (
        <>
          <FormField label="Operator">
            <Select
              value={condition.op ?? 'purchased'}
              options={[{ value: 'purchased', label: 'purchased' }, { value: 'not_purchased', label: 'has not purchased' }]}
              onChange={(e) => set({ op: e.target.value })}
            />
          </FormField>
          <FormField label="In the last (days)">
            <Input type="number" min={1} value={condition.withinDays ?? ''} onChange={(e) => set({ withinDays: days(e.target.value) })} />
          </FormField>
        </>
      )}

      {k === 'consent' && (
        <>
          <FormField label="Channel">
            <Select
              value={condition.channel ?? 'email'}
              options={[{ value: 'email', label: 'Email' }, { value: 'sms', label: 'SMS' }, { value: 'whatsapp', label: 'WhatsApp' }]}
              onChange={(e) => set({ channel: e.target.value })}
            />
          </FormField>
          <FormField label="Operator">
            <Select value={condition.op ?? 'granted'} options={[{ value: 'granted', label: 'granted' }, { value: 'not_granted', label: 'not granted' }]} onChange={(e) => set({ op: e.target.value })} />
          </FormField>
        </>
      )}

      {k === 'event' && (
        <>
          <FormField label="Operator">
            <Select value={condition.op ?? 'occurred'} options={[{ value: 'occurred', label: 'happened' }, { value: 'not_occurred', label: 'did not happen' }]} onChange={(e) => set({ op: e.target.value })} />
          </FormField>
          <FormField label="Event name">
            <Input value={condition.value ?? ''} onChange={(e) => set({ value: e.target.value })} placeholder="cart_abandoned" />
          </FormField>
          <FormField label="In the last (days)">
            <Input type="number" min={1} value={condition.withinDays ?? ''} onChange={(e) => set({ withinDays: days(e.target.value) })} />
          </FormField>
        </>
      )}

      <IconButton label={`Remove ${label}`} icon={<Trash2 />} variant="ghost" onClick={onRemove} />
    </div>
  );
}

function GroupEditor({
  group,
  onChange,
  onRemove,
  depth,
  label,
  lookups,
}: {
  group: SegmentDefinition;
  onChange: (g: SegmentDefinition) => void;
  onRemove?: () => void;
  depth: number;
  label: string;
  lookups: Lookups;
}) {
  return (
    <fieldset className="segment-group">
      <legend>{label}</legend>
      <FormField label="Contacts must match">
        <Select
          value={group.match}
          options={[
            { value: 'all', label: 'all of these rules (AND)' },
            { value: 'any', label: 'any of these rules (OR)' },
          ]}
          onChange={(e) => onChange({ ...group, match: e.target.value as 'all' | 'any' })}
        />
      </FormField>
      {group.conditions.map((c, i) => (
        <ConditionEditor
          key={i}
          label={`${label}, rule ${i + 1}`}
          condition={c}
          lookups={lookups}
          onChange={(next) => onChange({ ...group, conditions: group.conditions.map((x, j) => (j === i ? next : x)) })}
          onRemove={() => onChange({ ...group, conditions: group.conditions.filter((_, j) => j !== i) })}
        />
      ))}
      {group.groups.map((g, i) => (
        <GroupEditor
          key={i}
          group={g}
          depth={depth + 1}
          label={`${label}, group ${i + 1}`}
          lookups={lookups}
          onChange={(next) => onChange({ ...group, groups: group.groups.map((x, j) => (j === i ? next : x)) })}
          onRemove={() => onChange({ ...group, groups: group.groups.filter((_, j) => j !== i) })}
        />
      ))}
      <div className="cluster">
        <Button
          variant="secondary"
          size="sm"
          leadingIcon={<Plus />}
          onClick={() => onChange({ ...group, conditions: [...group.conditions, defaultsFor('field')] })}
        >
          Add rule
        </Button>
        {depth < 3 && (
          <Button
            variant="secondary"
            size="sm"
            leadingIcon={<Plus />}
            onClick={() => onChange({ ...group, groups: [...group.groups, { match: 'any', conditions: [defaultsFor('tag')], groups: [] }] })}
          >
            Add group
          </Button>
        )}
        {onRemove && (
          <Button variant="ghost" size="sm" onClick={onRemove}>
            Remove group
          </Button>
        )}
      </div>
    </fieldset>
  );
}

/** Rule builder: field/tag/list/engagement/purchase/consent/event conditions combined with AND/OR, nested up to 3 levels. */
export function SegmentBuilder({
  definition,
  onChange,
  lists = [],
  campaigns = [],
}: {
  definition: SegmentDefinition;
  onChange: (d: SegmentDefinition) => void;
  lists?: SelectOption[];
  campaigns?: SelectOption[];
}) {
  return <GroupEditor group={definition} onChange={onChange} depth={1} label="Segment rules" lookups={{ lists, campaigns }} />;
}
