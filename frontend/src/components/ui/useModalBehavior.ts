import { useEffect, useRef, type RefObject } from 'react';

const FOCUSABLE = [
  'a[href]',
  'area[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  'iframe',
  'audio[controls]',
  'video[controls]',
  '[contenteditable]:not([contenteditable="false"])',
  '[tabindex]:not([tabindex="-1"])',
].join(',');

export function getFocusable(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
    (el) => !el.hasAttribute('inert') && el.getAttribute('aria-hidden') !== 'true' && !el.closest('[inert]'),
  );
}

/** Open modal layers, top-most last — only the top layer handles Escape/Tab. */
const stack: symbol[] = [];
let scrollLocks = 0;
let previousOverflow = '';

function lockScroll() {
  if (scrollLocks === 0) {
    previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
  }
  scrollLocks += 1;
}

function unlockScroll() {
  scrollLocks = Math.max(0, scrollLocks - 1);
  if (scrollLocks === 0) document.body.style.overflow = previousOverflow;
}

export interface ModalBehaviorOptions {
  onClose: () => void;
  /** Element to focus on open (defaults to the first focusable element, then the container). */
  initialFocusRef?: RefObject<HTMLElement | null>;
  closeOnEscape?: boolean;
}

/**
 * Focus management for dialogs and drawers: moves focus in on open, traps Tab/Shift+Tab, closes on Escape,
 * locks page scroll and restores focus to the previously focused element on close.
 */
export function useModalBehavior(
  open: boolean,
  containerRef: RefObject<HTMLElement | null>,
  { onClose, initialFocusRef, closeOnEscape = true }: ModalBehaviorOptions,
): void {
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  useEffect(() => {
    if (!open) return;
    const id = Symbol('modal');
    stack.push(id);
    const previouslyFocused = document.activeElement as HTMLElement | null;
    lockScroll();

    const container = containerRef.current;
    const focusInitial = () => {
      if (!container) return;
      const target = initialFocusRef?.current ?? getFocusable(container)[0] ?? container;
      target.focus({ preventScroll: true });
    };
    focusInitial();

    const onKeyDown = (event: KeyboardEvent) => {
      if (stack[stack.length - 1] !== id || !container) return;
      if (event.key === 'Escape' && closeOnEscape) {
        event.stopPropagation();
        event.preventDefault();
        onCloseRef.current();
        return;
      }
      if (event.key !== 'Tab') return;
      const focusable = getFocusable(container);
      if (focusable.length === 0) {
        event.preventDefault();
        container.focus();
        return;
      }
      const first = focusable[0]!;
      const last = focusable[focusable.length - 1]!;
      const active = document.activeElement as HTMLElement | null;
      if (event.shiftKey && (active === first || !container.contains(active))) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && (active === last || !container.contains(active))) {
        event.preventDefault();
        first.focus();
      }
    };

    // Keep focus inside if something outside (e.g. a click on the backdrop) grabs it.
    const onFocusIn = (event: FocusEvent) => {
      if (stack[stack.length - 1] !== id || !container) return;
      if (event.target instanceof Node && !container.contains(event.target)) focusInitial();
    };

    document.addEventListener('keydown', onKeyDown);
    document.addEventListener('focusin', onFocusIn);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.removeEventListener('focusin', onFocusIn);
      const index = stack.indexOf(id);
      if (index >= 0) stack.splice(index, 1);
      unlockScroll();
      if (previouslyFocused && document.contains(previouslyFocused))
        previouslyFocused.focus({ preventScroll: true });
    };
  }, [open, containerRef, initialFocusRef, closeOnEscape]);
}
