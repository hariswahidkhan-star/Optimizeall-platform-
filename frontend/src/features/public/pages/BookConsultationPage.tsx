import { useQuery } from '@tanstack/react-query';
import { CalendarClock } from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';
import { Alert, Button, EmptyState, FormField, Skeleton, Textarea } from '@/components/ui';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import { browserTimeZone } from '@/lib/format/dates';
import { type Slots, useServices } from '../site/api';
import { PageHero } from '../site/components';
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
  useDocumentHead({ title: 'Book a consultation', description: 'Book a free 30-minute strategy call with an Optimize All strategist at a time that suits you.' });

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
        <PageHero eyebrow="Book a consultation" title="You're booked in" breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Book a consultation' }]} />
        <div className="container site-form-layout">
          <FormSuccess title="See you soon" reference={booked.reference}>
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
        eyebrow="Book a consultation"
        title="Book a free 30-minute strategy call"
        lead="Talk through your goals with a senior strategist. No sales pitch — just an honest view of where your growth can come from."
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
              <EmptyState compact title="No free slots right now" headingLevel={3} description="Please use the contact form and we'll find a time." />
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
            {slot ? `Book ${fmtDay(slot)} at ${fmtTime(slot)}` : 'Book my call'}
          </Button>
        </form>
        <aside className="site-aside">
          <div className="site-hero__panel">
            <h2 className="public-footer__heading">On the call</h2>
            <ul className="site-prose">
              <li>Your goals and current marketing</li>
              <li>Quick wins we can see straight away</li>
              <li>Whether we're the right fit — honestly</li>
            </ul>
          </div>
        </aside>
      </div>
    </>
  );
}
