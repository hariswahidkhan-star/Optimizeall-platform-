import {
  accounts,
  errorOf,
  expect,
  isoDate,
  landing,
  login,
  need,
  staffNames,
  test,
  watchErrors,
} from './support/delivery';

/**
 * Time: the strategist runs the timer on the journey's project, logs a manual entry (with boundary checks), submits the
 * week; the account manager returns it with a comment, the strategist resubmits, the account manager approves, then
 * reopens it with a reason. The admin can't approve their own week.
 */

interface Week {
  id: string | null;
  status: string;
  weekStart: string;
  concurrencyStamp: string;
  entries: { id: string; projectId: string; minutes: number; note: string | null; concurrencyStamp: string }[];
}

test('the strategist runs the timer, logs time manually and submits the week', async ({ as }) => {
  const { id: projectId, name: projectName } = need('project');
  const { name: clientName } = need('client');
  const strategist = await as(accounts.strategist, landing.agency);
  const errors = watchErrors(strategist);
  const api = await login(accounts.strategist);
  const before = await api.get<Week>('/agency/time/timesheets/week');
  expect(before.status, 'the strategist’s current week is open').toBe('Open');

  await strategist.goto('/agency/time');
  const timer = strategist.getByRole('region', { name: 'Timer' });
  await timer.getByLabel('Project').selectOption({ label: `${clientName} — ${projectName}` });
  await timer.getByLabel('What are you working on?').fill('Technical crawl');
  await timer.getByRole('button', { name: 'Start timer' }).click();
  await expect(timer.getByRole('timer', { name: 'Elapsed time' })).toBeVisible();
  await expect(timer).toContainText(projectName);
  // Only one timer at a time.
  expect((await errorOf(api.post('/agency/time/timer/start', { projectId, billable: true }))).status).toBe(409);
  // The running timer shows in the week, without edit buttons.
  const entries = strategist.getByRole('table', { name: 'Entries this week' });
  await expect(entries.getByText('Running')).toBeVisible();
  await timer.getByRole('button', { name: 'Stop timer' }).dblclick();
  await expect(timer.getByRole('button', { name: 'Start timer' })).toBeVisible();
  await expect(timer.getByRole('alert')).toHaveCount(0);
  await expect(entries.getByText('Running')).toHaveCount(0);
  await expect(entries.getByRole('row').filter({ hasText: 'Technical crawl' })).toHaveCount(1);

  // Manual entry.
  const log = strategist.getByRole('form', { name: 'Log time' });
  await log.getByLabel('Project').selectOption({ label: `${clientName} — ${projectName}` });
  await log.getByLabel('Hours').fill('0');
  await expect(log.getByRole('button', { name: 'Log time' })).toBeDisabled();
  await log.getByLabel('Hours').fill('1.5');
  await log.getByLabel('Note').fill('On-page audit');
  await log.getByLabel('Billable').uncheck();
  await log.getByRole('button', { name: 'Log time' }).click();
  const manual = entries.getByRole('row').filter({ hasText: 'On-page audit' });
  await expect(manual).toContainText('1h 30m');
  await expect(manual).toContainText('No');

  // Boundaries enforced by the API: no zero or >24h entries, no future dates.
  const today = isoDate(0);
  for (const [minutes, date, why] of [
    [0, today, 'zero minutes'],
    [24 * 60 + 1, today, 'more than a day'],
    [60, isoDate(2), 'a future date'],
  ] as const)
    expect((await errorOf(api.post('/agency/time/entries', { projectId, date, minutes, billable: true }))).status, why).toBe(400);

  // Edit the manual entry, then submit the week.
  await strategist.getByRole('button', { name: /^Edit entry of / }).last().click();
  const week = await api.get<Week>('/agency/time/timesheets/week');
  expect(week.entries.filter((e) => e.projectId === projectId).length).toBe(2);
  await strategist.keyboard.press('Escape');
  await strategist.getByRole('button', { name: 'Submit week for approval' }).click();
  await expect(strategist.getByText('Submitted: entries are locked')).toBeVisible();
  await expect(strategist.getByRole('button', { name: /^Edit entry of / })).toHaveCount(0);
  // Locked: editing through the API is refused.
  const entry = week.entries.find((e) => e.note === 'On-page audit')!;
  expect(
    (await errorOf(api.put(`/agency/time/entries/${entry.id}`, { projectId, date: today, minutes: 30, billable: false, note: 'x', concurrencyStamp: entry.concurrencyStamp })))
      .status,
  ).toBeGreaterThanOrEqual(400);
  errors.expectClean('tracking and submitting time');
});

