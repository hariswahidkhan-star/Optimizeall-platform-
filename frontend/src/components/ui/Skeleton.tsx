import clsx from 'clsx';
import type { CSSProperties } from 'react';
import './feedback.css';

export interface SkeletonProps {
  width?: CSSProperties['width'];
  height?: CSSProperties['height'];
  radius?: CSSProperties['borderRadius'];
  variant?: 'block' | 'text' | 'circle';
  className?: string;
}

/** Decorative loading placeholder. Wrap loading regions in an element with aria-busy for assistive tech. */
export function Skeleton({ width, height, radius, variant = 'block', className }: SkeletonProps) {
  return (
    <span
      aria-hidden="true"
      className={clsx('ui-skeleton', variant !== 'block' && `ui-skeleton--${variant}`, className)}
      style={{ width, height: height ?? (variant === 'block' ? 16 : undefined), borderRadius: radius }}
    />
  );
}

/** A paragraph-shaped skeleton; the last line is shorter. */
export function SkeletonText({ lines = 3, className }: { lines?: number; className?: string }) {
  return (
    <span className={clsx('ui-skeleton-lines', className)} aria-hidden="true">
      {Array.from({ length: lines }, (_, i) => (
        <Skeleton key={i} variant="text" width={i === lines - 1 && lines > 1 ? '60%' : '100%'} />
      ))}
    </span>
  );
}
