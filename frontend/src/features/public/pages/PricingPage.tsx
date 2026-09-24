import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { EmptyState } from '@/components/ui';
import { usePage, usePricing } from '../site/api';
import { Blocks } from '../site/Blocks';
import { useSiteCopy } from '../site/copy';
import { CtaBand, PackageCard, PageHero, PublicQueryState, Section } from '../site/components';
import { useDocumentHead } from '../site/head';

type Mode = 'recurring' | 'oneTime';

/** /pricing — all packages, switchable between monthly retainers and one-time projects, plus the CMS "pricing" page. */
export function PricingPage() {
  const { data, isLoading, error } = usePricing();
  const page = usePage('pricing');
  const [mode, setMode] = useState<Mode>('recurring');
  const copy = useSiteCopy();
  useDocumentHead({ title: copy.text('pricing.seo.title'), description: copy.text('pricing.seo.description') });

  const services = useMemo(
    () =>
      (data?.services ?? [])
        .map((s) => ({
          ...s,
          packages: s.packages.filter((p) => (mode === 'oneTime' ? p.billingPeriod === 'OneTime' : p.billingPeriod !== 'OneTime')),
        }))
        .filter((s) => s.packages.length > 0),
    [data, mode],
  );

  return (
    <>
      <PageHero
        eyebrow={copy.text('pricing.hero.eyebrow')}
        title={copy.text('pricing.hero.title')}
        lead={copy.text('pricing.hero.lead')}
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Pricing' }]}
      />
      {page.data && <Blocks blocks={page.data.blocks.filter((b) => b.type !== 'faq')} />}
      <div className="site-section site-section--tight">
        <div className="container">
          <div className="site-toggle" role="group" aria-label="Billing type">
            <button type="button" aria-pressed={mode === 'recurring'} onClick={() => setMode('recurring')}>
              {copy.text('pricing.toggle.recurring')}
            </button>
            <button type="button" aria-pressed={mode === 'oneTime'} onClick={() => setMode('oneTime')}>
              {copy.text('pricing.toggle.oneTime')}
            </button>
          </div>
        </div>
      </div>
      <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="Pricing unavailable">
        {services.length === 0 && (
          <div className="container">
            <EmptyState title={copy.text('pricing.empty')} headingLevel={2} />
          </div>
        )}
        {services.map(({ service, packages }) => (
          <Section
            key={service.slug}
            title={service.name}
            intro={service.tagline}
            actions={<Link to={`/services/${service.slug}`}>About {service.name}</Link>}
          >
            <div className="site-packages">
              {packages.map((p) => (
                <PackageCard key={p.id} pkg={p} serviceSlug={service.slug} />
              ))}
            </div>
          </Section>
        ))}
      </PublicQueryState>
      {page.data && <Blocks blocks={page.data.blocks.filter((b) => b.type === 'faq')} />}
      <CtaBand title={copy.text('pricing.cta.title')} text={copy.text('pricing.cta.text')} />
    </>
  );
}
