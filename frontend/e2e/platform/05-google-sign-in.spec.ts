import { accounts, expect, landing, test, watchErrors } from './support/platform';

/**
 * Sign in with Google, not configured (the e2e API runs without Authentication:Google credentials): the API reports the
 * provider disabled and no Google button or connection card is offered anywhere. Real Google is never contacted.
 */
test('with Google not configured, no Google sign-in is offered', async ({ as, anonymous }) => {
  const visitor = await anonymous();
  const errors = watchErrors(visitor);

  const providers = visitor.waitForResponse((r) => r.url().endsWith('/api/v1/auth/providers'));
  await visitor.goto('/login');
  expect(await (await providers).json()).toEqual({ google: { enabled: false } });
  await expect(visitor.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  await expect(visitor.getByRole('button', { name: /Google/ })).toHaveCount(0);
  await expect(visitor.getByRole('separator', { name: 'or' })).toHaveCount(0);

  await visitor.goto('/register');
  await expect(visitor.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(visitor.getByRole('button', { name: /Google/ })).toHaveCount(0);
  errors.expectClean('the sign-in and register pages');

  // Profile → Security offers no Google connection either.
  const participant = await as(accounts.participant, landing.participant);
  const participantErrors = watchErrors(participant);
  await participant.goto('/app/profile/security');
  await expect(participant.getByRole('form', { name: 'Change password' })).toBeVisible();
  await expect(participant.getByRole('heading', { name: /Google/ })).toHaveCount(0);
  await expect(participant.getByRole('button', { name: /Google/ })).toHaveCount(0);
  participantErrors.expectClean('the security page');
});
