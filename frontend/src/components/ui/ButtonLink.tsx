import { forwardRef, type ReactNode } from 'react';
import { Link, type LinkProps } from 'react-router-dom';
import { buttonClasses, type ButtonSize, type ButtonVariant } from './buttonStyles';
import './Button.css';

export interface ButtonLinkProps extends LinkProps {
  variant?: ButtonVariant;
  size?: ButtonSize;
  fullWidth?: boolean;
  leadingIcon?: ReactNode;
  trailingIcon?: ReactNode;
}

/** A router link that looks like a Button (navigation must be a link, not a button). */
export const ButtonLink = forwardRef<HTMLAnchorElement, ButtonLinkProps>(function ButtonLink(
  { variant = 'primary', size = 'md', fullWidth, leadingIcon, trailingIcon, className, children, ...rest },
  ref,
) {
  return (
    <Link ref={ref} className={buttonClasses(variant, size, { fullWidth, className })} {...rest}>
      {leadingIcon}
      {children}
      {trailingIcon}
    </Link>
  );
});
