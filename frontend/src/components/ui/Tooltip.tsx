import clsx from 'clsx';
import {
  cloneElement,
  isValidElement,
  useEffect,
  useId,
  useRef,
  useState,
  type FocusEvent,
  type MouseEvent,
  type ReactElement,
  type ReactNode,
} from 'react';
import './overlay.css';

type AnchorProps = {
  'aria-describedby'?: string;
  onFocus?: (event: FocusEvent<HTMLElement>) => void;
  onBlur?: (event: FocusEvent<HTMLElement>) => void;
  onMouseEnter?: (event: MouseEvent<HTMLElement>) => void;
  onMouseLeave?: (event: MouseEvent<HTMLElement>) => void;
};

export interface TooltipProps {
  content: ReactNode;
  /** A single focusable element (button, link, input). */
  children: ReactElement<AnchorProps>;
  placement?: 'top' | 'bottom';
  /** Hover delay in ms. */
  delay?: number;
  className?: string;
}

/**
 * Supplementary description shown on hover and keyboard focus, wired via aria-describedby. Escape dismisses it.
 * Never put essential information or interactive content in a tooltip.
 */
export function Tooltip({ content, children, placement = 'top', delay = 300, className }: TooltipProps) {
  const [open, setOpen] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const id = useId();

  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open]);

  useEffect(() => () => clearTimeout(timer.current), []);

  if (!isValidElement(children)) return children;
  const props = children.props;

  const show = (immediate: boolean) => {
    clearTimeout(timer.current);
    if (immediate) setOpen(true);
    else timer.current = setTimeout(() => setOpen(true), delay);
  };
  const hide = () => {
    clearTimeout(timer.current);
    setOpen(false);
  };

  return (
    <span className={clsx('ui-tooltip-anchor', className)}>
      {cloneElement(children, {
        'aria-describedby':
          [props['aria-describedby'], open ? id : undefined].filter(Boolean).join(' ') || undefined,
        onFocus: (event: FocusEvent<HTMLElement>) => {
          props.onFocus?.(event);
          show(true);
        },
        onBlur: (event: FocusEvent<HTMLElement>) => {
          props.onBlur?.(event);
          hide();
        },
        onMouseEnter: (event: MouseEvent<HTMLElement>) => {
          props.onMouseEnter?.(event);
          show(false);
        },
        onMouseLeave: (event: MouseEvent<HTMLElement>) => {
          props.onMouseLeave?.(event);
          hide();
        },
      })}
      {open && (
        <span id={id} role="tooltip" className={clsx('ui-tooltip', `ui-tooltip--${placement}`)}>
          {content}
        </span>
      )}
    </span>
  );
}
