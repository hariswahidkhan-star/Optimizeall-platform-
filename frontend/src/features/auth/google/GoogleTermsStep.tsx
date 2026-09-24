import { UserRound } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type CSSProperties, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { defaultLandingPath } from '@/app/portals';
import { safeNextPath } from '@/app/redirects';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/Checkbox';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { Select } from '@/components/ui/Select';
import { api } from '@/lib/api/client';
import type { AuthResponse, GoogleCallbackResponse, GoogleCompleteRequest } from '@/lib/api/types';
import { getDeviceId } from '@/lib/auth/deviceId';
import { useAuth } from '@/lib/auth/useAuth';
import { mapServerErrors, type MappedErrors } from '../formErrors';
import {
  countryOptions,
  defaultCountry,
  defaultLanguage,
  defaultTimeZone,
  languageOptions,
  timeZoneOptions,
} from '../localeOptions';
import { forgetSignupCodes, recallSignupCodes } from './googleApi';

const FIELDS = ['displayName', 'countryCode', 'languageCode', 'timeZone', 'acceptTerms'] as const;
type Field = (typeof FIELDS)[number];

/**
 * New Google users accept the participant rules (the same consent as the registration form) and complete the profile
 * fields registration requires, before the account is created.
 */
export function GoogleTermsStep({ response }: { response: GoogleCallbackResponse }) {
  const { startSession } = useAuth();
  const navigate = useNavigate();
  const [displayName, setDisplayName] = useState(response.displayName ?? '');
  const [countryCode, setCountryCode] = useState(defaultCountry);
  const [languageCode, setLanguageCode] = useState(defaultLanguage);
  const [timeZone, setTimeZone] = useState(defaultTimeZone);
  const [acceptTerms, setAcceptTerms] = useState(false);
  const [marketingEmailOptIn, setMarketingEmailOptIn] = useState(false);
  const [clientErrors, setClientErrors] = useState<Partial<Record<Field, string>>>({});
  const [server, setServer] = useState<MappedErrors | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const alertRef = useRef<HTMLDivElement>(null);

  const countries = useMemo(() => countryOptions(), []);
  const languages = useMemo(() => languageOptions(), []);
  const zones = useMemo(() => timeZoneOptions(timeZone), [timeZone]);

  useEffect(() => {
    document.title = 'Finish creating your account · Optimize All';
  }, []);

  useEffect(() => {
    if (server?.form) alertRef.current?.focus();
  }, [server]);

  const errorFor = (field: Field) => clientErrors[field] ?? server?.fields[field];

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const errors: Partial<Record<Field, string>> = {};
    const name = displayName.trim();
    if (name.length < 2) errors.displayName = 'Enter a display name of at least 2 characters.';
    else if (name.length > 100) errors.displayName = 'Keep your display name under 100 characters.';
    if (!countryCode) errors.countryCode = 'Choose the country you live in.';
    if (!timeZone) errors.timeZone = 'Choose your time zone.';
    if (!acceptTerms) errors.acceptTerms = 'You need to accept the participant rules to create an account.';
    setClientErrors(errors);
    setServer(null);
    const first = FIELDS.find((f) => errors[f]);
    if (first) {
      document.getElementById(`google-${first}`)?.focus();
      return;
    }

    setSubmitting(true);
    try {
      const request: GoogleCompleteRequest = {
        ticket: response.ticket ?? '',
        acceptTerms,
        marketingEmailOptIn,
        displayName: name,
        countryCode,
        languageCode,
        timeZone,
        deviceId: getDeviceId(),
        ...recallSignupCodes(),
      };
      const session = await api.post<AuthResponse>('/auth/google/complete', request);
      forgetSignupCodes();
      const user = startSession(session);
      navigate(defaultLandingPath(user.permissions, safeNextPath(response.returnTo)), { replace: true });
    } catch (error) {
      setServer(
        mapServerErrors(error, FIELDS, {
          'auth.terms_required': 'acceptTerms',
          'auth.invalid_timezone': 'timeZone',
          'auth.invalid_display_name': 'displayName',
        }),
      );
      setSubmitting(false);
    }
  };

  const expired =
    server?.form?.code === 'auth.google_ticket_invalid' ||
    server?.form?.code === 'auth.google_account_exists';

  return (
    <div className="auth-page">
      <div className="auth-page__header">
        <h1 className="auth-page__title">Finish creating your account</h1>
        <p className="auth-page__subtitle">
          You’re signing up with Google as <strong>{response.email}</strong>.
        </p>
      </div>

      {server?.form && (
        <div ref={alertRef} tabIndex={-1}>
          <Alert
            tone="danger"
            role="alert"
            title={server.form.title}
            actions={
              expired ? (
                <Link className="ui-link" to="/login">
                  Back to sign in
                </Link>
              ) : undefined
            }
          >
            {server.form.details.length > 0 && (
              <ul>
                {server.form.details.map((d) => (
                  <li key={d}>{d}</li>
                ))}
              </ul>
            )}
          </Alert>
        </div>
      )}

      <form className="auth-form" onSubmit={onSubmit} noValidate aria-label="Finish creating your account">
        <FormField
          id="google-displayName"
          label="Display name"
          required
          hint="Shown to reviewers and on your profile. You can use your name or your handle."
          error={errorFor('displayName')}
        >
          <Input
            autoComplete="nickname"
            leading={<UserRound />}
            maxLength={100}
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
          />
        </FormField>

        <div className="auth-form__row">
          <FormField id="google-countryCode" label="Country" required error={errorFor('countryCode')}>
            <Select
              autoComplete="country"
              placeholder="Choose a country"
              options={countries}
              value={countryCode}
              onChange={(e) => setCountryCode(e.target.value)}
            />
          </FormField>
          <FormField id="google-languageCode" label="Language" error={errorFor('languageCode')}>
            <Select
              options={languages}
              value={languageCode}
              onChange={(e) => setLanguageCode(e.target.value)}
            />
          </FormField>
        </div>

        <FormField
          id="google-timeZone"
          label="Time zone"
          required
          hint="Used for campaign deadlines and payout dates."
          error={errorFor('timeZone')}
        >
          <Select options={zones} value={timeZone} onChange={(e) => setTimeZone(e.target.value)} />
        </FormField>

        <div className="stack" style={{ '--stack-gap': 'var(--space-3)' } as CSSProperties}>
          <div>
            <Checkbox
              id="google-acceptTerms"
              checked={acceptTerms}
              invalid={!!errorFor('acceptTerms')}
              aria-describedby={errorFor('acceptTerms') ? 'google-acceptTerms-error' : undefined}
              onChange={(e) => setAcceptTerms(e.target.checked)}
              label="I accept the participant rules"
              description={
                <>
                  I’ll only share from established accounts I own, always disclose paid posts, and accept that
                  every submission is reviewed.{' '}
                  <Link className="ui-link" to="/#rules">
                    Read the rules
                  </Link>
                </>
              }
            />
            {errorFor('acceptTerms') && (
              <p
                id="google-acceptTerms-error"
                className="ui-field__error"
                style={{ marginTop: 'var(--space-2)' }}
              >
                {[errorFor('acceptTerms')].flat().join(' ')}
              </p>
            )}
          </div>
          <Checkbox
            id="google-marketing"
            checked={marketingEmailOptIn}
            onChange={(e) => setMarketingEmailOptIn(e.target.checked)}
            label="Email me about new campaigns and tips"
            description="Optional. You can unsubscribe at any time."
          />
        </div>

        <Button type="submit" variant="highlight" size="lg" fullWidth loading={submitting}>
          Create account
        </Button>
      </form>

      <p className="auth-page__switch">
        Not you?{' '}
        <Link className="ui-link" to="/login">
          Use another account
        </Link>
      </p>
    </div>
  );
}
