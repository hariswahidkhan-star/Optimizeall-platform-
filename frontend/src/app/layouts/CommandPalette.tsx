import { useQuery } from '@tanstack/react-query';
import clsx from 'clsx';
import { CornerDownLeft, FileText, Search } from 'lucide-react';
import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Dialog } from '@/components/ui/Dialog';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { meetsRequirement, Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { accessiblePortals, canOpenPath } from '../portals';
import './CommandPalette.css';

export const SEARCH_MIN_LENGTH = 2;
/**
 * Any of these opens record search (`GET /search`, mirrors `SearchService.SearchPermissions`); staff without one
 * (e.g. a content editor) get the page shortcuts only, instead of a 403 on every keystroke.
 */
export const SEARCH_PERMISSIONS = [
  Permissions.ClientsView,
  Permissions.CrmView,
  Permissions.ProjectsView,
  Permissions.BillingView,
  Permissions.CampaignsView,
  Permissions.UsersView,
] as const;
const MAX_PAGES = 8;

interface SearchHit {
  id: string;
  title: string;
  subtitle: string | null;
  url: string;
}

interface SearchResult {
  query: string;
  groups: { type: string; label: string; items: SearchHit[] }[];
}

interface Option {
  id: string;
  label: string;
  detail?: string | null;
  to: string;
}

interface OptionGroup {
  id: string;
  label: string;
  options: Option[];
}

/** Portal pages the user may open, as "go to" shortcuts. */
function usePageShortcuts(): Option[] {
  const { permissions } = useAuth();
  return useMemo(
    () =>
      accessiblePortals(permissions).flatMap((portal) =>
        portal.nav
          .filter((item) => !item.requires || meetsRequirement(permissions, item.requires))
          .map((item) => ({
            id: `page:${portal.id}:${item.to}`,
            label: item.label,
            detail: portal.label,
            to: item.to ? `${portal.basePath}/${item.to}` : portal.basePath,
          })),
      ),
    [permissions],
  );
}

/**
 * Ctrl/Cmd+K command palette for staff: jump to any portal page the user may open, or search clients, contacts,
 * deals, projects, invoices, campaigns and users (`GET /search`, permission- and tenancy-filtered by the server).
 * WAI-ARIA combobox with a grouped listbox: arrows move, Enter opens, Escape closes; the result count is announced.
 */
export function CommandPalette({ open, onClose }: { open: boolean; onClose: () => void }) {
  const inputRef = useRef<HTMLInputElement>(null);
  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Search"
      size="lg"
      initialFocusRef={inputRef}
      className="cmdk"
    >
      {open && <PaletteBody inputRef={inputRef} onClose={onClose} />}
    </Dialog>
  );
}

