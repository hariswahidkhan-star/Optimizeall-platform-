import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/render';
import { setViewportWidth } from '@/test/viewport';
import { DataTable, type DataTableColumn } from './DataTable';
import { FileDrop } from './FileDrop';
import { validateFile } from './fileValidation';
import { FilterBar } from './FilterBar';
import { Money } from './Money';
import { StatusBadge } from './StatusBadge';

interface Row {
  id: string;
  name: string;
  amount: number;
  status: string;
}

const rows: Row[] = [
  { id: '1', name: 'Charlie', amount: 30, status: 'Approved' },
  { id: '2', name: 'alice', amount: 10, status: 'UnderReview' },
  { id: '3', name: 'Bob', amount: 20, status: 'Rejected' },
];

const columns: DataTableColumn<Row>[] = [
  {
    id: 'name',
    header: 'Participant',
    cell: (r) => r.name,
    sortable: true,
    sortValue: (r) => r.name,
    primary: true,
  },
  { id: 'status', header: 'Status', cell: (r) => <StatusBadge kind="submission" status={r.status} /> },
  {
    id: 'amount',
    header: 'Reward',
    align: 'right',
    cell: (r) => <Money amount={r.amount} currency="USD" />,
    sortable: true,
    sortValue: (r) => r.amount,
  },
];

function renderTable(props: Partial<Parameters<typeof DataTable<Row>>[0]> = {}) {
  return render(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <DataTable caption="Submissions" columns={columns} rows={rows} getRowId={(r) => r.id} {...props} />
    </MemoryRouter>,
  );
}

const bodyNames = () =>
  screen
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('cell')[0]!.textContent);

