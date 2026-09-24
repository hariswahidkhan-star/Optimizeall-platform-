import { useMutation } from '@tanstack/react-query';
import { Briefcase, Clock, MapPin } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Alert, Button, EmptyState, FormField, Input, Textarea } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { formatMoney } from '@/lib/format/money';
import { initials } from '@/lib/format/text';
import { isExternalHref } from '@/lib/safeHref';
import { type EmploymentType, type PublicJob, useJob, useJobs, useSite, useTeam, type WorkplaceType } from '../site/api';
import { useSiteCopy } from '../site/copy';
import { CtaBand, formatPublished, PageHero, PublicQueryState, Section } from '../site/components';
import { ConsentCheckbox, fieldErrorsOf, Honeypot, useFormToken, useRenewFormToken } from '../site/forms';
import { headFromSeo, useDocumentHead } from '../site/head';
import { Markdown } from '../site/Markdown';

/** /team */
export function TeamPage() {
  const { data, isLoading, error } = useTeam();
  const copy = useSiteCopy();
  useDocumentHead({ title: copy.text('team.seo.title'), description: copy.text('team.seo.description') });
  return (
    <>
      <PageHero
        eyebrow={copy.text('team.hero.eyebrow')}
        title={copy.text('team.hero.title')}
        lead={copy.text('team.hero.lead')}
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Team' }]}
      />
      <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="Team unavailable">
        <div className="site-section">
          <div className="container">
            <ul className="site-team">
              {(data ?? []).map((m) => (
                <li key={m.slug}>
                  {m.photoUrl ? (
                    <img src={m.photoUrl} alt="" loading="lazy" decoding="async" width={320} height={320} />
                  ) : (
                    <span className="site-team__placeholder" aria-hidden="true">
                      {initials(m.name)}
                    </span>
                  )}
                  <h2>{m.name}</h2>
                  <p className="text-muted">{m.role}</p>
                  {m.bio && <p className="text-small">{m.bio}</p>}
                  {m.expertise.length > 0 && (
                    <ul className="site-chips" aria-label={`${m.name}'s expertise`}>
                      {m.expertise.map((x) => (
                        <li key={x} className="site-chip">
                          {x}
                        </li>
                      ))}
                    </ul>
                  )}
                  {m.socialLinks.filter((l) => isExternalHref(l.url)).map((l) => (
                    <a key={l.url} href={l.url} target="_blank" rel="noopener noreferrer" className="text-small">
                      {l.label}
                      <span className="visually-hidden"> profile of {m.name} (opens in a new tab)</span>
                    </a>
                  ))}
                </li>
              ))}
            </ul>
          </div>
        </div>
      </PublicQueryState>
      <Section title={copy.text('team.join.title')} tone="muted">
        <p>
          {copy.text('team.join.text')} <Link to="/careers">{copy.text('team.join.link')}</Link>.
        </p>
      </Section>
    </>
  );
}

export const WORKPLACE_LABEL: Record<WorkplaceType, string> = { OnSite: 'On-site', Hybrid: 'Hybrid', Remote: 'Remote' };
export const EMPLOYMENT_LABEL: Record<EmploymentType, string> = {
  FullTime: 'Full-time',
  PartTime: 'Part-time',
  Contract: 'Contract',
  Internship: 'Internship',
  Temporary: 'Temporary',
};

/** /careers */
export function CareersPage() {
  const { data, isLoading, error } = useJobs();
  const copy = useSiteCopy();
  useDocumentHead({ title: copy.text('careers.seo.title'), description: copy.text('careers.seo.description') });
  return (
    <>
      <PageHero
        eyebrow={copy.text('careers.hero.eyebrow')}
        title={copy.text('careers.hero.title')}
        lead={copy.text('careers.hero.lead')}
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Careers' }]}
      />
      <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="Careers unavailable">
        <Section title={copy.text('careers.openRoles')}>
          {data && data.length === 0 ? (
            <EmptyState title={copy.text('careers.empty.title')} headingLevel={3} description={copy.text('careers.empty.description')} />
          ) : (
            <ul className="site-grid site-grid--2">
              {(data ?? []).map((job) => (
                <li key={job.slug}>
                  <article className="site-card">
                    <p className="site-card__eyebrow">{job.department}</p>
                    <h3 className="site-card__title">
                      <Link to={`/careers/${job.slug}`} className="site-card__link">
                        {job.title}
                      </Link>
                    </h3>
                    <p className="site-card__text">{job.summary}</p>
                    <p className="site-card__meta">
                      {job.location} · {WORKPLACE_LABEL[job.workplace]} · {EMPLOYMENT_LABEL[job.employmentType]}
                    </p>
                  </article>
                </li>
              ))}
            </ul>
          )}
        </Section>
      </PublicQueryState>
    </>
  );
}

