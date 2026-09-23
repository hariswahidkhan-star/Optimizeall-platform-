import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { Outlet } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { BookConsultationPage, groupSlotsByDay } from './pages/BookConsultationPage';
import { HomePage } from './pages/HomePage';
import { NewsletterConfirmPage, NewsletterUnsubscribePage } from './pages/MiscPages';
import { QuotePage } from './pages/QuotePage';
import { ServiceDetailPage } from './pages/ServicePages';
import { clearConsent, resetConsentCache } from './site/consent';
import { SiteChrome } from './site/SiteChrome';
import * as fx from './testFixtures';
import type { PublicSite } from './site/api';

type Routes = Parameters<typeof mockFetch>[0];

function baseRoutes(site: PublicSite = fx.site): Routes {
  return {
    'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired'),
    'GET /public/site': () => json(200, site),
    'GET /public/home': () => json(200, fx.home),
    'GET /public/services': () => json(200, fx.serviceGroups),
    'GET /public/services/seo': () => json(200, fx.service),
    'GET /public/pricing': () => json(200, fx.pricing),
    'GET /public/forms/token': () => json(200, fx.formToken),
    'GET /public/consultations/slots': () => json(200, fx.slots),
  };
}

/** Renders a public page inside the real site chrome (header, footer, consent banner). */
function renderPublic(page: ReactElement, { route = '/', path = '/', routes = {} as Routes, site = fx.site } = {}) {
  const mock = mockFetch({ ...baseRoutes(site), ...routes });
  const result = renderWithApp(<p>unused</p>, {
    route,
    path: '/__unused',
    routes: [
      {
        element: (
          <SiteChrome>
            <Outlet />
          </SiteChrome>
        ),
        children: [{ path, element: page }],
      },
    ],
  });
  return { ...result, ...mock };
}

function removeTags() {
  document.head.querySelectorAll('script').forEach((s) => s.remove());
  const w = window as unknown as Record<string, unknown>;
  delete w.gtag;
  delete w.dataLayer;
  delete w.fbq;
  delete w._fbq;
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  resetConsentCache();
  removeTags();
});

afterEach(() => {
  clearConsent();
  removeTags();
});

