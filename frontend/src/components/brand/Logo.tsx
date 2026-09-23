import clsx from 'clsx';
import { useId, type CSSProperties } from 'react';
import './Logo.css';

export interface LogoProps {
  /** `mark` (rings only), `horizontal` (mark + wordmark), `stacked` (mark over wordmark + optional tagline). */
  variant?: 'mark' | 'horizontal' | 'stacked';
  /** Height of the mark in px; the wordmark scales with it. */
  size?: number;
  /** Show the tagline (stacked variant). */
  tagline?: boolean;
  /**
   * `auto` follows the current theme (navy ring lightens in dark mode); `onDark` forces the light-on-dark colors,
   * e.g. on the navy auth brand panel.
   */
  tone?: 'auto' | 'onDark';
  /** Accessible name. Pass '' when the logo sits next to visible "Optimize All" text. */
  title?: string;
  className?: string;
}

export const BRAND_NAME = 'Optimize All';
export const BRAND_TAGLINE = 'Discover the world of solution';

/**
 * The two interlocking rings: navy (upper-left, smaller) and amber (lower-right, larger). The amber ring passes over
 * the navy ring at the lower-left crossing and under it at the upper-right one.
 */
export function LogoMark({
  size = 32,
  title,
  className,
}: {
  size?: number;
  title?: string;
  className?: string;
}) {
  const titleId = useId();
  return (
    <svg
      className={clsx('oa-logo__mark', className)}
      width={size}
      height={size}
      viewBox="8 4 88 88"
      role={title ? 'img' : undefined}
      aria-labelledby={title ? titleId : undefined}
      aria-hidden={title ? undefined : true}
      focusable="false"
    >
      {title && <title id={titleId}>{title}</title>}
      <circle cx="56" cy="52" r="30" fill="none" strokeWidth="12" className="oa-logo__ring-accent" />
      <circle cx="38" cy="34" r="22" fill="none" strokeWidth="11" className="oa-logo__ring-primary" />
      <path
        d="M27.81 41.74 A30 30 0 0 0 27.81 62.26"
        fill="none"
        strokeWidth="12"
        className="oa-logo__ring-accent"
      />
    </svg>
  );
}

export function Logo({
  variant = 'horizontal',
  size = 32,
  tagline = false,
  tone = 'auto',
  title = BRAND_NAME,
  className,
}: LogoProps) {
  const toneClass = tone === 'onDark' && 'oa-logo--on-dark';
  if (variant === 'mark') {
    return (
      <span className={clsx('oa-logo', toneClass, className)}>
        <LogoMark size={size} title={title || undefined} />
      </span>
    );
  }
  return (
    <span
      className={clsx('oa-logo', `oa-logo--${variant}`, toneClass, className)}
      role={title ? 'img' : undefined}
      aria-label={title || undefined}
      aria-hidden={title ? undefined : true}
      style={{ '--logo-size': `${size}px` } as CSSProperties}
    >
      <LogoMark size={size} />
      <span className="oa-logo__text" aria-hidden="true">
        <span className="oa-logo__wordmark">OPTIMIZE ALL</span>
        {variant === 'stacked' && tagline && <span className="oa-logo__tagline">{BRAND_TAGLINE}</span>}
      </span>
    </span>
  );
}
