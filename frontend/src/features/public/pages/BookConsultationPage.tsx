import { useQuery } from '@tanstack/react-query';
import { CalendarClock } from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';
import { Alert, Button, EmptyState, FormField, Skeleton, Textarea } from '@/components/ui';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import { browserTimeZone } from '@/lib/format/dates';
import { type Slots, useServices } from '../site/api';
import { PageHero } from '../site/components';
import { useSiteCopy } from '../site/copy';
import { useDocumentHead } from '../site/head';
import { ContactFields, type ContactValues, EMPTY_CONTACT, FormSuccess, ServicePicker, useLeadForm, validateContact } from './leadForm';

function dayKey(iso: string, timeZone: string): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(iso));
}

/** Slots grouped by the visitor's local day (in their own time zone). Pure; exported for tests. */
export function groupSlotsByDay(slots: string[], timeZone: string): { key: string; slots: string[] }[] {
  const days = new Map<string, string[]>();
  for (const slot of slots) {
    const key = dayKey(slot, timeZone);
    days.set(key, [...(days.get(key) ?? []), slot]);
  }
  return [...days.entries()].map(([key, list]) => ({ key, slots: list }));
}

/** /book-a-consultation — pick a free slot (shown in the visitor's time zone) and book a 30-minute call. */
export function BookConsultationPage() {
  const timeZone = browserTimeZone();
  const services = useServices();
  const slotsQuery = useQuery({ queryKey: ['public', 'slots'], queryFn: () => api.get<Slots>('/public/consultations/slots', { query: { days: 21 } }) });
  const form = useLeadForm<object>('/public/consultations');
  const [day, setDay] = useState<string | null>(null);
  const [slot, setSlot] = useState<string | null>(null);
  const [contact, setContact] = useState<ContactValues>(EMPTY_CONTACT);
  const [notes, setNotes] = useState('');
  const [slugs, setSlugs] = useState<string[]>([]);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const copy = useSiteCopy();
  useDocumentHead({ title: copy.text('booking.seo.title'), description: copy.text('booking.seo.description') });

  const days = useMemo(() => groupSlotsByDay(slotsQuery.data?.slots ?? [], timeZone), [slotsQuery.data, timeZone]);
  const activeDay = days.find((d) => d.key === day) ?? days[0];
  const fmtDay = (iso: string) => new Intl.DateTimeFormat(undefined, { timeZone, weekday: 'short', day: 'numeric', month: 'short' }).format(new Date(iso));
  const fmtTime = (iso: string) => new Intl.DateTimeFormat(undefined, { timeZone, hour: 'numeric', minute: '2-digit' }).format(new Date(iso));
  const slotTaken = isApiError(form.mutation.error) && form.mutation.error.code === 'website.slot_taken';

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next = validateContact(contact);
    if (!slot) next.slot = 'Pick a time for your call.';
    if (!form.consent) next.consent = 'Please tick the box so we can confirm your booking.';
    setErrors(next);
    if (Object.keys(next).length === 0)
      form.mutation.mutate(
        { ...contact, slotStart: slot, visitorTimeZone: timeZone, notes, serviceSlugs: slugs },
        {
          onError: (error) => {
            if (isApiError(error) && error.code === 'website.slot_taken') {
              setSlot(null);
              void slotsQuery.refetch();
            }
          },
        },
      );
  };
  const all = { ...form.serverErrors, ...errors };

  if (form.mutation.isSuccess) {
    const booked = form.mutation.data as unknown as { reference: string; slotStart: string };
    return (
      <>
        <PageHero eyebrow={copy.text('booking.hero.eyebrow')} title={copy.text('booking.success.hero')} breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Book a consultation' }]} />
        <div className="container site-form-layout">
          <FormSuccess title={copy.text('booking.success.title')} reference={booked.reference}>
            <p>
              Your call is on <strong>{fmtDay(booked.slotStart)}</strong> at <strong>{fmtTime(booked.slotStart)}</strong> ({timeZone}). We've
              emailed you the details.
            </p>
          </FormSuccess>
        </div>
      </>
    );
  }

  return (
    <>
      <PageHero
        eyebrow={copy.text('booking.hero.eyebrow')}
        title={copy.text('booking.hero.title')}
        lead={copy.text('booking.hero.lead')}
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Book a consultation' }]}
      />
      <div className="container site-form-layout">
        <form className="site-form" onSubmit={submit} noValidate aria-label="Book a consultation">
          <fieldset className="site-checkgroup">
            <legend>
              <CalendarClock aria-hidden="true" width={18} height={18} /> Choose a time <span className="text-muted">(times shown in {timeZone})</span>
            </legend>
            {slotsQuery.isLoading ? (
              <Skeleton height={120} />
            ) : days.length === 0 ? (
              <EmptyState compact title={copy.text('booking.empty.title')} headingLevel={3} description={copy.text('booking.empty.description')} />
            ) : (
              <>
                <div className="site-days" role="group" aria-label="Day">
                  {days.map((d) => (
                    <button
                      key={d.key}
                      type="button"
                      className="site-day"
                      aria-pressed={activeDay?.key === d.key}
                      onClick={() => {
                        setDay(d.key);
                        setSlot(null);
                      }}
                    >
                      {fmtDay(d.slots[0])}
                    </button>
                  ))}
                </div>
                <div className="site-slots" role="group" aria-label={`Times on ${activeDay ? fmtDay(activeDay.slots[0]) : ''}`}>
                  {(activeDay?.slots ?? []).map((s) => (
                    <button key={s} type="button" className="site-slot" aria-pressed={slot === s} onClick={() => setSlot(s)}>
                      {fmtTime(s)}
                    </button>
                  ))}
                </div>
              </>
            )}
            {all.slot && (
              <p className="site-field-error" role="alert">
                {all.slot}
              </p>
            )}
            {slotTaken && (
              <Alert tone="warning" title="That time was just taken">
                Please pick another slot — the list has been refreshed.
              </Alert>
            )}
          </fieldset>
          <ContactFields values={contact} onChange={setContact} errors={all} />
          <ServicePicker groups={services.data ?? []} selected={slugs} onChange={setSlugs} legend="What would you like to discuss? (optional)" />
          <FormField label="Anything we should know?" optional>
            <Textarea rows={3} maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} />
          </FormField>
          {form.consentField(errors.consent)}
          {!slotTaken && form.generalError}
          <Button type="submit" size="lg" variant="highlight" loading={form.mutation.isPending} disabled={form.token.isLoading}>
            {slot ? `Book ${fmtDay(slot)} at ${fmtTime(slot)}` : copy.text('booking.submit')}
          </Button>
        </form>
        <aside className="site-aside">
          <div className="site-hero__panel">
            <h2 className="public-footer__heading">{copy.text('booking.agenda.title')}</h2>
            <ul className="site-prose">
              {copy.list('booking.agenda.items').map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </div>
        </aside>
      </div>
    </>
  );
}
