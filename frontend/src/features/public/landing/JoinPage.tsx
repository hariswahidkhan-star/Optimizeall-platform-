import { useEffect } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import { ErrorState } from '@/components/ui';
import { isApiError } from '@/lib/api/errors';
import { LandingSkeleton, LandingUnavailable, LandingView } from './LandingView';
import { useInvitationLanding } from './landingApi';

/** Codes are URL-safe; anything else is dropped from the registration link (the API validates codes for real). */
const CODE = /^[A-Za-z0-9_-]{1,64}$/;

/** `/register?invite={code}` plus the referral code from `?ref=` when present. */
export function invitationRegisterPath(code: string, ref: string | null): string {
  const params = new URLSearchParams({ invite: code });
  if (ref && CODE.test(ref)) params.set('ref', ref);
  return `/register?${params.toString()}`;
}

/** Public invitation landing page (`/join/:code`), the target of invitation links built by the API. */
export function JoinPage() {
  const { code = '' } = useParams();
  const [searchParams] = useSearchParams();
  const query = useInvitationLanding(code);
  const data = query.data;

  useEffect(() => {
    if (data) document.title = `${data.headline} · Optimize All`;
  }, [data]);

  if (query.isPending) return <LandingSkeleton />;
  if (query.isError) {
    if (isApiError(query.error) && query.error.status === 404) return <LandingUnavailable kind="invitation" />;
    return (
      <div className="container campaign-landing__loading">
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </div>
    );
  }
  if (!data) return <LandingSkeleton />;

  const campaign = data.campaign;
  return (
    <LandingView
      eyebrow={campaign ? `Invitation · ${campaign.title}` : 'You’re invited to Optimize All'}
      headline={data.headline}
      body={data.body}
      heroImageUrl={data.heroImageUrl}
      cta={{ to: invitationRegisterPath(data.code, searchParams.get('ref')), label: 'Accept invitation' }}
      secondaryCta={{ to: '/login', label: 'I already have an account' }}
      reward={campaign?.reward}
      platforms={campaign?.platforms}
      startsAt={campaign?.startsAt}
      endsAt={campaign?.endsAt}
      category={campaign?.category}
    />
  );
}
