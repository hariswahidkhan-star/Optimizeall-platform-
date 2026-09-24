import clsx from 'clsx';
import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from 'react';
import { buttonClasses, type ButtonSize, type ButtonVariant } from './buttonStyles';
import { Spinner } from './Spinner';
import './Button.css';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /**
   * Shows a spinner, sets aria-busy and ignores clicks while keeping the button's size and accessible name. The second
   * click of a double click is always ignored, so an action is never sent twice.
   */
  loading?: boolean;
  leadingIcon?: ReactNode;
  trailingIcon?: ReactNode;
  fullWidth?: boolean;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  {
    variant = 'primary',
    size = 'md',
    loading = false,
    leadingIcon,
    trailingIcon,
    fullWidth,
    className,
    children,
    type = 'button',
    onClick,
    ...rest
  },
  ref,
) {
  return (
    <button
      ref={ref}
      type={type}
      className={buttonClasses(variant, size, {
        fullWidth,
        className: clsx(loading && 'ui-button--loading', className),
      })}
      aria-busy={loading || undefined}
      aria-disabled={loading || undefined}
      onClick={(event) => {
        // `detail` counts rapid clicks: the second click of a double click arrives before the pending state renders
        // (`loading` is still false), so it would send the action or submit the form a second time.
        if (loading || event.detail > 1) {
          event.preventDefault();
          return;
        }
        onClick?.(event);
      }}
      {...rest}
    >
      {loading && (
        <span className="ui-button__spinner" aria-hidden="true">
          <Spinner size="sm" decorative />
        </span>
      )}
      <span className="ui-button__content">
        {leadingIcon}
        {children}
        {trailingIcon}
      </span>
    </button>
  );
});