function salaryText(job: PublicJob): string | null {
  const s = job.salary;
  if (!s) return null;
  const fmt = (n: number) => formatMoney(n, s.currency, { currencyDisplay: 'narrowSymbol' }).replace(/\.00$/, '');
  const range = s.min !== null && s.max !== null ? `${fmt(s.min)} – ${fmt(s.max)}` : s.min !== null ? `From ${fmt(s.min)}` : s.max !== null ? `Up to ${fmt(s.max)}` : null;
  return range ? `${range} per ${s.period.toLowerCase()}` : null;
}

/** /careers/:slug — role details and the application form (PDF CV). */
export function JobDetailPage() {
  const { slug = '' } = useParams();
  const { data: job, isLoading, error } = useJob(slug);
  const copy = useSiteCopy();
  useDocumentHead(job ? headFromSeo(job.seo, job.jsonLd) : { title: 'Careers' });
  return (
    <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="This role is no longer open">
      {job && (
        <>
          <PageHero
            eyebrow={job.department}
            title={job.title}
            lead={job.summary}
            breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Careers', to: '/careers' }, { label: job.title }]}
          >
            <div className="site-hero__panel">
              <ul className="site-checklist">
                <li>
                  <MapPin aria-hidden="true" /> {job.location} ({WORKPLACE_LABEL[job.workplace]})
                </li>
                <li>
                  <Briefcase aria-hidden="true" /> {EMPLOYMENT_LABEL[job.employmentType]}
                </li>
                {salaryText(job) && (
                  <li>
                    <Clock aria-hidden="true" /> {salaryText(job)}
                  </li>
                )}
              </ul>
              {job.postedAt && <p className="text-small text-muted">Posted {formatPublished(job.postedAt)}</p>}
            </div>
          </PageHero>
          <div className="container site-form-layout">
            <div>
              <Markdown source={job.descriptionMarkdown} />
              {job.requirements.length > 0 && (
                <>
                  <h2 className="site-subheading">{copy.text('careers.detail.requirementsTitle')}</h2>
                  <ul className="site-prose">
                    {job.requirements.map((r) => (
                      <li key={r}>{r}</li>
                    ))}
                  </ul>
                </>
              )}
              {job.benefits.length > 0 && (
                <>
                  <h2 className="site-subheading">{copy.text('careers.detail.benefitsTitle')}</h2>
                  <ul className="site-prose">
                    {job.benefits.map((b) => (
                      <li key={b}>{b}</li>
                    ))}
                  </ul>
                </>
              )}
            </div>
            <ApplicationForm job={job} />
          </div>
        </>
      )}
    </PublicQueryState>
  );
}

const PDF_TYPES = ['application/pdf'];

