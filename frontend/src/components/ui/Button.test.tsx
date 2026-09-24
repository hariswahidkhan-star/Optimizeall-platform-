import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { Button } from './Button';

describe('Button', () => {
  it('runs its action on a click and on keyboard activation (click detail 0)', () => {
    const onClick = vi.fn();
    render(<Button onClick={onClick}>Save</Button>);
    fireEvent.click(screen.getByRole('button', { name: 'Save' }), { detail: 1 });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }), { detail: 0 });
    expect(onClick).toHaveBeenCalledTimes(2);
  });

  it('ignores the second click of a double click, so an action is never sent twice', () => {
    const onClick = vi.fn();
    render(<Button onClick={onClick}>Approve</Button>);
    const button = screen.getByRole('button', { name: 'Approve' });
    fireEvent.click(button, { detail: 1 });
    fireEvent.click(button, { detail: 2 });
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('does not submit its form a second time on a double click', () => {
    const onSubmit = vi.fn((e: { preventDefault: () => void }) => e.preventDefault());
    render(
      <form onSubmit={onSubmit}>
        <Button type="submit">Create client</Button>
      </form>,
    );
    const button = screen.getByRole('button', { name: 'Create client' });
    fireEvent.click(button, { detail: 1 });
    fireEvent.click(button, { detail: 2 });
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it('ignores clicks while loading', () => {
    const onClick = vi.fn();
    render(
      <Button loading onClick={onClick}>
        Send
      </Button>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Send' }), { detail: 1 });
    expect(onClick).not.toHaveBeenCalled();
  });
});
