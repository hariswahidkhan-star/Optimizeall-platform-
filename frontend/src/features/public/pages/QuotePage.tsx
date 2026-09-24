import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Button, Checkbox, FormField, RadioGroup, Select, Textarea } from '@/components/ui';
import { formatMoney } from '@/lib/format/money';
import { usePricing, useServices } from '../site/api';
import { PageHero, PERIOD_SUFFIX } from '../site/components';
import { useSiteCopy } from '../site/copy';
import { useDocumentHead } from '../site/head';
import { ContactFields, type ContactValues, EMPTY_CONTACT, FormSuccess, ServicePicker, useLeadForm, validateContact } from './leadForm';

const STEPS = ['Services', 'Project', 'Your details'] as const;

/** /get-a-quote — three-step quote request: services & packages → budget & timeline → contact details. */
export function QuotePage() {
  const [params] = useSearchParams();
  const services = useServices();
  const pricing = usePricing();
  const form = useLeadForm<object>('/public/inquiries/quote');
  const [step, setStep] = useState(0);
  const [slugs, setSlugs] = useState<string[]>(() => (params.get('service') ? [params.get('service')!] : []));
  const [packageIds, setPackageIds] = useState<string[]>(() => (params.get('package') ? [params.get('package')!] : []));
  const [budget, setBudget] = useState('');
  const [timeline, setTimeline] = useState('');
  const [message, setMessage] = useState('');
  const [contact, setContact] = useState<ContactValues>(EMPTY_CONTACT);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const headingRef = useRef<HTMLHeadingElement>(null);
  const firstRender = useRef(true);
  const copy = useSiteCopy();
  useDocumentHead({ title: copy.text('quote.seo.title'), description: copy.text('quote.seo.description') });

  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false;
      return;
    }
    headingRef.current?.focus();
  }, [step]);

  // A preselected package implies its service.
  useEffect(() => {
    const pkg = params.get('package');
    if (!pkg || !pricing.data) return;
    const owner = pricing.data.services.find((s) => s.packages.some((p) => p.id === pkg));
    if (owner) setSlugs((current) => (current.includes(owner.service.slug) ? current : [...current, owner.service.slug]));
  }, [params, pricing.data]);

  const packagesForSelection = useMemo(
    () => (pricing.data?.services ?? []).filter((s) => slugs.includes(s.service.slug)),
    [pricing.data, slugs],
  );

  const validate = (index: number): Record<string, string> => {
    const next: Record<string, string> = {};
    if (index === 0 && slugs.length === 0 && packageIds.length === 0) next.serviceSlugs = 'Pick at least one service.';
    if (index === 1) {
      if (!budget) next.budgetRange = 'Pick a budget range.';
      if (!timeline) next.timeline = 'Pick a timeline.';
      if (message.trim().length < 10) next.message = 'Describe your project in a sentence or two (at least 10 characters).';
    }
    if (index === 2) {
      Object.assign(next, validateContact(contact));
      if (!form.consent) next.consent = 'Please tick the box so we can prepare your quote.';
    }
    return next;
  };

  const next = () => {
    const e = validate(step);
    setErrors(e);
    if (Object.keys(e).length === 0) setStep((s) => Math.min(s + 1, STEPS.length - 1));
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (step < STEPS.length - 1) {
      next();
      return;
    }
    const all = { ...validate(0), ...validate(1), ...validate(2) };
    setErrors(all);
    if (Object.keys(all).length > 0) {
      if (all.serviceSlugs) setStep(0);
      else if (all.budgetRange || all.timeline || all.message) setStep(1);
      return;
    }
    // Only send packages that belong to a selected service.
    const validPackages = packageIds.filter((id) => packagesForSelection.some((s) => s.packages.some((p) => p.id === id)));
    form.mutation.mutate({ ...contact, serviceSlugs: slugs, packageIds: validPackages, budgetRange: budget, timeline, message });
  };

  const all = { ...form.serverErrors, ...errors };

  return (
    <>
      <PageHero
        eyebrow={copy.text('quote.hero.eyebrow')}
        title={copy.text('quote.hero.title')}
        lead={copy.text('quote.hero.lead')}
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Get a quote' }]}
      />
      <div className="container site-form-layout">
        {form.mutation.isSuccess ? (
          <FormSuccess title={copy.text('quote.success.title')} reference={form.mutation.data.reference}>
            <p>{copy.text('quote.success.text')}</p>
          </FormSuccess>
        ) : (
          <form className="site-form" onSubmit={submit} noValidate aria-labelledby="quote-step-title">
            <ol className="site-wizard" aria-label="Quote steps">
              {STEPS.map((label, i) => (
                <li key={label} aria-current={i === step ? 'step' : undefined} className={i < step ? 'is-done' : i === step ? 'is-current' : undefined}>
                  <span className="tabular">{i + 1}</span> {label}
                </li>
              ))}
            </ol>
            <h2 id="quote-step-title" ref={headingRef} tabIndex={-1} className="site-section__title">
              Step {step + 1} of {STEPS.length}: {STEPS[step]}
            </h2>

            {step === 0 && (
              <>
                <ServicePicker groups={services.data ?? []} selected={slugs} onChange={setSlugs} legend="Which services do you need?" error={all.serviceSlugs} />
                {packagesForSelection.length > 0 && (
                  <fieldset className="site-checkgroup">
                    <legend>Interested in a specific package? (optional)</legend>
                    <div className="site-checkgroup__grid">
                      {packagesForSelection.flatMap(({ service, packages }) =>
                        packages.map((p) => (
                          <Checkbox
                            key={p.id}
                            label={`${service.name} — ${p.name}`}
                            description={
                              p.isCustomQuote || p.price === null
                                ? 'Custom quote'
                                : `${formatMoney(p.price, p.currency, { currencyDisplay: 'narrowSymbol' })} ${PERIOD_SUFFIX[p.billingPeriod]}`
                            }
                            checked={packageIds.includes(p.id)}
                            onChange={(e) => setPackageIds((ids) => (e.target.checked ? [...ids, p.id] : ids.filter((x) => x !== p.id)))}
                          />
                        )),
                      )}
                    </div>
                  </fieldset>
                )}
              </>
            )}

            {step === 1 && (
              <>
                <FormField label="Monthly budget" required error={all.budgetRange}>
                  <Select value={budget} onChange={(e) => setBudget(e.target.value)} placeholder="Choose a range" options={form.budgetRanges} />
                </FormField>
                <RadioGroup
                  legend="When would you like to start?"
                  value={timeline || null}
                  onChange={setTimeline}
                  options={form.timelines.map((t) => ({ value: t.value, label: t.label }))}
                  error={all.timeline}
                  required
                />
                <FormField label="Project details" required error={all.message} hint="Goals, current situation, anything we should know.">
                  <Textarea rows={5} maxLength={5000} value={message} onChange={(e) => setMessage(e.target.value)} />
                </FormField>
              </>
            )}

            {step === 2 && (
              <>
                <ContactFields values={contact} onChange={setContact} errors={all} />
                {form.consentField(errors.consent)}
                {form.generalError}
              </>
            )}

            <div className="site-form__actions">
              {step > 0 ? (
                <Button type="button" variant="secondary" onClick={() => setStep((s) => s - 1)}>
                  Back
                </Button>
              ) : (
                <span />
              )}
              {step < STEPS.length - 1 ? (
                <Button type="button" onClick={next}>
                  Next
                </Button>
              ) : (
                <Button type="submit" variant="highlight" loading={form.mutation.isPending} disabled={form.token.isLoading}>
                  {copy.text('quote.submit')}
                </Button>
              )}
            </div>
          </form>
        )}
        <aside className="site-aside">
          <div className="site-hero__panel">
            <h2 className="public-footer__heading">{copy.text('quote.next.title')}</h2>
            <ol className="site-prose">
              {copy.list('quote.next.items').map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ol>
          </div>
        </aside>
      </div>
    </>
  );
}
