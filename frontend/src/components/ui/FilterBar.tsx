import clsx from 'clsx';
import { RotateCcw, Search, X } from 'lucide-react';
import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { Button } from './Button';
import { IconButton } from './IconButton';
import { Input } from './Input';
import { Select, type SelectOption } from './Select';
import './FilterBar.css';

export interface FilterDefinition {
  id: string;
  label: string;
  options: SelectOption[];
  /** Label of the "no filter" option (default "All"). */
  allLabel?: string;
}

export interface FilterBarProps {
  /** Current search text (controlled, already debounced by the bar). */
  search?: string;
  onSearchChange?: (search: string) => void;
  searchPlaceholder?: string;
  searchLabel?: string;
  /** Debounce for onSearchChange in ms. */
  debounceMs?: number;
  filters?: FilterDefinition[];
  values?: Record<string, string | undefined>;
  onFilterChange?: (id: string, value: string | undefined) => void;
  /** Clears search and filters. Shown when anything is active. */
  onReset?: () => void;
  /** Extra controls on the right (e.g. export button). */
  actions?: ReactNode;
  className?: string;
}

/** Search box (debounced) + select filters + removable active-filter chips + reset. */
export function FilterBar({
  search = '',
  onSearchChange,
  searchPlaceholder = 'Search…',
  searchLabel = 'Search',
  debounceMs = 300,
  filters = [],
  values = {},
  onFilterChange,
  onReset,
  actions,
  className,
}: FilterBarProps) {
  const [text, setText] = useState(search);
  const baseId = useId();
  const onSearchRef = useRef(onSearchChange);
  const lastEmitted = useRef(search);

  useEffect(() => {
    onSearchRef.current = onSearchChange;
  }, [onSearchChange]);

  // Sync external resets (e.g. "Reset" or navigation) into the input.
  useEffect(() => {
    if (search !== lastEmitted.current) {
      lastEmitted.current = search;
      setText(search);
    }
  }, [search]);

  useEffect(() => {
    if (text === lastEmitted.current) return;
    const timer = setTimeout(() => {
      lastEmitted.current = text;
      onSearchRef.current?.(text);
    }, debounceMs);
    return () => clearTimeout(timer);
  }, [text, debounceMs]);

  const active = filters
    .map((filter) => ({ filter, value: values[filter.id] }))
    .filter((entry): entry is { filter: FilterDefinition; value: string } => !!entry.value);
  const hasAnything = active.length > 0 || search.trim() !== '' || text.trim() !== '';

  return (
    <div className={clsx('ui-filterbar', className)} role="search">
      <div className="ui-filterbar__row">
        {onSearchChange && (
          <div className="ui-filterbar__search">
            <label htmlFor={`${baseId}-search`} className="visually-hidden">
              {searchLabel}
            </label>
            <Input
              id={`${baseId}-search`}
              type="search"
              value={text}
              placeholder={searchPlaceholder}
              leading={<Search />}
              onChange={(e) => setText(e.target.value)}
              autoComplete="off"
            />
          </div>
        )}
        {filters.map((filter) => (
          <div key={filter.id} className="ui-filterbar__filter">
            <label htmlFor={`${baseId}-${filter.id}`} className="visually-hidden">
              {filter.label}
            </label>
            <Select
              id={`${baseId}-${filter.id}`}
              value={values[filter.id] ?? ''}
              onChange={(e) => onFilterChange?.(filter.id, e.target.value || undefined)}
              options={[
                { value: '', label: `${filter.label}: ${filter.allLabel ?? 'All'}` },
                ...filter.options,
              ]}
            />
          </div>
        ))}
        {actions && <div className="ui-filterbar__actions">{actions}</div>}
      </div>
      {(active.length > 0 || (onReset && hasAnything)) && (
        <div className="ui-filterbar__chips">
          {active.length > 0 && (
            <ul aria-label="Active filters">
              {active.map(({ filter, value }) => {
                const optionLabel = filter.options.find((o) => o.value === value)?.label ?? value;
                return (
                  <li key={filter.id} className="ui-filterbar__chip">
                    <span>
                      {filter.label}: <strong>{optionLabel}</strong>
                    </span>
                    <IconButton
                      size="sm"
                      label={`Remove filter ${filter.label}: ${optionLabel}`}
                      icon={<X />}
                      onClick={() => onFilterChange?.(filter.id, undefined)}
                    />
                  </li>
                );
              })}
            </ul>
          )}
          {onReset && hasAnything && (
            <Button
              variant="ghost"
              size="sm"
              leadingIcon={<RotateCcw />}
              onClick={() => {
                setText('');
                lastEmitted.current = '';
                onReset();
              }}
            >
              Reset filters
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
