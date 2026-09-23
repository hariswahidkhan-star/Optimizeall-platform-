import clsx from 'clsx';
import { ChevronRight } from 'lucide-react';
import { Fragment, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import './display.css';

export interface Breadcrumb {
  label: ReactNode;
  /** Omit for the current page (last crumb). */
  to?: string;
}

export interface PageHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  breadcrumbs?: Breadcrumb[];
  /** Primary page actions (buttons/links). */
  actions?: ReactNode;
  /** Badges/metadata under the title. */
  meta?: ReactNode;
  /** Small label above the title. */
  eyebrow?: ReactNode;
  className?: string;
}

/** Page title block. Renders the page's single <h1>. */
export function PageHeader({
  title,
  description,
  breadcrumbs,
  actions,
  meta,
  eyebrow,
  className,
}: PageHeaderProps) {
  return (
    <header className={clsx('ui-page-header', className)}>
      {breadcrumbs && breadcrumbs.length > 0 && (
        <nav aria-label="Breadcrumb" className="ui-breadcrumbs">
          <ol>
            {breadcrumbs.map((crumb, index) => {
              const last = index === breadcrumbs.length - 1;
              return (
                <Fragment key={index}>
                  <li>
                    {crumb.to && !last ? (
                      <Link to={crumb.to}>{crumb.label}</Link>
                    ) : (
                      <span aria-current={last ? 'page' : undefined}>{crumb.label}</span>
                    )}
                  </li>
                  {!last && (
                    <li aria-hidden="true">
                      <ChevronRight />
                    </li>
                  )}
                </Fragment>
              );
            })}
          </ol>
        </nav>
      )}
      <div className="ui-page-header__row">
        <div className="ui-page-header__titles">
          {eyebrow && <p className="eyebrow">{eyebrow}</p>}
          <h1 className="ui-page-header__title">{title}</h1>
          {description && <p className="ui-page-header__description">{description}</p>}
          {meta && <div className="ui-page-header__meta">{meta}</div>}
        </div>
        {actions && <div className="ui-page-header__actions">{actions}</div>}
      </div>
    </header>
  );
}
