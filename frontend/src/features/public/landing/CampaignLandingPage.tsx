import { useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { ErrorState } from '@/components/ui';
import { isApiError } from '@/lib/api/errors';
import { useAuth } from '@/lib/auth/useAuth';
import { LandingSkeleton, LandingUnavailable, LandingView } from './LandingView';
import { useCampaignLanding } from './landingApi';

/** Public campaign landing page (`/c/:slug`), the shareable link of a public campaign. */
export function CampaignLandingPage() {
  const { slug = '' } = useParams();
  const { status } = useAuth();
  const query = useCampaignLanding(slug);
  const data = query.data;

  useEffect(() => {
    if (data) document.title = `${data.title} | Optimize All`;
  }, [data]);

  if (query.isPending) return <LandingSkeleton />;
  if (query.isError) {
    if (isApiError(query.error) && query.error.status === 404) return <LandingUnavailable kind="campaign" />;
    return (
      <div className="container campaign-landing__loading">
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </div>
    );
  }
  if (!data) return <LandingSkeleton />;

  const signedIn = status === 'authenticated';
  return (
    <LandingView
      eyebrow={data.category ? `Campaign · ${data.category.name}` : 'Campaign'}
      headline={data.headline}
      body={data.body}
      heroImageUrl={data.heroImageUrl}
      cta={
        signedIn
          ? { to: `/app/campaigns/${encodeURIComponent(data.slug)}`, label: 'Open the campaign' }
          : { to: '/register', label: 'Join to take part' }
      }
      secondaryCta={signedIn ? undefined : { to: '/login', label: 'Sign in' }}
      reward={data.reward}
      platforms={data.platforms}
      startsAt={data.startsAt}
      endsAt={data.endsAt}
      submissionDeadline={data.submissionDeadline}
      category={data.category}
      disclosure={data.disclosure}
    >
      {data.assets.length > 0 && (
        <section aria-labelledby="landing-assets-title" className="campaign-landing__assets">
          <h2 id="landing-assets-title" className="campaign-landing__section-title">
            Content you’ll share
          </h2>
          <ul className="campaign-landing__gallery">
            {data.assets.map((asset) => (
              <li key={asset.id}>
                <figure>
                  {/* The caption names the image. */}
                  <img src={asset.url} alt="" loading="lazy" />
                  <figcaption>{asset.title}</figcaption>
                </figure>
              </li>
            ))}
          </ul>
        </section>
      )}
    </LandingView>
  );
}
