import clsx from 'clsx';
import { forwardRef, type InputHTMLAttributes, type ReactNode } from 'react';
import { useFieldControlProps } from './fieldContext';
import './forms.css';

export interface InputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'size'> {
  invalid?: boolean;
  /** Decorative icon inside the start of the field. */
  leading?: ReactNode;
  /** Interactive adornment at the end (e.g. show-password IconButton). */
  trailing?: ReactNode;
  size?: 'sm' | 'md';
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { className, leading, trailing, size = 'md', type = 'text', ...props },
  ref,
) {
  const controlProps = useFieldControlProps(props);
  const input = (
    <input
      ref={ref}
      type={type}
      className={clsx(
        'ui-input',
        size === 'sm' && 'ui-input--sm',
        leading && 'ui-input--has-leading',
        trailing && 'ui-input--has-trailing',
        className,
      )}
      {...controlProps}
    />
  );
  if (!leading && !trailing) return input;
  return (
    <div className="ui-input-wrap">
      {leading && (
        <span className="ui-input-wrap__leading" aria-hidden="true">
          {leading}
        </span>
      )}
      {input}
      {trailing && <span className="ui-input-wrap__trailing">{trailing}</span>}
    </div>
  );
});
