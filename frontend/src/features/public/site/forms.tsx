import { useQuery } from '@tanstack/react-query';
import { useCallback, type ChangeEvent } from 'react';
import { Link } from 'react-router-dom';
import { Checkbox } from '@/components/ui';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import type { FormToken } from './api';
import { getAttribution } from './attribution';

/**
 * Shared plumbing for the public lead forms: a signed form token (proves minimum fill time), a honeypot field,
 * versioned consent and campaign attribution. The server repeats every check.
 */

/** Fetches a fresh form token (and the budget/timeline options) when a form mounts. */
export function useFormToken() {
  const query = useQuery({
    queryKey: ['public', 'form-token'],
    queryFn: () => api.get<FormToken>('/public/forms/token'),
    staleTime: 60 * 60_000,
    gcTime: 0,
    refetchOnWindowFocus: false,
  });
  return query;
}

export interface PublicFormFields {
  nickname: string;
  consent: boolean;
}

/** Builds the request body every public form endpoint expects. */
export function withFormEnvelope<T extends object>(body: T, token: string | undefined, consentVersion: string, fields: PublicFormFields) {
  const attribution = getAttribution();
  return {
    ...body,
    nickname: fields.nickname,
    formToken: token ?? '',
    consent: fields.consent,
    consentVersion,
    utm: attribution.utm,
    referrer: attribution.referrer,
    landingPath: attribution.landingPath,
  };
}

/** Maps a problem response's field errors (camelCase keys) to a flat record; unknown fields go under `form`. */
export function fieldErrorsOf(error: unknown): Record<string, string> {
  if (!isApiError(error)) return {};
  const out: Record<string, string> = {};
  for (const [key, messages] of Object.entries(error.errors ?? {})) {
    if (messages?.[0]) out[key.split('.')[0].replace(/\[\d+\]$/, '')] = messages[0];
  }
  return out;
}

/** A visually hidden field that people never fill in (bots do). Not focusable and hidden from assistive tech. */
export function Honeypot({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return (
    <div className="site-hp" aria-hidden="true">
      <label>
        Nickname
        <input
          type="text"
          name="nickname"
          tabIndex={-1}
          autoComplete="off"
          value={value}
          onChange={(e: ChangeEvent<HTMLInputElement>) => onChange(e.target.value)}
        />
      </label>
    </div>
  );
}

export function ConsentCheckbox({
  text,
  checked,
  onChange,
  error,
  id,
}: {
  text: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  error?: string | null;
  id?: string;
}) {
  const handle = useCallback((e: ChangeEvent<HTMLInputElement>) => onChange(e.target.checked), [onChange]);
  return (
    <div className="site-consent-field">
      <Checkbox
        id={id}
        checked={checked}
        onChange={handle}
        invalid={!!error}
        aria-describedby={error && id ? `${id}-error` : undefined}
        label={text}
        description={
          <>
            Read our <Link to="/privacy-policy">privacy policy</Link>.
          </>
        }
      />
      {error && (
        <p className="site-field-error" id={id ? `${id}-error` : undefined} role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