test('the manager returns the week, the strategist resubmits, the manager approves and reopens it', async ({ as }) => {
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto('/agency/time');
  await am.getByRole('tab', { name: 'Approvals' }).click();
  const card = am.getByRole('article', { name: new RegExp(`^${staffNames.strategist}, week of`) }).last();
  await expect(card).toContainText('On-page audit');
  await expect(card.getByRole('button', { name: 'Return for changes' })).toBeDisabled();
  await card.getByLabel('Comment').fill('Please split the crawl by page type.');
  await card.getByRole('button', { name: 'Return for changes' }).click();
  await expect(card).toHaveCount(0);

  const strategist = await as(accounts.strategist, landing.agency);
  await strategist.goto('/agency/time');
  await expect(strategist.getByRole('alert').filter({ hasText: 'Returned by your manager' })).toContainText('split the crawl');
  await strategist.getByRole('button', { name: 'Submit week for approval' }).click();
  await expect(strategist.getByText('Submitted: entries are locked')).toBeVisible();

  await am.reload();
  await am.getByRole('tab', { name: 'Approvals' }).click();
  const again = am.getByRole('article', { name: new RegExp(`^${staffNames.strategist}, week of`) }).last();
  await again.getByRole('button', { name: 'Approve' }).dblclick();
  await expect(again).toHaveCount(0);
  await expect(am.getByRole('alert')).toHaveCount(0);
  const api = await login(accounts.strategist);
  expect((await api.get<Week>('/agency/time/timesheets/week')).status).toBe('Approved');

  // Reopen with a reason (required for an approved week).
  const reopen = am.getByRole('region', { name: 'Reopen a decided week' });
  await reopen.getByLabel('Person').selectOption({ label: staffNames.strategist });
  await expect(reopen.getByText('Approved', { exact: true })).toBeVisible();
  await expect(reopen.getByRole('button', { name: 'Reopen week' })).toBeDisabled();
  await reopen.getByLabel('Reason').fill('Client asked to move 30 minutes to next month.');
  await reopen.getByRole('button', { name: 'Reopen week' }).click();
  await expect(reopen.getByText('Open', { exact: true })).toBeVisible();
  expect((await api.get<Week>('/agency/time/timesheets/week')).status).toBe('Open');
  errors.expectClean('deciding the timesheet');
});

test('the admin cannot approve their own week', async ({ as }) => {
  const { id: projectId } = need('project');
  const adminApi = await login(accounts.admin);
  await adminApi.post('/agency/time/entries', { projectId, date: isoDate(0), minutes: 45, billable: true, note: 'Account review' });
  const sheet = await adminApi.post<Week>(`/agency/time/timesheets/submit?date=${isoDate(0)}`);
  expect(sheet.status).toBe('Submitted');
  const refused = await errorOf(adminApi.post(`/agency/time/timesheets/${sheet.id}/approve`, { concurrencyStamp: sheet.concurrencyStamp }));
  expect(refused).toEqual({ status: 403, code: 'time.own_timesheet' });

  // The approvals list doesn't offer the admin a decision they can't take on their own week.
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/agency/time');
  await admin.getByRole('tab', { name: 'Approvals' }).click();
  const own = admin.getByRole('article', { name: new RegExp(`^${staffNames.admin}, week of`) });
  await expect(own).toBeVisible();
  await expect(own.getByRole('button', { name: 'Approve' })).toHaveCount(0);
  await expect(own).toContainText('Someone else must approve your timesheet');
  // The account manager approves it.
  const amApi = await login(accounts.am);
  const decided = await amApi.post<Week>(`/agency/time/timesheets/${sheet.id}/approve`, { concurrencyStamp: sheet.concurrencyStamp });
  expect(decided.status).toBe('Approved');
  errors.expectClean('the admin’s own timesheet');
});
