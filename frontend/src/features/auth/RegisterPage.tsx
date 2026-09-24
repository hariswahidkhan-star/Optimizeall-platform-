import { Gift, Mail, UserRound } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type CSSProperties, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/Checkbox';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PasswordInput } from '@/components/ui/PasswordInput';
import { Select } from '@/components/ui/Select';
import { useAuth } from '@/lib/auth/useAuth';
import { mapServerErrors, type MappedErrors } from './formErrors';
import { ContinueWithGoogle } from './google/GoogleButton';
import { rememberSignupCodes } from './google/googleApi';
import {
  countryOptions,
  defaultCountry,
  defaultLanguage,
  defaultTimeZone,
  languageOptions,
  timeZoneOptions,
} from './localeOptions';
import { passwordProblem } from './passwordPolicy';
import { PasswordStrength } from './PasswordStrength';
import '@/app/layouts/AuthLayout.css';

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const CODE_RE = /^[A-Za-z0-9_-]{1,32}$/;

const FIELDS = [
  'email',
  'password',
  'displayName',
  'countryCode',
  'languageCode',
  'timeZone',
  'referralCode',
  'inviteCode',
  'acceptTerms',
] as const;
type Field = (typeof FIELDS)[number];

interface FormState {
  email: string;
  password: string;
  displayName: string;
  countryCode: string;
  languageCode: string;
  timeZone: string;
  acceptTerms: boolean;
  marketingEmailOptIn: boolean;
}

function validate(values: FormState): Partial<Record<Field, string>> {
  const errors: Partial<Record<Field, string>> = {};
  if (!EMAIL_RE.test(values.email.trim()))
    errors.email = 'Enter a valid email address, like name@example.com.';
  const pw = passwordProblem(values.password, values.email.trim());
  if (pw) errors.password = pw;
  const name = values.displayName.trim();
  if (name.length < 2) errors.displayName = 'Enter a display name of at least 2 characters.';
  else if (name.length > 100) errors.displayName = 'Keep your display name under 100 characters.';
  if (!values.countryCode) errors.countryCode = 'Choose the country you live in.';
  if (!values.timeZone) errors.timeZone = 'Choose your time zone.';
  if (!values.acceptTerms)
    errors.acceptTerms = 'You need to accept the participant rules to create an account.';
  return errors;
}

/** Only accept referral/invite codes that look like codes (the API validates them for real). */
function cleanCode(value: string | null): string | undefined {
  const trimmed = value?.trim();
  return trimmed && CODE_RE.test(trimmed) ? trimmed : undefined;
}

