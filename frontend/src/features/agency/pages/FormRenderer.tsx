import { useMutation } from '@tanstack/react-query';
import { CheckCircle2 } from 'lucide-react';
import { useEffect, useId, useMemo, useRef, useState, type FormEvent } from 'react';
import { Alert, Button, Checkbox, FormField, Input, RadioGroup, Select, Textarea } from '@/components/ui';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import type { FormFieldDef, PublicForm } from './api';
import { isVisible, submittableValues, validateFields, type FormValues } from './formLogic';

export interface FormRendererProps {
  form: PublicForm;
  landingPageId?: string;
  variantKey?: string;
  /** Origin of the page embedding the form (sent as X-Embed-Origin; the API checks the allow-list). */
  embedOrigin?: string | null;
  /** Builder preview: interactive but never submits. */
  preview?: boolean;
  onSubmitted?: (message: string) => void;
}

const UTM_PARAMS = ['utm_source', 'utm_medium', 'utm_campaign', 'utm_term', 'utm_content', 'gclid', 'fbclid', 'ref'] as const;

function urlParams(): Record<string, string> {
  if (typeof window === 'undefined') return {};
  const params = new URLSearchParams(window.location.search);
  const result: Record<string, string> = {};
  for (const key of UTM_PARAMS) {
    const value = params.get(key);
    if (value) result[key] = value.slice(0, 150);
  }
  return result;
}

/**
 * Renders a builder form for visitors: conditional fields (same rule as the server), multi-step navigation with
 * per-step validation, a honeypot, UTM capture into hidden fields, optional CAPTCHA and file uploads.
 */
