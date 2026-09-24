import {
  accounts,
  clientUser,
  expect,
  fakePng,
  landing,
  login,
  modal,
  need,
  pdf,
  png,
  raw,
  runId,
  staffNames,
  statusOf,
  test,
  watchErrors,
} from './support/delivery';

/**
 * Messages and meetings: the client Owner starts a conversation with a PDF, the account manager replies with an image;
 * both sides see the attachments and read receipts. Files shared in a conversation reach the client; internal files (a
 * task attachment, an unsent version) do not. The account manager schedules a meeting, adds notes and action items,
 * turns one into a task on the project board; the client sees the upcoming meeting but not the internal notes.
 */

const subject = () => `Launch timing ${runId()}`;

test('client and account manager exchange messages with attachments and read receipts', async ({ as }) => {
  const { id: clientId } = need('client');
  const owner = await as(clientUser('Owner'), landing.client);
  const ownerErrors = watchErrors(owner);
  await owner.goto('/client/messages');
  const compose = owner.getByRole('form', { name: 'New conversation' });
  await compose.getByLabel('Subject').fill(subject());
  await compose.getByLabel('Message').fill('Can we move the homepage launch to the 15th? Timeline attached.');
  await compose.getByLabel('Attachments').setInputFiles(pdf('launch-timeline.pdf'));
  await compose.getByRole('button', { name: 'Start conversation' }).dblclick();
  const thread = owner.getByRole('list', { name: 'Messages, oldest first' });
  await expect(thread.getByRole('listitem')).toHaveCount(1);
  await expect(thread).toContainText('launch-timeline.pdf');
  await expect(owner.getByRole('list', { name: 'Conversations' }).getByRole('button', { name: subject() })).toHaveCount(1);
  ownerErrors.expectClean('starting a conversation');

  // The account manager sees it unread, replies with an image; a fake image is refused.
  const am = await as(accounts.am, landing.agency);
  const amErrors = watchErrors(am);
  await am.goto(`/agency/clients/${clientId}?tab=messages`);
  const item = am.getByRole('list', { name: 'Conversations' }).getByRole('listitem').filter({ hasText: subject() });
  await expect(item).toContainText('1 unread');
  await item.getByRole('button', { name: subject() }).click();
  await expect(am.getByRole('list', { name: 'Messages, oldest first' })).toContainText('(client)');
  const reply = am.getByRole('form', { name: 'Reply' });
  amErrors.ignore(/HTTP 400 POST \S+\/files$/);
  await reply.getByLabel('Your reply').fill('Yes — updated mock attached.');
  await reply.getByLabel('Attachments').setInputFiles(fakePng('mock.png'));
  await reply.getByRole('button', { name: 'Send reply' }).click();
  await expect(reply.getByRole('alert')).toContainText('Upload a PNG, JPEG, WebP, PDF or MP4 file');
  await reply.getByLabel('Attachments').setInputFiles(png(41, 'hero-mock.png'));
  await reply.getByRole('button', { name: 'Send reply' }).click();
  const staffThread = am.getByRole('list', { name: 'Messages, oldest first' });
  await expect(staffThread.getByRole('listitem')).toHaveCount(2);
  await expect(staffThread.getByRole('listitem').first()).toContainText(`Read by ${staffNames.am}`);
  amErrors.expectClean('replying to the client');

  // The client reads the reply; the receipt shows for staff.
  await owner.reload();
  await expect(thread.getByRole('listitem')).toHaveCount(2);
  await expect(thread.getByRole('listitem').nth(1)).toContainText('(your agency team)');
  await expect(thread.getByRole('img', { name: 'hero-mock.png' })).toBeVisible();
  await am.reload();
  await expect(staffThread.getByRole('listitem').nth(1)).toContainText(`Read by ${clientUser('Owner').displayName}`);

  // Shared vs internal files: the conversation's files reach every member; internal files don't.
  const viewer = await login(clientUser('Viewer'));
  const threads = await viewer.get<{ id: string; subject: string }[]>(`/client/orgs/${clientId}/threads`);
  const t = await viewer.get<{ messages: { attachments: { id: string }[] }[] }>(
    `/client/orgs/${clientId}/threads/${threads.find((x) => x.subject === subject())!.id}`,
  );
  for (const file of t.messages.flatMap((m) => m.attachments))
    expect((await raw(viewer, 'GET', `/client/orgs/${clientId}/files/${file.id}`)).status).toBe(200);
  const internal = need('internalTask');
  expect((await raw(viewer, 'GET', `/client/orgs/${clientId}/files/${internal.attachmentFileId}`)).status).toBe(404);
  // …and a client can't smuggle an internal file into a message.
  expect(
    await statusOf(viewer.post(`/client/orgs/${clientId}/threads`, { subject: 'x', body: 'x', attachmentFileIds: [internal.attachmentFileId] })),
  ).toBe(400);
});

