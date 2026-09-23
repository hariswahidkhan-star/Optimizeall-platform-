import clsx from 'clsx';

export type ButtonVariant = 'primary' | 'highlight' | 'secondary' | 'ghost' | 'danger' | 'link';
export type ButtonSize = 'sm' | 'md' | 'lg';

/** Class names for anything that should look like a Button (e.g. router links). */
export function buttonClasses(
  variant: ButtonVariant = 'primary',
  size: ButtonSize = 'md',
  options: { fullWidth?: boolean; iconOnly?: boolean; className?: string } = {},
): string {
  return clsx(
    'ui-button',
    `ui-button--${variant}`,
    `ui-button--${size}`,
    options.fullWidth && 'ui-button--full',
    options.iconOnly && 'ui-button--icon',
    options.className,
  );
}