function ApplicationForm({ job }: { job: PublicJob }) {
  const { data: site } = useSite();
  const copy = useSiteCopy();
  const token = useFormToken();
  const renewToken = useRenewFormToken();
  const id = useId();
  const [values, setValues] = useState({ name: '', email: '', phone: '', portfolioUrl: '', coverLetter: '' });
  const [cv, setCv] = useState<File | null>(null);
  const [consent, setConsent] = useState(false);
  const [nickname, setNickname] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const set = (key: keyof typeof values) => (value: string) => setValues((v) => ({ ...v, [key]: value }));

  const apply = useMutation({
    mutationFn: () => {
      const form = new FormData();
      for (const [k, v] of Object.entries(values)) if (v.trim()) form.append(k, v.trim());
      form.append('nickname', nickname);
      form.append('formToken', token.data?.token ?? '');
      form.append('consent', String(consent));
      form.append('consentVersion', site?.consent.careersVersion ?? 'careers-2026-09');
      if (cv) form.append('cv', cv);
      return api.upload<{ reference: string; message: string }>(`/public/careers/${encodeURIComponent(job.slug)}/applications`, form);
    },
    onSuccess: () => void renewToken(),
    onError: (e) => setErrors(fieldErrorsOf(e)),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next: Record<string, string> = {};
    if (values.name.trim().length < 2) next.name = 'Enter your name.';
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(values.email.trim())) next.email = 'Enter a valid email address.';
    if (values.portfolioUrl.trim() && !values.portfolioUrl.trim().startsWith('https://')) next.portfolioUrl = 'Use an https:// link.';
    if (!cv) next.cv = 'Attach your CV as a PDF.';
    if (!consent) next.consent = 'Please agree so we can process your application.';
    setErrors(next);
    if (Object.keys(next).length === 0) apply.mutate();
  };

  if (apply.isSuccess)
    return (
      <Alert tone="success" title="Application received">
        {apply.data.message} Reference {apply.data.reference}.
      </Alert>
    );

  return (
    <form className="site-form" onSubmit={submit} noValidate aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`} className="site-section__title">
        {copy.text('careers.detail.applyTitle')}
      </h2>
      <FormField label="Full name" required error={errors.name}>
        <Input autoComplete="name" value={values.name} onChange={(e) => set('name')(e.target.value)} />
      </FormField>
      <FormField label="Email" required error={errors.email}>
        <Input type="email" autoComplete="email" value={values.email} onChange={(e) => set('email')(e.target.value)} />
      </FormField>
      <FormField label="Phone" optional error={errors.phone}>
        <Input type="tel" autoComplete="tel" value={values.phone} onChange={(e) => set('phone')(e.target.value)} />
      </FormField>
      <FormField label="Portfolio or LinkedIn" optional hint="An https:// link." error={errors.portfolioUrl}>
        <Input type="url" value={values.portfolioUrl} onChange={(e) => set('portfolioUrl')(e.target.value)} />
      </FormField>
      <FormField label="CV" required hint="PDF only, up to 5 MB." error={errors.cv}>
        <input
          type="file"
          className="ui-input site-file"
          accept={PDF_TYPES.join(',')}
          onChange={(e) => {
            const file = e.target.files?.[0] ?? null;
            if (file && (file.type !== 'application/pdf' || file.size > 5 * 1024 * 1024)) {
              setErrors((x) => ({ ...x, cv: file.size > 5 * 1024 * 1024 ? 'Your CV must be 5 MB or smaller.' : 'Upload your CV as a PDF file.' }));
              setCv(null);
              e.target.value = '';
              return;
            }
            setErrors(({ cv: _ignored, ...rest }) => rest);
            setCv(file);
          }}
        />
      </FormField>
      <FormField label="Cover letter" optional error={errors.coverLetter}>
        <Textarea rows={5} maxLength={5000} value={values.coverLetter} onChange={(e) => set('coverLetter')(e.target.value)} />
      </FormField>
      <Honeypot value={nickname} onChange={setNickname} />
      <ConsentCheckbox
        id={`${id}-consent`}
        text={site?.consent.careersText ?? 'I agree that Optimize All may store my application to assess me for this role.'}
        checked={consent}
        onChange={setConsent}
        error={errors.consent}
      />
      {apply.isError && !Object.keys(errors).length && (
        <Alert tone="danger" title="We couldn't send your application">
          {errorMessage(apply.error)}
        </Alert>
      )}
      <Button type="submit" loading={apply.isPending} disabled={token.isLoading}>
        Send application
      </Button>
    </form>
  );
}

export function CareersCta() {
  const copy = useSiteCopy();
  return <CtaBand title={copy.text('careers.cta.title')} text={copy.text('careers.cta.text')} />;
}
