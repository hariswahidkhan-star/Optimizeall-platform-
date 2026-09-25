import { useMutation } from '@tanstack/react-query';
import { MailCheck, MailX } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Alert, Button, ButtonLink, EmptyState, Input } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { type SearchHit, useSearch } from '../site/api';
import { PageHero, Section } from '../site/components';
import { useDocumentHead } from '../site/head';

function NewsletterTokenPage({ action }: { action: 'confirm' | 'unsubscribe' }) {
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';
  // The action needs a click: mail security scanners open (and may render) links, and must not confirm or unsubscribe.
  const mutation = useMutation({
    mutationFn: () => api.post<{ status: string; message: string }>(`/public/newsletter/${action}`, { token }),
  });
  // The confirmation token is single-use: a double click must send it once (the second request would fail and replace
  // "You're subscribed" with an error). The ref blocks the second click before React re-renders the button as busy.
  const sent = useRef(false);
  const send = () => {
    if (sent.current) return;
    sent.current = true;
    mutation.mutate(undefined, { onError: () => (sent.current = false) });
  };
  useDocumentHead({ title: action === 'confirm' ? 'Confirm your subscription' : 'Unsubscribe', noIndex: true });

  const title = action === 'confirm' ? 'Newsletter subscription' : 'Unsubscribe';
  return (
    <>
      <PageHero title={title} breadcrumbs={[{ label: 'Home', to: '/' }, { label: title }]} />
      <div className="container site-section site-narrow">
        {!token ? (
          <Alert tone="warning" title="This link is incomplete">
            Open the link from your email again, or copy the whole address into your browser.
          </Alert>
        ) : mutation.isPending || mutation.isIdle ? (
          <div>
            <p>
              {action === 'confirm'
                ? 'Confirm that you want to receive our newsletter.'
                : 'Unsubscribe this email address from our newsletter?'}
            </p>
            <Button onClick={send} loading={mutation.isPending}>
              {action === 'confirm' ? 'Confirm subscription' : 'Unsubscribe'}
            </Button>
          </div>
        ) : mutation.isSuccess ? (
          <EmptyState
            icon={action === 'confirm' ? <MailCheck /> : <MailX />}
            headingLevel={2}
            title={action === 'confirm' ? "You're subscribed" : "You've been unsubscribed"}
            description={mutation.data.message}
            action={
              <ButtonLink to="/blog" variant="secondary">
                Read the latest articles
              </ButtonLink>
            }
          />
        ) : (
          <Alert tone="danger" title={action === 'confirm' ? "We couldn't confirm your subscription" : "We couldn't unsubscribe you"}>
            {errorMessage(mutation.error)}{' '}
            {action === 'confirm' && (
              <>
                You can <Link to="/blog">sign up again</Link>.
              </>
            )}
          </Alert>
        )}
      </div>
    </>
  );
}

export function NewsletterConfirmPage() {
  return <NewsletterTokenPage action="confirm" />;
}

export function NewsletterUnsubscribePage() {
  return <NewsletterTokenPage action="unsubscribe" />;
}

function Hits({ title, hits }: { title: string; hits: SearchHit[] }) {
  if (hits.length === 0) return null;
  return (
    <Section title={title}>
      <ul className="site-grid site-grid--2">
        {hits.map((hit) => (
          <li key={hit.url}>
            <article className="site-card">
              <h3 className="site-card__title">
                <Link to={hit.url} className="site-card__link">
                  {hit.title}
                </Link>
              </h3>
              <p className="site-card__text">{hit.summary}</p>
            </article>
          </li>
        ))}
      </ul>
    </Section>
  );
}

/** /search — services, articles and case studies. */
export function SearchPage() {
  const [params, setParams] = useSearchParams();
  const [q, setQ] = useState(params.get('q') ?? '');
  const debounced = useDebouncedValue(q.trim(), 300);
  const { data, isFetching } = useSearch(debounced);
  useDocumentHead({ title: 'Search', noIndex: true, follow: true });
  useEffect(() => {
    setParams(debounced ? { q: debounced } : {}, { replace: true });
  }, [debounced, setParams]);
  const empty = data && data.services.length + data.posts.length + data.caseStudies.length === 0;

  return (
    <>
      <PageHero title="Search" breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Search' }]}>
        <form role="search" onSubmit={(e) => e.preventDefault()}>
          <label htmlFor="site-search" className="visually-hidden">
            Search services, articles and case studies
          </label>
          <Input id="site-search" type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search services, articles and case studies" />
        </form>
      </PageHero>
      <div aria-live="polite" aria-busy={isFetching}>
        {empty && (
          <div className="container site-section">
            <EmptyState title={`No results for “${debounced}”`} headingLevel={2} description="Try a different word, or browse our services." />
          </div>
        )}
        {data && (
          <>
            <Hits title="Services" hits={data.services} />
            <Hits title="Articles" hits={data.posts} />
            <Hits title="Case studies" hits={data.caseStudies} />
          </>
        )}
      </div>
    </>
  );
}
