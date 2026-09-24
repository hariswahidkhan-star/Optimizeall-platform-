import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { TagInput } from './TagInput';

function Countries() {
  const [value, setValue] = useState<string[]>([]);
  return (
    <TagInput
      value={value}
      onChange={setValue}
      itemLabel="country"
      itemLabelPlural="countries"
      normalize={(s) => (/^[A-Za-z]{2}$/.test(s.trim()) ? s.trim().toUpperCase() : '')}
    />
  );
}

describe('TagInput', () => {
  it('names the selected list with the proper plural and each remove button with the singular', async () => {
    const user = userEvent.setup();
    render(<Countries />);
    await user.type(screen.getByRole('textbox'), 'gb{Enter}PK{Enter}');
    const list = screen.getByRole('list', { name: 'Selected countries' });
    expect(list).toHaveTextContent('GBPK');
    expect(screen.getByRole('button', { name: 'Remove country GB' })).toBeInTheDocument();
  });

  it('defaults the plural to the item label + "s"', async () => {
    const user = userEvent.setup();
    function Topics() {
      const [value, setValue] = useState<string[]>([]);
      return <TagInput value={value} onChange={setValue} itemLabel="topic" />;
    }
    render(<Topics />);
    await user.type(screen.getByRole('textbox'), 'skincare{Enter}');
    expect(screen.getByRole('list', { name: 'Selected topics' })).toHaveTextContent('skincare');
  });
});