test('meeting with notes and action items; an action item becomes a task', async ({ as }) => {
  const { id: clientId } = need('client');
  const { id: projectId, name: projectName } = need('project');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  const meeting = `Kickoff ${runId()}`;
  await am.goto(`/agency/clients/${clientId}?tab=meetings`);
  await am.getByRole('button', { name: 'Schedule meeting' }).click();
  const dialog = modal(am, 'Schedule a meeting');
  await dialog.getByLabel('Title').fill(meeting);
  await dialog.getByLabel('Type').selectOption('Kickoff');
  const start = new Date(Date.now() + 3 * 86_400_000);
  const local = `${start.getFullYear()}-${String(start.getMonth() + 1).padStart(2, '0')}-${String(start.getDate()).padStart(2, '0')}T10:00`;
  await dialog.getByLabel('Starts').fill(local);
  await dialog.getByLabel('Location or link').fill('https://meet.example.com/lumen');
  await dialog.getByLabel('Agenda').fill('Goals, access, reporting cadence.');
  await dialog.getByRole('button', { name: 'Schedule' }).click();
  await expect(dialog).toBeHidden();
  const card = am.getByRole('article', { name: meeting });
  await expect(card).toContainText('Scheduled');

  await card.getByRole('button', { name: `Edit ${meeting}` }).click();
  const editor = modal(am, `Edit ${meeting}`);
  await editor.getByLabel('Notes').fill('Internal: client is price sensitive.');
  await editor.getByLabel('New action item').fill('Send the GA4 access guide');
  await editor.getByRole('button', { name: 'Add', exact: true }).click();
  await editor.getByLabel('New action item').fill('Draft the Q4 content calendar');
  await editor.getByRole('button', { name: 'Add', exact: true }).click();
  await editor.getByRole('button', { name: 'Save meeting' }).click();
  await expect(editor).toBeHidden();
  const actions = card.getByRole('list', { name: 'Action items' });
  await expect(actions.getByRole('listitem')).toHaveCount(2);

  await card.getByLabel('Project for “Draft the Q4 content calendar”').selectOption({ label: projectName });
  await card.getByRole('button', { name: 'Create task for Draft the Q4 content calendar' }).click();
  await expect(actions.getByRole('listitem').filter({ hasText: 'Draft the Q4 content calendar' })).toContainText('Task created');
  const api = await login(accounts.am);
  const tasks = await api.get<{ title: string }[]>(`/agency/projects/${projectId}/tasks`);
  expect(tasks.filter((t) => t.title === 'Draft the Q4 content calendar')).toHaveLength(1);
  // Converting the same action item twice is refused.
  const meetings = await api.get<{ id: string; title: string; actionItems: { id: string; text: string }[] }[]>(`/agency/meetings?clientId=${clientId}`);
  const m = meetings.find((x) => x.title === meeting)!;
  const converted = m.actionItems.find((a) => a.text === 'Draft the Q4 content calendar')!;
  expect(await statusOf(api.post(`/agency/meetings/${m.id}/action-items/${converted.id}/convert`, { projectId }))).toBe(409);
  errors.expectClean('the meeting and its action items');

  // The client sees the upcoming meeting but not the internal notes.
  const owner = await as(clientUser('Owner'), landing.client);
  const upcoming = owner.getByRole('list', { name: 'Upcoming meetings' });
  await expect(upcoming).toContainText(meeting);
  const ownerApi = await login(clientUser('Owner'));
  const clientMeetings = await ownerApi.get<{ title: string; notes: string | null; actionItems: unknown[] }[]>(`/client/orgs/${clientId}/meetings`);
  const seen = clientMeetings.find((x) => x.title === meeting)!;
  expect(seen.notes).toBeNull();
  expect(seen.actionItems).toEqual([]);
});
