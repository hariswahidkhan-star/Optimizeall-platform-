import { act, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/render';
import { DataTable } from './DataTable';
import { FormField } from './FormField';
import { Input } from './Input';
import { ScrollArea } from './ScrollArea';
import { useToast } from './toastContext';
import { ToastProvider } from './ToastProvider';

/** jsdom has no layout: fake an element whose content is wider than itself, and a ResizeObserver. */
function fakeOverflow(scrollWidth: number, clientWidth: number) {
  vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockReturnValue(scrollWidth);
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(clientWidth);
  vi.spyOn(HTMLElement.prototype, 'scrollHeight', 'get').mockReturnValue(100);
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(100);
}

class NoopResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}

describe('ScrollArea', () => {
  beforeEach(() => vi.stubGlobal('ResizeObserver', NoopResizeObserver));

  it('is a plain container, with no tab stop or name, while its content fits', async () => {
    fakeOverflow(300, 300);
    const { container } = render(
      <ScrollArea label="Wide content" data-testid="area">
        <p>Short</p>
      </ScrollArea>,
    );
    const area = screen.getByTestId('area');
    expect(area).not.toHaveAttribute('tabindex');
    expect(area).not.toHaveAttribute('role');
    expect(area).not.toHaveAttribute('aria-label');
    expect(await axeViolations(container)).toEqual([]);
  });

  it('becomes a focusable, named group while its content overflows (scrollable-region-focusable)', async () => {
    fakeOverflow(900, 300);
    const { container } = render(
      <ScrollArea label="Wide content">
        <p>A very wide line of text</p>
      </ScrollArea>,
    );
    const group = screen.getByRole('group', { name: 'Wide content' });
    expect(group).toHaveAttribute('tabindex', '0');
    expect(group).toHaveAttribute('data-scrollable', 'true');
    expect(await axeViolations(container)).toEqual([]);
  });

  it('can be named by another element (a caption)', () => {
    fakeOverflow(900, 300);
    render(
      <>
        <p id="cap">Line items</p>
        <ScrollArea labelledBy="cap">
          <p>Wide</p>
        </ScrollArea>
      </>,
    );
    expect(screen.getByRole('group', { name: 'Line items' })).toHaveAttribute('tabindex', '0');
  });
});

describe('DataTable scrolling', () => {
  beforeEach(() => vi.stubGlobal('ResizeObserver', NoopResizeObserver));

  it('a table wider than its container scrolls in a focusable group named by the caption', async () => {
    fakeOverflow(1400, 700);
    const { container } = render(
      <DataTable
        caption="Reconciliation"
        columns={[
          { id: 'a', header: 'Item', cell: (r: { id: string }) => r.id, primary: true },
          { id: 'b', header: 'Amount', cell: () => '10.00' },
        ]}
        rows={[{ id: 'Row 1' }, { id: 'Row 2' }]}
        getRowId={(r) => r.id}
      />,
    );
    const group = screen.getByRole('group', { name: 'Reconciliation' });
    expect(group).toHaveAttribute('tabindex', '0');
    expect(group).toContainElement(screen.getByRole('table', { name: 'Reconciliation' }));
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('FormField with a file input', () => {
  it('labels the input and wires hint and error to it', async () => {
    const { container } = render(
      <FormField label="CV" hint="PDF only, up to 5 MB." error="Choose a PDF file.">
        <Input type="file" accept="application/pdf" />
      </FormField>,
    );
    const input = screen.getByLabelText('CV');
    expect(input).toHaveAttribute('type', 'file');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    // The error is read first, then the hint.
    expect(input).toHaveAccessibleDescription('Choose a PDF file. PDF only, up to 5 MB.');
    expect(await axeViolations(container)).toEqual([]);
  });
});

function ToastButtons() {
  const toast = useToast();
  return (
    <>
      <button type="button" onClick={() => toast.success('Saved')}>
        success
      </button>
      <button type="button" onClick={() => toast.error('Could not save', 'The server is unavailable.')}>
        error
      </button>
    </>
  );
}

describe('Toasts', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('announce in a polite live region; success toasts auto-dismiss, error toasts stay until dismissed', () => {
    render(
      <MemoryRouter>
        <ToastProvider>
          <ToastButtons />
        </ToastProvider>
      </MemoryRouter>,
    );
    const region = screen.getByRole('region', { name: 'Notifications' });
    expect(region.querySelector('[aria-live="polite"]')).not.toBeNull();

    act(() => screen.getByRole('button', { name: 'success' }).click());
    act(() => screen.getByRole('button', { name: 'error' }).click());
    expect(region).toHaveTextContent('Saved');
    expect(region).toHaveTextContent('Could not save');

    act(() => vi.advanceTimersByTime(60_000));
    expect(region).not.toHaveTextContent('Saved');
    expect(region).toHaveTextContent('Could not save');

    act(() => screen.getByRole('button', { name: 'Dismiss notification' }).click());
    expect(region).not.toHaveTextContent('Could not save');
  });
});
