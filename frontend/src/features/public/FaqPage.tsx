import { useQuery } from '@tanstack/react-query';
import { ChevronDown, MessageCircleQuestion } from 'lucide-react';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { EmptyState } from '@/components/ui/EmptyState';
import { Skeleton } from '@/components/ui/Skeleton';
import { api } from '@/lib/api/client';
import { normalizeFaqs } from './faqs';
import { useSiteCopy } from './site/copy';
import { useDocumentHead } from './site/head';
import './FaqPage.css';

function slug(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '-');
}

function Paragraphs({ text }: { text: string }) {
  return (
    <>
      {text
        .split(/\n{2,}/)
        .map((p) => p.trim())
        .filter(Boolean)
        .map((p, i) => (
          <p key={i}>{p}</p>
        ))}
    </>
  );
}

/** Public FAQ, grouped by category, from the content API. */
export function FaqPage() {
  const copy = useSiteCopy();
  useDocumentHead({ title: copy.text('faq.seo.title'), description: copy.text('faq.seo.description') });

  const query = useQuery({
    queryKey: ['content', 'faqs'],
    queryFn: async () => normalizeFaqs(await api.get<unknown>('/content/faqs')),
    staleTime: 5 * 60_000,
  });

  const unavailable = query.isError || (query.isSuccess && query.data.length === 0);

  return (
    <div className="container faq-page">
      <header className="faq-page__header">
        <p className="eyebrow">{copy.text('faq.hero.eyebrow')}</p>
        <h1>{copy.text('faq.hero.title')}</h1>
        <p className="faq-page__lead">{copy.text('faq.hero.lead')}</p>
      </header>

      {query.isPending && (
        <div className="faq-page__loading" aria-busy="true" aria-label="Loading questions">
          {Array.from({ length: 5 }, (_, i) => (
            <Skeleton key={i} height={56} radius={14} />
          ))}
        </div>
      )}

      {unavailable && (
        <EmptyState
          icon={<MessageCircleQuestion />}
          title={copy.text('faq.empty.title')}
          description={copy.text('faq.empty.description')}
          action={
            <>
              <ButtonLink to="/#how-it-works">How it works</ButtonLink>
              <ButtonLink to="/#rules" variant="secondary">
                Rules & trust
              </ButtonLink>
            </>
          }
        />
      )}

      {query.isSuccess &&
        query.data.map((group) => (
          <section key={group.category} className="faq-group" aria-labelledby={`faq-${slug(group.category)}`}>
            <h2 id={`faq-${slug(group.category)}`} className="faq-group__title">
              {group.category}
            </h2>
            <div className="faq-group__items">
              {group.items.map((item) => (
                <details key={item.id} className="faq-item">
                  <summary>
                    <span>{item.question}</span>
                    <ChevronDown aria-hidden="true" />
                  </summary>
                  <div className="faq-item__answer">
                    <Paragraphs text={item.answer} />
                  </div>
                </details>
              ))}
            </div>
          </section>
        ))}
    </div>
  );
}
