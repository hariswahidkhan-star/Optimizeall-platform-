import { useMutation } from '@tanstack/react-query';
import { useId, useState, type FormEvent } from 'react';
import { Alert, Button, FormField, Input } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useSite } from './api';
import { useSiteCopy } from './copy';
import { ConsentCheckbox, fieldErrorsOf, Honeypot, useFormToken, withFormEnvelope } from './forms';

/** Newsletter signup (double opt-in: the API emails a confirmation link). */
export function NewsletterSignup({ source, compact }: { source: string; compact?: boolean }) {
  const { data: site } = useSite();
  const copy = useSiteCopy();
  const token = useFormToken();
  const [email, setEmail] = useState('');
  const [consent, setConsent] = useState(false);
  const [nickname, setNickname] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const id = useId();
  const consentVersion = site?.consent.newsletterVersion ?? 'newsletter-2026-09';
  const subscribe = useMutation({
    mutationFn: () =>
      api.post<{ status: string; message: string }>(
        '/public/newsletter/subscribe',
        withFormEnvelope({ email, source }, token.data?.token, consentVersion, { nickname, consent }),
      ),
    onError: (error) => setErrors(fieldErrorsOf(error)),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next: Record<string, string> = {};
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) next.email = 'Enter a valid email address.';
    if (!consent) next.consent = 'Please tick the box to subscribe.';
    setErrors(next);
    if (Object.keys(next).length === 0) subscribe.mutate();
  };

  if (subscribe.isSuccess)
    return (
      <Alert tone="success" title={copy.text('shared.newsletter.success')}>
        {subscribe.data.message}
      </Alert>
    );

  return (
    <form className={compact ? 'site-newsletter site-newsletter--compact' : 'site-newsletter'} onSubmit={submit} noValidate aria-label={compact ? 'Newsletter signup' : `Newsletter signup (${source})`}>
      <FormField label="Email address" error={errors.email} id={`${id}-email`}>
        <Input type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="you@company.com" />
      </FormField>
      <Honeypot value={nickname} onChange={setNickname} />
      <ConsentCheckbox
        id={`${id}-consent`}
        text={site?.consent.newsletterText ?? 'Send me the Optimize All newsletter. I can unsubscribe at any time.'}
        checked={consent}
        onChange={setConsent}
        error={errors.consent}
      />
      {subscribe.isError && !Object.keys(errors).length && (
        <Alert tone="danger" title="We couldn't subscribe you">
          {errorMessage(subscribe.error)}
        </Alert>
      )}
      <div>
        <Button type="submit" loading={subscribe.isPending} disabled={token.isLoading}>
          Subscribe
        </Button>
      </div>
    </form>
  );
}
