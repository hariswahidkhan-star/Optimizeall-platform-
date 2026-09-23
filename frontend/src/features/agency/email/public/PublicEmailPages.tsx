import { useMutation, useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody } from '@/components/ui/Card';
import { Checkbox } from '@/components/ui/Checkbox';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { RadioGroup } from '@/components/ui/RadioGroup';
import { SkeletonText } from '@/components/ui/Skeleton';
import { Switch } from '@/components/ui/Switch';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import type { Preferences, SignupForm } from '../api/types';
import '../email.css';

const PUBLIC_API = '/public/email';
const anonymous = { skipRefresh: true } as const;

function PublicShell({ title, children }: { title: string; children: React.ReactNode }) {
  useEffect(() => {
    document.title = title;
  }, [title]);
  return (
    <div className="email-public">
      <Card>
        <CardBody className="stack">
          <h1 className="email-public__title">{title}</h1>
          {children}
        </CardBody>
      </Card>
    </div>
  );
}

function notFound(error: unknown) {
  return isApiError(error) && (error.status === 404 || error.status === 400);
}

/**
 * Unsubscribe confirmation. Opening the link never unsubscribes by itself (mail scanners follow links); the person
 * confirms with the button. Mail clients use the RFC 8058 one-click POST instead.
 */
export function UnsubscribePage() {
  const { token = '' } = useParams();
  const unsubscribe = useMutation({ mutationFn: () => api.post<void>(`${PUBLIC_API}/unsubscribe/${encodeURIComponent(token)}`, undefined, anonymous) });
  if (unsubscribe.isSuccess)
    return (
      <PublicShell title="You are unsubscribed">
        <p role="status">You will not receive marketing email from this sender any more. It can take up to 48 hours for scheduled messages to stop.</p>
        <p>
          Changed your mind or only want fewer emails? <a href={`/email/preferences/${token}`}>Manage your preferences</a>.
        </p>
      </PublicShell>
    );
  return (
    <PublicShell title="Unsubscribe">
      <p>Confirm that you no longer want to receive marketing email from this sender.</p>
      {unsubscribe.isError && (
        <Alert tone="danger">{notFound(unsubscribe.error) ? 'This link is invalid or has expired.' : errorMessage(unsubscribe.error)}</Alert>
      )}
      <div className="cluster">
        <Button onClick={() => unsubscribe.mutate()} loading={unsubscribe.isPending}>
          Unsubscribe
        </Button>
        <a href={`/email/preferences/${token}`}>Manage preferences instead</a>
      </div>
    </PublicShell>
  );
}

/** Preference center: topics (lists), frequency, or unsubscribe from everything. */
export function PreferencesPage() {
  const { token = '' } = useParams();
  const prefs = useQuery({
    queryKey: ['public-email', 'preferences', token],
    queryFn: ({ signal }) => api.get<Preferences>(`${PUBLIC_API}/preferences/${encodeURIComponent(token)}`, { signal, ...anonymous }),
    retry: false,
  });
  if (prefs.isPending)
    return (
      <PublicShell title="Email preferences">
        <SkeletonText lines={4} />
      </PublicShell>
    );
  if (prefs.isError)
    return (
      <PublicShell title="Email preferences">
        <Alert tone="danger">{notFound(prefs.error) ? 'This link is invalid or has expired.' : errorMessage(prefs.error)}</Alert>
      </PublicShell>
    );
  return <PreferencesForm token={token} initial={prefs.data} />;
}

