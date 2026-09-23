import clsx from 'clsx';
import { AlertCircle } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import './forms.css';

export interface RadioOption {
  value: string;
  label: ReactNode;
  description?: ReactNode;
  disabled?: boolean;
}

export interface RadioGroupProps {
  legend: ReactNode;
  name?: string;
  value: string | null;
  onChange: (value: string) => void;
  options: RadioOption[];
  orientation?: 'vertical' | 'horizontal';
  /** Bordered, card-like options. */
  variant?: 'plain' | 'cards';
  hint?: ReactNode;
  error?: string | null;
  required?: boolean;
  className?: string;
}

/** Native radios in a fieldset: arrow-key navigation and single tab stop come from the browser. */
export function RadioGroup({
  legend,
  name,
  value,
  onChange,
  options,
  orientation = 'vertical',
  variant = 'plain',
  hint,
  error,
  required,
  className,
}: RadioGroupProps) {
  const generated = useId();
  const groupName = name ?? `radio-${generated}`;
  const hintId = hint ? `${groupName}-hint` : undefined;
  const errorId = error ? `${groupName}-error` : undefined;

  return (
    <fieldset
      className={clsx(
        'ui-radio-group',
        orientation === 'horizontal' && 'ui-radio-group--horizontal',
        variant === 'cards' && 'ui-radio-group--cards',
        className,
      )}
      aria-describedby={[errorId, hintId].filter(Boolean).join(' ') || undefined}
    >
      <legend className="ui-radio-group__legend">{legend}</legend>
      <div className="ui-radio-group__options">
        {options.map((option) => {
          const optionId = `${groupName}-${option.value}`;
          return (
            <label key={option.value} className={clsx('ui-check', option.disabled && 'ui-check--disabled')}>
              <input
                id={optionId}
                type="radio"
                className="ui-check__input"
                name={groupName}
                value={option.value}
                checked={value === option.value}
                disabled={option.disabled}
                required={required}
                onChange={() => onChange(option.value)}
              />
              <span className="ui-check__text">
                <span className="ui-check__label">{option.label}</span>
                {option.description && <span className="ui-check__description">{option.description}</span>}
              </span>
            </label>
          );
        })}
      </div>
      {error && (
        <p id={errorId} className="ui-field__error" style={{ marginTop: 'var(--space-2)' }}>
          <AlertCircle aria-hidden="true" />
          <span>{error}</span>
        </p>
      )}
      {hint && (
        <p id={hintId} className="ui-field__hint" style={{ marginTop: 'var(--space-2)' }}>
          {hint}
        </p>
      )}
    </fieldset>
  );
}