describe('HomePage', () => {
  it('renders services, labelled stats, case studies and pricing from the API', async () => {
    renderPublic(<HomePage />);
    expect(screen.getByRole('heading', { level: 1, name: /Marketing that grows revenue/ })).toBeInTheDocument();
    const services = (await screen.findByRole('heading', { name: 'Every channel, one accountable team' })).closest('section')!;
    expect(await within(services).findByRole('heading', { name: 'Search' })).toBeInTheDocument();
    expect(within(services).getByRole('link', { name: 'Search engine optimization' })).toHaveAttribute('href', '/services/seo');

    expect(screen.getByText('$48M')).toBeInTheDocument();
    expect(screen.getAllByText(/Estimated/).length).toBeGreaterThan(0);
    expect(screen.getByText('Tripling organic leads for Northwind')).toBeInTheDocument();
    expect(screen.getByText('They doubled our pipeline in two quarters.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Healthcare/ })).toHaveAttribute('href', '/industries/healthcare');
    expect(screen.getAllByText('Growth').length).toBeGreaterThan(0);
    await waitFor(() => expect(document.head.querySelector('script[type="application/ld+json"]')?.textContent).toContain('"Organization"'));
  });

  it('has no axe violations', async () => {
    const { container } = renderPublic(<HomePage />);
    await screen.findByText('Tripling organic leads for Northwind');
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('ServiceDetailPage', () => {
  it('writes Service and FAQPage JSON-LD as inert text, plus title and canonical', async () => {
    const { container } = renderPublic(<ServiceDetailPage />, { route: '/services/seo', path: '/services/:slug' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Search engine optimization' })).toBeInTheDocument();

    await waitFor(() => expect(document.head.querySelectorAll('script[type="application/ld+json"]')).toHaveLength(2));
    const blocks = Array.from(document.head.querySelectorAll('script[type="application/ld+json"]')).map((s) => JSON.parse(s.textContent ?? '') as Record<string, unknown>);
    expect(blocks.map((b) => b['@type'])).toEqual(['Service', 'FAQPage']);
    expect(blocks[0]).toMatchObject({ name: 'Search engine optimization', offers: [{ price: 1200, priceCurrency: 'USD' }] });
    // The hostile answer stays a JSON string: nothing was parsed into elements.
    expect(document.head.querySelector('img')).toBeNull();
    expect(JSON.stringify(blocks[1])).toContain('onerror');

    await waitFor(() => expect(document.title).toBe('SEO services | Optimize All'));
    expect(document.head.querySelector('link[rel="canonical"]')?.getAttribute('href')).toBe('https://www.optimizeall.com/services/seo');
    expect(document.head.querySelector('meta[name="description"]')?.getAttribute('content')).toBe('Technical SEO, content and links.');
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('QuotePage', () => {
  it('validates each step before moving on and submits the whole request', async () => {
    const user = userEvent.setup();
    const { calls } = renderPublic(<QuotePage />, {
      route: '/get-a-quote',
      path: '/get-a-quote',
      routes: { 'POST /public/inquiries/quote': () => json(202, { reference: 'Q-1001', message: 'Thanks' }) },
    });
    expect(await screen.findByRole('heading', { name: 'Step 1 of 3: Services' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByText('Pick at least one service.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Step 1 of 3: Services' })).toBeInTheDocument();

    await user.click(await screen.findByRole('checkbox', { name: 'Search engine optimization' }));
    await user.click(await screen.findByRole('checkbox', { name: /Search engine optimization — Growth/ }));
    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByRole('heading', { name: 'Step 2 of 3: Project' })).toHaveFocus();

    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByText('Pick a budget range.')).toBeInTheDocument();
    expect(screen.getByText('Pick a timeline.')).toBeInTheDocument();
    expect(screen.getByText(/Describe your project/)).toBeInTheDocument();

    await user.selectOptions(screen.getByRole('combobox', { name: /Monthly budget/ }), '2k-5k');
    await user.click(screen.getByRole('radio', { name: 'In 1–3 months' }));
    await user.type(screen.getByRole('textbox', { name: /Project details/ }), 'We need a steady flow of local leads.');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByRole('heading', { name: 'Step 3 of 3: Your details' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Request my quote' }));
    expect(await screen.findByText('Enter your name.')).toBeInTheDocument();
    expect(screen.getByText(/Please tick the box/)).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST' && c.path === '/public/inquiries/quote')).toBe(false);

    await user.type(screen.getByRole('textbox', { name: /Full name/ }), 'Ada Lovelace');
    await user.type(screen.getByRole('textbox', { name: /Work email/ }), 'ada@example.com');
    await user.click(screen.getByRole('checkbox', { name: /may use my details/ }));
    await user.click(screen.getByRole('button', { name: 'Request my quote' }));

    expect(await screen.findByText('Quote request received')).toBeInTheDocument();
    const body = calls.find((c) => c.path === '/public/inquiries/quote')!.body as Record<string, unknown>;
    expect(body).toMatchObject({
      name: 'Ada Lovelace',
      email: 'ada@example.com',
      serviceSlugs: ['seo'],
      packageIds: ['p1'],
      budgetRange: '2k-5k',
      timeline: '1-3-months',
      consent: true,
      consentVersion: 'forms-2026-09',
      formToken: 'form-token-1',
      nickname: '',
    });
  });

  it('has no axe violations', async () => {
    const { container } = renderPublic(<QuotePage />, { route: '/get-a-quote', path: '/get-a-quote' });
    await screen.findByRole('checkbox', { name: 'Local SEO' });
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('BookConsultationPage', () => {
  it('groups slots by the visitor’s local day', () => {
    const days = groupSlotsByDay(['2026-09-28T02:00:00Z', '2026-09-28T09:00:00Z', '2026-09-29T14:00:00Z'], 'America/Los_Angeles');
    expect(days.map((d) => d.key)).toEqual(['2026-09-27', '2026-09-28', '2026-09-29']);
    expect(groupSlotsByDay(fx.slots.slots, 'UTC').map((d) => d.slots.length)).toEqual([2, 1]);
  });

  it('selects a day and a slot, then books it', async () => {
    const user = userEvent.setup();
    const { calls } = renderPublic(<BookConsultationPage />, {
      route: '/book-a-consultation',
      path: '/book-a-consultation',
      routes: { 'POST /public/consultations': () => json(201, { reference: 'B-2001', slotStart: '2026-09-29T14:00:00Z', message: 'Booked' }) },
    });
    const dayGroup = await screen.findByRole('group', { name: 'Day' });
    const dayButtons = within(dayGroup).getAllByRole('button');
    expect(dayButtons).toHaveLength(2);
    expect(dayButtons[0]).toHaveAttribute('aria-pressed', 'true');
    expect(within(screen.getByRole('group', { name: /^Times on/ })).getAllByRole('button')).toHaveLength(2);

    await user.click(dayButtons[1]);
    expect(dayButtons[1]).toHaveAttribute('aria-pressed', 'true');
    const times = within(screen.getByRole('group', { name: /^Times on/ })).getAllByRole('button');
    expect(times).toHaveLength(1);

    await user.click(screen.getByRole('button', { name: 'Book my call' }));
    expect(await screen.findByText('Pick a time for your call.')).toBeInTheDocument();

    await user.click(times[0]);
    expect(times[0]).toHaveAttribute('aria-pressed', 'true');
    await user.type(screen.getByRole('textbox', { name: /Full name/ }), 'Ada Lovelace');
    await user.type(screen.getByRole('textbox', { name: /Work email/ }), 'ada@example.com');
    await user.click(screen.getByRole('checkbox', { name: /may use my details/ }));
    await user.click(screen.getByRole('button', { name: /^Book .+ at / }));

    expect(await screen.findByText('See you soon')).toBeInTheDocument();
    const body = calls.find((c) => c.method === 'POST' && c.path === '/public/consultations')!.body as Record<string, unknown>;
    expect(body).toMatchObject({ slotStart: '2026-09-29T14:00:00Z', email: 'ada@example.com', consent: true });
    expect(typeof body.visitorTimeZone).toBe('string');
  });

  it('refreshes the slots and asks for another time when the slot was just taken', async () => {
    const user = userEvent.setup();
    const { calls } = renderPublic(<BookConsultationPage />, {
      route: '/book-a-consultation',
      path: '/book-a-consultation',
      routes: { 'POST /public/consultations': () => problem(409, 'website.slot_taken', 'That time was just booked') },
    });
    const times = within(await screen.findByRole('group', { name: /^Times on/ })).getAllByRole('button');
    await user.click(times[0]);
    await user.type(screen.getByRole('textbox', { name: /Full name/ }), 'Ada Lovelace');
    await user.type(screen.getByRole('textbox', { name: /Work email/ }), 'ada@example.com');
    await user.click(screen.getByRole('checkbox', { name: /may use my details/ }));
    await user.click(screen.getByRole('button', { name: /^Book .+ at / }));

    expect(await screen.findByText('That time was just taken')).toBeInTheDocument();
    await waitFor(() => expect(calls.filter((c) => c.path === '/public/consultations/slots')).toHaveLength(2));
    expect(screen.getByRole('button', { name: 'Book my call' })).toBeInTheDocument();
  });

  it('has no axe violations', async () => {
    const { container } = renderPublic(<BookConsultationPage />, { route: '/book-a-consultation', path: '/book-a-consultation' });
    await screen.findByRole('group', { name: 'Day' });
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Cookie consent', () => {
  const tracked: PublicSite = { ...fx.site, analytics: { ga4MeasurementId: 'G-TEST12345', gtmContainerId: 'GTM-ABC1234', metaPixelId: null } };
  const vendorScripts = () => Array.from(document.head.querySelectorAll('script[src]')).map((s) => s.getAttribute('src'));

  it('loads no analytics or marketing script before consent, and only the accepted categories after', async () => {
    const user = userEvent.setup();
    renderPublic(<p>Page</p>, { site: tracked });
    const banner = (await screen.findByRole('heading', { name: 'Your privacy choices' })).closest('section')!;
    expect(vendorScripts()).toEqual([]);
    expect((window as unknown as { gtag?: unknown }).gtag).toBeUndefined();

    await user.click(within(banner).getByRole('button', { name: 'Reject all' }));
    expect(screen.queryByRole('heading', { name: 'Your privacy choices' })).not.toBeInTheDocument();
    expect(vendorScripts()).toEqual([]);
    expect(JSON.parse(localStorage.getItem('oa.consent')!)).toMatchObject({ analytics: false, marketing: false });

    await user.click(screen.getByRole('button', { name: 'Cookie settings' }));
    const settings = (await screen.findByRole('heading', { name: 'Your privacy choices' })).closest('section')!;
    await user.click(within(settings).getByRole('switch', { name: /Analytics/ }));
    await user.click(within(settings).getByRole('button', { name: 'Save choices' }));

    await waitFor(() => expect(vendorScripts()).toEqual(['https://www.googletagmanager.com/gtag/js?id=G-TEST12345']));
    expect(vendorScripts().some((src) => src?.includes('gtm.js'))).toBe(false);
  });

  it('shows no banner when the site has no tags configured', async () => {
    renderPublic(<p>Page</p>);
    await screen.findByRole('link', { name: 'Privacy policy' });
    await act(async () => {
      await new Promise((r) => setTimeout(r, 0));
    });
    expect(screen.queryByRole('heading', { name: 'Your privacy choices' })).not.toBeInTheDocument();
  });
});

describe('Services mega-menu', () => {
  it('opens from the keyboard, moves with arrows/Home/End and closes with Escape', async () => {
    const user = userEvent.setup();
    renderPublic(<p>Page</p>);
    const nav = screen.getByRole('navigation', { name: 'Main' });
    const trigger = within(nav).getByRole('button', { name: 'Services' });
    await waitFor(() => expect(within(nav).getAllByRole('link', { hidden: true, name: 'Local SEO' })).toHaveLength(1));
    expect(trigger).toHaveAttribute('aria-expanded', 'false');

    trigger.focus();
    await user.keyboard('{ArrowDown}');
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    const panel = document.getElementById(trigger.getAttribute('aria-controls')!)!;
    expect(panel).toBeVisible();
    expect(within(panel).getByRole('link', { name: 'Search engine optimization' })).toHaveFocus();

    await user.keyboard('{ArrowDown}');
    expect(within(panel).getByRole('link', { name: 'Local SEO' })).toHaveFocus();
    await user.keyboard('{End}');
    expect(within(panel).getByRole('link', { name: 'Get a free marketing audit' })).toHaveFocus();
    await user.keyboard('{ArrowDown}');
    expect(within(panel).getByRole('link', { name: 'Search engine optimization' })).toHaveFocus();
    await user.keyboard('{ArrowUp}');
    expect(within(panel).getByRole('link', { name: 'Get a free marketing audit' })).toHaveFocus();
    await user.keyboard('{Home}');
    expect(within(panel).getByRole('link', { name: 'Search engine optimization' })).toHaveFocus();

    await user.keyboard('{Escape}');
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    expect(trigger).toHaveFocus();
    expect(panel).not.toBeVisible();

    // Enter also opens; tabbing out of the panel closes it.
    await user.keyboard('{Enter}');
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    await user.click(document.body);
    await waitFor(() => expect(trigger).toHaveAttribute('aria-expanded', 'false'));
  });
});

describe('Newsletter email links', () => {
  it.each([
    ['confirm', NewsletterConfirmPage, 'Confirm subscription', "You're subscribed"],
    ['unsubscribe', NewsletterUnsubscribePage, 'Unsubscribe', "You've been unsubscribed"],
  ] as const)('only %s when the reader presses the button (link scanners must not act)', async (action, Page, button, done) => {
    const user = userEvent.setup();
    const { calls } = mockFetch({ [`POST /public/newsletter/${action}`]: () => json(200, { status: 'ok', message: 'Done.' }) });
    renderWithApp(<Page />, { route: `/newsletter/${action}?token=tok-1`, path: `/newsletter/${action}`, withAuth: false });
    const press = await screen.findByRole('button', { name: button });
    expect(calls.filter((c) => c.method === 'POST' && c.path.includes('/newsletter/'))).toHaveLength(0);
    await user.click(press);
    expect(await screen.findByText(done)).toBeInTheDocument();
    const posts = calls.filter((c) => c.method === 'POST' && c.path.includes('/newsletter/'));
    expect(posts).toHaveLength(1);
    expect(posts[0].body).toEqual({ token: 'tok-1' });
  });
});
