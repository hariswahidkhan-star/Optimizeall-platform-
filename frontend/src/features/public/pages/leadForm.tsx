import { useMutation } from '@tanstack/react-query';
import { CheckCircle2 } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { Alert, ButtonLink, Checkbox, FormField, Input } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { type ServiceCategoryGroup, useSite } from '../site/api';
import { ConsentCheckbox, fieldErrorsOf, Honeypot, useFormToken, useRenewFormToken, withFormEnvelope } from '../site/forms';

export interface ContactValues {
  name: string;
  email: string;
  phone: string;
  company: string;
  website: string;
}

export const EMPTY_CONTACT: ContactValues = { name: '', email: '', phone: '', company: '', website: '' };

export const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function validateContact(values: ContactValues, requireWebsite = false): Record<string, string> {
  const errors: Record<string, string> = {};
  if (values.name.trim().length < 2) errors.name = 'Enter your name.';
  if (!EMAIL_RE.test(values.email.trim())) errors.email = 'Enter a valid email address, e.g. name@company.com.';
  if (values.phone.trim() && !/^\+?[0-9][0-9 ().-]{5,30}$/.test(values.phone.trim())) errors.phone = 'Enter a valid phone number.';
  if (requireWebsite && !values.website.trim()) errors.website = 'Enter the website you would like us to audit.';
  else if (values.website.trim() && !/^(https?:\/\/)?[^\s/$.?#]+\.[^\s]+$/i.test(values.website.trim())) errors.website = 'Enter a website address such as example.com.';
  return errors;
}

/** Name, email, phone, company and website fields shared by the lead forms. */
export function ContactFields({
  values,
  onChange,
  errors,
  websiteRequired,
  showWebsite = true,
}: {
  values: ContactValues;
  onChange: (values: ContactValues) => void;
  errors: Record<string, string>;
  websiteRequired?: boolean;
  showWebsite?: boolean;
}) {
  const set = (key: keyof ContactValues) => (e: { target: { value: string } }) => onChange({ ...values, [key]: e.target.value });
  return (
    <>
      <div className="site-form__row">
        <FormField label="Full name" required error={errors.name}>
          <Input autoComplete="name" value={values.name} onChange={set('name')} />
        </FormField>
        <FormField label="Work email" required error={errors.email}>
          <Input type="email" autoComplete="email" value={values.email} onChange={set('email')} />
        </FormField>
      </div>
      <div className="site-form__row">
        <FormField label="Company" optional error={errors.company}>
          <Input autoComplete="organization" value={values.company} onChange={set('company')} />
        </FormField>
        <FormField label="Phone" optional error={errors.phone}>
          <Input type="tel" autoComplete="tel" value={values.phone} onChange={set('phone')} />
        </FormField>
      </div>
      {showWebsite && (
        <FormField label="Website" required={websiteRequired} optional={!websiteRequired} error={errors.website} hint="For example yourcompany.com">
          <Input type="url" inputMode="url" autoComplete="url" value={values.website} onChange={set('website')} />
        </FormField>
      )}
    </>
  );
}

/** Checkbox list of services grouped by service line. */
export function ServicePicker({
  groups,
  selected,
  onChange,
  legend,
  error,
}: {
  groups: ServiceCategoryGroup[];
  selected: string[];
  onChange: (slugs: string[]) => void;
  legend: string;
  error?: string;
}) {
  const id = useId();
  const toggle = (slug: string, on: boolean) => onChange(on ? [...selected, slug] : selected.filter((s) => s !== slug));
  return (
    <fieldset className="site-checkgroup" aria-describedby={error ? `${id}-error` : undefined}>
      <legend>{legend}</legend>
      <div className="site-checkgroup__grid">
        {groups.flatMap((g) =>
          g.services.map((s) => (
            <Checkbox key={s.slug} label={s.name} checked={selected.includes(s.slug)} onChange={(e) => toggle(s.slug, e.target.checked)} />
          )),
        )}
      </div>
      {error && (
        <p className="site-field-error" id={`${id}-error`} role="alert">
          {error}
        </p>
      )}
    </fieldset>
  );
}

/** Submission plumbing shared by the contact, audit and quote forms. */
export function useLeadForm<T extends object>(path: string) {
  const token = useFormToken();
  const renewToken = useRenewFormToken();
  const { data: site } = useSite();
  const [nickname, setNickname] = useState('');
  const [consent, setConsent] = useState(false);
  const [serverErrors, setServerErrors] = useState<Record<string, string>>({});
  const consentVersion = site?.consent.formVersion ?? 'forms-2026-09';
  const mutation = useMutation({
    mutationFn: (body: T) =>
      api.post<{ reference: string; message: string }>(path, withFormEnvelope(body, token.data?.token, consentVersion, { nickname, consent })),
    onSuccess: () => void renewToken(),
    onError: (error) => setServerErrors(fieldErrorsOf(error)),
  });
  const consentField = (error?: string) => (
    <>
      <Honeypot value={nickname} onChange={setNickname} />
      <ConsentCheckbox
        id={`consent-${path.replace(/\W/g, '-')}`}
        text={site?.consent.formText ?? 'I agree that Optimize All may use my details to respond to my request.'}
        checked={consent}
        onChange={setConsent}
        error={error ?? serverErrors.consent}
      />
    </>
  );
  const generalError =
    mutation.isError && !Object.keys(serverErrors).some((k) => k !== 'consent') ? (
      <Alert tone="danger" title="We couldn't send your request">
        {isApiError(mutation.error) && mutation.error.code === 'website.form_too_fast'
          ? 'That was quick! Please check your answers and send the form again.'
          : errorMessage(mutation.error)}
      </Alert>
    ) : null;
  return { token, consent, mutation, serverErrors, consentField, generalError, budgetRanges: token.data?.budgetRanges ?? [], timelines: token.data?.timelines ?? [] };
}

export function FormSuccess({ title, reference, children }: { title: string; reference?: string; children?: ReactNode }) {
  return (
    <div className="site-form" role="status" aria-live="polite">
      <CheckCircle2 aria-hidden="true" width={40} height={40} className="site-success-icon" />
      <h2 className="site-section__title">{title}</h2>
      {children}
      {reference && <p className="text-small text-muted">Your reference: {reference}</p>}
      <div className="site-form__actions">
        <ButtonLink to="/case-studies" variant="secondary">
          Browse case studies
        </ButtonLink>
        <ButtonLink to="/" variant="ghost">
          Back to the homepage
        </ButtonLink>
      </div>
    </div>
  );
}
