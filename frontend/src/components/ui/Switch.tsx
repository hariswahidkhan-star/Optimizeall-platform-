import clsx from 'clsx';
import { forwardRef, useId, type ReactNode } from 'react';
import './forms.css';

export interface SwitchProps {
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  label: ReactNode;
  description?: ReactNode;
  disabled?: boolean;
  /** Hide the visible label (still used as the accessible name). */
  hideLabel?: boolean;
  className?: string;
  id?: string;
}

/** On/off toggle using the WAI-ARIA switch pattern (Space/Enter toggle via native button behaviour). */
export const Switch = forwardRef<HTMLButtonElement, SwitchProps>(function Switch(
  { checked, onCheckedChange, label, description, disabled, hideLabel, className, id },
  ref,
) {
  const generated = useId();
  const labelId = `${id ?? generated}-label`;
  const descriptionId = description ? `${id ?? generated}-description` : undefined;
  return (
    <button
      ref={ref}
      id={id}
      type="button"
      role="switch"
      aria-checked={checked}
      aria-labelledby={labelId}
      aria-describedby={descriptionId}
      disabled={disabled}
      className={clsx('ui-switch', className)}
      onClick={() => onCheckedChange(!checked)}
    >
      <span className="ui-switch__track" aria-hidden="true">
        <span className="ui-switch__thumb" />
      </span>
      <span className={clsx('ui-switch__text', hideLabel && 'visually-hidden')}>
        <span id={labelId}>{label}</span>
        {description && (
          <span id={descriptionId} className="ui-switch__description">
            {description}
          </span>
        )}
      </span>
    </button>
  );
});
