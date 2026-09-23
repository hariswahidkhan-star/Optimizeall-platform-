import type { FieldCondition, FormFieldDef, FormSchema } from './api';

/** A form value: text, a list (multiselect) or a boolean (checkbox/consent). */
export type FormValue = string | string[] | boolean;
export type FormValues = Record<string, FormValue | undefined>;

function asList(value: FormValue | undefined): string[] {
  if (value === undefined) return [];
  if (typeof value === 'boolean') return value ? ['true'] : [];
  if (Array.isArray(value)) return value.map((v) => v.trim()).filter(Boolean);
  return value.trim() ? [value.trim()] : [];
}

/** Mirrors `FormSchemas.Evaluate` on the server. */
export function evaluate(cond: FieldCondition, current: string[]): boolean {
  const lower = (s: string | null | undefined) => (s ?? '').toLowerCase();
  const first = current[0] ?? '';
  const num = (s: string | null | undefined) => (s === null || s === undefined || s.trim() === '' ? NaN : Number(s));
  switch (cond.operator) {
    case 'equals':
      return current.some((c) => lower(c) === lower(cond.value));
    case 'notEquals':
      return !current.some((c) => lower(c) === lower(cond.value));
    case 'contains':
      return !!cond.value && current.some((c) => lower(c).includes(lower(cond.value)));
    case 'in':
      return (cond.values ?? []).length > 0 && current.some((c) => (cond.values ?? []).some((v) => lower(v) === lower(c)));
    case 'isEmpty':
      return current.length === 0;
    case 'isNotEmpty':
      return current.length > 0;
    case 'greaterThan':
      return num(first) > num(cond.value);
    case 'lessThan':
      return num(first) < num(cond.value);
    default:
      return false;
  }
}

/**
 * Whether a field is shown for the current values — the same rule the server applies (a condition on a hidden field
 * treats that field as empty, so chains collapse).
 */
export function isVisible(field: FormFieldDef, values: FormValues, schema: FormSchema, depth = 0): boolean {
  const cond = field.showIf;
  if (!cond) return true;
  if (depth > 60) return false;
  const all = schema.steps.flatMap((s) => s.fields);
  const controller = all.find((f) => f.key === cond.field);
  const controllerVisible = !!controller && isVisible(controller, values, schema, depth + 1);
  return evaluate(cond, controllerVisible ? asList(values[cond.field]) : []);
}

const EMAIL = /^[^\s@<>()[\]\\,;:"]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$/;
const PHONE = /^\+?[0-9 ().-]{7,25}$/;

/** Client-side checks for instant feedback (the server re-validates everything). Returns field → message. */
export function validateFields(fields: FormFieldDef[], values: FormValues, files: Record<string, File | undefined>, schema: FormSchema) {
  const errors: Record<string, string> = {};
  for (const field of fields) {
    if (field.type === 'hidden' || !isVisible(field, values, schema)) continue;
    const list = asList(values[field.key]);
    if (field.type === 'file') {
      const file = files[field.key];
      if (!file) {
        if (field.required) errors[field.key] = `${field.label} is required.`;
        continue;
      }
      const maxMb = Math.min(field.validation?.maxSizeMb ?? 5, 10);
      if (file.size > maxMb * 1024 * 1024) errors[field.key] = `The file must be at most ${maxMb} MB.`;
      continue;
    }
    if (list.length === 0) {
      if (field.required) errors[field.key] = field.type === 'consent' ? 'Please accept to continue.' : `${field.label} is required.`;
      continue;
    }
    const value = list[0];
    const v = field.validation;
    if (field.type === 'email' && !EMAIL.test(value)) errors[field.key] = 'Enter a valid email address.';
    else if (field.type === 'phone' && (!PHONE.test(value) || value.replace(/\D/g, '').length < 7)) errors[field.key] = 'Enter a valid phone number.';
    else if (field.type === 'number') {
      const n = Number(value);
      if (Number.isNaN(n)) errors[field.key] = 'Enter a number.';
      else if (v?.min != null && n < v.min) errors[field.key] = `Must be at least ${v.min}.`;
      else if (v?.max != null && n > v.max) errors[field.key] = `Must be at most ${v.max}.`;
    } else if ((field.type === 'text' || field.type === 'textarea') && v) {
      if (v.maxLength != null && value.length > v.maxLength) errors[field.key] = `At most ${v.maxLength} characters.`;
      else if (v.minLength != null && value.length < v.minLength) errors[field.key] = `At least ${v.minLength} characters.`;
    } else if (field.type === 'multiselect') {
      if (v?.minChoices != null && list.length < v.minChoices) errors[field.key] = `Choose at least ${v.minChoices}.`;
      if (v?.maxChoices != null && list.length > v.maxChoices) errors[field.key] = `Choose at most ${v.maxChoices}.`;
    }
  }
  return errors;
}

/** Values to submit: visible fields only (hidden-by-condition values are dropped, like the server does). */
export function submittableValues(schema: FormSchema, values: FormValues): Record<string, FormValue> {
  const result: Record<string, FormValue> = {};
  for (const field of schema.steps.flatMap((s) => s.fields)) {
    if (field.type === 'file' || !isVisible(field, values, schema)) continue;
    const value = values[field.key];
    if (value === undefined || value === '' || (Array.isArray(value) && value.length === 0)) continue;
    result[field.key] = value;
  }
  return result;
}
