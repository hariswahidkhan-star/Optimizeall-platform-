import type { Page } from '@playwright/test';
import {
  accounts,
  clientUser,
  expect,
  fakePng,
  landing,
  login,
  modal,
  need,
  png,
  raw,
  runId,
  saveState,
  staffNames,
  statusOf,
  test,
  toast,
  watchErrors,
} from './support/delivery';

/**
 * The project: the account manager starts an SEO retainer from its template (milestones, tasks, recurring task), edits a
 * milestone and the recurring task (pause/resume, day-of-month bounds), moves cards on the kanban board with the
 * keyboard, and works a task in the drawer (fields, assignees, visibility, checklist, comments, attachments). Two
 * people saving the same task: the second gets a conflict. The client sees only client-visible tasks.
 */

const TEMPLATE_TASKS = 8;
const projectName = () => `Lumen SEO retainer ${runId()}`;

function column(page: Page, label: string) {
  return page.getByRole('list', { name: `${label} tasks` });
}

test('the account manager creates the project from the SEO template', async ({ as }) => {
  const { id: clientId, name: clientName } = need('client');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(`/agency/clients/${clientId}?tab=projects`);
  await am.getByRole('link', { name: 'Create a project' }).click();
  const dialog = modal(am, 'New project');
  await expect(dialog.getByLabel('Client')).toHaveValue(clientId);
  await dialog.getByLabel('Template').selectOption({ label: `SEO monthly retainer (${TEMPLATE_TASKS} tasks)` });
  await expect(dialog.getByLabel('Name')).toHaveValue('SEO monthly retainer');
  await expect(dialog.getByLabel('Budget hours')).toHaveValue('40');
  await dialog.getByLabel('Name').fill(projectName());
  await dialog.getByLabel(/^Budget amount/).fill('3200');
  await dialog.getByRole('button', { name: 'Create project' }).click();
  await expect(am).toHaveURL(/\/agency\/projects\/[0-9a-f-]{36}/);
  await expect(am.getByRole('heading', { level: 1, name: projectName() })).toBeVisible();
  const projectId = am.url().split('/').pop()!.split('?')[0]!;
  saveState({ project: { id: projectId, name: projectName() } });
  await expect(am.getByRole('navigation', { name: /breadcrumb/i })).toContainText(clientName);

  // Template: 8 tasks on the board, 3 milestones, 1 monthly recurring task.
  await expect(column(am, 'To do').getByRole('listitem')).toHaveCount(TEMPLATE_TASKS);
  await am.getByRole('tab', { name: 'Milestones' }).click();
  const milestones = am.getByRole('list', { name: 'Milestones' });
  await expect(milestones.getByRole('listitem')).toHaveCount(3);
  await expect(milestones).toContainText('Technical health check');

  // Edit a milestone (title, hidden from the client), then mark it done.
  await am.getByRole('button', { name: 'Edit Monthly report' }).click();
  const edit = modal(am, 'Edit milestone');
  await edit.getByLabel('Title').fill('Monthly report & review call');
  await edit.getByRole('switch', { name: 'Visible to the client' }).click();
  await edit.getByRole('button', { name: 'Save' }).click();
  await expect(edit).toBeHidden();
  const renamed = milestones.getByRole('listitem').filter({ hasText: 'Monthly report & review call' });
  await expect(renamed).not.toContainText('visible to client');
  await milestones.getByRole('listitem').filter({ hasText: 'Technical health check' }).getByRole('button', { name: 'Mark done' }).click();
  await expect(milestones.getByRole('listitem').filter({ hasText: 'Technical health check' })).toContainText('Done');

  // Recurring task: listed, edited, paused and resumed.
  await am.getByRole('tab', { name: 'Settings' }).click();
  const rules = am.getByRole('list', { name: 'Recurring task rules' });
  await expect(rules.getByRole('listitem')).toHaveCount(1);
  await expect(rules).toContainText('Monthly SEO report');
  await expect(rules).toContainText('Day 1');
  await rules.getByRole('button', { name: 'Edit Monthly SEO report' }).click();
  const ruleEditor = am.getByRole('group', { name: 'Edit recurring task Monthly SEO report' });
  await ruleEditor.getByLabel('Day of month').fill('3');
  await ruleEditor.getByLabel('Due after (days)').fill('5');
  await ruleEditor.getByRole('button', { name: 'Save recurring task' }).click();
  await expect(rules).toContainText('Day 3 · due 5 days later');
  await rules.getByRole('button', { name: 'Pause Monthly SEO report' }).click();
  await expect(rules).toContainText('paused');
  await rules.getByRole('button', { name: 'Resume Monthly SEO report' }).click();
  await expect(rules).not.toContainText('paused');

  // Day of month is 1–28 (every month has it): the API refuses 0 and 29.
  const api = await login(accounts.am);
  const [rule] = await api.get<{ id: string }[]>(`/agency/projects/${projectId}/recurring-tasks`);
  for (const day of [0, 29])
    expect(
      await statusOf(api.put(`/agency/projects/${projectId}/recurring-tasks/${rule!.id}`, { title: 'Monthly SEO report', dayOfMonth: day, dueInDays: 5, isActive: true, labels: [], clientVisible: false })),
      `day of month ${day}`,
    ).toBe(400);
  errors.expectClean('creating the project from its template');
});

