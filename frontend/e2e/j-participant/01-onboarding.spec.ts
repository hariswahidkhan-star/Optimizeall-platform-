import { type Page, expect, test } from '@playwright/test';
import { ApiError, ApiSession, publicApi } from '../journeys/support/api';
import { linkIn, mailsTo } from './support/mail';
import { state } from './support/state';

/**
 * Participant lifecycle, part 1 — onboarding: registration (client validation, a reload mid-form that must not create
 * anything, a double click that must register once, a duplicate registration that must not reveal the account),
 * email verification through the dev mailbox (a used link is refused), sign-in (wrong password first) and profile
 * completion (country, language, time zone, interests; max-length boundaries; a reload that drops unsaved edits).
 */
test.describe.serial('participant onboarding', () => {
  let page: Page;
  const pat = () => state().pat;

  test.beforeAll(async ({ browser }) => {
    page = await (await browser.newContext()).newPage();
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  const field = (label: string) => page.getByLabel(label, { exact: true });

  test('registration validates on the client and focuses the first problem', async () => {
    await page.goto('/register');
    await expect(page.getByRole('heading', { level: 1, name: 'Create your account' })).toBeVisible();
    await page.getByRole('button', { name: 'Create account' }).click();

    await expect(field('Email')).toBeFocused();
    await expect(field('Email')).toHaveAccessibleDescription(/Enter a valid email address/);
    await expect(field('Display name')).toHaveAccessibleDescription(/at least 2 characters/);
    await expect(page.getByText('You need to accept the participant rules to create an account.')).toBeVisible();

    await field('Email').fill('not-an-email');
    await field('Password').fill('short');
    await page.getByRole('button', { name: 'Create account' }).click();
    await expect(field('Email')).toHaveAccessibleDescription(/Enter a valid email address/);
    await expect(field('Password')).toHaveAttribute('aria-invalid', 'true');
  });

  test('a reload in the middle of the form creates nothing', async () => {
    await field('Email').fill(pat().email);
    await field('Password').fill(pat().password);
    await field('Display name').fill(pat().displayName);
    await page.reload();
    await expect(field('Email')).toHaveValue('');
    await expect(field('Display name')).toHaveValue('');
    // No account was created: nothing was mailed to the address.
    expect(mailsTo(pat().email)).toEqual([]);
  });

  test('registers once even when "Create account" is double-clicked', async () => {
    await field('Email').fill(pat().email);
    await field('Password').fill(pat().password);
    // Display name at its 100-character limit (boundary); renamed on the profile page later.
    const longName = `Pat ${'L'.repeat(96)}`;
    expect(longName).toHaveLength(100);
    await field('Display name').fill(longName);
    await expect(field('Display name')).toHaveValue(longName);
    await field('Country').selectOption('GB');
    await field('Language').selectOption('en');
    await field('Time zone').selectOption('UTC');
    await page.getByRole('checkbox', { name: /accept the participant rules/i }).check();
    await page.getByRole('button', { name: 'Create account' }).dblclick();

    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
    await expect(page.getByText(pat().email)).toBeVisible();
    // Exactly one verification email; a second registration request would have mailed the "someone tried to
    // register with your email" notice instead.
    await expect.poll(() => mailsTo(pat().email).map((m) => m.subject)).toHaveLength(1);
    expect(mailsTo(pat().email)[0]!.subject).toMatch(/verify/i);
  });

  test('registering the same email again looks the same but only notifies the owner', async () => {
    await page.goto('/register');
    await field('Email').fill(pat().email.toUpperCase());
    await field('Password').fill('Another-Pass#2026!');
    await field('Display name').fill('Impostor');
    await field('Country').selectOption('US');
    await page.getByRole('checkbox', { name: /accept the participant rules/i }).check();
    await page.getByRole('button', { name: 'Create account' }).click();
    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();

    await expect
      .poll(() => mailsTo(pat().email).map((m) => m.subject))
      .toEqual([expect.stringMatching(/verify/i), 'Someone tried to register with your email']);
    // The impostor's password does not work.
    await expect(ApiSession.login(pat().email, 'Another-Pass#2026!')).rejects.toMatchObject({ status: 401 });
  });

  test('verifies the email with the mailbox link; the link cannot be used twice', async () => {
    const link = linkIn(pat().email, /verify/i, '/verify-email');
    await page.goto(`${link.pathname}${link.search}`);
    await expect(page.getByRole('heading', { name: 'Your email is verified' })).toBeVisible();

    await page.goto(`${link.pathname}${link.search}`);
    await expect(page.getByRole('heading', { name: 'This link has expired' })).toBeVisible();
    await expect(
      publicApi.post('/auth/verify-email', { token: link.searchParams.get('token') }),
    ).rejects.toBeInstanceOf(ApiError);
  });

  test('a wrong password is refused, the right one lands on the home page', async () => {
    await page.goto('/login');
    await field('Email').fill(pat().email);
    await field('Password').fill('Wrong-Pass#2026!');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page.getByRole('alert').filter({ hasText: 'Sign-in failed' })).toBeVisible();
    await expect(field('Password')).toHaveValue('');

    await field('Password').fill(pat().password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page).toHaveURL(/\/app$/);
    await expect(page.getByRole('heading', { name: 'Add the social profile you post from' })).toBeVisible();
  });

  test('completes the profile: country, language, time zone and interests', async () => {
    await page.goto('/app/profile');
    const name = field('Display name');
    await expect(name).toHaveValue(/^Pat L+$/);
    // 101 characters cannot be typed (the field stops at 100)…
    await name.fill(`X${'y'.repeat(100)}`);
    await expect(name).toHaveValue(`X${'y'.repeat(99)}`);
    // …and the API refuses them too.
    const api = await ApiSession.login(pat().email, pat().password);
    const profile = await api.get<Record<string, unknown>>('/me/profile');
    await expect(
      api.put('/me/profile', { ...profile, displayName: 'z'.repeat(101), interests: [] }),
    ).rejects.toMatchObject({ status: 400 });

    await name.fill(pat().displayName);
    await field('Country').selectOption('PK');
    await field('Language').selectOption('ur');
    await field('Time zone').selectOption('Asia/Karachi');

    const interest = field('Interests');
    await interest.fill('Fitness');
    await interest.press('Enter');
    await interest.fill('travel');
    await interest.press('Enter');
    await interest.fill('fitness'); // same tag again: ignored
    await interest.press('Enter');
    const forty = 'a'.repeat(40);
    await interest.fill(`${forty}b`); // 41 characters: the field keeps 40
    await expect(interest).toHaveValue(forty);
    await interest.press('Enter');
    const chips = page.getByRole('list', { name: 'Your interests' }).getByRole('listitem');
    await expect(chips).toHaveText(['fitness', 'travel', forty]);
    await page.getByRole('button', { name: `Remove interest ${forty}` }).click();
    await expect(chips).toHaveText(['fitness', 'travel']);

    // An invalid WhatsApp number is caught before saving.
    await page.getByLabel('WhatsApp number').fill('12345');
    await page.getByRole('button', { name: 'Save profile' }).click();
    await expect(page.getByLabel('WhatsApp number')).toBeFocused();
    await expect(page.getByLabel('WhatsApp number')).toHaveAccessibleDescription(/international format/i);
    await page.getByLabel('WhatsApp number').fill('');

    await page.getByRole('button', { name: 'Save profile' }).click();
    await expect(page.getByText('Profile saved')).toBeVisible();

    const saved = await api.get<{
      displayName: string;
      countryCode: string;
      languageCode: string;
      timeZone: string;
      interests: string[];
    }>('/me/profile');
    expect(saved).toMatchObject({
      displayName: pat().displayName,
      countryCode: 'PK',
      languageCode: 'ur',
      timeZone: 'Asia/Karachi',
      interests: ['fitness', 'travel'],
    });
  });

  test('a reload drops unsaved edits and keeps what was saved', async () => {
    await field('Display name').fill('Unsaved Name');
    await field('Time zone').selectOption('Europe/London');
    await page.reload();
    await expect(field('Display name')).toHaveValue(pat().displayName);
    await expect(field('Country')).toHaveValue('PK');
    await expect(field('Language')).toHaveValue('ur');
    await expect(field('Time zone')).toHaveValue('Asia/Karachi');
    await expect(page.getByRole('list', { name: 'Your interests' }).getByRole('listitem')).toHaveText([
      'fitness',
      'travel',
    ]);
  });
});
