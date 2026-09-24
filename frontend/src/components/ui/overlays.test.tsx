import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type FormEvent, useState } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { Button } from './Button';
import { ConfirmDialog } from './ConfirmDialog';
import { Dialog } from './Dialog';
import { DropdownMenu } from './DropdownMenu';
import { Switch } from './Switch';
import { Tabs } from './Tabs';

function DialogHarness() {
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button onClick={() => setOpen(true)}>Open</Button>
      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        title="Submit proof"
        description="Paste the link"
        footer={<Button onClick={() => setOpen(false)}>Done</Button>}
      >
        <label>
          Post URL
          <input />
        </label>
      </Dialog>
    </>
  );
}

describe('Dialog', () => {
  it('is a labelled modal that traps focus, closes on Escape and restores focus', async () => {
    const user = userEvent.setup();
    render(<DialogHarness />);
    const trigger = screen.getByRole('button', { name: 'Open' });
    await user.click(trigger);

    const dialog = screen.getByRole('dialog', { name: 'Submit proof' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).toHaveAccessibleDescription('Paste the link');
    expect(document.body.style.overflow).toBe('hidden');

    const close = screen.getByRole('button', { name: 'Close dialog' });
    const input = screen.getByLabelText('Post URL');
    const done = screen.getByRole('button', { name: 'Done' });
    expect(close).toHaveFocus();

    await user.tab();
    expect(input).toHaveFocus();
    await user.tab();
    expect(done).toHaveFocus();
    await user.tab();
    expect(close).toHaveFocus();
    await user.tab({ shift: true });
    expect(done).toHaveFocus();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(document.body.style.overflow).toBe('');
  });
});

describe('ConfirmDialog', () => {
  it('confirming inside another form never submits that form (React events bubble through the portal)', async () => {
    // Regression: the CMS page editor renders its version history (and so "Restore version?") inside the page form;
    // confirming the restore also saved the page, which then made the restore itself fail with a 409.
    const user = userEvent.setup();
    const outerSubmit = vi.fn((e: React.FormEvent) => e.preventDefault());
    const onConfirm = vi.fn();
    render(
      <form aria-label="Page editor" onSubmit={outerSubmit}>
        <ConfirmDialog
          open
          onClose={() => {}}
          onConfirm={onConfirm}
          title="Restore version 1?"
          confirmLabel="Restore"
        />
      </form>,
    );
    await user.click(screen.getByRole('button', { name: 'Restore' }));
    await waitFor(() => expect(onConfirm).toHaveBeenCalledTimes(1));
    expect(outerSubmit).not.toHaveBeenCalled();
  });

  it('requires the typed phrase and a reason, then passes the reason', async () => {
    const user = userEvent.setup();
    const onConfirm = vi.fn();
    const onClose = vi.fn();
    render(
      <ConfirmDialog
        open
        onClose={onClose}
        onConfirm={onConfirm}
        title="Finalize batch?"
        tone="danger"
        confirmLabel="Finalize"
        requireReason
        confirmText="FINALIZE"
      />,
    );

    expect(screen.getByRole('alertdialog', { name: 'Finalize batch?' })).toBeInTheDocument();
    const confirm = screen.getByRole('button', { name: 'Finalize' });
    expect(confirm).toBeDisabled();

    await user.type(screen.getByLabelText(/Type FINALIZE to confirm/), 'FINAL');
    expect(confirm).toBeDisabled();
    await user.type(screen.getByLabelText(/Type FINALIZE to confirm/), 'IZE');
    expect(confirm).toBeEnabled();

    await user.click(confirm);
    expect(onConfirm).not.toHaveBeenCalled();
    expect(await screen.findByText(/Enter a reason/)).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Reason/), '  Approved by finance lead  ');
    await user.click(confirm);
    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith({ reason: 'Approved by finance lead' }));
    expect(onClose).toHaveBeenCalled();
  });

  it('shows the error and stays open when the action fails', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <ConfirmDialog
        open
        onClose={onClose}
        onConfirm={() => Promise.reject(new Error('Batch was changed by someone else.'))}
        title="Cancel batch?"
      />,
    );
    await user.click(screen.getByRole('button', { name: 'Confirm' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Batch was changed by someone else.');
    expect(onClose).not.toHaveBeenCalled();
  });

  it('confirming does not submit a form the dialog was opened from (React bubbles events through portals)', async () => {
    const user = userEvent.setup();
    const onConfirm = vi.fn();
    const onOuterSubmit = vi.fn((e: FormEvent) => e.preventDefault());
    render(
      <form aria-label="Page editor" onSubmit={onOuterSubmit}>
        <ConfirmDialog
          open
          onClose={() => undefined}
          onConfirm={onConfirm}
          title="Restore version 2?"
          confirmLabel="Restore version"
        />
      </form>,
    );
    await user.click(screen.getByRole('button', { name: 'Restore version' }));
    await waitFor(() => expect(onConfirm).toHaveBeenCalled());
    expect(onOuterSubmit).not.toHaveBeenCalled();
  });
});

