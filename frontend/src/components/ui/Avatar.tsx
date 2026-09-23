import clsx from 'clsx';
import { useState, type CSSProperties } from 'react';
import { initials } from '@/lib/format/text';
import './display.css';

export interface AvatarProps {
  name: string;
  src?: string | null;
  size?: number;
  /** Decorative avatars (next to a visible name) should be hidden from assistive tech. */
  decorative?: boolean;
  className?: string;
}

export function Avatar({ name, src, size = 36, decorative = false, className }: AvatarProps) {
  const [failed, setFailed] = useState(false);
  const showImage = src && !failed;
  return (
    <span
      className={clsx('ui-avatar', className)}
      style={{ '--avatar-size': `${size}px` } as CSSProperties}
      role={decorative ? undefined : 'img'}
      aria-label={decorative ? undefined : name}
      aria-hidden={decorative || undefined}
    >
      {showImage ? (
        <img src={src} alt="" onError={() => setFailed(true)} />
      ) : (
        <span aria-hidden="true">{initials(name)}</span>
      )}
    </span>
  );
}
