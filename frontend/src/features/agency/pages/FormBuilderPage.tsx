import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, Copy, Inbox, Plus, Save, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  ErrorState,
  FormField,
  IconButton,
  Input,
  PageHeader,
  Select,
  Skeleton,
  Tabs,
  Textarea,
  useToast,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { fieldErrors, lines } from '../seo/common';
import {
  pageKeys,
  type CaptchaProvider,
  type ConditionOperator,
  type EmbedInfo,
  type FieldType,
  type FormDetail,
  type FormFieldDef,
  type FormSchema,
  type FormStatus,
  type PublicForm,
} from './api';
import { move } from './blockDefaults';
import { formStatusTone } from './FormsListPage';
import { FormRenderer } from './FormRenderer';
import './pages.css';

export const fieldTypeLabels: Record<FieldType, string> = {
  text: 'Short text',
  email: 'Email',
  phone: 'Phone',
  number: 'Number',
  select: 'Dropdown',
  multiselect: 'Multiple choice (checkboxes)',
  checkbox: 'Single checkbox',
  radio: 'Single choice (radio)',
  date: 'Date',
  textarea: 'Long text',
  file: 'File upload',
  hidden: 'Hidden (URL parameter)',
  consent: 'Consent checkbox',
};

const operatorLabels: Record<ConditionOperator, string> = {
  equals: 'equals',
  notEquals: 'does not equal',
  contains: 'contains',
  in: 'is one of',
  isEmpty: 'is empty',
  isNotEmpty: 'is not empty',
  greaterThan: 'is greater than',
  lessThan: 'is less than',
};

const hasOptions = (t: FieldType) => t === 'select' || t === 'multiselect' || t === 'radio';

interface Draft {
  name: string;
  status: FormStatus;
  schema: FormSchema;
  submitLabel: string;
  successMessage: string;
  redirectUrl: string;
  notifyUserIds: string[];
  autoresponderEnabled: boolean;
  autoresponderSubject: string;
  autoresponderBody: string;
  allowedOrigins: string;
  consentText: string;
  captcha: CaptchaProvider;
  minFillSeconds: number;
}

const fromDetail = (f: FormDetail): Draft => ({
  name: f.name,
  status: f.status,
  schema: f.schema,
  submitLabel: f.submitLabel,
  successMessage: f.successMessage,
  redirectUrl: f.redirectUrl ?? '',
  notifyUserIds: f.notifyUserIds,
  autoresponderEnabled: f.autoresponderEnabled,
  autoresponderSubject: f.autoresponderSubject ?? '',
  autoresponderBody: f.autoresponderBody ?? '',
  allowedOrigins: f.allowedOrigins.join('\n'),
  consentText: f.consentText ?? '',
  captcha: f.captcha,
  minFillSeconds: f.minFillSeconds,
});

export function FormBuilderPage() {
  const { formId = '' } = useParams();
  const query = useQuery({ queryKey: pageKeys.form(formId), queryFn: () => api.get<FormDetail>(`/agency/pages/forms/${formId}`) });
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  if (!query.data)
    return (
      <>
        <PageHeader title="Form" breadcrumbs={[{ label: 'Forms', to: '/agency/pages/forms' }, { label: 'Loading…' }]} />
        <Skeleton height="20rem" />
      </>
    );
  return <FormEditor key={query.data.id} initial={query.data} />;
}

function newFieldKey(type: FieldType, schema: FormSchema): string {
  const used = new Set(schema.steps.flatMap((s) => s.fields.map((f) => f.key)));
  let n = 1;
  while (used.has(`${type}_${n}`)) n++;
  return `${type}_${n}`;
}

export function createField(type: FieldType, schema: FormSchema): FormFieldDef {
  const key = newFieldKey(type, schema);
  const base: FormFieldDef = { key, type, label: fieldTypeLabels[type], required: false };
  if (hasOptions(type))
    return { ...base, options: [{ value: 'option_1', label: 'Option 1' }, { value: 'option_2', label: 'Option 2' }] };
  if (type === 'hidden') return { ...base, label: 'Campaign', urlParam: 'campaign' };
  if (type === 'consent') return { ...base, label: 'I agree to be contacted about my enquiry.', required: true };
  if (type === 'file') return { ...base, validation: { accept: ['pdf', 'image'], maxSizeMb: 5 } };
  return base;
}