test('kanban: keyboard moves between columns and within a column are saved', async ({ as }) => {
  const { id: projectId } = need('project');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(`/agency/projects/${projectId}`);
  const todo = column(am, 'To do');
  const first = todo.getByRole('button').first();
  const title = (await first.innerText()).trim();
  const status = am.getByRole('status').filter({ hasText: /^Moved / });

  // → moves the card to In progress; focus stays on the card.
  await first.focus();
  await am.keyboard.press('ArrowRight');
  await expect(status).toHaveText(new RegExp(`^Moved ${escape(title)} to In progress, position 1 of 1\\.$`));
  const moved = column(am, 'In progress').getByRole('button', { name: title });
  await expect(moved).toBeFocused();

  // Two quick presses move it two columns (the second press must not fail on the first move's stale version).
  await am.keyboard.press('ArrowRight');
  await am.keyboard.press('ArrowRight');
  await expect(column(am, 'Blocked').getByRole('button', { name: title })).toBeVisible();
  await expect(am.getByRole('alert')).toHaveCount(0);
  await am.keyboard.press('ArrowLeft');
  await am.keyboard.press('ArrowLeft');
  await expect(column(am, 'In progress').getByRole('button', { name: title })).toBeVisible();

  // ↓ reorders within To do.
  const second = todo.getByRole('button').nth(1);
  const secondTitle = (await second.innerText()).trim();
  await todo.getByRole('button').first().focus();
  const firstTodo = (await todo.getByRole('button').first().innerText()).trim();
  await am.keyboard.press('ArrowDown');
  await expect(todo.getByRole('button').first()).toHaveText(secondTitle);
  await expect(todo.getByRole('button').nth(1)).toHaveText(firstTodo);

  // Everything was saved: a reload shows the same board.
  await am.reload();
  await expect(column(am, 'In progress').getByRole('button', { name: title })).toBeVisible();
  await expect(todo.getByRole('button').first()).toHaveText(secondTitle);
  errors.expectClean('moving cards with the keyboard');
});

function escape(text: string) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

test('task drawer: fields, assignees, checklist, comments and attachments', async ({ as }) => {
  const { id: projectId } = need('project');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(`/agency/projects/${projectId}`);
  const internalTitle = 'Review keyword rankings and opportunities';
  await am.getByRole('button', { name: internalTitle }).click();
  const drawer = modal(am, internalTitle);
  await expect(drawer).toBeVisible();
  const details = drawer.getByRole('form', { name: 'Task details' });
  await expect(details.getByRole('switch', { name: 'Visible to the client' })).not.toBeChecked();

  // Fields and two assignees.
  await details.getByLabel('Priority').selectOption('High');
  await details.getByLabel('Estimate (hours)').fill('3.5');
  await details.getByLabel('Description').fill(`Keyword gap analysis (${runId()}).`);
  await details.getByRole('checkbox', { name: staffNames.strategist }).check();
  await details.getByRole('checkbox', { name: staffNames.am }).check();
  await details.getByRole('button', { name: 'Save task' }).click();
  await expect(toast(am, 'Task saved')).toBeVisible();

  // Checklist.
  await drawer.getByLabel('New checklist item').fill('Export Search Console queries');
  await drawer.getByRole('button', { name: 'Add', exact: true }).click();
  const item = drawer.getByRole('checkbox', { name: 'Export Search Console queries' });
  // The box reflects the saved state: it turns checked once the server confirms.
  await item.click();
  await expect(item).toBeChecked();

  // Comment with a mention; edit it; delete a second one.
  const comments = drawer.getByRole('list', { name: 'Comments, oldest first' });
  await drawer.getByLabel('Mention').selectOption({ label: staffNames.strategist });
  await expect(drawer.getByLabel('Add a comment')).toHaveValue(`@${staffNames.strategist} `);
  await drawer.getByLabel('Add a comment').pressSequentially('can you pull the data by Friday?');
  await drawer.getByRole('button', { name: 'Comment', exact: true }).click();
  await expect(comments.getByRole('listitem')).toHaveCount(1);
  await expect(comments).toContainText(`mentioned @${staffNames.strategist}`);
  await comments.getByRole('button', { name: `Edit comment by ${staffNames.am}` }).click();
  await comments.getByLabel('Edit comment').fill(`@${staffNames.strategist} can you pull the data by Thursday?`);
  await comments.getByRole('button', { name: 'Save' }).click();
  await expect(comments).toContainText('by Thursday?');
  await expect(comments).toContainText('(edited)');
  await drawer.getByLabel('Add a comment').fill('Scratch that.');
  await drawer.getByRole('button', { name: 'Comment', exact: true }).click();
  await expect(comments.getByRole('listitem')).toHaveCount(2);
  await comments.getByRole('listitem').nth(1).getByRole('button', { name: /^Delete/ }).click();
  await modal(am, 'Delete this comment?').getByRole('button', { name: 'Delete' }).click();
  await expect(comments.getByRole('listitem')).toHaveCount(1);

  // Attachments: a fake image is refused, a PNG is attached.
  errors.ignore(/HTTP 400 POST \S+\/files$/);
  const attach = drawer.getByLabel('Attach a file');
  await attach.setInputFiles(fakePng('keywords.png'));
  await drawer.getByRole('button', { name: 'Upload', exact: true }).click();
  await expect(drawer.getByRole('alert')).toContainText('Upload a PNG, JPEG, WebP, PDF or MP4 file');
  await attach.setInputFiles(png(21, 'keyword-gap.png'));
  await drawer.getByRole('button', { name: 'Upload', exact: true }).click();
  await expect(drawer.getByRole('img', { name: 'keyword-gap.png' })).toBeVisible();

  // After a reload everything is still there.
  await am.reload();
  await expect(drawer.getByRole('form', { name: 'Task details' }).getByLabel('Priority')).toHaveValue('High');
  await expect(drawer.getByRole('checkbox', { name: staffNames.strategist })).toBeChecked();
  await expect(drawer.getByRole('checkbox', { name: 'Export Search Console queries' })).toBeChecked();
  await expect(drawer.getByRole('img', { name: 'keyword-gap.png' })).toBeVisible();

  const api = await login(accounts.am);
  const tasks = await api.get<{ id: string; title: string; clientVisible: boolean; assignees: { displayName: string }[] }[]>(`/agency/projects/${projectId}/tasks`);
  const internal = tasks.find((t) => t.title === internalTitle)!;
  expect(internal.assignees.map((a) => a.displayName).sort()).toEqual([staffNames.am, staffNames.strategist].sort());
  const detail = await api.get<{ attachments: { file: { id: string } }[] }>(`/agency/tasks/${internal.id}`);
  const shared = tasks.find((t) => t.clientVisible)!;
  saveState({
    internalTask: { id: internal.id, title: internalTitle, attachmentFileId: detail.attachments[0]!.file.id },
    sharedTask: { id: shared.id, title: shared.title },
  });
  errors.expectClean('working a task in the drawer');
});

