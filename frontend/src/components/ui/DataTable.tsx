import clsx from 'clsx';
import { ArrowDown, ArrowUp, ChevronsUpDown, MoreHorizontal } from 'lucide-react';
import { useId, useMemo, useState, type CSSProperties, type ReactNode } from 'react';
import { useIsMobile } from '@/lib/hooks/useMediaQuery';
import { Checkbox } from './Checkbox';
import { DropdownMenu, type MenuEntry } from './DropdownMenu';
import { EmptyState } from './EmptyState';
import { ScrollArea } from './ScrollArea';
import { IconButton } from './IconButton';
import { Skeleton } from './Skeleton';
import './DataTable.css';

export interface DataTableColumn<T> {
  id: string;
  header: ReactNode;
  cell: (row: T) => ReactNode;
  /** Makes the header a sort button. */
  sortable?: boolean;
  /** Value used for client-side sorting (only when `onSortChange` is not provided). */
  sortValue?: (row: T) => string | number | null | undefined;
  align?: 'left' | 'right' | 'center';
  width?: string;
  nowrap?: boolean;
  /** Title of the card in the mobile layout (first column when none is marked). */
  primary?: boolean;
  /** Leave this column out of mobile cards. */
  hideOnMobile?: boolean;
  /**
   * Where the column sits in a mobile card. `badge`: beside the title (a status pill). `value`: the emphasised figure
   * at the end of the meta line (`compact` layout only). In `compact` rows a column with id `status` is the badge
   * unless another column claims the slot.
   */
  mobileSlot?: 'badge' | 'value';
  /** `compact` layout: show the header before the value in the meta line ("Submitted Sep 25"). */
  mobileLabel?: boolean;
}

export interface SortState {
  id: string;
  desc: boolean;
}

export interface DataTableProps<T> {
  columns: DataTableColumn<T>[];
  rows: T[];
  getRowId: (row: T) => string;
  /** Accessible name of the table (rendered as <caption>, visually hidden unless showCaption). */
  caption: string;
  showCaption?: boolean;
  /** Controlled sort. With onSortChange the parent sorts (server-side); otherwise rows are sorted locally. */
  sort?: SortState | null;
  onSortChange?: (sort: SortState) => void;
  defaultSort?: SortState;
  loading?: boolean;
  loadingRows?: number;
  /** Shown when there are no rows (and not loading). */
  emptyState?: ReactNode;
  /** Per-row menu of actions. */
  rowActions?: (row: T) => MenuEntry[];
  /** Accessible label for a row (used for the actions button and selection checkbox). */
  rowLabel?: (row: T) => string;
  selectable?: boolean;
  selectedIds?: string[];
  onSelectionChange?: (ids: string[]) => void;
  /** Toolbar rendered while rows are selected (bulk actions). */
  bulkActions?: (selectedIds: string[]) => ReactNode;
  /** Max height before the body scrolls with a sticky header. */
  maxHeight?: string;
  /**
   * Below the md breakpoint: stacked cards with a labelled row per field (default); `compact` rows (title + status on
   * top, one meta line below — for long lists); or horizontal scrolling with a sticky first column.
   */
  mobileLayout?: 'cards' | 'compact' | 'scroll';
  className?: string;
}

function compare(a: unknown, b: unknown): number {
  if (a === b) return 0;
  if (a === null || a === undefined) return 1;
  if (b === null || b === undefined) return -1;
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
}

/**
 * Accessible data table: sortable headers expose aria-sort, optional row selection and row action menus, skeleton
 * loading rows, an empty state, a sticky header, and a stacked card layout on phones.
 */