export function FormRenderer({ form, landingPageId, variantKey, embedOrigin, preview, onSubmitted }: FormRendererProps) {
  const schema = form.schema;
  const utm = useMemo(urlParams, []);
  const [values, setValues] = useState<FormValues>(() => {
    const initial: FormValues = {};
    for (const f of schema.steps.flatMap((s) => s.fields)) {
      if (f.type === 'hidden') initial[f.key] = (f.urlParam && utm[f.urlParam]) || f.defaultValue || undefined;
    }
    return initial;
  });
  const [files, setFiles] = useState<Record<string, File | undefined>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [step, setStep] = useState(0);
  const [hp, setHp] = useState('');
  const [captchaToken, setCaptchaToken] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const formId = useId();
  const statusRef = useRef<HTMLDivElement>(null);
  const steps = schema.steps;
  const current = steps[step];
  const isLast = step === steps.length - 1;

  const submit = useMutation({
    mutationFn: async () => {
      const payload = {
        values: submittableValues(schema, values),
        hp,
        token: form.token,
        captchaToken,
        landingPageId,
        variantKey,
        utmSource: utm.utm_source,
        utmMedium: utm.utm_medium,
        utmCampaign: utm.utm_campaign,
        utmTerm: utm.utm_term,
        utmContent: utm.utm_content,
        referrer: typeof document !== 'undefined' && document.referrer ? document.referrer : undefined,
      };
      const headers: Record<string, string> = embedOrigin ? { 'X-Embed-Origin': embedOrigin } : {};
      const attached = Object.entries(files).filter((e): e is [string, File] => !!e[1]);
      if (attached.length === 0) return api.post<{ ok: boolean; message: string; redirectUrl: string | null }>(`/public/forms/${form.id}/submissions`, payload, { headers });
      const data = new FormData();
      data.append('payload', JSON.stringify(payload));
      for (const [key, file] of attached) data.append(key, file, file.name);
      return api.upload<{ ok: boolean; message: string; redirectUrl: string | null }>(`/public/forms/${form.id}/submissions`, data, { headers });
    },
    onSuccess: (result) => {
      setDone(result.message);
      onSubmitted?.(result.message);
      if (result.redirectUrl && /^https:\/\//.test(result.redirectUrl)) window.location.assign(result.redirectUrl);
    },
    onError: (err) => {
      if (isApiError(err) && err.errors) {
        const mapped: Record<string, string> = {};
        for (const [k, v] of Object.entries(err.errors)) mapped[k] = v[0];
        setErrors(mapped);
        const firstStep = steps.findIndex((s) => s.fields.some((f) => mapped[f.key]));
        if (firstStep >= 0) setStep(firstStep);
      }
    },
  });

  useEffect(() => {
    if (done) statusRef.current?.focus();
  }, [done]);

  if (done) {
    return (
      <div ref={statusRef} tabIndex={-1} role="status" className="lp-form__done">
        <CheckCircle2 aria-hidden="true" />
        <p>{done}</p>
      </div>
    );
  }

  const set = (key: string, value: FormValues[string]) => {
    setValues((v) => ({ ...v, [key]: value }));
    setErrors((e) => {
      if (!e[key]) return e;
      const next = { ...e };
      delete next[key];
      return next;
    });
  };

  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    const stepErrors = validateFields(current.fields, values, files, schema);
    setErrors(stepErrors);
    if (Object.keys(stepErrors).length > 0) return;
    if (!isLast) {
      setStep((s) => s + 1);
      return;
    }
    if (preview) {
      setDone(`${form.successMessage} (preview — nothing was sent)`);
      return;
    }
    submit.mutate();
  };

  const topError = submit.error && (!isApiError(submit.error) || !submit.error.errors) ? submit.error : null;

  return (
    <form className="lp-form" onSubmit={onSubmit} noValidate aria-describedby={steps.length > 1 ? `${formId}-progress` : undefined}>
      {steps.length > 1 && (
        <p id={`${formId}-progress`} className="lp-form__progress">
          Step {step + 1} of {steps.length}
          {current.title ? `: ${current.title}` : ''}
        </p>
      )}
      {current.description && <p className="lp-form__description">{current.description}</p>}
      {topError && (
        <Alert tone="danger" title="We couldn't send the form">
          {isApiError(topError) ? topError.title : 'Please try again.'}
        </Alert>
      )}
      <div className="lp-form__fields">
        {current.fields
          .filter((f) => f.type !== 'hidden' && isVisible(f, values, schema))
          .map((field) => (
            <FieldControl
              key={field.key}
              field={field}
              value={values[field.key]}
              error={errors[field.key]}
              consentText={form.consentText}
              onChange={(v) => set(field.key, v)}
              onFile={(file) => {
                setFiles((f) => ({ ...f, [field.key]: file }));
                setErrors((e) => ({ ...e, [field.key]: '' }));
              }}
            />
          ))}
      </div>
      {/* Honeypot: invisible to people and skipped by keyboard; bots that fill it are silently discarded. */}
      <div className="lp-form__hp" aria-hidden="true">
        <label>
          Leave this field empty
          <input type="text" name="company_website" tabIndex={-1} autoComplete="off" value={hp} onChange={(e) => setHp(e.target.value)} />
        </label>
      </div>
      {isLast && form.captcha && !preview && <CaptchaWidget provider={form.captcha.provider} siteKey={form.captcha.siteKey} onToken={setCaptchaToken} />}
      <div className="lp-form__actions">
        {step > 0 && (
          <Button type="button" variant="secondary" onClick={() => setStep((s) => s - 1)}>
            Back
          </Button>
        )}
        <Button type="submit" loading={submit.isPending}>
          {isLast ? form.submitLabel : 'Next'}
        </Button>
      </div>
    </form>
  );
}

