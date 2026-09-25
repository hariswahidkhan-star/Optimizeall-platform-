import clsx from 'clsx';
import { useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useScrollEdges } from './useScrollEdges';
import './display.css';

export interface TabItem {
  id: string;
  label: ReactNode;
  content: ReactNode;
  /** Small count/badge after the label. */
  badge?: ReactNode;
  disabled?: boolean;
}

export interface TabsProps {
  tabs: TabItem[];
  /** Controlled selected tab id. */
  value?: string;
  defaultValue?: string;
  onValueChange?: (id: string) => void;
  /** Accessible name for the tab list. */
  label: string;
  className?: string;
}

/**
 * WAI-ARIA tabs with roving tabindex: one tab stop; Left/Right (and Home/End) move and activate tabs.
 */
export function Tabs({ tabs, value, defaultValue, onValueChange, label, className }: TabsProps) {
  const baseId = useId();
  const [internal, setInternal] = useState(defaultValue ?? tabs.find((t) => !t.disabled)?.id ?? '');
  const selected = value ?? internal;
  const tabRefs = useRef<Map<string, HTMLButtonElement>>(new Map());
  const listRef = useScrollEdges<HTMLDivElement>('[aria-selected="true"]', selected);

  const select = (id: string) => {
    if (value === undefined) setInternal(id);
    onValueChange?.(id);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    const enabled = tabs.filter((t) => !t.disabled);
    const index = enabled.findIndex((t) => t.id === selected);
    let next: TabItem | undefined;
    if (event.key === 'ArrowRight') next = enabled[(index + 1) % enabled.length];
    else if (event.key === 'ArrowLeft') next = enabled[(index - 1 + enabled.length) % enabled.length];
    else if (event.key === 'Home') next = enabled[0];
    else if (event.key === 'End') next = enabled[enabled.length - 1];
    if (!next) return;
    event.preventDefault();
    select(next.id);
    tabRefs.current.get(next.id)?.focus();
  };

  return (
    <div className={clsx('ui-tabs', className)}>
      <div ref={listRef} role="tablist" aria-label={label} className="ui-tabs__list ui-scroll-fade">
        {tabs.map((tab) => {
          const isSelected = tab.id === selected;
          return (
            <button
              key={tab.id}
              ref={(node) => {
                if (node) tabRefs.current.set(tab.id, node);
                else tabRefs.current.delete(tab.id);
              }}
              type="button"
              role="tab"
              id={`${baseId}-tab-${tab.id}`}
              aria-selected={isSelected}
              aria-controls={`${baseId}-panel-${tab.id}`}
              tabIndex={isSelected ? 0 : -1}
              disabled={tab.disabled}
              className="ui-tabs__tab"
              onClick={() => select(tab.id)}
              onKeyDown={onKeyDown}
            >
              {tab.label}
              {tab.badge !== undefined && <span className="ui-tabs__badge">{tab.badge}</span>}
            </button>
          );
        })}
      </div>
      {tabs.map((tab) => (
        <div
          key={tab.id}
          role="tabpanel"
          id={`${baseId}-panel-${tab.id}`}
          aria-labelledby={`${baseId}-tab-${tab.id}`}
          hidden={tab.id !== selected}
          tabIndex={0}
          className="ui-tabs__panel"
        >
          {tab.id === selected && tab.content}
        </div>
      ))}
    </div>
  );
}
