import { expect, test } from '@playwright/test';
import {
  accounts,
  actor,
  clients,
  isoDate,
  landing,
  modal,
  runId,
  toast,
  watchErrors,
} from './support/agency';

/**
 * Social publishing with client approval (the demo seed makes Nimbus Fitness require client approval for posts):
 *   the account manager composes a LinkedIn post for Nimbus and submits it for review → the social media manager
 *   approves it internally (→ client approval) → the Nimbus approver approves it in the client portal → the social
 *   media manager schedules it.
 */
test('compose → submit → internal approval → client approval → schedule', async ({ browser }) => {
  const id = runId();
  const title = `E2E summer shred teaser ${id}`;
  const text = `Summer Shred starts Monday — 21 days, 15 minutes a day. Who's in? (${id})`;

  // ---------------------------------------------------------------- account manager: compose + submit
  const am = await actor(browser, accounts.am, landing.agency);
  const amErrors = watchErrors(am);
  await am
    .getByRole('navigation', { name: 'Agency navigation' })
    .getByRole('link', { name: 'Compose' })
    .click();
  await expect(am.getByRole('heading', { level: 1, name: 'New post' })).toBeVisible();
  const setup = am.getByRole('region', { name: 'Post' });
  await setup.getByLabel('Client').selectOption({ label: clients.nimbus.name });
  await setup.getByLabel('Internal title').fill(title);
  const network = setup.getByRole('checkbox', { name: /^LinkedIn · @/ }).first();
  await network.check();
  const variants = am.getByRole('region', { name: 'Content per network' });
  await variants.getByLabel('LinkedIn text').fill(text);
  await variants.getByLabel('Hashtags').fill('#SummerShred #NimbusFitness');
  await expect(
    am
      .getByRole('region', { name: 'Preview' })
      .getByText(/Summer Shred starts Monday/)
      .first(),
  ).toBeVisible();
  await expect(am.getByText('Ready for review')).toBeVisible();
  await am.getByRole('button', { name: 'Save draft' }).click();
  await expect(toast(am, 'Draft created')).toBeVisible();
  await expect(am).toHaveURL(/\/agency\/social\/posts\/[0-9a-f-]{36}$/);
  await expect(am.getByRole('heading', { level: 1, name: title })).toBeVisible();
  const postUrl = new URL(am.url()).pathname;

  await am.getByRole('button', { name: 'Submit for review' }).click();
  await modal(am, 'Submit for review').getByRole('button', { name: 'Submit for review' }).click();
  await expect(toast(am, 'Submit for review: done')).toBeVisible();
  // Account managers cannot approve (social.publish).
  await expect(am.getByRole('button', { name: 'Approve', exact: true })).toHaveCount(0);
  amErrors.expectClean('the social composer');

  // ---------------------------------------------------------------- social media manager: internal approval
  const social = await actor(browser, accounts.social, landing.agency);
  const socialErrors = watchErrors(social);
  await social.goto('/agency/social/approvals');
  await social.getByRole('link', { name: title }).first().click();
  await expect(social.getByRole('heading', { level: 1, name: title })).toBeVisible();
  await social.getByRole('button', { name: 'Approve', exact: true }).click();
  const approve = modal(social, 'Approve');
  await expect(approve.getByText('The client will be asked to approve it in their portal.')).toBeVisible();
  await approve.getByRole('button', { name: 'Approve' }).click();
  await expect(toast(social, 'Approve: done')).toBeVisible();
  await expect(social.getByRole('button', { name: 'Schedule', exact: true })).toHaveCount(0);

  // ---------------------------------------------------------------- client approver: approve in the portal
  const approver = await actor(browser, accounts.nimbusApprover, landing.client);
  const clientErrors = watchErrors(approver);
  await approver.getByRole('navigation').getByRole('link', { name: 'Post approvals' }).click();
  await expect(approver.getByRole('heading', { level: 1, name: 'Approvals' })).toBeVisible();
  const card = approver
    .getByRole('list', { name: 'Posts awaiting approval' })
    .getByRole('article', { name: title });
  await expect(card).toContainText('Summer Shred starts Monday');
  await card.getByRole('button', { name: 'Review' }).click();
  const review = modal(approver, title);
  await expect(review.getByText(/Summer Shred starts Monday/).first()).toBeVisible();
  await review.getByRole('button', { name: 'Approve' }).click();
  await expect(toast(approver, 'Post approved')).toBeVisible();
  await expect(approver.getByRole('article', { name: title })).toHaveCount(0);
  clientErrors.expectClean('the client social approval');

  // ---------------------------------------------------------------- social media manager: schedule
  await social.goto(postUrl);
  await expect(social.getByText('Approved', { exact: true }).first()).toBeVisible();
  await social.getByRole('button', { name: 'Schedule', exact: true }).click();
  const schedule = modal(social, 'Schedule post');
  await schedule.getByLabel('Publish at').fill(`${isoDate(3)}T10:30`);
  await schedule.getByRole('button', { name: 'Schedule' }).click();
  await expect(toast(social, 'Schedule: done')).toBeVisible();
  await expect(social.getByText('Scheduled', { exact: true }).first()).toBeVisible();
  await expect(social.getByRole('button', { name: 'Unschedule' })).toBeVisible();
  socialErrors.expectClean('the social manager journey');
});