function FieldControl({
  field,
  value,
  error,
  consentText,
  onChange,
  onFile,
}: {
  field: FormFieldDef;
  value: FormValues[string];
  error?: string;
  consentText: string | null;
  onChange: (value: FormValues[string]) => void;
  onFile: (file: File | undefined) => void;
}) {
  const common = { required: !!field.required, error: error || undefined, hint: field.helpText ?? undefined };
  const className = field.width === 'half' ? 'lp-field lp-field--half' : 'lp-field';
  switch (field.type) {
    case 'textarea':
      return (
        <FormField label={field.label} {...common} className={className}>
          <Textarea value={(value as string) ?? ''} placeholder={field.placeholder ?? undefined} rows={4} onChange={(e) => onChange(e.target.value)} />
        </FormField>
      );
    case 'select':
      return (
        <FormField label={field.label} {...common} className={className}>
          <Select value={(value as string) ?? ''} placeholder="Choose…" options={field.options ?? []} onChange={(e) => onChange(e.target.value)} />
        </FormField>
      );
    case 'radio':
      return (
        <RadioGroup
          className={className}
          legend={field.label}
          required={!!field.required}
          value={(value as string) ?? null}
          onChange={(v) => onChange(v)}
          options={field.options ?? []}
          error={error || null}
          hint={field.helpText ?? undefined}
        />
      );
    case 'multiselect': {
      const list = Array.isArray(value) ? value : [];
      return (
        <fieldset className={`${className} lp-fieldset`} aria-invalid={!!error || undefined}>
          <legend className="ui-field__label">
            {field.label}
            {!field.required && <span className="ui-field__optional"> (optional)</span>}
          </legend>
          {(field.options ?? []).map((o) => (
            <Checkbox
              key={o.value}
              label={o.label}
              checked={list.includes(o.value)}
              onChange={(e) => onChange(e.target.checked ? [...list, o.value] : list.filter((x) => x !== o.value))}
            />
          ))}
          {error && <p className="ui-field__error">{error}</p>}
        </fieldset>
      );
    }
    case 'checkbox':
    case 'consent':
      return (
        <div className={className}>
          <Checkbox
            label={field.type === 'consent' ? consentText ?? field.label : field.label}
            checked={value === true}
            invalid={!!error}
            onChange={(e) => onChange(e.target.checked)}
            description={field.helpText ?? undefined}
          />
          {error && (
            <p className="ui-field__error" role="alert">
              {error}
            </p>
          )}
        </div>
      );
    case 'file': {
      const accept = (field.validation?.accept ?? ['pdf', 'image']).flatMap((k) => (k === 'pdf' ? ['application/pdf', '.pdf'] : ['image/png', 'image/jpeg', 'image/webp']));
      return (
        <FormField label={field.label} {...common} hint={field.helpText ?? `Up to ${field.validation?.maxSizeMb ?? 5} MB.`} className={className}>
          <Input type="file" accept={accept.join(',')} onChange={(e) => onFile(e.target.files?.[0])} />
        </FormField>
      );
    }
    default: {
      const type = field.type === 'phone' ? 'tel' : field.type === 'text' ? 'text' : field.type;
      return (
        <FormField label={field.label} {...common} className={className}>
          <Input
            type={type}
            value={(value as string) ?? ''}
            placeholder={field.placeholder ?? undefined}
            autoComplete={field.type === 'email' ? 'email' : field.type === 'phone' ? 'tel' : field.key === 'name' ? 'name' : undefined}
            onChange={(e) => onChange(e.target.value)}
          />
        </FormField>
      );
    }
  }
}

declare global {
  interface Window {
    hcaptcha?: { render: (el: HTMLElement, opts: { sitekey: string; callback: (t: string) => void }) => unknown };
    turnstile?: { render: (el: HTMLElement, opts: { sitekey: string; callback: (t: string) => void }) => unknown };
  }
}

/** Loads the provider's widget script on demand (the site CSP must allow it; see docs/SEO_CRO.md). */
function CaptchaWidget({ provider, siteKey, onToken }: { provider: 'hcaptcha' | 'turnstile'; siteKey: string; onToken: (t: string) => void }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const src = provider === 'hcaptcha' ? 'https://js.hcaptcha.com/1/api.js?render=explicit' : 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
    const render = () => {
      const api = provider === 'hcaptcha' ? window.hcaptcha : window.turnstile;
      if (api && ref.current && ref.current.childElementCount === 0) api.render(ref.current, { sitekey: siteKey, callback: onToken });
    };
    if ((provider === 'hcaptcha' ? window.hcaptcha : window.turnstile) !== undefined) {
      render();
      return;
    }
    const script = document.createElement('script');
    script.src = src;
    script.async = true;
    script.onload = render;
    document.head.appendChild(script);
  }, [provider, siteKey, onToken]);
  return <div ref={ref} className="lp-form__captcha" aria-label="Verification challenge" role="group" />;
}