function FormEditor({ initial }: { initial: FormDetail }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [form, setForm] = useState(initial);
  const [draft, setDraft] = useState<Draft>(() => fromDetail(initial));
  const [dirty, setDirty] = useState(false);
  const [selected, setSelected] = useState<{ step: number; field: number } | null>(initial.schema.steps[0]?.fields.length ? { step: 0, field: 0 } : null);
  const [newType, setNewType] = useState<FieldType>('text');
  const [serverErrors, setServerErrors] = useState<Record<string, string[]>>({});
  const [announcement, setAnnouncement] = useState('');

  const update = (patch: Partial<Draft>) => {
    setDraft((d) => ({ ...d, ...patch }));
    setDirty(true);
  };
  const setSchema = (schema: FormSchema) => update({ schema });
  const steps = draft.schema.steps;

  const save = useMutation({
    mutationFn: () =>
      api.put<FormDetail>(`/agency/pages/forms/${form.id}`, {
        name: draft.name,
        status: draft.status,
        schema: draft.schema,
        submitLabel: draft.submitLabel,
        successMessage: draft.successMessage,
        redirectUrl: draft.redirectUrl || null,
        notifyUserIds: draft.notifyUserIds,
        autoresponderEnabled: draft.autoresponderEnabled,
        autoresponderSubject: draft.autoresponderSubject || null,
        autoresponderBody: draft.autoresponderBody || null,
        allowedOrigins: lines(draft.allowedOrigins),
        consentText: draft.consentText || null,
        captcha: draft.captcha,
        minFillSeconds: draft.minFillSeconds,
        concurrencyStamp: form.concurrencyStamp,
      }),
    onSuccess: (saved) => {
      setForm(saved);
      setDraft(fromDetail(saved));
      setDirty(false);
      setServerErrors({});
      queryClient.setQueryData(pageKeys.form(saved.id), saved);
      void queryClient.invalidateQueries({ queryKey: ['agency', 'pages', 'forms'] });
      toast.success('Form saved');
    },
    onError: (err) => {
      setServerErrors(fieldErrors(err));
      if (!isApiError(err) || !err.errors) toast.error('Could not save', errorMessage(err));
    },
  });

  const updateField = (stepIndex: number, fieldIndex: number, next: FormFieldDef) =>
    setSchema({ steps: steps.map((s, i) => (i === stepIndex ? { ...s, fields: s.fields.map((f, j) => (j === fieldIndex ? next : f)) } : s)) });

  const moveField = (stepIndex: number, from: number, to: number) => {
    const fields = steps[stepIndex].fields;
    if (to < 0 || to >= fields.length) return;
    setSchema({ steps: steps.map((s, i) => (i === stepIndex ? { ...s, fields: move(s.fields, from, to) } : s)) });
    setSelected({ step: stepIndex, field: to });
    setAnnouncement(`${fields[from].label} moved to position ${to + 1} of ${fields.length}.`);
  };

  const addField = (stepIndex: number) => {
    const field = createField(newType, draft.schema);
    const fields = [...steps[stepIndex].fields, field];
    setSchema({ steps: steps.map((s, i) => (i === stepIndex ? { ...s, fields } : s)) });
    setSelected({ step: stepIndex, field: fields.length - 1 });
    setAnnouncement(`${fieldTypeLabels[newType]} field added.`);
  };

  const selectedField = selected ? steps[selected.step]?.fields[selected.field] : undefined;
  const earlierFields = selected
    ? steps.flatMap((s, i) => s.fields.filter((_, j) => i < selected.step || (i === selected.step && j < selected.field)))
    : [];

  const previewForm: PublicForm = {
    id: form.id,
    name: draft.name,
    schema: draft.schema,
    submitLabel: draft.submitLabel,
    successMessage: draft.successMessage,
    redirectUrl: null,
    consentText: draft.consentText || null,
    consentVersion: form.consentVersion,
    captcha: null,
    token: '',
  };
  const errorCount = Object.keys(serverErrors).length;

  return (
    <>
      <PageHeader
        title={draft.name}
        description={form.clientName}
        breadcrumbs={[
          { label: 'Landing pages', to: '/agency/pages' },
          { label: 'Forms', to: '/agency/pages/forms' },
          { label: draft.name },
        ]}
        meta={
          <>
            <Badge tone={formStatusTone[form.status]}>{form.status}</Badge>
            {dirty && <Badge tone="info">Unsaved changes</Badge>}
          </>
        }
        actions={
          <>
            <ButtonLink to="submissions" variant="secondary" leadingIcon={<Inbox />}>
              Submissions
            </ButtonLink>
            <Button leadingIcon={<Save />} onClick={() => save.mutate()} loading={save.isPending} disabled={!dirty}>
              Save
            </Button>
          </>
        }
      />
      {errorCount > 0 && (
        <Alert tone="danger" title={`The form has ${errorCount} problem${errorCount === 1 ? '' : 's'}`} onDismiss={() => setServerErrors({})}>
          <ul className="pb-error-list">
            {Object.entries(serverErrors).slice(0, 8).map(([key, messages]) => (
              <li key={key}>
                <code>{key}</code>: {messages[0]}
              </li>
            ))}
          </ul>
        </Alert>
      )}
      <Tabs
        label="Form builder"
        tabs={[
          {
            id: 'fields',
            label: 'Fields & logic',
            content: (
              <div className="pb-layout">
                <div className="pb-sidebar stack">
                  {steps.map((step, stepIndex) => (
                    <section key={step.id} className="stack" aria-labelledby={`pb-step-${step.id}`}>
                      <h2 className="pb-panel-title" id={`pb-step-${step.id}`}>
                        Step {stepIndex + 1}
                        {step.title ? `: ${step.title}` : ''}
                      </h2>
                      <ol className="pb-block-list" aria-labelledby={`pb-step-${step.id}`}>
                        {step.fields.map((field, fieldIndex) => {
                          const isSel = selected?.step === stepIndex && selected.field === fieldIndex;
                          return (
                            <li key={field.key} className={isSel ? 'is-selected' : undefined}>
                              <button
                                type="button"
                                className="pb-block-list__select"
                                aria-current={isSel ? 'true' : undefined}
                                onClick={() => setSelected({ step: stepIndex, field: fieldIndex })}
                                onKeyDown={(e) => {
                                  if (!e.altKey || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown')) return;
                                  e.preventDefault();
                                  moveField(stepIndex, fieldIndex, fieldIndex + (e.key === 'ArrowUp' ? -1 : 1));
                                }}
                              >
                                <span className="pb-block-list__type">
                                  {field.label}
                                  {field.required ? ' *' : ''}
                                </span>
                                <span className="pb-block-list__summary">
                                  {fieldTypeLabels[field.type]}
                                  {field.showIf ? ' · conditional' : ''}
                                </span>
                              </button>
                              <span className="pb-block-list__actions">
                                <IconButton size="sm" variant="ghost" icon={<ArrowUp />} label={`Move ${field.label} up`} disabled={fieldIndex === 0} onClick={() => moveField(stepIndex, fieldIndex, fieldIndex - 1)} />
                                <IconButton
                                  size="sm"
                                  variant="ghost"
                                  icon={<ArrowDown />}
                                  label={`Move ${field.label} down`}
                                  disabled={fieldIndex === step.fields.length - 1}
                                  onClick={() => moveField(stepIndex, fieldIndex, fieldIndex + 1)}
                                />
                                <IconButton
                                  size="sm"
                                  variant="ghost"
                                  icon={<Trash2 />}
                                  label={`Remove ${field.label}`}
                                  onClick={() => {
                                    setSchema({ steps: steps.map((s, i) => (i === stepIndex ? { ...s, fields: s.fields.filter((_, j) => j !== fieldIndex) } : s)) });
                                    setSelected(null);
                                    setAnnouncement(`${field.label} removed.`);
                                  }}
                                />
                              </span>
                            </li>
                          );
                        })}
                      </ol>
                      <Button size="sm" variant="secondary" leadingIcon={<Plus />} onClick={() => addField(stepIndex)}>
                        Add {fieldTypeLabels[newType].toLowerCase()} to step {stepIndex + 1}
                      </Button>
                    </section>
                  ))}
                  <FormField label="New field type">
                    <Select value={newType} onChange={(e) => setNewType(e.target.value as FieldType)} options={(Object.keys(fieldTypeLabels) as FieldType[]).map((t) => ({ value: t, label: fieldTypeLabels[t] }))} />
                  </FormField>
                  <Button
                    variant="ghost"
                    leadingIcon={<Plus />}
                    disabled={steps.length >= 10}
                    onClick={() => {
                      let n = steps.length + 1;
                      while (steps.some((s) => s.id === `step-${n}`)) n++;
                      setSchema({ steps: [...steps, { id: `step-${n}`, title: `Step ${steps.length + 1}`, fields: [] }] });
                    }}
                  >
                    Add step
                  </Button>
                  <div aria-live="polite" role="status" className="visually-hidden">
                    {announcement}
                  </div>
                </div>
                <div className="pb-preview-column">
                  <p className="text-small text-muted">Preview — conditional fields appear as you fill it in. Submitting is disabled.</p>
                  <div className="pb-preview" role="region" aria-label="Form preview">
                    <div className="lp-block">
                      <div className="lp-block__inner lp-form-block">
                        <FormRenderer form={previewForm} preview />
                      </div>
                    </div>
                  </div>
                </div>
                <div className="pb-inspector-column">
                  {selected && selectedField ? (
                    <FieldEditor
                      key={`${selected.step}-${selected.field}`}
                      field={selectedField}
                      earlierFields={earlierFields}
                      steps={steps.map((s, i) => ({ id: s.id, label: `Step ${i + 1}` }))}
                      stepIndex={selected.step}
                      errors={serverErrors}
                      prefix={`schema.steps[${selected.step}].fields[${selected.field}]`}
                      onChange={(next) => updateField(selected.step, selected.field, next)}
                      onMoveToStep={(target) => {
                        const field = selectedField;
                        const next = steps.map((s, i) =>
                          i === selected.step ? { ...s, fields: s.fields.filter((_, j) => j !== selected.field) } : i === target ? { ...s, fields: [...s.fields, field] } : s,
                        );
                        setSchema({ steps: next });
                        setSelected({ step: target, field: next[target].fields.length - 1 });
                      }}
                    />
                  ) : (
                    <StepEditor
                      steps={steps}
                      onChange={(next) => setSchema({ steps: next })}
                    />
                  )}
                </div>
              </div>
            ),
          },
          { id: 'settings', label: 'Settings', content: <SettingsPanel draft={draft} update={update} consentVersion={form.consentVersion} errors={serverErrors} /> },
          { id: 'embed', label: 'Embed', content: <EmbedPanel formId={form.id} /> },
        ]}
      />
    </>
  );
}

function StepEditor({ steps, onChange }: { steps: FormSchema['steps']; onChange: (next: FormSchema['steps']) => void }) {
  return (
    <div className="stack">
      <h2 className="pb-panel-title">Steps</h2>
      <p className="text-small text-muted">Select a field to edit it. Name each step when the form has more than one.</p>
      {steps.map((step, i) => (
        <div key={step.id} className="pb-list__item">
          <FormField label={`Step ${i + 1} title`}>
            <Input value={step.title ?? ''} onChange={(e) => onChange(steps.map((s, j) => (j === i ? { ...s, title: e.target.value } : s)))} />
          </FormField>
          <FormField label={`Step ${i + 1} description`} optional>
            <Input value={step.description ?? ''} onChange={(e) => onChange(steps.map((s, j) => (j === i ? { ...s, description: e.target.value || null } : s)))} />
          </FormField>
          {steps.length > 1 && (
            <div className="pb-list__actions">
              <Button size="sm" variant="ghost" leadingIcon={<Trash2 />} disabled={step.fields.length > 0} onClick={() => onChange(steps.filter((_, j) => j !== i))}>
                Remove empty step
              </Button>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}

export function FieldEditor({
  field,
  earlierFields,
  steps,
  stepIndex,
  errors,
  prefix,
  onChange,
  onMoveToStep,
}: {
  field: FormFieldDef;
  earlierFields: FormFieldDef[];
  steps: { id: string; label: string }[];
  stepIndex: number;
  errors: Record<string, string[]>;
  prefix: string;
  onChange: (next: FormFieldDef) => void;
  onMoveToStep: (step: number) => void;
}) {
  const err = (k: string) => errors[`${prefix}.${k}`.toLowerCase()]?.[0];
  const set = (patch: Partial<FormFieldDef>) => onChange({ ...field, ...patch });
  const validation = field.validation ?? {};
  const setValidation = (patch: Partial<NonNullable<FormFieldDef['validation']>>) => set({ validation: { ...validation, ...patch } });
  const num = (v: string) => (v === '' ? null : Number(v));
  const cond = field.showIf;
  const condSource = earlierFields.find((f) => f.key === cond?.field);
  const needsValue = cond && !['isEmpty', 'isNotEmpty'].includes(cond.operator);

  return (
    <div className="stack pb-inspector">
      <h2 className="pb-panel-title">{fieldTypeLabels[field.type]}</h2>
      <FormField label="Label" required error={err('label')}>
        <Textarea rows={field.type === 'consent' ? 3 : 1} value={field.label} onChange={(e) => set({ label: e.target.value })} />
      </FormField>
      <FormField label="Field key" required hint="Used in exports and the lead record. Letters, digits, - and _." error={err('key')}>
        <Input value={field.key} onChange={(e) => set({ key: e.target.value.replace(/\s+/g, '_') })} />
      </FormField>
      <FormField label="Type">
        <Select
          value={field.type}
          onChange={(e) => {
            const type = e.target.value as FieldType;
            set({ type, options: hasOptions(type) ? (field.options ?? [{ value: 'option_1', label: 'Option 1' }]) : null });
          }}
          options={(Object.keys(fieldTypeLabels) as FieldType[]).map((t) => ({ value: t, label: fieldTypeLabels[t] }))}
        />
      </FormField>
      {field.type !== 'hidden' && <Checkbox label="Required" checked={!!field.required} onChange={(e) => set({ required: e.target.checked })} />}
      {['text', 'email', 'phone', 'number', 'textarea', 'select'].includes(field.type) && (
        <FormField label="Placeholder" optional>
          <Input value={field.placeholder ?? ''} onChange={(e) => set({ placeholder: e.target.value || null })} />
        </FormField>
      )}
      {field.type !== 'hidden' && (
        <FormField label="Help text" optional>
          <Input value={field.helpText ?? ''} onChange={(e) => set({ helpText: e.target.value || null })} />
        </FormField>
      )}
      {hasOptions(field.type) && (
        <FormField label="Options" required hint="One per line. Use value|Label to set a different stored value." error={err('options')}>
          <Textarea
            rows={5}
            value={(field.options ?? []).map((o) => (o.value === o.label ? o.label : `${o.value}|${o.label}`)).join('\n')}
            onChange={(e) =>
              set({
                options: e.target.value.split('\n').filter((l) => l.trim()).map((l) => {
                  const [value, label] = l.includes('|') ? l.split('|', 2) : [l, l];
                  return { value: value.trim(), label: (label ?? value).trim() };
                }),
              })
            }
          />
        </FormField>
      )}
      {['text', 'textarea'].includes(field.type) && (
        <div className="pb-grid-2">
          <FormField label="Min length" optional>
            <Input type="number" min={0} value={validation.minLength ?? ''} onChange={(e) => setValidation({ minLength: num(e.target.value) })} />
          </FormField>
          <FormField label="Max length" optional>
            <Input type="number" min={1} value={validation.maxLength ?? ''} onChange={(e) => setValidation({ maxLength: num(e.target.value) })} />
          </FormField>
        </div>
      )}
      {field.type === 'text' && (
        <>
          <FormField label="Pattern (regular expression)" optional hint="The whole value must match." error={err('validation.pattern')}>
            <Input value={validation.pattern ?? ''} onChange={(e) => setValidation({ pattern: e.target.value || null })} />
          </FormField>
          {validation.pattern && (
            <FormField label="Message when the pattern doesn’t match">
              <Input value={validation.patternMessage ?? ''} onChange={(e) => setValidation({ patternMessage: e.target.value || null })} />
            </FormField>
          )}
        </>
      )}
      {field.type === 'number' && (
        <div className="pb-grid-2">
          <FormField label="Minimum" optional>
            <Input type="number" value={validation.min ?? ''} onChange={(e) => setValidation({ min: num(e.target.value) })} />
          </FormField>
          <FormField label="Maximum" optional>
            <Input type="number" value={validation.max ?? ''} onChange={(e) => setValidation({ max: num(e.target.value) })} />
          </FormField>
        </div>
      )}
      {field.type === 'multiselect' && (
        <div className="pb-grid-2">
          <FormField label="Min choices" optional>
            <Input type="number" min={0} value={validation.minChoices ?? ''} onChange={(e) => setValidation({ minChoices: num(e.target.value) })} />
          </FormField>
          <FormField label="Max choices" optional>
            <Input type="number" min={1} value={validation.maxChoices ?? ''} onChange={(e) => setValidation({ maxChoices: num(e.target.value) })} />
          </FormField>
        </div>
      )}
      {field.type === 'file' && (
        <>
          <fieldset className="lp-fieldset">
            <legend className="ui-field__label">Accepted files</legend>
            {(['pdf', 'image'] as const).map((kind) => (
              <Checkbox
                key={kind}
                label={kind === 'pdf' ? 'PDF documents' : 'Images (JPEG, PNG, WebP, GIF)'}
                checked={(validation.accept ?? ['pdf', 'image']).includes(kind)}
                onChange={(e) => {
                  const current = validation.accept ?? ['pdf', 'image'];
                  setValidation({ accept: e.target.checked ? [...new Set([...current, kind])] : current.filter((k) => k !== kind) });
                }}
              />
            ))}
          </fieldset>
          <FormField label="Max size (MB)" hint="Up to 10 MB." error={err('validation.maxSizeMb')}>
            <Input type="number" min={1} max={10} value={validation.maxSizeMb ?? 5} onChange={(e) => setValidation({ maxSizeMb: num(e.target.value) })} />
          </FormField>
        </>
      )}
      {field.type === 'hidden' && (
        <>
          <FormField label="URL parameter" hint="Filled from the page URL, e.g. ?campaign=spring." error={err('urlParam')}>
            <Input value={field.urlParam ?? ''} onChange={(e) => set({ urlParam: e.target.value || null })} />
          </FormField>
          <FormField label="Default value" optional>
            <Input value={field.defaultValue ?? ''} onChange={(e) => set({ defaultValue: e.target.value || null })} />
          </FormField>
        </>
      )}
      {field.type !== 'hidden' && (
        <FormField label="Width">
          <Select
            value={field.width ?? 'full'}
            onChange={(e) => set({ width: e.target.value as 'full' | 'half' })}
            options={[
              { value: 'full', label: 'Full width' },
              { value: 'half', label: 'Half width (side by side on desktop)' },
            ]}
          />
        </FormField>
      )}
      {steps.length > 1 && (
        <FormField label="Step">
          <Select value={String(stepIndex)} onChange={(e) => onMoveToStep(Number(e.target.value))} options={steps.map((s, i) => ({ value: String(i), label: s.label }))} />
        </FormField>
      )}

      <fieldset className="stack pb-list__item">
        <legend className="ui-field__label">Conditional logic</legend>
        {earlierFields.length === 0 ? (
          <p className="text-small text-muted">Add fields before this one to show it conditionally.</p>
        ) : (
          <>
            <Checkbox
              label="Only show this field when…"
              checked={!!cond}
              onChange={(e) => set({ showIf: e.target.checked ? { field: earlierFields[0].key, operator: 'isNotEmpty' } : null })}
            />
            {cond && (
              <>
                <FormField label="Field" error={err('showIf.field')}>
                  <Select value={cond.field} onChange={(e) => set({ showIf: { ...cond, field: e.target.value } })} options={earlierFields.map((f) => ({ value: f.key, label: f.label }))} />
                </FormField>
                <FormField label="Condition">
                  <Select
                    value={cond.operator}
                    onChange={(e) => set({ showIf: { ...cond, operator: e.target.value as ConditionOperator } })}
                    options={(Object.keys(operatorLabels) as ConditionOperator[]).map((o) => ({ value: o, label: operatorLabels[o] }))}
                  />
                </FormField>
                {needsValue &&
                  (cond.operator === 'in' ? (
                    <FormField label="Values" hint="One per line." error={err('showIf.values')}>
                      <Textarea rows={3} value={(cond.values ?? []).join('\n')} onChange={(e) => set({ showIf: { ...cond, values: lines(e.target.value) } })} />
                    </FormField>
                  ) : condSource?.options && ['equals', 'notEquals', 'contains'].includes(cond.operator) ? (
                    <FormField label="Value" error={err('showIf.value')}>
                      <Select
                        value={cond.value ?? ''}
                        placeholder="Choose a value"
                        onChange={(e) => set({ showIf: { ...cond, value: e.target.value } })}
                        options={condSource.options.map((o) => ({ value: o.value, label: o.label }))}
                      />
                    </FormField>
                  ) : (
                    <FormField label="Value" error={err('showIf.value')} hint={condSource?.type === 'checkbox' || condSource?.type === 'consent' ? 'Use true or false.' : undefined}>
                      <Input value={cond.value ?? ''} onChange={(e) => set({ showIf: { ...cond, value: e.target.value } })} />
                    </FormField>
                  ))}
              </>
            )}
          </>
        )}
      </fieldset>
    </div>
  );
}

function SettingsPanel({
  draft,
  update,
  consentVersion,
  errors,
}: {
  draft: Draft;
  update: (patch: Partial<Draft>) => void;
  consentVersion: number;
  errors: Record<string, string[]>;
}) {
  const e = (k: string) => errors[k.toLowerCase()]?.[0];
  const staff = useQuery({
    queryKey: ['agency', 'pages', 'staff-options'],
    queryFn: () => api.get<{ id: string; displayName: string; email: string }[]>('/agency/pages/staff-options'),
    staleTime: 5 * 60_000,
  });
  return (
    <div className="stack">
      <Card>
        <CardHeader title="General" headingLevel={2} />
        <CardBody>
          <div className="stack">
            <div className="pb-grid-2">
              <FormField label="Form name" required error={e('name')}>
                <Input value={draft.name} onChange={(ev) => update({ name: ev.target.value })} />
              </FormField>
              <FormField label="Status" hint="Only active forms accept submissions.">
                <Select
                  value={draft.status}
                  onChange={(ev) => update({ status: ev.target.value as FormStatus })}
                  options={(['Active', 'Draft', 'Archived'] as const).map((s) => ({ value: s, label: s }))}
                />
              </FormField>
            </div>
            <div className="pb-grid-2">
              <FormField label="Submit button label" required error={e('submitLabel')}>
                <Input value={draft.submitLabel} onChange={(ev) => update({ submitLabel: ev.target.value })} />
              </FormField>
              <FormField label="Redirect after submit" optional hint="An https:// URL; otherwise the success message shows." error={e('redirectUrl')}>
                <Input value={draft.redirectUrl} inputMode="url" onChange={(ev) => update({ redirectUrl: ev.target.value })} />
              </FormField>
            </div>
            <FormField label="Success message" required error={e('successMessage')}>
              <Textarea rows={2} value={draft.successMessage} onChange={(ev) => update({ successMessage: ev.target.value })} />
            </FormField>
          </div>
        </CardBody>
      </Card>
      <Card>
        <CardHeader title="Notifications" description="Who hears about new submissions, and what the submitter receives." headingLevel={2} />
        <CardBody>
          <div className="stack">
            <fieldset className="lp-fieldset">
              <legend className="ui-field__label">Email these team members</legend>
              {(staff.data ?? []).map((s) => (
                <Checkbox
                  key={s.id}
                  label={s.displayName}
                  description={s.email}
                  checked={draft.notifyUserIds.includes(s.id)}
                  onChange={(ev) => update({ notifyUserIds: ev.target.checked ? [...draft.notifyUserIds, s.id] : draft.notifyUserIds.filter((id) => id !== s.id) })}
                />
              ))}
              {staff.data?.length === 0 && <p className="text-small text-muted">No team members with access to this client.</p>}
            </fieldset>
            <Checkbox
              label="Send an autoresponder email"
              description="Sent to the address in the form’s email field."
              checked={draft.autoresponderEnabled}
              onChange={(ev) => update({ autoresponderEnabled: ev.target.checked })}
            />
            {draft.autoresponderEnabled && (
              <>
                <FormField label="Autoresponder subject" required error={e('autoresponderSubject')}>
                  <Input value={draft.autoresponderSubject} onChange={(ev) => update({ autoresponderSubject: ev.target.value })} />
                </FormField>
                <FormField label="Autoresponder message" required hint="Plain text. {{field_key}} inserts a submitted value (e.g. {{first_name}}); {{form}} inserts the form name." error={e('autoresponderBody')}>
                  <Textarea rows={5} value={draft.autoresponderBody} onChange={(ev) => update({ autoresponderBody: ev.target.value })} />
                </FormField>
              </>
            )}
          </div>
        </CardBody>
      </Card>
      <Card>
        <CardHeader title="Consent & spam protection" headingLevel={2} />
        <CardBody>
          <div className="stack">
            <FormField
              label="Consent text"
              optional
              hint={`Shown with consent fields and stored with each submission. Current version: v${consentVersion}; changing the text creates a new version.`}
              error={e('consentText')}
            >
              <Textarea rows={3} value={draft.consentText} onChange={(ev) => update({ consentText: ev.target.value })} />
            </FormField>
            <div className="pb-grid-2">
              <FormField label="CAPTCHA" hint="Requires site and secret keys under Integrations.">
                <Select
                  value={draft.captcha}
                  onChange={(ev) => update({ captcha: ev.target.value as CaptchaProvider })}
                  options={[
                    { value: 'None', label: 'None (honeypot + timing only)' },
                    { value: 'Turnstile', label: 'Cloudflare Turnstile' },
                    { value: 'HCaptcha', label: 'hCaptcha' },
                  ]}
                />
              </FormField>
              <FormField label="Minimum fill time (seconds)" hint="Faster submissions are treated as bots." error={e('minFillSeconds')}>
                <Input type="number" min={0} max={60} value={draft.minFillSeconds} onChange={(ev) => update({ minFillSeconds: Number(ev.target.value) || 0 })} />
              </FormField>
            </div>
          </div>
        </CardBody>
      </Card>
      <Card>
        <CardHeader title="Embedding" headingLevel={2} />
        <CardBody>
          <FormField label="Allowed websites" optional hint="One origin per line, e.g. https://www.example.com. The form only loads and submits from these sites and this platform." error={e('allowedOrigins')}>
            <Textarea rows={3} value={draft.allowedOrigins} onChange={(ev) => update({ allowedOrigins: ev.target.value })} />
          </FormField>
        </CardBody>
      </Card>
    </div>
  );
}

function EmbedPanel({ formId }: { formId: string }) {
  const toast = useToast();
  const embed = useQuery({ queryKey: [...pageKeys.form(formId), 'embed'], queryFn: () => api.get<EmbedInfo>(`/agency/pages/forms/${formId}/embed`) });
  if (embed.isError) return <ErrorState error={embed.error} onRetry={() => void embed.refetch()} />;
  if (!embed.data) return <Skeleton height="10rem" />;
  const copy = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      toast.success('Copied to clipboard');
    } catch {
      toast.error('Copy failed', 'Select the code and copy it manually.');
    }
  };
  return (
    <Card>
      <CardHeader title="Embed on a website" description={embed.data.guidance} headingLevel={2} />
      <CardBody>
        <div className="stack">
          <dl className="pb-kv">
            <dt>Form URL</dt>
            <dd>
              <code>{embed.data.formUrl}</code>
            </dd>
            <dt>Allowed websites</dt>
            <dd>{embed.data.allowedOrigins.length ? embed.data.allowedOrigins.join(', ') : 'None yet — add them in Settings.'}</dd>
            <dt>frame-ancestors</dt>
            <dd>
              <code>{embed.data.frameAncestors}</code>
            </dd>
          </dl>
          <FormField label="Embed code">
            <Textarea className="pb-code" readOnly rows={4} value={embed.data.iframeSnippet} onFocus={(e) => e.currentTarget.select()} />
          </FormField>
          <div>
            <Button variant="secondary" leadingIcon={<Copy />} onClick={() => void copy(embed.data.iframeSnippet)}>
              Copy embed code
            </Button>
          </div>
        </div>
      </CardBody>
    </Card>
  );
}