function PaletteBody({
  inputRef,
  onClose,
}: {
  inputRef: React.RefObject<HTMLInputElement>;
  onClose: () => void;
}) {
  const navigate = useNavigate();
  const { permissions } = useAuth();
  const pages = usePageShortcuts();
  const [text, setText] = useState('');
  const [active, setActive] = useState(0);
  const listId = useId();
  const statusId = useId();
  const term = text.trim();
  const debounced = useDebouncedValue(term, 250);
  const canSearch = SEARCH_PERMISSIONS.some((p) => permissions.includes(p));
  const searching = canSearch && debounced.length >= SEARCH_MIN_LENGTH;

  const search = useQuery({
    queryKey: ['search', debounced],
    queryFn: ({ signal }) => api.get<SearchResult>('/search', { query: { q: debounced }, signal }),
    enabled: searching,
    staleTime: 30_000,
  });

  const groups: OptionGroup[] = useMemo(() => {
    const needle = term.toLowerCase();
    const matchingPages = (
      needle ? pages.filter((p) => `${p.label} ${p.detail}`.toLowerCase().includes(needle)) : pages
    ).slice(0, MAX_PAGES);
    const list: OptionGroup[] = [];
    if (matchingPages.length > 0) list.push({ id: 'pages', label: 'Go to', options: matchingPages });
    if (searching && search.data && search.data.query === debounced) {
      for (const g of search.data.groups) {
        const options = g.items
          .filter((hit) => canOpenPath(permissions, hit.url))
          .map((hit) => ({ id: `${g.type}:${hit.id}`, label: hit.title, detail: hit.subtitle, to: hit.url }));
        if (options.length > 0) list.push({ id: g.type, label: g.label, options });
      }
    }
    return list;
  }, [term, pages, searching, search.data, debounced, permissions]);

  const flat = groups.flatMap((g) => g.options);
  const activeIndex = flat.length === 0 ? -1 : Math.min(active, flat.length - 1);
  const activeOption = activeIndex >= 0 ? flat[activeIndex] : undefined;
  const optionId = (option: Option) => `${listId}-${option.id.replace(/[^a-zA-Z0-9_-]/g, '_')}`;

  useEffect(() => setActive(0), [term, search.data]);

  // Keep the active option visible.
  useEffect(() => {
    if (!activeOption) return;
    document.getElementById(optionId(activeOption))?.scrollIntoView?.({ block: 'nearest' });
  });

  const go = (option: Option) => {
    onClose();
    navigate(option.to);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (flat.length === 0) return;
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActive((i) => (Math.min(i, flat.length - 1) + 1) % flat.length);
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActive((i) => (Math.min(i, flat.length - 1) - 1 + flat.length) % flat.length);
    } else if (event.key === 'Home' && event.ctrlKey) {
      event.preventDefault();
      setActive(0);
    } else if (event.key === 'End' && event.ctrlKey) {
      event.preventDefault();
      setActive(flat.length - 1);
    } else if (event.key === 'Enter' && activeOption) {
      event.preventDefault();
      go(activeOption);
    }
  };

  const loading = searching && (search.isFetching || debounced !== term);
  const status = search.isError
    ? `Search failed: ${errorMessage(search.error)}`
    : canSearch && term.length > 0 && term.length < SEARCH_MIN_LENGTH
      ? `Type at least ${SEARCH_MIN_LENGTH} characters to search records.`
      : loading
        ? 'Searching…'
        : flat.length === 0
          ? 'No matches.'
          : `${flat.length} ${flat.length === 1 ? 'result' : 'results'}.`;

  return (
    <div className="cmdk__body">
      <div className="cmdk__field">
        <Search aria-hidden="true" className="cmdk__icon" />
        <input
          ref={inputRef}
          className="cmdk__input"
          type="text"
          role="combobox"
          aria-label="Search pages and records"
          aria-expanded={flat.length > 0}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={activeOption ? optionId(activeOption) : undefined}
          aria-describedby={statusId}
          autoComplete="off"
          spellCheck={false}
          placeholder="Search clients, deals, invoices, users… or go to a page"
          value={text}
          maxLength={100}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={onKeyDown}
        />
      </div>
      <p id={statusId} role="status" aria-live="polite" className="cmdk__status">
        {status}
      </p>
      <div id={listId} role="listbox" aria-label="Results" className="cmdk__list">
        {groups.map((group) => (
          <div key={group.id} role="group" aria-labelledby={`${listId}-${group.id}`} className="cmdk__group">
            <div id={`${listId}-${group.id}`} role="presentation" className="cmdk__group-label">
              {group.label}
            </div>
            {group.options.map((option) => {
              const selected = option === activeOption;
              return (
                <div
                  key={option.id}
                  id={optionId(option)}
                  role="option"
                  aria-selected={selected}
                  className={clsx('cmdk__option', selected && 'is-active')}
                  onMouseDown={(e) => e.preventDefault()}
                  onMouseMove={() => setActive(flat.indexOf(option))}
                  // Focus stays in the combobox (aria-activedescendant); options are reached with the arrow keys.
                  tabIndex={-1}
                  onClick={() => go(option)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') go(option);
                  }}
                >
                  <FileText aria-hidden="true" className="cmdk__option-icon" />
                  <span className="cmdk__option-text">
                    <span className="cmdk__option-label">{option.label}</span>
                    {option.detail && <span className="cmdk__option-detail">{option.detail}</span>}
                  </span>
                  {selected && <CornerDownLeft aria-hidden="true" className="cmdk__enter" />}
                </div>
              );
            })}
          </div>
        ))}
      </div>
      <p className="cmdk__hint" aria-hidden="true">
        <kbd>↑</kbd> <kbd>↓</kbd> to move · <kbd>Enter</kbd> to open · <kbd>Esc</kbd> to close
      </p>
    </div>
  );
}

/** Opens the palette on Ctrl+K / Cmd+K. */
export function useCommandPaletteShortcut(enabled: boolean, onOpen: () => void) {
  useEffect(() => {
    if (!enabled) return;
    const handler = (event: globalThis.KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        onOpen();
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [enabled, onOpen]);
}
