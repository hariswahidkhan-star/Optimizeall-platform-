import clsx from 'clsx';
import {
  cloneElement,
  isValidElement,
  useCallback,
  useEffect,
  useId,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactElement,
  type Ref,
  type ReactNode,
} from 'react';
import { Link } from 'react-router-dom';
import './overlay.css';

export type MenuEntry =
  | {
      type?: 'item';
      id: string;
      label: ReactNode;
      description?: ReactNode;
      icon?: ReactNode;
      /** Navigate with the router instead of running onSelect. */
      to?: string;
      onSelect?: () => void;
      danger?: boolean;
      disabled?: boolean;
      /** Marks the current choice (e.g. active portal). */
      current?: boolean;
    }
  | { type: 'separator'; id: string }
  | { type: 'label'; id: string; label: ReactNode };

type TriggerProps = {
  id?: string;
  'aria-haspopup'?: 'menu';
  'aria-expanded'?: boolean;
  'aria-controls'?: string;
  onClick?: () => void;
  onKeyDown?: (event: KeyboardEvent<HTMLElement>) => void;
  ref?: Ref<HTMLButtonElement>;
};

export interface DropdownMenuProps {
  /** A button element; it receives the menu-button ARIA attributes and handlers. */
  trigger: ReactElement<TriggerProps>;
  items: MenuEntry[];
  /** Accessible label for the menu (defaults to the trigger's name via aria-labelledby). */
  label?: string;
  align?: 'start' | 'end';
  /** Open upwards (e.g. from a sidebar footer). */
  placement?: 'down' | 'up';
  className?: string;
}

type ActionableEntry = Exclude<MenuEntry, { type: 'separator' } | { type: 'label' }>;

/**
 * WAI-ARIA menu button: Enter/Space/ArrowDown open and focus the first item, ArrowUp the last; arrows, Home/End and
 * type-ahead move between items; Escape closes and returns focus to the trigger; Tab closes.
 */
export function DropdownMenu({
  trigger,
  items,
  label,
  align = 'end',
  placement = 'down',
  className,
}: DropdownMenuProps) {
  const [open, setOpen] = useState(false);
  const [focusTarget, setFocusTarget] = useState<'first' | 'last'>('first');
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLUListElement>(null);
  const menuId = useId();
  const triggerId = useId();

  const enabledItems = useCallback(
    () =>
      Array.from(
        menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]:not([aria-disabled="true"])') ?? [],
      ),
    [],
  );

  const close = useCallback((restoreFocus: boolean) => {
    setOpen(false);
    if (restoreFocus) triggerRef.current?.focus();
  }, []);

  useEffect(() => {
    if (!open) return;
    const nodes = enabledItems();
    (focusTarget === 'first' ? nodes[0] : nodes[nodes.length - 1])?.focus();
    const onPointerDown = (event: PointerEvent | MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onPointerDown);
    return () => document.removeEventListener('mousedown', onPointerDown);
  }, [open, focusTarget, enabledItems]);

  const openMenu = (target: 'first' | 'last') => {
    setFocusTarget(target);
    setOpen(true);
  };

  const onTriggerKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      openMenu('first');
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      openMenu('last');
    }
  };

  const onMenuKeyDown = (event: KeyboardEvent<HTMLUListElement>) => {
    const nodes = enabledItems();
    const index = nodes.indexOf(document.activeElement as HTMLElement);
    const focusAt = (i: number) => nodes[(i + nodes.length) % nodes.length]?.focus();
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        focusAt(index + 1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        focusAt(index - 1);
        break;
      case 'Home':
        event.preventDefault();
        focusAt(0);
        break;
      case 'End':
        event.preventDefault();
        focusAt(nodes.length - 1);
        break;
      case 'Escape':
        event.preventDefault();
        event.stopPropagation();
        close(true);
        break;
      case 'Tab':
        close(false);
        break;
      default:
        if (event.key.length === 1 && /\S/.test(event.key)) {
          const char = event.key.toLowerCase();
          const ordered = [...nodes.slice(index + 1), ...nodes.slice(0, index + 1)];
          ordered.find((node) => node.textContent?.trim().toLowerCase().startsWith(char))?.focus();
        }
    }
  };

  const select = (entry: ActionableEntry) => {
    if (entry.disabled) return;
    close(true);
    entry.onSelect?.();
  };

  const triggerElement = isValidElement(trigger)
    ? cloneElement(trigger, {
        id: trigger.props.id ?? triggerId,
        ref: triggerRef,
        'aria-haspopup': 'menu',
        'aria-expanded': open,
        'aria-controls': open ? menuId : undefined,
        onClick: () => (open ? close(false) : openMenu('first')),
        onKeyDown: onTriggerKeyDown,
      })
    : trigger;

  return (
    <div ref={rootRef} className={clsx('ui-menu', className)}>
      {triggerElement}
      {open && (
        <ul
          ref={menuRef}
          id={menuId}
          role="menu"
          aria-label={label}
          aria-labelledby={label ? undefined : (trigger.props.id ?? triggerId)}
          className={clsx(
            'ui-menu__list',
            `ui-menu__list--${align}`,
            placement === 'up' && 'ui-menu__list--up',
          )}
          onKeyDown={onMenuKeyDown}
        >
          {items.map((entry) => {
            if (entry.type === 'separator')
              return <li key={entry.id} role="separator" className="ui-menu__separator" />;
            if (entry.type === 'label')
              return (
                <li key={entry.id} role="presentation" className="ui-menu__label">
                  {entry.label}
                </li>
              );
            const content = (
              <>
                {entry.icon && <span aria-hidden="true">{entry.icon}</span>}
                <span className="ui-menu__item-text">
                  <span>{entry.label}</span>
                  {entry.description && (
                    <span className="ui-menu__item-description">{entry.description}</span>
                  )}
                </span>
              </>
            );
            const itemClass = clsx('ui-menu__item', entry.danger && 'ui-menu__item--danger');
            return (
              <li key={entry.id} role="none">
                {entry.to && !entry.disabled ? (
                  <Link
                    to={entry.to}
                    role="menuitem"
                    tabIndex={-1}
                    className={itemClass}
                    aria-current={entry.current ? 'true' : undefined}
                    onClick={() => select(entry)}
                  >
                    {content}
                  </Link>
                ) : (
                  <button
                    type="button"
                    role="menuitem"
                    tabIndex={-1}
                    className={itemClass}
                    aria-disabled={entry.disabled || undefined}
                    aria-current={entry.current ? 'true' : undefined}
                    onClick={() => select(entry)}
                  >
                    {content}
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