describe('DataTable', () => {
  it('sorts locally and exposes aria-sort', async () => {
    const user = userEvent.setup();
    renderTable();
    const header = screen.getByRole('columnheader', { name: /Participant/ });
    expect(header).toHaveAttribute('aria-sort', 'none');
    expect(screen.getByRole('columnheader', { name: 'Status' })).not.toHaveAttribute('aria-sort');

    await user.click(within(header).getByRole('button'));
    expect(header).toHaveAttribute('aria-sort', 'ascending');
    expect(bodyNames()).toEqual(['alice', 'Bob', 'Charlie']);

    await user.click(within(header).getByRole('button'));
    expect(header).toHaveAttribute('aria-sort', 'descending');
    expect(bodyNames()).toEqual(['Charlie', 'Bob', 'alice']);
  });

  it('delegates sorting when controlled', async () => {
    const user = userEvent.setup();
    const onSortChange = vi.fn();
    renderTable({ sort: { id: 'amount', desc: true }, onSortChange });
    expect(screen.getByRole('columnheader', { name: /Reward/ })).toHaveAttribute('aria-sort', 'descending');
    await user.click(within(screen.getByRole('columnheader', { name: /Participant/ })).getByRole('button'));
    expect(onSortChange).toHaveBeenCalledWith({ id: 'name', desc: false });
    expect(bodyNames()).toEqual(['Charlie', 'alice', 'Bob']);
  });

  it('supports row selection including select-all', async () => {
    const user = userEvent.setup();
    function Harness() {
      const [selected, setSelected] = useState<string[]>([]);
      return (
        <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
          <DataTable
            caption="Submissions"
            columns={columns}
            rows={rows}
            getRowId={(r) => r.id}
            rowLabel={(r) => r.name}
            selectable
            selectedIds={selected}
            onSelectionChange={setSelected}
            bulkActions={(ids) => <span>bulk for {ids.length}</span>}
          />
        </MemoryRouter>
      );
    }
    render(<Harness />);
    await user.click(screen.getByRole('checkbox', { name: 'Select Bob' }));
    expect(screen.getByText('bulk for 1')).toBeInTheDocument();
    await user.click(screen.getByRole('checkbox', { name: 'Select all rows' }));
    expect(screen.getByText('bulk for 3')).toBeInTheDocument();
  });

  it('renders loading skeletons and the empty state', () => {
    const { rerender } = renderTable({ loading: true });
    expect(screen.getByRole('table')).toHaveAttribute('aria-busy', 'true');
    rerender(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <DataTable caption="Submissions" columns={columns} rows={[]} getRowId={(r) => r.id} />
      </MemoryRouter>,
    );
    expect(screen.getByRole('heading', { name: 'Nothing here yet' })).toBeInTheDocument();
  });

  it('switches to stacked cards below the md breakpoint', () => {
    setViewportWidth(360);
    renderTable({ rowActions: () => [{ id: 'open', label: 'Open' }], rowLabel: (r) => r.name });
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    const list = screen.getByRole('list', { name: 'Submissions' });
    const cards = within(list).getAllByRole('listitem');
    expect(cards).toHaveLength(3);
    expect(within(cards[0]!).getByText('Charlie')).toBeInTheDocument();
    expect(within(cards[0]!).getByText('Reward')).toBeInTheDocument();
    expect(within(cards[0]!).getByRole('button', { name: 'Actions for Charlie' })).toBeInTheDocument();
  });

  it('renders compact rows on phones: status beside the title, one meta line, every field keeps its term', async () => {
    setViewportWidth(360);
    const { container } = renderTable({ mobileLayout: 'compact' });
    const list = screen.getByRole('list', { name: 'Submissions' });
    const cards = within(list).getAllByRole('listitem');
    expect(cards).toHaveLength(3);
    const first = cards[0]!;
    expect(within(first).getByText('Charlie')).toBeInTheDocument();
    // Terms stay in the accessibility tree (visually hidden) next to their values.
    expect(within(first).getByText('Status')).toHaveClass('visually-hidden');
    expect(within(first).getByText('Reward')).toHaveClass('visually-hidden');
    expect(within(first).getByText('Approved')).toBeInTheDocument();
    expect(first.querySelector('.ui-table-card__head .ui-table-card__badge')).not.toBeNull();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('has no axe violations', async () => {
    const { container } = renderTable({
      selectable: true,
      selectedIds: [],
      onSelectionChange: () => undefined,
      rowActions: () => [{ id: 'open', label: 'Open' }],
      rowLabel: (r) => r.name,
    });
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('FileDrop', () => {
  const png = (bytes: number, name = 'shot.png', type = 'image/png') =>
    new File([new Uint8Array(bytes)], name, { type });

  it('validates type and size', () => {
    const types = ['image/png', 'image/jpeg', 'image/webp'];
    expect(validateFile(png(100), types, 1000)).toBeNull();
    expect(validateFile(png(100, 'doc.pdf', 'application/pdf'), types, 1000)).toMatch(/isn’t supported/);
    expect(validateFile(png(2000), types, 1000)).toMatch(/The limit is/);
    expect(validateFile(png(0), types, 1000)).toMatch(/empty/);
  });

  it('accepts a valid image via the keyboard-accessible file input and shows a preview', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    function Harness() {
      const [file, setFile] = useState<File | null>(null);
      return (
        <FileDrop
          label="Screenshot"
          value={file}
          onChange={(f) => {
            onChange(f);
            setFile(f);
          }}
        />
      );
    }
    render(<Harness />);
    const input = screen.getByLabelText('Screenshot');
    expect(input).toHaveAttribute('type', 'file');
    await user.upload(input, png(1024));
    expect(onChange).toHaveBeenCalledWith(expect.any(File));
    expect(screen.getByRole('img', { name: 'Preview of shot.png' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Remove' }));
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it('rejects dropped files that are too large or the wrong type', () => {
    const onChange = vi.fn();
    const { container } = render(
      <FileDrop label="Screenshot" value={null} onChange={onChange} maxSizeBytes={500} />,
    );
    const zone = container.querySelector('.ui-filedrop__zone')!;
    fireEvent.drop(zone, { dataTransfer: { files: [png(1000)] } });
    expect(screen.getByRole('alert')).toHaveTextContent(/The limit is 500 B/);
    fireEvent.drop(zone, { dataTransfer: { files: [png(10, 'a.gif', 'image/gif')] } });
    expect(screen.getByRole('alert')).toHaveTextContent(/isn’t supported/);
    expect(onChange).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Screenshot')).toHaveAttribute('aria-invalid', 'true');
  });
});

describe('FilterBar', () => {
  it('debounces search and supports removing filters and reset', async () => {
    vi.useFakeTimers();
    const onSearchChange = vi.fn();
    const onFilterChange = vi.fn();
    const onReset = vi.fn();
    render(
      <FilterBar
        onSearchChange={onSearchChange}
        filters={[{ id: 'status', label: 'Status', options: [{ value: 'Approved', label: 'Approved' }] }]}
        values={{ status: 'Approved' }}
        onFilterChange={onFilterChange}
        onReset={onReset}
      />,
    );
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search' }), { target: { value: 'ada' } });
    expect(onSearchChange).not.toHaveBeenCalled();
    act(() => {
      vi.advanceTimersByTime(300);
    });
    expect(onSearchChange).toHaveBeenCalledWith('ada');

    fireEvent.click(screen.getByRole('button', { name: 'Remove filter Status: Approved' }));
    expect(onFilterChange).toHaveBeenCalledWith('status', undefined);
    fireEvent.click(screen.getByRole('button', { name: 'Reset filters' }));
    expect(onReset).toHaveBeenCalled();
    vi.useRealTimers();
  });
});
