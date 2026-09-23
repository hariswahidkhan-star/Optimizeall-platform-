import clsx from 'clsx';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useId } from 'react';
import { formatNumber } from '@/lib/format/money';
import './display.css';
import './forms.css';

export interface PaginationProps {
  /** 1-based current page. */
  page: number;
  pageSize: number;
  total: number;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (pageSize: number) => void;
  pageSizeOptions?: number[];
  /** Accessible label of the nav landmark. */
  label?: string;
  className?: string;
}

/** Page numbers with ellipses around the current page: 1 … 4 5 [6] 7 8 … 20 */
export function pageWindow(page: number, totalPages: number): (number | 'gap')[] {
  if (totalPages <= 7) return Array.from({ length: totalPages }, (_, i) => i + 1);
  const pages = new Set([1, totalPages, page - 1, page, page + 1]);
  if (page <= 3) [2, 3, 4].forEach((p) => pages.add(p));
  if (page >= totalPages - 2) [totalPages - 3, totalPages - 2, totalPages - 1].forEach((p) => pages.add(p));
  const sorted = [...pages].filter((p) => p >= 1 && p <= totalPages).sort((a, b) => a - b);
  const result: (number | 'gap')[] = [];
  sorted.forEach((p, i) => {
    if (i > 0 && p - sorted[i - 1]! > 1) result.push('gap');
    result.push(p);
  });
  return result;
}

export function Pagination({
  page,
  pageSize,
  total,
  onPageChange,
  onPageSizeChange,
  pageSizeOptions = [10, 25, 50, 100],
  label = 'Pagination',
  className,
}: PaginationProps) {
  const sizeId = useId();
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);

  return (
    <nav className={clsx('ui-pagination', className)} aria-label={label}>
      <p aria-live="polite">
        {total === 0
          ? 'No results'
          : `Showing ${formatNumber(from)}–${formatNumber(to)} of ${formatNumber(total)}`}
      </p>
      {onPageSizeChange && (
        <div className="ui-pagination__size">
          <label htmlFor={sizeId}>Rows per page</label>
          <select
            id={sizeId}
            className="ui-select ui-select--sm"
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value))}
          >
            {pageSizeOptions.map((size) => (
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </select>
        </div>
      )}
      {totalPages > 1 && (
        <ul className="ui-pagination__pages">
          <li>
            <button
              type="button"
              className="ui-pagination__page ui-pagination__nav"
              aria-label="Previous page"
              disabled={page <= 1}
              onClick={() => onPageChange(page - 1)}
            >
              <ChevronLeft aria-hidden="true" size={16} />
            </button>
          </li>
          {pageWindow(page, totalPages).map((p, i) =>
            p === 'gap' ? (
              <li key={`gap-${i}`} className="ui-pagination__ellipsis" aria-hidden="true">
                …
              </li>
            ) : (
              <li key={p}>
                <button
                  type="button"
                  className="ui-pagination__page"
                  aria-label={`Page ${p}`}
                  aria-current={p === page ? 'page' : undefined}
                  onClick={() => onPageChange(p)}
                >
                  {p}
                </button>
              </li>
            ),
          )}
          <li>
            <button
              type="button"
              className="ui-pagination__page ui-pagination__nav"
              aria-label="Next page"
              disabled={page >= totalPages}
              onClick={() => onPageChange(page + 1)}
            >
              <ChevronRight aria-hidden="true" size={16} />
            </button>
          </li>
        </ul>
      )}
    </nav>
  );
}
