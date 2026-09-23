import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Badge, Button, ErrorState, PageHeader, Skeleton } from '@/components/ui';
import { api } from '@/lib/api/client';
import { pageKeys, type FormTemplateInfo, type PageTemplate } from './api';
import { blockLabels } from './blockDefaults';
import { CreatePageDialog } from './LandingPagesListPage';
import './pages.css';

/** Landing-page and form template gallery. */
export function TemplatesPage() {
  const [using, setUsing] = useState<string | null>(null);
  const templates = useQuery({ queryKey: pageKeys.templates, queryFn: () => api.get<PageTemplate[]>('/agency/pages/templates'), staleTime: 10 * 60_000 });
  const formTemplates = useQuery({
    queryKey: pageKeys.formTemplates,
    queryFn: () => api.get<FormTemplateInfo[]>('/agency/pages/form-templates'),
    staleTime: 10 * 60_000,
  });

  return (
    <>
      <PageHeader
        title="Templates"
        description="Proven page layouts with real copy. Every template is fully editable after you create a page from it."
        breadcrumbs={[{ label: 'Landing pages', to: '/agency/pages' }, { label: 'Templates' }]}
      />
      <div className="stack">
        <section aria-labelledby="pb-page-templates" className="stack">
          <h2 id="pb-page-templates">Page templates</h2>
          {templates.isError ? (
            <ErrorState error={templates.error} onRetry={() => void templates.refetch()} />
          ) : !templates.data ? (
            <Skeleton height="12rem" />
          ) : (
            <ul className="pb-card-grid">
              {templates.data.map((t) => (
                <li key={t.key}>
                  <article className="pb-template-card" aria-labelledby={`pb-tpl-${t.key}`}>
                    <Badge tone="neutral">{t.category}</Badge>
                    <h3 id={`pb-tpl-${t.key}`}>{t.name}</h3>
                    <p>{t.description}</p>
                    <p className="pb-template-card__blocks">{t.blocks.map((b) => blockLabels[b.type]).join(' · ')}</p>
                    <div>
                      <Button size="sm" onClick={() => setUsing(t.key)} aria-label={`Use the ${t.name} template`}>
                        Use template
                      </Button>
                    </div>
                  </article>
                </li>
              ))}
            </ul>
          )}
        </section>
        <section aria-labelledby="pb-form-templates" className="stack">
          <h2 id="pb-form-templates">Form templates</h2>
          <p className="text-muted">Choose one when you create a form from the Forms page.</p>
          {formTemplates.isError ? (
            <ErrorState error={formTemplates.error} onRetry={() => void formTemplates.refetch()} />
          ) : !formTemplates.data ? (
            <Skeleton height="8rem" />
          ) : (
            <ul className="pb-card-grid">
              {formTemplates.data.map((t) => (
                <li key={t.key}>
                  <article className="pb-template-card">
                    <h3>{t.name}</h3>
                    <p>{t.description}</p>
                    <p className="pb-template-card__blocks">
                      {t.schema.steps.flatMap((s) => s.fields).filter((f) => f.type !== 'hidden').map((f) => f.label).join(' · ')}
                    </p>
                  </article>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>
      {using && <CreatePageDialog initialTemplate={using} onClose={() => setUsing(null)} />}
    </>
  );
}
