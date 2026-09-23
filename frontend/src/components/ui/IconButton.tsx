import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from 'react';
import { buttonClasses, type ButtonSize, type ButtonVariant } from './buttonStyles';
import './Button.css';

export interface IconButtonProps extends Omit<
  ButtonHTMLAttributes<HTMLButtonElement>,
  'aria-label' | 'children'
> {
  /** Required accessible name — icon-only buttons have no visible text. */
  label: string;
  icon: ReactNode;
  variant?: Exclude<ButtonVariant, 'link'>;
  size?: ButtonSize;
}

export const IconButton = forwardRef<HTMLButtonElement, IconButtonProps>(function IconButton(
  { label, icon, variant = 'ghost', size = 'md', className, type = 'button', ...rest },
  ref,
) {
  return (
    <button
      ref={ref}
      type={type}
      aria-label={label}
      title={label}
      className={buttonClasses(variant, size, { iconOnly: true, className })}
      {...rest}
    >
      <span aria-hidden="true" style={{ display: 'contents' }}>
        {icon}
      </span>
    </button>
  );
});
