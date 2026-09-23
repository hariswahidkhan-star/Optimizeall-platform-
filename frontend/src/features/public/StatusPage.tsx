import type { ReactNode } from 'react';
import './StatusPage.css';

export interface StatusPageProps {
  code?: string;
  icon: ReactNode;
  title: string;
  description: ReactNode;
  actions?: ReactNode;
  /** Render as the page's main landmark (for pages outside any layout). */
  standalone?: boolean;
}

/** Full-width message page (404, 403, crash). */
export function StatusPage({ code, icon, title, description, actions, standalone }: StatusPageProps) {
  const content = (
    <div className="status-page">
      <span className="status-page__icon" aria-hidden="true">
        {icon}
      </span>
      {code && <p className="status-page__code">{code}</p>}
      <h1 className="status-page__title">{title}</h1>
      <p className="status-page__description">{description}</p>
      {actions && <div className="status-page__actions">{actions}</div>}
    </div>
  );
  return standalone ? (
    <main id="main" className="status-page__standalone">
      {content}
    </main>
  ) : (
    content
  );
}
