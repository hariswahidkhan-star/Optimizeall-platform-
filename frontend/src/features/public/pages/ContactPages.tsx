import { Clock, Mail, MapPin, MessageCircle, Phone } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Button, FormField, Select, Textarea } from '@/components/ui';
import { usePage, useServices, useSite } from '../site/api';
import { Blocks } from '../site/Blocks';
import { useSiteCopy } from '../site/copy';
import { PageHero } from '../site/components';
import { useDocumentHead } from '../site/head';
import { ContactFields, type ContactValues, EMPTY_CONTACT, FormSuccess, ServicePicker, useLeadForm, validateContact } from './leadForm';

function ContactDetails() {
  const { data: site } = useSite();
  const copy = useSiteCopy();
  const c = site?.contact;
  if (!c) return null;
  return (
    <div className="site-hero__panel">
      <h2 className="public-footer__heading">{copy.text('contact.details.title')}</h2>
      <ul className="site-checklist">
        {c.email && (
          <li>
            <Mail aria-hidden="true" /> <a href={`mailto:${c.email}`}>{c.email}</a>
          </li>
        )}
        {c.phone && (
          <li>
            <Phone aria-hidden="true" /> <a href={`tel:${c.phone.replace(/[^\d+]/g, '')}`}>{c.phone}</a>
          </li>
        )}
        {c.whatsApp && (
          <li>
            <MessageCircle aria-hidden="true" />{' '}
            <a href={`https://wa.me/${c.whatsApp.replace(/\D/g, '')}`} target="_blank" rel="noopener noreferrer">
              WhatsApp us<span className="visually-hidden"> (opens in a new tab)</span>
            </a>
          </li>
        )}
        {c.address && (
          <li>
            <MapPin aria-hidden="true" /> {c.address}
          </li>
        )}
        {c.hours && (
          <li>
            <Clock aria-hidden="true" /> {c.hours}
          </li>
        )}
      </ul>
    </div>
  );
}

/** /contact */
export function ContactPage() {
  const services = useServices();
  const page = usePage('contact');
  const form = useLeadForm<object>('/public/inquiries/contact');
  const [contact, setContact] = useState<ContactValues>(EMPTY_CONTACT);
  const [message, setMessage] = useState('');
  const [slugs, setSlugs] = useState<string[]>([]);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const copy = useSiteCopy();
  useDocumentHead({ title: copy.text('contact.seo.title'), description: copy.text('contact.seo.description') });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next = validateContact(contact);
    if (message.trim().length < 10) next.message = 'Tell us a little more (at least 10 characters).';
    if (!form.consent) next.consent = 'Please tick the box so we can reply.';
    setErrors(next);
    if (Object.keys(next).length === 0) form.mutation.mutate({ ...contact, message, serviceSlugs: slugs });
  };
  const all = { ...form.serverErrors, ...errors };

  return (
    <>
      <PageHero eyebrow={copy.text('contact.hero.eyebrow')} title={copy.text('contact.hero.title')} breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Contact' }]} />
      <div className="container site-form-layout">
        {form.mutation.isSuccess ? (
          <FormSuccess title={copy.text('contact.success.title')} reference={form.mutation.data.reference}>
            <p>{form.mutation.data.message}</p>
          </FormSuccess>
        ) : (
          <form className="site-form" onSubmit={submit} noValidate aria-label="Contact form">
            <ContactFields values={contact} onChange={setContact} errors={all} />
            <FormField label="How can we help?" required error={all.message}>
              <Textarea rows={5} maxLength={5000} value={message} onChange={(e) => setMessage(e.target.value)} />
            </FormField>
            <ServicePicker groups={services.data ?? []} selected={slugs} onChange={setSlugs} legend="Services you're interested in (optional)" error={all.serviceSlugs} />
            {form.consentField(errors.consent)}
            {form.generalError}
            <Button type="submit" size="lg" loading={form.mutation.isPending} disabled={form.token.isLoading}>
              {copy.text('contact.submit')}
            </Button>
          </form>
        )}
        <aside className="site-aside">
          {page.data && <Blocks blocks={page.data.blocks.filter((b) => b.type === 'richText')} />}
          <ContactDetails />
        </aside>
      </div>
    </>
  );
}

/** /free-audit — free marketing audit request. */
export function FreeAuditPage() {
  const services = useServices();
  const form = useLeadForm<object>('/public/inquiries/audit');
  const [contact, setContact] = useState<ContactValues>(EMPTY_CONTACT);
  const [goals, setGoals] = useState('');
  const [budget, setBudget] = useState('');
  const [competitors, setCompetitors] = useState('');
  const [slugs, setSlugs] = useState<string[]>([]);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const copy = useSiteCopy();
  useDocumentHead({ title: copy.text('audit.seo.title'), description: copy.text('audit.seo.description') });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next = validateContact(contact, true);
    if (goals.trim().length < 5) next.goals = 'Tell us what you want to achieve.';
    if (!budget) next.budgetRange = 'Pick a budget range.';
    if (slugs.length === 0) next.serviceSlugs = 'Pick at least one area to audit.';
    if (!form.consent) next.consent = 'Please tick the box so we can send your audit.';
    setErrors(next);
    if (Object.keys(next).length === 0)
      form.mutation.mutate({ ...contact, goals, budgetRange: budget, competitors, serviceSlugs: slugs });
  };
  const all = { ...form.serverErrors, ...errors };

  return (
    <>
      <PageHero
        eyebrow={copy.text('audit.hero.eyebrow')}
        title={copy.text('audit.hero.title')}
        lead={copy.text('audit.hero.lead')}
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Free audit' }]}
      />
      <div className="container site-form-layout">
        {form.mutation.isSuccess ? (
          <FormSuccess title={copy.text('audit.success.title')} reference={form.mutation.data.reference}>
            <p>{copy.text('audit.success.text')}</p>
          </FormSuccess>
        ) : (
          <form className="site-form" onSubmit={submit} noValidate aria-label="Free audit request">
            <ContactFields values={contact} onChange={setContact} errors={all} websiteRequired />
            <FormField label="What are your goals?" required error={all.goals} hint="E.g. more qualified leads, lower cost per sale, rank for key terms.">
              <Textarea rows={4} maxLength={2000} value={goals} onChange={(e) => setGoals(e.target.value)} />
            </FormField>
            <FormField label="Monthly marketing budget" required error={all.budgetRange}>
              <Select value={budget} onChange={(e) => setBudget(e.target.value)} placeholder="Choose a range" options={form.budgetRanges} />
            </FormField>
            <ServicePicker groups={services.data ?? []} selected={slugs} onChange={setSlugs} legend="What should we look at?" error={all.serviceSlugs} />
            <FormField label="Main competitors" optional error={all.competitors}>
              <Textarea rows={2} maxLength={500} value={competitors} onChange={(e) => setCompetitors(e.target.value)} />
            </FormField>
            {form.consentField(errors.consent)}
            {form.generalError}
            <Button type="submit" size="lg" variant="highlight" loading={form.mutation.isPending} disabled={form.token.isLoading}>
              {copy.text('audit.submit')}
            </Button>
          </form>
        )}
        <aside className="site-aside">
          <div className="site-hero__panel">
            <h2 className="public-footer__heading">{copy.text('audit.included.title')}</h2>
            <ul className="site-checklist">
              {copy.list('audit.included.items').map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </div>
        </aside>
      </div>
    </>
  );
}
