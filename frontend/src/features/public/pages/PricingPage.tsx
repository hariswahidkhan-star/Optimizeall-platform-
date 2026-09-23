import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { EmptyState } from '@/components/ui';
import { usePage, usePricing } from '../site/api';
import { Blocks } from '../site/Blocks';
import { CtaBand, PackageCard, PageHero, PublicQueryState, Section } from '../site/components';
import { useDocumentHead } from '../site/head';

type Mode = 'recurring' | 'oneTime';

/** /pricing — all packages, switchable between monthly retainers and one-time projects, plus the CMS "pricing" page. */
export function PricingPage() {
  const { data, isLoading, error } = usePricing();
  const page = usePage('pricing');
  const [mode, setMode] = useState<Mode>('recurring');
  useDocumentHead({
    title: 'Pricing',
    description: 'Transparent starting packages for every Optimize All service: monthly retainers and one-time projects.',
  });

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
        eyebrow="Pricing"
        title="Clear prices. No surprises."
        lead="Starting packages for every service. Taxes and third-party costs such as ad spend are extra and always billed at cost."
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Pricing' }]}
      />
      {page.data && <Blocks blocks={page.data.blocks.filter((b) => b.type !== 'faq')} />}
      <div className="site-section site-section--tight">
        <div className="container">
          <div className="site-toggle" role="group" aria-label="Billing type">
            <button type="button" aria-pressed={mode === 'recurring'} onClick={() => setMode('recurring')}>
              Monthly retainers
            </button>
            <button type="button" aria-pressed={mode === 'oneTime'} onClick={() => setMode('oneTime')}>
              One-time projects
            </button>
          </div>
        </div>
      </div>
      <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="Pricing unavailable">
        {services.length === 0 && (
          <div className="container">
            <EmptyState title="No packages of this type yet" headingLevel={2} />
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
      <CtaBand title="Need something bespoke?" text="Combine services or scale across markets — we'll build a custom quote around your goals." />
    </>
  );
}