test('two people save the same task: the second gets a conflict instead of overwriting', async ({ as }) => {
  const { id: projectId } = need('project');
  const { id: taskId, title } = need('sharedTask');
  const am = await as(accounts.am, landing.agency);
  const strategist = await as(accounts.strategist, landing.agency);
  const strategistErrors = watchErrors(strategist);
  strategistErrors.ignore(new RegExp(`HTTP 409 PUT \\S+/tasks/${taskId}$`));
  for (const page of [am, strategist]) await page.goto(`/agency/projects/${projectId}?task=${taskId}`);
  const amForm = modal(am, title).getByRole('form', { name: 'Task details' });
  const stForm = modal(strategist, title).getByRole('form', { name: 'Task details' });
  await expect(stForm.getByLabel('Priority')).toBeVisible();
  await amForm.getByLabel('Priority').selectOption('Urgent');
  await amForm.getByRole('button', { name: 'Save task' }).click();
  await expect(toast(am, 'Task saved')).toBeVisible();

  await stForm.getByLabel('Priority').selectOption('Low');
  await stForm.getByRole('button', { name: 'Save task' }).click();
  await expect(stForm.getByRole('alert')).toContainText(/changed|reload|someone/i);
  const api = await login(accounts.am);
  const detail = await api.get<{ task: { priority: string } }>(`/agency/tasks/${taskId}`);
  expect(detail.task.priority, 'the first save is kept').toBe('Urgent');
  strategistErrors.expectClean('the conflicting task save');
});

test('the client sees only client-visible tasks and milestones', async ({ as }) => {
  const { id: projectId, name } = need('project');
  const { id: clientId } = need('client');
  const internal = need('internalTask');
  const shared = need('sharedTask');
  const owner = await as(clientUser('Owner'), landing.client);
  const errors = watchErrors(owner);
  await owner.goto('/client/projects');
  await owner.getByRole('link', { name }).click();
  await expect(owner.getByRole('heading', { level: 2, name })).toBeVisible();
  const tasks = owner.getByRole('table', { name: 'Tasks' });
  await expect(tasks).toContainText(shared.title);
  await expect(tasks).not.toContainText(internal.title);
  await expect(tasks).not.toContainText('Link-building outreach');
  const milestones = owner.getByRole('list', { name: 'Milestones' });
  await expect(milestones).toContainText('Technical health check');
  await expect(milestones).not.toContainText('Monthly report & review call');

  // The internal task's attachment is not a client file.
  const api = await login(clientUser('Owner'));
  expect((await raw(api, 'GET', `/client/orgs/${clientId}/files/${internal.attachmentFileId}`)).status).toBe(404);
  expect(await statusOf(api.get(`/client/orgs/${clientId}/projects/${projectId}`))).toBe(200);
  errors.expectClean('the client project view');
});
