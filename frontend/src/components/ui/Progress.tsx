import clsx from 'clsx';
import { useId, type ReactNode } from 'react';
import './display.css';

export interface ProgressBarProps {
  value: number;
  max?: number;
  label: ReactNode;
  /** Visible value text (defaults to a percentage). Also used as aria-valuetext. */
  valueText?: string;
  showValue?: boolean;
  hideLabel?: boolean;
  tone?: 'primary' | 'accent' | 'success';
  className?: string;
}

function clampRatio(value: number, max: number): number {
  if (max <= 0) return 0;
  return Math.min(1, Math.max(0, value / max));
}

export function ProgressBar({
  value,
  max = 100,
  label,
  valueText,
  showValue = true,
  hideLabel,
  tone = 'primary',
  className,
}: ProgressBarProps) {
  const labelId = useId();
  const ratio = clampRatio(value, max);
  const text = valueText ?? `${Math.round(ratio * 100)}%`;
  return (
    <div className={clsx('ui-progress', tone !== 'primary' && `ui-progress--${tone}`, className)}>
      <div className={clsx('ui-progress__meta', hideLabel && 'visually-hidden')}>
        <span id={labelId} className="ui-progress__label">
          {label}
        </span>
        {showValue && <span className="ui-progress__value">{text}</span>}
      </div>
      <div
        role="progressbar"
        aria-labelledby={labelId}
        aria-valuemin={0}
        aria-valuemax={max}
        aria-valuenow={value}
        aria-valuetext={text}
        className="ui-progress__track"
      >
        <div className="ui-progress__fill" style={{ width: `${ratio * 100}%` }} />
      </div>
    </div>
  );
}

export interface ProgressRingProps {
  value: number;
  max?: number;
  /** Accessible name. */
  label: string;
  size?: number;
  strokeWidth?: number;
  /** Center text (defaults to percentage). */
  centerText?: ReactNode;
  className?: string;
}

export function ProgressRing({
  value,
  max = 100,
  label,
  size = 72,
  strokeWidth = 7,
  centerText,
  className,
}: ProgressRingProps) {
  const ratio = clampRatio(value, max);
  const radius = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const percent = `${Math.round(ratio * 100)}%`;
  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={max}
      aria-valuenow={value}
      aria-valuetext={percent}
      className={clsx('ui-ring', className)}
      style={{ width: size, height: size }}
    >
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} aria-hidden="true">
        <circle
          className="ui-ring__track"
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          strokeWidth={strokeWidth}
        />
        <circle
          className="ui-ring__fill"
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          strokeWidth={strokeWidth}
          strokeLinecap="round"
          strokeDasharray={circumference}
          strokeDashoffset={circumference * (1 - ratio)}
        />
      </svg>
      <span className="ui-ring__label" aria-hidden="true">
        {centerText ?? percent}
      </span>
    </div>
  );
}
