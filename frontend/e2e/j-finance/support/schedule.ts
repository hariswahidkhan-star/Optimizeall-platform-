import { type Page, expect } from '@playwright/test';
import { modal } from '../../journeys/support/ui';
import { toast } from '../../agency/support/agency';
import { localMinute } from './finance';

/** Local date (yyyy-mm-dd) and time (HH:mm:ss) of `instant` in an IANA time zone. */
export function localParts(instant: Date, timeZone: string) {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat('en-CA', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hourCycle: 'h23',
    })
      .formatToParts(instant)
      .map((p) => [p.type, p.value]),
  );
  return { date: `${parts.year}-${parts.month}-${parts.day}`, time: `${parts.hour}:${parts.minute}:${parts.second}` };
}

/**
 * Closes a payout period "just now" through Payout schedule → Change schedule: a weekly schedule in `timeZone` whose
 * cutoff is one second ago (anchored on that cutoff's local date), so everything approved before it is payable and the
 * period can be prepared. Different time zones give different period keys (the cutoff's local date), which is how the
 * journey gets two distinct completed periods minutes apart. Returns the period key.
 */
export async function closePeriodNow(page: Page, timeZone: string, reason: string): Promise<string> {
  // Everything that happened before this call (approvals…) must fall inside the period: wait until the cutoff — one
  // second ago, whole seconds — is strictly after that moment.
  const notBefore = Date.now();
  await expect.poll(() => Math.floor(Date.now() / 1000) * 1000 - 1000 > notBefore, { intervals: [100] }).toBe(true);
  await page.goto('/finance/schedule');
  await expect(page.getByRole('heading', { level: 1, name: 'Payout schedule' })).toBeVisible();
  await page.getByRole('button', { name: 'Change schedule' }).click();
  const dialog = modal(page, 'Change payout schedule');
  const cutoff = new Date(Math.floor(Date.now() / 1000) * 1000 - 1000);
  const { date, time } = localParts(cutoff, timeZone);
  await dialog.getByLabel('Frequency').selectOption('Weekly');
  await dialog.getByLabel('Anchor cutoff date').fill(date);
  await dialog.getByLabel('Cutoff time').fill(time);
  await dialog.getByLabel('Time zone').fill(timeZone);
  await dialog.getByLabel('Effective from').fill(localMinute());
  await dialog.getByLabel('Reason').fill(reason);
  await dialog.getByRole('checkbox', { name: 'I confirm this schedule change.' }).check();
  await dialog.getByRole('button', { name: 'Save schedule' }).click();
  await expect(toast(page, 'Payout schedule saved')).toBeVisible();
  await expect(page.getByRole('region', { name: 'Current period' })).toContainText(`Last completed period${date}`);
  return date;
}
