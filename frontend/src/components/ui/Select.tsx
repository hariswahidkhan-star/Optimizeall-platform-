import clsx from 'clsx';
import { forwardRef, type SelectHTMLAttributes } from 'react';
import { useFieldControlProps } from './fieldContext';
import './forms.css';

export interface SelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

export interface SelectOptionGroup {
  label: string;
  options: SelectOption[];
}

export interface SelectProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'size'> {
  /** Flat options or grouped (<optgroup>) options. Children are used when omitted. */
  options?: (SelectOption | SelectOptionGroup)[];
  /** Adds a first empty option with this text. */
  placeholder?: string;
  invalid?: boolean;
  size?: 'sm' | 'md';
}

function isGroup(option: SelectOption | SelectOptionGroup): option is SelectOptionGroup {
  return 'options' in option;
}

/** Native <select> with the design-system look (keeps native keyboard and mobile pickers). */
export const Select = forwardRef<HTMLSelectElement, SelectProps>(function Select(
  { className, options, placeholder, children, size = 'md', ...props },
  ref,
) {
  const controlProps = useFieldControlProps(props);
  return (
    <select
      ref={ref}
      className={clsx('ui-select', size === 'sm' && 'ui-select--sm', className)}
      {...controlProps}
    >
      {placeholder !== undefined && <option value="">{placeholder}</option>}
      {options
        ? options.map((option) =>
            isGroup(option) ? (
              <optgroup key={option.label} label={option.label}>
                {option.options.map((o) => (
                  <option key={o.value} value={o.value} disabled={o.disabled}>
                    {o.label}
                  </option>
                ))}
              </optgroup>
            ) : (
              <option key={option.value} value={option.value} disabled={option.disabled}>
                {option.label}
              </option>
            ),
          )
        : children}
    </select>
  );
});