export function RegisterPage() {
  const { register } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const referralCode = cleanCode(params.get('ref'));
  const inviteCode = cleanCode(params.get('invite'));

  const [values, setValues] = useState<FormState>(() => ({
    email: '',
    password: '',
    displayName: '',
    countryCode: defaultCountry(),
    languageCode: defaultLanguage(),
    timeZone: defaultTimeZone(),
    acceptTerms: false,
    marketingEmailOptIn: false,
  }));
  const [touched, setTouched] = useState<Partial<Record<Field, boolean>>>({});
  const [submitted, setSubmitted] = useState(false);
  const [server, setServer] = useState<MappedErrors | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const alertRef = useRef<HTMLDivElement>(null);

  const countries = useMemo(() => countryOptions(), []);
  const languages = useMemo(() => languageOptions(), []);
  const zones = useMemo(() => timeZoneOptions(values.timeZone), [values.timeZone]);

  useEffect(() => {
    document.title = 'Create your account · Optimize All';
  }, []);

  useEffect(() => {
    if (server?.form) alertRef.current?.focus();
  }, [server]);

  const clientErrors = validate(values);
  const errorFor = (field: Field): string | string[] | undefined => {
    const serverMessages = server?.fields[field];
    if (serverMessages?.length) return serverMessages;
    return submitted || touched[field] ? clientErrors[field] : undefined;
  };

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => {
    setValues((v) => ({ ...v, [key]: value }));
    if (server?.fields[key]) {
      setServer((s) => {
        if (!s) return s;
        const rest = { ...s.fields };
        delete rest[key];
        return { ...s, fields: rest };
      });
    }
  };
  const blur = (field: Field) => () => setTouched((t) => ({ ...t, [field]: true }));

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitted(true);
    setServer(null);
    const firstInvalid = FIELDS.find((f) => clientErrors[f]);
    if (firstInvalid) {
      document.getElementById(`register-${firstInvalid}`)?.focus();
      return;
    }
    setSubmitting(true);
    try {
      await register({
        email: values.email.trim(),
        password: values.password,
        displayName: values.displayName.trim(),
        countryCode: values.countryCode,
        languageCode: values.languageCode,
        timeZone: values.timeZone,
        referralCode,
        inviteCode,
        acceptTerms: values.acceptTerms,
        marketingEmailOptIn: values.marketingEmailOptIn,
      });
      navigate('/check-email', { state: { email: values.email.trim() } });
    } catch (error) {
      const mapped = mapServerErrors(error, FIELDS, {
        'auth.terms_required': 'acceptTerms',
        'auth.invalid_timezone': 'timeZone',
        'auth.weak_password': 'password',
      });
      setServer(mapped);
      const firstServerField = FIELDS.find((f) => mapped.fields[f]);
      if (!mapped.form && firstServerField) document.getElementById(`register-${firstServerField}`)?.focus();
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="auth-page">
      <div className="auth-page__header">
        <h1 className="auth-page__title">Create your account</h1>
        <p className="auth-page__subtitle">
          Join campaigns, share from your own accounts and get paid for approved posts.
        </p>
      </div>

      {(referralCode || inviteCode) && (
        <Alert tone="brand" icon={<Gift />} title="You were invited">
          {inviteCode ? (
            <>
              Your invitation code <strong>{inviteCode}</strong> will be applied to your new account.
            </>
          ) : (
            <>
              Your referral code <strong>{referralCode}</strong> will be applied when you sign up.
            </>
          )}
        </Alert>
      )}

      {server?.form && (
        <div ref={alertRef} tabIndex={-1}>
          <Alert tone="danger" role="alert" title={server.form.title}>
            {server.form.details.length > 0 && (
              <ul>
                {server.form.details.map((d) => (
                  <li key={d}>{d}</li>
                ))}
              </ul>
            )}
            {server.form.traceId && (
              <p className="text-small">
                Reference: <code>{server.form.traceId}</code>
              </p>
            )}
          </Alert>
        </div>
      )}

      <ContinueWithGoogle onBeforeRedirect={() => rememberSignupCodes({ referralCode, inviteCode })} />

      <form className="auth-form" onSubmit={onSubmit} noValidate aria-label="Create account">
        <FormField id="register-email" label="Email" required error={errorFor('email')}>
          <Input
            type="email"
            autoComplete="email"
            inputMode="email"
            leading={<Mail />}
            maxLength={254}
            value={values.email}
            onChange={(e) => set('email', e.target.value)}
            onBlur={blur('email')}
          />
        </FormField>

        <FormField
          id="register-password"
          label="Password"
          required
          error={errorFor('password')}
          hint={<PasswordStrength password={values.password} email={values.email} />}
        >
          <PasswordInput
            autoComplete="new-password"
            maxLength={128}
            value={values.password}
            onChange={(e) => set('password', e.target.value)}
            onBlur={blur('password')}
          />
        </FormField>

        <FormField
          id="register-displayName"
          label="Display name"
          required
          hint="Shown to reviewers and on your profile. You can use your name or your handle."
          error={errorFor('displayName')}
        >
          <Input
            autoComplete="nickname"
            leading={<UserRound />}
            maxLength={100}
            value={values.displayName}
            onChange={(e) => set('displayName', e.target.value)}
            onBlur={blur('displayName')}
          />
        </FormField>

        <div className="auth-form__row">
          <FormField id="register-countryCode" label="Country" required error={errorFor('countryCode')}>
            <Select
              autoComplete="country"
              placeholder="Choose a country"
              options={countries}
              value={values.countryCode}
              onChange={(e) => set('countryCode', e.target.value)}
              onBlur={blur('countryCode')}
            />
          </FormField>
          <FormField id="register-languageCode" label="Language" error={errorFor('languageCode')}>
            <Select
              options={languages}
              value={values.languageCode}
              onChange={(e) => set('languageCode', e.target.value)}
            />
          </FormField>
        </div>

        <FormField
          id="register-timeZone"
          label="Time zone"
          required
          hint="Used for campaign deadlines and payout dates."
          error={errorFor('timeZone')}
        >
          <Select
            options={zones}
            value={values.timeZone}
            onChange={(e) => set('timeZone', e.target.value)}
            onBlur={blur('timeZone')}
          />
        </FormField>

        {(server?.fields.referralCode || server?.fields.inviteCode) && (
          <Alert tone="warning" role="alert" title="There’s a problem with your invitation">
            {[...(server.fields.referralCode ?? []), ...(server.fields.inviteCode ?? [])].join(' ')}
          </Alert>
        )}

        <div className="stack" style={{ '--stack-gap': 'var(--space-3)' } as CSSProperties}>
          <div>
            <Checkbox
              id="register-acceptTerms"
              checked={values.acceptTerms}
              invalid={!!errorFor('acceptTerms')}
              aria-describedby={errorFor('acceptTerms') ? 'register-acceptTerms-error' : undefined}
              onChange={(e) => set('acceptTerms', e.target.checked)}
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
                id="register-acceptTerms-error"
                className="ui-field__error"
                style={{ marginTop: 'var(--space-2)' }}
              >
                {[errorFor('acceptTerms')].flat().join(' ')}
              </p>
            )}
          </div>
          <Checkbox
            id="register-marketing"
            checked={values.marketingEmailOptIn}
            onChange={(e) => set('marketingEmailOptIn', e.target.checked)}
            label="Email me about new campaigns and tips"
            description="Optional. You can unsubscribe at any time."
          />
        </div>

        <Button type="submit" variant="highlight" size="lg" fullWidth loading={submitting}>
          Create account
        </Button>
      </form>

      <p className="auth-page__switch">
        Already have an account?{' '}
        <Link className="ui-link" to="/login">
          Sign in
        </Link>
      </p>
    </div>
  );
}
