import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { mockStaffApi } from '../delivery/testData';
import { MessagesPanel } from './MessagesPanel';

function sizedFile(name: string, bytes: number, type = 'image/png') {
  const file = new File(['x'], name, { type });
  Object.defineProperty(file, 'size', { value: bytes });
  return file;
}

describe('MessagesPanel composer', () => {
  it('names a file over 50 MB instead of silently sending the message without it', async () => {
    const user = userEvent.setup({ applyAccept: false });
    const { calls } = mockStaffApi({ 'GET /agency/clients/c1/threads': () => json(200, []) });
    renderWithApp(<MessagesPanel base="/agency/clients/c1" audience="staff" />, { route: '/agency/clients/c1' });
    const form = await screen.findByRole('form', { name: 'New conversation' });
    await user.type(within(form).getByLabelText(/Subject/), 'Launch video');
    await user.type(within(form).getByLabelText(/Message/), 'Attached.');
    await user.upload(within(form).getByLabelText(/Attachments/), [sizedFile('ok.png', 1000), sizedFile('video.png', 50 * 1024 * 1024 + 1)]);
    expect(within(form).getByRole('alert')).toHaveTextContent('video.png is larger than 50 MB');
    expect(within(form).getByRole('button', { name: 'Start conversation' })).toBeDisabled();
    expect(calls.filter((c) => c.method === 'POST' && !c.path.startsWith('/auth/'))).toHaveLength(0);

    await user.upload(within(form).getByLabelText(/Attachments/), [sizedFile('ok.png', 1000)]);
    expect(within(form).queryByRole('alert')).not.toBeInTheDocument();
    expect(within(form).getByRole('button', { name: 'Start conversation' })).toBeEnabled();
  });

  it('refuses more than 10 attachments with a message', async () => {
    const user = userEvent.setup({ applyAccept: false });
    mockStaffApi({ 'GET /agency/clients/c1/threads': () => json(200, []) });
    renderWithApp(<MessagesPanel base="/agency/clients/c1" audience="staff" />, { route: '/agency/clients/c1' });
    const form = await screen.findByRole('form', { name: 'New conversation' });
    await user.upload(
      within(form).getByLabelText(/Attachments/),
      Array.from({ length: 11 }, (_, i) => sizedFile(`f${i}.png`, 10)),
    );
    expect(within(form).getByRole('alert')).toHaveTextContent('At most 10 attachments');
  });
});
