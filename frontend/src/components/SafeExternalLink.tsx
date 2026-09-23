import type { AnchorHTMLAttributes, ReactNode } from 'react';
import { isSafeHref } from '@/lib/safeHref';

export interface SafeExternalLinkProps extends Omit<
  AnchorHTMLAttributes<HTMLAnchorElement>,
  'href' | 'target' | 'rel' | 'children'
> {
  /** Backend-provided URL; rendered only when {@link isSafeHref} accepts it. */
  href: string | null | undefined;
  children: ReactNode;
  /** Rendered instead of the link when the href is unsafe or missing (default: the children as plain text). */
  fallback?: ReactNode;
  /** Adds `nofollow` (user-generated links such as participants' posts and profiles). */
  nofollow?: boolean;
}

/**
 * Opens a backend-provided URL in a new tab without opener/referrer. Unsafe values (other schemes,
 * protocol-relative URLs, backslashes) never become links: the children (or `fallback`) render as plain text.
 */
export function SafeExternalLink({
  href,
  children,
  fallback,
  nofollow,
  className,
  ...rest
}: SafeExternalLinkProps) {
  if (!isSafeHref(href)) return <>{fallback ?? <span className={className}>{children}</span>}</>;
  return (
    <a
      {...rest}
      href={href}
      className={className}
      target="_blank"
      rel={nofollow ? 'noopener noreferrer nofollow' : 'noopener noreferrer'}
    >
      {children}
    </a>
  );
}
