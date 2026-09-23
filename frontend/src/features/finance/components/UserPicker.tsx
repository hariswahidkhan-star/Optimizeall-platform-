import { Check, Search } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { FormField, Input, Spinner } from '@/components/ui';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useUserSearch } from '../api/hooks';
import { useCan } from '../lib/useCan';

export interface PickedUser {
  id: string;
  label?: string;
}

export interface UserPickerProps {
  value: PickedUser | null;
  onChange: (value: PickedUser | null) => void;
  label?: string;
  error?: string | null;
}

/**
 * Participant picker: searches `/admin/users?search=` when the user may view users, and always accepts a pasted user
 * id (the only option without `users.view`).
 */
export function UserPicker({ value, onChange, label = 'Participant', error }: UserPickerProps) {
  const can = useCan();
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search, 300);
  const results = useUserSearch(debounced, can.viewUsers);
  const listId = useId();
  const [idText, setIdText] = useState(value?.id ?? '');
  const valueId = value?.id ?? '';

  // Follow selections made from outside (pre-filled participant, search result).
  useEffect(() => {
    setIdText((current) => (current.trim() === valueId ? current : valueId));
  }, [valueId]);

  const pick = (id: string, name?: string) => {
    setIdText(id);
    onChange({ id, label: name });
  };

  return (
    <fieldset className="fin-fieldset">
      <legend className="ui-field__label">{label}</legend>
      {can.viewUsers && (
        <div className="stack fin-userpicker">
          <FormField label="Search by name or email" hint="Type at least two characters.">
            <Input
              type="search"
              value={search}
              leading={<Search />}
              autoComplete="off"
              aria-controls={listId}
              onChange={(e) => setSearch(e.target.value)}
            />
          </FormField>
          <div id={listId} aria-live="polite">
            {results.isFetching && <Spinner size="sm" label="Searching users" />}
            {results.data && results.data.items.length === 0 && (
              <p className="text-muted text-small">No users match “{debounced}”.</p>
            )}
            {results.data && results.data.items.length > 0 && (
              <ul className="fin-userpicker__results" aria-label="Matching users">
                {results.data.items.map((u) => {
                  const selected = value?.id === u.id;
                  return (
                    <li key={u.id}>
                      <button
                        type="button"
                        className="fin-userpicker__option"
                        aria-pressed={selected}
                        onClick={() => pick(u.id, `${u.displayName} (${u.email})`)}
                      >
                        <span>
                          <strong>{u.displayName}</strong> <span className="text-muted">{u.email}</span>
                        </span>
                        {selected && <Check aria-hidden="true" />}
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        </div>
      )}
      <FormField
        label={can.viewUsers ? 'Or paste the user id' : 'User id'}
        hint={value?.label ? `Selected: ${value.label}` : 'The participant’s user id (a GUID).'}
        error={error}
        required
      >
        <Input
          value={idText}
          autoComplete="off"
          spellCheck={false}
          placeholder="00000000-0000-0000-0000-000000000000"
          onChange={(e) => {
            const text = e.target.value.trim();
            setIdText(e.target.value);
            onChange(text ? { id: text } : null);
          }}
        />
      </FormField>
    </fieldset>
  );
}