function PreferencesForm({ token, initial }: { token: string; initial: Preferences }) {
  const [prefs, setPrefs] = useState(initial);
  const [topics, setTopics] = useState(() => Object.fromEntries(initial.topics.map((t) => [t.listId, t.subscribed])));
  const [frequency, setFrequency] = useState(initial.frequency);
  const save = useMutation({
    mutationFn: (unsubscribeAll: boolean) =>
      api.put<Preferences>(
        `${PUBLIC_API}/preferences/${encodeURIComponent(token)}`,
        { topics: Object.entries(topics).map(([listId, subscribed]) => ({ listId, subscribed })), frequency, unsubscribeAll },
        anonymous,
      ),
    onSuccess: (p) => {
      setPrefs(p);
      setTopics(Object.fromEntries(p.topics.map((t) => [t.listId, t.subscribed])));
      setFrequency(p.frequency);
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate(false);
  };
  return (
    <PublicShell title="Email preferences">
      <p>
        Emails from <strong>{prefs.workspace}</strong> to {prefs.maskedEmail}.
      </p>
      {prefs.unsubscribedFromAll && <Alert tone="info">You are unsubscribed from all marketing email. Tick a topic below to subscribe again.</Alert>}
      <form className="stack" onSubmit={submit}>
        <fieldset className="stack">
          <legend>Topics</legend>
          {prefs.topics.length === 0 && <p>There are no topics to choose from.</p>}
          {prefs.topics.map((t) => (
            <Switch
              key={t.listId}
              checked={!!topics[t.listId]}
              onCheckedChange={(v) => setTopics({ ...topics, [t.listId]: v })}
              label={t.name}
              description={t.description ?? undefined}
            />
          ))}
        </fieldset>
        <RadioGroup
          legend="How often"
          value={frequency}
          onChange={(v) => setFrequency(v as Preferences['frequency'])}
          options={[
            { value: 'Any', label: 'Every email' },
            { value: 'Weekly', label: 'At most one a week' },
            { value: 'Monthly', label: 'At most one a month' },
          ]}
        />
        {save.isError && <Alert tone="danger">{errorMessage(save.error)}</Alert>}
        {save.isSuccess && (
          <p role="status" className="email-muted">
            Your preferences were saved.
          </p>
        )}
        <div className="cluster">
          <Button type="submit" loading={save.isPending && save.variables === false}>
            Save preferences
          </Button>
          <Button variant="secondary" onClick={() => save.mutate(true)} loading={save.isPending && save.variables === true}>
            Unsubscribe from all
          </Button>
        </div>
      </form>
    </PublicShell>
  );
}

/** Double opt-in confirmation: confirms once with an explicit POST (the page itself makes it, not a GET link). */
export function ConfirmSubscriptionPage() {
  const { token = '' } = useParams();
  const started = useRef(false);
  const confirm = useMutation({
    mutationFn: () => api.post<{ list: string; workspace: string }>(`${PUBLIC_API}/confirm/${encodeURIComponent(token)}`, undefined, anonymous),
  });
  const { mutate } = confirm;
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    mutate();
  }, [mutate]);
  return (
    <PublicShell title="Confirm your subscription">
      {confirm.isPending && <p>Confirming…</p>}
      {confirm.isSuccess && (
        <p role="status">
          Thanks, you are subscribed to <strong>{confirm.data.list}</strong> from {confirm.data.workspace}.
        </p>
      )}
      {confirm.isError && <Alert tone="danger">{errorMessage(confirm.error)}</Alert>}
    </PublicShell>
  );
}

/** Hosted sign-up form with an explicit, unticked consent box and a honeypot field. */
export function SignupPage() {
  const { formKey = '' } = useParams();
  const form = useQuery({
    queryKey: ['public-email', 'form', formKey],
    queryFn: ({ signal }) => api.get<SignupForm>(`${PUBLIC_API}/forms/${encodeURIComponent(formKey)}`, { signal, ...anonymous }),
    retry: false,
  });
  const [email, setEmail] = useState('');
  const [firstName, setFirstName] = useState('');
  const [consent, setConsent] = useState(false);
  const [website, setWebsite] = useState('');
  const submit = useMutation({
    mutationFn: () => api.post<{ message: string }>(`${PUBLIC_API}/forms/${encodeURIComponent(formKey)}`, { email, firstName: firstName || null, consent, website }, anonymous),
  });
  if (form.isPending)
    return (
      <PublicShell title="Subscribe">
        <SkeletonText lines={4} />
      </PublicShell>
    );
  if (form.isError)
    return (
      <PublicShell title="Subscribe">
        <Alert tone="danger">{notFound(form.error) ? 'This sign-up form is not available.' : errorMessage(form.error)}</Alert>
      </PublicShell>
    );
  const f = form.data;
  if (submit.isSuccess)
    return (
      <PublicShell title={`Subscribe to ${f.listName}`}>
        <p role="status">{submit.data?.message ?? 'Thanks!'}</p>
      </PublicShell>
    );
  const errors = isApiError(submit.error) && submit.error.errors ? Object.values(submit.error.errors).flat() : submit.error ? [errorMessage(submit.error)] : [];
  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    submit.mutate();
  };
  return (
    <PublicShell title={`Subscribe to ${f.listName}`}>
      <p>News and offers from {f.workspace}.</p>
      <form className="stack" onSubmit={onSubmit}>
        <FormField label="Email" required>
          <Input type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} required maxLength={254} />
        </FormField>
        <FormField label="First name" optional>
          <Input autoComplete="given-name" value={firstName} onChange={(e) => setFirstName(e.target.value)} maxLength={100} />
        </FormField>
        <div className="email-hp" aria-hidden="true">
          <label>
            Website
            <input tabIndex={-1} autoComplete="off" value={website} onChange={(e) => setWebsite(e.target.value)} />
          </label>
        </div>
        <Checkbox checked={consent} onChange={(e) => setConsent(e.target.checked)} label={f.consentText} required />
        {f.doubleOptIn && <p className="email-muted">We will email you a link to confirm your subscription.</p>}
        {errors.length > 0 && <Alert tone="danger">{errors.join(' ')}</Alert>}
        <Button type="submit" loading={submit.isPending} disabled={!consent}>
          Subscribe
        </Button>
      </form>
    </PublicShell>
  );
}
