import type { Page } from '@playwright/test';
import {
  axeViolations,
  expect,
  formToken,
  latestMail,
  pageFormToken,
  pathOf,
  postPublic,
  prospect,
  remember,
  runId,
  test,
  waitMinFill,
  watchErrors,
} from './support/journey';

/**
 * Lead to cash, part 1 — the anonymous visitor on the public website:
 *   home → services → pricing → case studies → blog, then the contact form (a too-fast first try is refused), a free
 *   audit request, a quote for a pricing package, a consultation booked on a picked slot (a second visitor racing for the
 *   same slot is told it was just taken) and a double opt-in newsletter signup confirmed from the dev mailbox.
 * Spam and replay protections are asserted against the public API: honeypot, single-use form tokens, forged tokens.
 */
test.describe.configure({ mode: 'serial' });

/** Waits until the form token the page fetched on mount is old enough, then clicks `submit`. */
async function submitWhenFilled(
  token: { minFillSeconds: number; issuedAt: number },
  submit: () => Promise<void>,
) {
  await expect
    .poll(() => Date.now() - token.issuedAt, { message: 'form token older than the minimum fill time' })
    .toBeGreaterThanOrEqual(token.minFillSeconds * 1000 + 250);
  await submit();
}

test('the visitor browses the site and sends the contact, audit and quote forms', async ({ anonymous }) => {
  const visitor = await anonymous();
  const errors = watchErrors(visitor);
  const lead = prospect();

  // ---------------------------------------------------------------- home → services → pricing → case studies → blog
  // Arrive from a paid campaign: the UTM parameters are kept for the visit and attributed to every form.
  await visitor.goto(`/?utm_source=google&utm_medium=cpc&utm_campaign=l2c-${runId()}`);
  await expect(visitor.getByRole('heading', { level: 1 })).toBeVisible();
  expect(await axeViolations(visitor), 'axe violations on the home page').toEqual([]);

  const mainNav = visitor.getByRole('navigation', { name: 'Main' });
  await visitor.goto('/services');
  await expect(visitor.getByRole('heading', { level: 1 })).toBeVisible();
  await visitor.getByRole('main').getByRole('link', { name: 'SEO', exact: true }).first().click();
  await expect(visitor).toHaveURL(/\/services\/seo$/);
  await expect(visitor.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(mainNav).toBeVisible();

  await visitor.goto('/pricing');
  await expect(visitor.getByRole('heading', { level: 1 })).toBeVisible();
  const packageLinks = visitor
    .getByRole('main')
    .getByRole('link', { name: /^(Get started|Request a quote) with / });
  await expect(packageLinks.first()).toBeVisible();

  await visitor.goto('/case-studies');
  await expect(visitor.getByRole('heading', { level: 1 })).toBeVisible();
  const caseStudy = visitor.getByRole('main').getByRole('article').first().getByRole('link').first();
  await caseStudy.click();
  await expect(visitor).toHaveURL(/\/case-studies\/[\w-]+$/);
  await expect(visitor.getByRole('heading', { level: 1 })).toBeVisible();

  await visitor.goto('/blog');
  const firstPost = visitor
    .getByRole('main')
    .getByRole('article')
    .first()
    .getByRole('heading', { level: 2 })
    .getByRole('link');
  const postTitle = (await firstPost.textContent())!.trim();
  await firstPost.click();
  await expect(visitor.getByRole('heading', { level: 1, name: postTitle })).toBeVisible();

  // ---------------------------------------------------------------- contact form: too fast, then accepted
  const contactToken = await pageFormToken(visitor, () => visitor.goto('/contact'));
  const contact = visitor.getByRole('form', { name: 'Contact form' });
  // Client-side validation first: nothing is sent while required fields are empty.
  await contact.getByRole('button', { name: 'Send message' }).click();
  await expect(contact.getByText('Enter your name.')).toBeVisible();
  await contact.getByLabel('Full name').fill(lead.name);
  await contact.getByLabel('Work email').fill(lead.email);
  await contact.getByLabel('Company').fill(lead.company);
  await contact.getByLabel('Website').fill(lead.website);
  await contact
    .getByLabel('How can we help?')
    .fill('We need SEO and paid social for our spring launch — can we talk budgets?');
  await contact.getByRole('checkbox', { name: /^I agree that Optimize All may use/ }).check();
  if (Date.now() - contactToken.issuedAt < contactToken.minFillSeconds * 1000 - 500) {
    // A bot-fast submission is refused with a clear message (the server enforces the minimum fill time).
    errors.ignore(/HTTP 400 POST .*\/public\/inquiries\/contact$/);
    await contact.getByRole('button', { name: 'Send message' }).click();
    await expect(
      contact.getByText('That was quick! Please check your answers and send the form again.'),
    ).toBeVisible();
  }
  await submitWhenFilled(contactToken, () => contact.getByRole('button', { name: 'Send message' }).click());
  await expect(visitor.getByRole('heading', { name: 'Thanks — message received' })).toBeVisible();
  const contactRef = (await visitor.getByText(/^Your reference: OA-/).textContent())!
    .replace('Your reference: ', '')
    .trim();

  // ---------------------------------------------------------------- free audit
  const auditToken = await pageFormToken(visitor, () => visitor.goto('/free-audit'));
  const audit = visitor.getByRole('form', { name: 'Free audit request' });
  await audit.getByLabel('Full name').fill(lead.name);
  await audit.getByLabel('Work email').fill(lead.email);
  await audit.getByLabel('Company').fill(lead.company);
  await audit.getByLabel('Website').fill(lead.website);
  await audit.getByRole('checkbox', { name: 'SEO', exact: true }).check();
  await audit.getByLabel('What are your goals?').fill('Rank for our top 20 commercial keywords');
  await audit.getByLabel('Monthly marketing budget').selectOption({ label: '$3,000 – $10,000 / month' });
  await audit.getByRole('checkbox', { name: /^I agree that Optimize All may use/ }).check();
  await submitWhenFilled(auditToken, () =>
    audit.getByRole('button', { name: 'Request my free audit' }).click(),
  );
  await expect(visitor.getByRole('heading', { name: 'Your audit request is in' })).toBeVisible();

  // ---------------------------------------------------------------- quote for a pricing package (3 steps)
  await visitor.goto('/pricing');
  const tokenForQuote = visitor.waitForResponse(
    (r) => r.url().includes('/api/v1/public/forms/token') && r.ok(),
  );
  const pkg = visitor
    .getByRole('main')
    .getByRole('link', { name: /^Get started with / })
    .first();
  await pkg.click();
  await expect(visitor).toHaveURL(/\/get-a-quote\?.*package=/);
  const quoteToken = {
    ...((await (await tokenForQuote).json()) as { minFillSeconds: number }),
    issuedAt: Date.now(),
  };
  const quote = visitor.getByRole('form', { name: /^Step 1 of 3/ });
  await expect(quote.getByRole('checkbox', { checked: true }).first()).toBeVisible(); // the package's service is preselected
  await visitor.getByRole('button', { name: 'Next' }).click();
  await visitor.getByLabel('Monthly budget').selectOption({ label: '$3,000 – $10,000 / month' });
  await visitor.getByRole('radio', { name: 'In 1–3 months' }).check();
  await visitor
    .getByLabel('Project details')
    .fill('Launching a new product line in spring; need a retainer.');
  await visitor.getByRole('button', { name: 'Next' }).click();
  await visitor.getByLabel('Full name').fill(lead.name);
  await visitor.getByLabel('Work email').fill(lead.email);
  await visitor.getByLabel('Company').fill(lead.company);
  await visitor.getByRole('checkbox', { name: /^I agree that Optimize All may use/ }).check();
  await submitWhenFilled(quoteToken, () => visitor.getByRole('button', { name: 'Request my quote' }).click());
  await expect(visitor.getByRole('heading', { name: 'Quote request received' })).toBeVisible();

  errors.expectClean('the public website forms');
  remember({ contactRef });
});

test('the visitor books a consultation; a second visitor racing for the same slot is told it was just taken', async ({
  anonymous,
}) => {
  const lead = prospect();
  const first = await anonymous();
  const second = await anonymous();
  const firstErrors = watchErrors(first);
  const secondErrors = watchErrors(second);

  // Both visitors open the booking page and see the same free slots.
  const [firstToken, secondToken] = await Promise.all([
    pageFormToken(first, () => first.goto('/book-a-consultation')),
    pageFormToken(second, () => second.goto('/book-a-consultation')),
  ]);
  for (const page of [first, second]) {
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(
      page
        .getByRole('group', { name: /^Times on / })
        .getByRole('button')
        .first(),
    ).toBeVisible();
  }

  // Pick the second day's first time (both see it), so the slot is well past the minimum notice.
  const pick = async (page: Page) => {
    await page.getByRole('group', { name: 'Day' }).getByRole('button').nth(1).click();
    const slot = page
      .getByRole('group', { name: /^Times on / })
      .getByRole('button')
      .first();
    const label = (await slot.textContent())!.trim();
    await slot.click();
    await expect(slot).toHaveAttribute('aria-pressed', 'true');
    return label;
  };
  const firstSlot = await pick(first);
  const secondSlot = await pick(second);
  expect(secondSlot).toBe(firstSlot);

  const fill = async (page: Page, name: string, email: string) => {
    const form = page.getByRole('form', { name: 'Book a consultation' });
    await form.getByLabel('Full name').fill(name);
    await form.getByLabel('Work email').fill(email);
    await form.getByLabel('Company').fill(lead.company);
    await form.getByLabel('Anything we should know?').fill('Spring launch planning');
    await form.getByRole('checkbox', { name: /^I agree that Optimize All may use/ }).check();
    return form;
  };
  const firstForm = await fill(first, lead.name, lead.email);
  const secondForm = await fill(second, `Rival ${runId()}`, `rival.${runId()}@othercorp-${runId()}.test`);

  // The first visitor books it.
  await submitWhenFilled(firstToken, () => firstForm.getByRole('button', { name: /^Book / }).click());
  await expect(first.getByRole('heading', { name: 'See you soon' })).toBeVisible();
  await expect(first.getByText(/^Your call is on /)).toBeVisible();
  const confirmation = await latestMail(lead.email, /consultation/i);
  expect(confirmation.text).toContain(lead.firstName);

  // The second visitor, still looking at the stale slot list, loses the race: 409, the list refreshes, the slot is gone.
  secondErrors.ignore(/HTTP 409 POST .*\/public\/consultations$/);
  const refreshed = second.waitForResponse(
    (r) => r.url().includes('/api/v1/public/consultations/slots') && r.ok(),
  );
  await submitWhenFilled(secondToken, () => secondForm.getByRole('button', { name: /^Book / }).click());
  await expect(second.getByRole('status', { name: 'That time was just taken' })).toBeVisible();
  await refreshed;
  await expect(secondForm.getByRole('button', { name: 'Book my call' })).toBeVisible(); // the selection was cleared
  await second.getByRole('group', { name: 'Day' }).getByRole('button').nth(1).click();
  await expect(
    second.getByRole('group', { name: /^Times on / }).getByRole('button', { name: firstSlot, exact: true }),
  ).toHaveCount(0);

  firstErrors.expectClean('the booking page');
  secondErrors.expectClean('the second visitor’s booking page');
});

test('the visitor subscribes to the newsletter and confirms from the email (double opt-in)', async ({
  anonymous,
}) => {
  const lead = prospect();
  const visitor = await anonymous();
  const errors = watchErrors(visitor);

  const token = await pageFormToken(visitor, () => visitor.goto('/blog'));
  const signup = visitor.getByRole('form', { name: 'Newsletter signup', exact: true });
  await signup.getByLabel('Email address').fill(lead.email);
  await signup.getByRole('checkbox', { name: /^Send me Optimize All/ }).check();
  await submitWhenFilled(token, () => signup.getByRole('button', { name: 'Subscribe' }).click());
  await expect(
    visitor.getByText('Almost there! Check your inbox and click the link to confirm your subscription.'),
  ).toBeVisible();

  const mail = await latestMail(lead.email, /Confirm your Optimize All newsletter subscription/);
  const confirmLink = mail.links.find((l) => l.includes('/newsletter/confirm'));
  expect(confirmLink, 'the email carries the confirmation link').toBeTruthy();
  expect(
    mail.links.some((l) => l.includes('/newsletter/unsubscribe')),
    'and an unsubscribe link',
  ).toBe(true);

  // Opening the link does not confirm by itself (mail scanners open links); the visitor presses the button.
  await visitor.goto(pathOf(confirmLink!));
  const confirm = visitor.getByRole('button', { name: 'Confirm subscription' });
  await expect(confirm).toBeVisible();
  await confirm.dblclick(); // a double click confirms once
  await expect(visitor.getByRole('heading', { name: "You're subscribed" })).toBeVisible();

  // Subscribing again never reveals that the address is already on the list.
  const again = await postPublic('/public/newsletter/subscribe', {
    email: lead.email,
    source: 'footer',
    formToken: token.token,
    consent: true,
    consentVersion: 'newsletter-2026-09',
  });
  expect(again.status).toBe(202);
  expect(again.body.message).toBe(
    'Almost there! Check your inbox and click the link to confirm your subscription.',
  );
  errors.expectClean('the newsletter signup');
});

test('spam and replay protections on the public forms', async () => {
  const id = runId();
  const envelope = (formTokenValue: string, extra: Record<string, unknown> = {}) => ({
    name: `Spam Check ${id}`,
    email: `spam.${id}@spamcheck-${id}.test`,
    message: 'Please send me your rates for SEO and PPC.',
    formToken: formTokenValue,
    consent: true,
    consentVersion: 'forms-2026-09',
    ...extra,
  });

  // A forged or garbled token is refused.
  const forged = await postPublic('/public/inquiries/contact', envelope('not-a-real-token'));
  expect(forged).toMatchObject({ status: 400, body: { code: 'website.form_expired' } });

  // Too fast: a token fetched a moment ago proves nobody filled the form.
  const fast = await formToken();
  const tooFast = await postPublic('/public/inquiries/contact', envelope(fast.token));
  expect(tooFast).toMatchObject({ status: 400, body: { code: 'website.form_too_fast' } });

  // Missing consent is a field error.
  await waitMinFill(fast);
  const noConsent = await postPublic('/public/inquiries/contact', envelope(fast.token, { consent: false }));
  expect(noConsent.status).toBe(400);
  expect(noConsent.body.errors).toHaveProperty('consent');

  // Honeypot: the bot is told "thanks" but nothing is stored, and the token is not spent.
  const bot = await postPublic(
    '/public/inquiries/contact',
    envelope(fast.token, { nickname: 'http://spam.example' }),
  );
  expect(bot.status).toBe(202);

  // Single-use tokens: the first real submission is accepted, an identical replay is a 409.
  const real = await postPublic('/public/inquiries/contact', envelope(fast.token));
  expect(real.status).toBe(202);
  const replay = await postPublic('/public/inquiries/contact', envelope(fast.token));
  expect(replay).toMatchObject({ status: 409, body: { code: 'website.form_already_submitted' } });
  // …on every form that spends tokens (the booking endpoint answers "already sent", not "slot taken").
  const replayBooking = await postPublic('/public/consultations', {
    ...envelope(fast.token),
    slotStart: new Date(Date.now() + 5 * 86_400_000).toISOString(),
    visitorTimeZone: 'UTC',
  });
  expect(replayBooking).toMatchObject({ status: 409, body: { code: 'website.form_already_submitted' } });

  // Two identical submissions at the same moment (a double click that bypasses the UI): exactly one is accepted.
  const race = await formToken();
  await waitMinFill(race);
  const body = envelope(race.token, { email: `race.${id}@spamcheck-${id}.test` });
  const results = await Promise.all([
    postPublic('/public/inquiries/contact', body),
    postPublic('/public/inquiries/contact', body),
  ]);
  expect(results.map((r) => r.status).sort()).toEqual([202, 409]);

  // Boundary values: the name must have 2+ characters, the message 10+, the email must be an address.
  const bounds = await formToken();
  await waitMinFill(bounds);
  const invalid = await postPublic(
    '/public/inquiries/contact',
    envelope(bounds.token, { name: 'A', email: 'not-an-email', message: 'short' }),
  );
  expect(invalid.status).toBe(400);
  expect(Object.keys(invalid.body.errors as object).map((k) => k.toLowerCase())).toEqual(
    expect.arrayContaining(['name', 'message']),
  );
  const exact = await postPublic(
    '/public/inquiries/contact',
    envelope(bounds.token, { name: 'Al', message: '0123456789' }),
  );
  expect(exact.status).toBe(202);
});
