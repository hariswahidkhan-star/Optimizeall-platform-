import { LogoMark } from './brand/Logo';
import { Skeleton } from './ui/Skeleton';
import './FullPageLoader.css';

/** Shown while the session is being restored: a calm skeleton of the app shell. */
export function FullPageLoader({ label = 'Loading Optimize All' }: { label?: string }) {
  return (
    <div className="full-loader" role="status" aria-live="polite">
      <span className="visually-hidden">{label}</span>
      <div className="full-loader__sidebar" aria-hidden="true">
        <LogoMark size={32} />
        {Array.from({ length: 6 }, (_, i) => (
          <Skeleton key={i} height={14} width={`${70 - i * 5}%`} />
        ))}
      </div>
      <div className="full-loader__main" aria-hidden="true">
        <Skeleton height={32} width="40%" />
        <Skeleton height={16} width="60%" />
        <div className="full-loader__grid">
          <Skeleton height={120} radius={20} />
          <Skeleton height={120} radius={20} />
          <Skeleton height={120} radius={20} />
        </div>
        <Skeleton height={240} radius={20} />
      </div>
    </div>
  );
}
