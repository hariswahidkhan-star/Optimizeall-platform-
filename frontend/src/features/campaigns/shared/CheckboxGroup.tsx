import type { ReactNode } from 'react';
import { Checkbox, type SelectOption } from '@/components/ui';

export interface CheckboxGroupProps {
  legend: ReactNode;
  hint?: ReactNode;
  options: SelectOption[];
  value: string[];
  onChange: (value: string[]) => void;
  error?: string[] | string | null;
  disabled?: boolean;
  required?: boolean;
}

/** A fieldset of checkboxes for multi-select enums (platforms, tiers). */
export function CheckboxGroup({
  legend,
  hint,
  options,
  value,
  onChange,
  error,
  disabled,
  required,
}: CheckboxGroupProps) {
  const errors = Array.isArray(error) ? error : error ? [error] : [];
  const toggle = (option: string, checked: boolean) =>
    onChange(
      checked
        ? [...value, option].filter((v, i, a) => a.indexOf(v) === i)
        : value.filter((v) => v !== option),
    );
  return (
    <fieldset className="mg-checkgroup" aria-invalid={errors.length > 0 || undefined}>
      <legend className="ui-field__label">
        {legend}
        {required && <span aria-hidden="true"> *</span>}
      </legend>
      {hint && <p className="mg-hint">{hint}</p>}
      <div className="mg-checkgroup__options">
        {options.map((option) => (
          <Checkbox
            key={option.value}
            label={option.label}
            checked={value.includes(option.value)}
            disabled={disabled || option.disabled}
            invalid={errors.length > 0}
            onChange={(e) => toggle(option.value, e.target.checked)}
          />
        ))}
      </div>
      {errors.length > 0 && (
        <p className="ui-field__error" role="alert">
          {errors.join(' ')}
        </p>
      )}
    </fieldset>
  );
}