describe('DropdownMenu', () => {
  it('follows the menu button pattern', async () => {
    const user = userEvent.setup();
    const onEdit = vi.fn();
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <DropdownMenu
          trigger={<Button>Actions</Button>}
          items={[
            { id: 'edit', label: 'Edit', onSelect: onEdit },
            { id: 'disabled', label: 'Archive', disabled: true },
            { id: 'delete', label: 'Delete', danger: true },
          ]}
        />
      </MemoryRouter>,
    );
    const trigger = screen.getByRole('button', { name: 'Actions' });
    expect(trigger).toHaveAttribute('aria-haspopup', 'menu');
    trigger.focus();
    await user.keyboard('{Enter}');
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    const items = screen.getAllByRole('menuitem');
    expect(items[0]).toHaveFocus();
    await user.keyboard('{ArrowDown}');
    expect(items[2]).toHaveFocus();
    await user.keyboard('{ArrowDown}');
    expect(items[0]).toHaveFocus();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();

    await user.click(trigger);
    await user.click(screen.getByRole('menuitem', { name: 'Edit' }));
    expect(onEdit).toHaveBeenCalled();
  });
});

describe('Tabs', () => {
  it('uses roving tabindex and arrow keys', async () => {
    const user = userEvent.setup();
    render(
      <Tabs
        label="Details"
        tabs={[
          { id: 'a', label: 'Overview', content: 'Overview panel' },
          { id: 'b', label: 'History', content: 'History panel' },
        ]}
      />,
    );
    const [first, second] = screen.getAllByRole('tab');
    expect(first).toHaveAttribute('tabindex', '0');
    expect(second).toHaveAttribute('tabindex', '-1');
    await user.click(first!);
    await user.keyboard('{ArrowRight}');
    expect(second).toHaveFocus();
    expect(second).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel', { name: 'History' })).toHaveTextContent('History panel');
  });
});

describe('Switch and Button', () => {
  it('toggles aria-checked', async () => {
    const user = userEvent.setup();
    function Harness() {
      const [on, setOn] = useState(false);
      return <Switch checked={on} onCheckedChange={setOn} label="Email me" />;
    }
    render(<Harness />);
    const toggle = screen.getByRole('switch', { name: 'Email me' });
    expect(toggle).toHaveAttribute('aria-checked', 'false');
    await user.click(toggle);
    expect(toggle).toHaveAttribute('aria-checked', 'true');
  });

  it('marks loading buttons busy and ignores clicks', async () => {
    const user = userEvent.setup();
    const onClick = vi.fn();
    render(
      <Button loading onClick={onClick}>
        Save
      </Button>,
    );
    const button = screen.getByRole('button', { name: 'Save' });
    expect(button).toHaveAttribute('aria-busy', 'true');
    await user.click(button);
    expect(onClick).not.toHaveBeenCalled();
  });
});
