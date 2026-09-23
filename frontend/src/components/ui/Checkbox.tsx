import clsx from 'clsx';
import {
  forwardRef,
  useEffect,
  useId,
  useImperativeHandle,
  useRef,
  type InputHTMLAttributes,
  type ReactNode,
} from 'react';
import './forms.css';

export interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label: ReactNode;
  description?: ReactNode;
  indeterminate?: boolean;
  invalid?: boolean;
}

export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(function Checkbox(
  {
    label,
    description,
    indeterminate = false,
    invalid,
    className,
    disabled,
    id,
    'aria-describedby': describedBy,
    ...rest
  },
  ref,
) {
  const inputRef = useRef<HTMLInputElement>(null);
  useImperativeHandle(ref, () => inputRef.current as HTMLInputElement);
  useEffect(() => {
    if (inputRef.current) inputRef.current.indeterminate = indeterminate;
  }, [indeterminate]);
  const generated = useId();
  const descriptionId = description ? `${id ?? generated}-description` : undefined;

  return (
    <label className={clsx('ui-check', disabled && 'ui-check--disabled', className)}>
      <input
        ref={inputRef}
        id={id}
        type="checkbox"
        className="ui-check__input"
        disabled={disabled}
        aria-invalid={invalid || undefined}
        aria-describedby={[descriptionId, describedBy].filter(Boolean).join(' ') || undefined}
        {...rest}
      />
      <span className="ui-check__text">
        <span className="ui-check__label">{label}</span>
        {description && (
          <span id={descriptionId} className="ui-check__description">
            {description}
          </span>
        )}
      </span>
    </label>
  );
});