export function DataTable<T>({
  columns,
  rows,
  getRowId,
  caption,
  showCaption,
  sort: controlledSort,
  onSortChange,
  defaultSort,
  loading,
  loadingRows = 5,
  emptyState,
  rowActions,
  rowLabel,
  selectable,
  selectedIds = [],
  onSelectionChange,
  bulkActions,
  maxHeight,
  mobileLayout = 'cards',
  className,
}: DataTableProps<T>) {
  const isMobile = useIsMobile();
  const captionId = useId();
  const [localSort, setLocalSort] = useState<SortState | null>(defaultSort ?? null);
  const sort = controlledSort !== undefined ? controlledSort : localSort;

  const sortedRows = useMemo(() => {
    if (onSortChange || !sort) return rows;
    const column = columns.find((c) => c.id === sort.id);
    if (!column?.sortValue) return rows;
    const accessor = column.sortValue;
    const copy = [...rows];
    copy.sort((a, b) => compare(accessor(a), accessor(b)) * (sort.desc ? -1 : 1));
    return copy;
  }, [rows, columns, sort, onSortChange]);

  const toggleSort = (column: DataTableColumn<T>) => {
    const next: SortState =
      sort?.id === column.id ? { id: column.id, desc: !sort.desc } : { id: column.id, desc: false };
    if (onSortChange) onSortChange(next);
    if (controlledSort === undefined) setLocalSort(next);
  };

  const selected = new Set(selectedIds);
  const allIds = sortedRows.map(getRowId);
  const allSelected = allIds.length > 0 && allIds.every((id) => selected.has(id));
  const someSelected = allIds.some((id) => selected.has(id));
  const toggleRow = (id: string) => {
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    onSelectionChange?.([...next]);
  };
  const toggleAll = () => onSelectionChange?.(allSelected ? [] : allIds);
  const labelFor = (row: T) => rowLabel?.(row) ?? getRowId(row);

  const toolbar =
    selectable && bulkActions && selectedIds.length > 0 ? (
      <div className="ui-table-toolbar" role="region" aria-label="Bulk actions">
        <span>{selectedIds.length} selected</span>
        <div className="cluster">{bulkActions(selectedIds)}</div>
      </div>
    ) : null;

  const actionsMenu = (row: T) =>
    rowActions ? (
      <DropdownMenu
        trigger={<IconButton size="sm" label={`Actions for ${labelFor(row)}`} icon={<MoreHorizontal />} />}
        items={rowActions(row)}
      />
    ) : null;

  if (!loading && sortedRows.length === 0) {
    return (
      <div className={clsx('ui-table-empty', className)}>
        {emptyState ?? <EmptyState compact headingLevel={3} title="Nothing here yet" />}
      </div>
    );
  }

  if (isMobile && mobileLayout !== 'scroll') {
    const compact = mobileLayout === 'compact';
    const primary = columns.find((c) => c.primary) ?? columns[0];
    const visible = columns.filter((c) => c !== primary && !c.hideOnMobile);
    const badges = visible.some((c) => c.mobileSlot === 'badge')
      ? visible.filter((c) => c.mobileSlot === 'badge')
      : compact
        ? visible.filter((c) => c.id === 'status').slice(0, 1)
        : [];
    const values = compact ? visible.filter((c) => c.mobileSlot === 'value') : [];
    const rest = visible.filter((c) => !badges.includes(c) && !values.includes(c));
    // Every field keeps its term: visible in the labelled rows, visually hidden next to a badge or in the meta line.
    const fieldList = (cols: DataTableColumn<T>[], row: T, listClass: string, showLabel: (c: DataTableColumn<T>) => boolean) =>
      cols.length > 0 && (
        <dl className={listClass}>
          {cols.map((column) => (
            <div key={column.id} className="ui-table-card__field">
              <dt className={showLabel(column) ? undefined : 'visually-hidden'}>{column.header}</dt>
              <dd>{column.cell(row)}</dd>
            </div>
          ))}
        </dl>
      );
    return (
      <div className={className} aria-busy={loading || undefined}>
        {toolbar}
        <p id={captionId} className={showCaption ? 'ui-card__title' : 'visually-hidden'}>
          {caption}
        </p>
        <ul className={compact ? 'ui-table-cards ui-table-cards--compact' : 'ui-table-cards'} aria-labelledby={captionId}>
          {loading
            ? Array.from({ length: Math.min(loadingRows, 4) }, (_, i) => (
                <li key={i} className="ui-table-card" aria-hidden="true">
                  <Skeleton width="55%" height={18} />
                  <Skeleton width="100%" height={14} />
                  {!compact && <Skeleton width="80%" height={14} />}
                </li>
              ))
            : sortedRows.map((row) => {
                const id = getRowId(row);
                return (
                  <li
                    key={id}
                    className="ui-table-card"
                    data-selected={selectable ? selected.has(id) : undefined}
                  >
                    <div className="ui-table-card__head">
                      {selectable && (
                        <Checkbox
                          label={<span className="visually-hidden">Select {labelFor(row)}</span>}
                          checked={selected.has(id)}
                          onChange={() => toggleRow(id)}
                        />
                      )}
                      <div className="ui-table-card__primary">{primary?.cell(row)}</div>
                      {fieldList(badges, row, 'ui-table-card__badge', () => false)}
                      {actionsMenu(row)}
                    </div>
                    {compact
                      ? (rest.length > 0 || values.length > 0) && (
                          <div className="ui-table-card__foot">
                            {fieldList(rest, row, 'ui-table-card__meta', (c) => !!c.mobileLabel)}
                            {fieldList(values, row, 'ui-table-card__value', () => false)}
                          </div>
                        )
                      : fieldList(rest, row, 'ui-table-card__fields', () => true)}
                  </li>
                );
              })}
        </ul>
      </div>
    );
  }

  return (
    <div className={className}>
      {toolbar}
      <ScrollArea
        className={clsx('ui-table-wrap', maxHeight && 'ui-table-wrap--scroll')}
        style={maxHeight ? ({ '--table-max-height': maxHeight } as CSSProperties) : undefined}
        labelledBy={captionId}
      >
        <table className="ui-table" aria-busy={loading || undefined}>
          <caption id={captionId} className={showCaption ? undefined : 'visually-hidden'}>
            {caption}
          </caption>
          <thead>
            <tr>
              {selectable && (
                <th scope="col" className="ui-table__select">
                  <Checkbox
                    label={<span className="visually-hidden">Select all rows</span>}
                    checked={allSelected}
                    indeterminate={!allSelected && someSelected}
                    onChange={toggleAll}
                    disabled={loading}
                  />
                </th>
              )}
              {columns.map((column) => {
                const active = sort?.id === column.id;
                const ariaSort = active
                  ? sort?.desc
                    ? 'descending'
                    : 'ascending'
                  : column.sortable
                    ? 'none'
                    : undefined;
                return (
                  <th
                    key={column.id}
                    scope="col"
                    aria-sort={ariaSort}
                    data-align={column.align}
                    style={column.width ? { width: column.width } : undefined}
                  >
                    {column.sortable ? (
                      <button type="button" className="ui-table__sort" onClick={() => toggleSort(column)}>
                        {column.header}
                        {active ? (
                          sort?.desc ? (
                            <ArrowDown aria-hidden="true" />
                          ) : (
                            <ArrowUp aria-hidden="true" />
                          )
                        ) : (
                          <ChevronsUpDown aria-hidden="true" />
                        )}
                      </button>
                    ) : (
                      column.header
                    )}
                  </th>
                );
              })}
              {rowActions && (
                <th scope="col" className="ui-table__actions">
                  <span className="visually-hidden">Actions</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {loading
              ? Array.from({ length: loadingRows }, (_, i) => (
                  <tr key={i} aria-hidden="true">
                    {selectable && <td />}
                    {columns.map((column) => (
                      <td key={column.id} data-align={column.align}>
                        <Skeleton width={column.align === 'right' ? '50%' : '75%'} height={14} />
                      </td>
                    ))}
                    {rowActions && <td />}
                  </tr>
                ))
              : sortedRows.map((row) => {
                  const id = getRowId(row);
                  return (
                    <tr key={id} aria-selected={selectable ? selected.has(id) : undefined}>
                      {selectable && (
                        <td>
                          <Checkbox
                            label={<span className="visually-hidden">Select {labelFor(row)}</span>}
                            checked={selected.has(id)}
                            onChange={() => toggleRow(id)}
                          />
                        </td>
                      )}
                      {columns.map((column) => (
                        <td
                          key={column.id}
                          data-align={column.align}
                          className={column.nowrap ? 'ui-table__cell--nowrap' : undefined}
                        >
                          {column.cell(row)}
                        </td>
                      ))}
                      {rowActions && <td className="ui-table__actions">{actionsMenu(row)}</td>}
                    </tr>
                  );
                })}
          </tbody>
        </table>
      </ScrollArea>
    </div>
  );
}
