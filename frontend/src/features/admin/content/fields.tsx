import type { ReactNode } from 'react';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { Select, type SelectOption } from '@/components/ui/Select';
import { Textarea } from '@/components/ui/Textarea';

type ErrorValue = string[] | string | undefined;

interface BaseProps {
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: ErrorValue;
  hint?: ReactNode;
  required?: boolean;
  optional?: boolean;
}

export function TextField({
  maxLength,
  type = 'text',
  ...props
}: BaseProps & { maxLength?: number; type?: 'text' | 'url' | 'number' | 'datetime-local' }) {
  return (
    <FormField
      label={props.label}
      error={props.error}
      hint={props.hint}
      required={props.required}
      optional={props.optional}
    >
      <Input
        type={type}
        value={props.value}
        maxLength={maxLength}
        onChange={(e) => props.onChange(e.target.value)}
        inputMode={type === 'number' ? 'numeric' : undefined}
      />
    </FormField>
  );
}

export function TextAreaField({
  maxLength,
  rows = 4,
  ...props
}: BaseProps & { maxLength?: number; rows?: number }) {
  return (
    <FormField
      label={props.label}
      error={props.error}
      hint={props.hint}
      required={props.required}
      optional={props.optional}
    >
      <Textarea
        rows={rows}
        value={props.value}
        maxLength={maxLength}
        onChange={(e) => props.onChange(e.target.value)}
      />
    </FormField>
  );
}

export function SelectField({
  options,
  placeholder,
  ...props
}: BaseProps & { options: SelectOption[]; placeholder?: string }) {
  return (
    <FormField
      label={props.label}
      error={props.error}
      hint={props.hint}
      required={props.required}
      optional={props.optional}
    >
      <Select
        value={props.value}
        options={options}
        placeholder={placeholder}
        onChange={(e) => props.onChange(e.target.value)}
      />
    </FormField>
  );
}

/** Links must be absolute https:// URLs or app paths starting with a single "/" (mirrors the server rule). */
export function isSafeLink(value: string): boolean {
  if (value.startsWith('/')) return !value.startsWith('//');
  try {
    const url = new URL(value);
    return url.protocol === 'https:';
  } catch {
    return false;
  }
}

export const LINK_HINT = 'An https:// address or an app path such as /app/campaigns.';

export function requireLength(
  errors: Record<string, string>,
  field: string,
  value: string,
  min: number,
  max: number,
  label: string,
) {
  const length = value.trim().length;
  if (length < min)
    errors[field] = min <= 1 ? `Enter ${label}.` : `${label} needs at least ${min} characters.`;
  else if (length > max) errors[field] = `${label} can be at most ${max} characters.`;
}

export function parseSortOrder(errors: Record<string, string>, value: string): number {
  const n = Number(value || '0');
  if (!Number.isInteger(n) || n < -100000 || n > 100000)
    errors.sortOrder = 'Use a whole number from -100,000 to 100,000.';
  return n;
}
