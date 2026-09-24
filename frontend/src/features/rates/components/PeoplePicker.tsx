import { useQuery } from '@tanstack/react-query';
import { Search, X } from 'lucide-react';
import { useId, useState } from 'react';
import { Badge, Checkbox, FormField, IconButton, Input, Spinner } from '@/components/ui';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';

export interface PickedPerson {
  id: string;
  displayName: string;
  email?: string;
}

interface UserRow {
  id: string;
  email: string;
  displayName: string;
  status: string;
  tier: string;
  countryCode: string;
  isTestAccount?: boolean;
}

export function useParticipantSearch(search: string) {
  return useQuery({
    queryKey: ['rates', 'people-search', search],
    queryFn: ({ signal }) =>
      api.get<PagedResult<UserRow>>('/admin/users', {
        query: { search, role: 'Participant', pageSize: 10 },
        signal,
      }),
    enabled: search.trim().length >= 2,
    staleTime: 30_000,
  });
}

export interface PeoplePickerProps {
  label: string;
  /** Selected people (multi-select) — pass a single-element array and `single` for one person. */
  value: PickedPerson[];
  onChange: (value: PickedPerson[]) => void;
  single?: boolean;
  hint?: string;
  error?: string;
}

/** Searches participants by name or email (users.view) and keeps a list of chosen people. */
export function PeoplePicker({ label, value, onChange, single, hint, error }: PeoplePickerProps) {
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search, 300);
  const results = useParticipantSearch(debounced);
  const listId = useId();
  const selected = new Set(value.map((p) => p.id));

  const toggle = (person: PickedPerson) => {
    if (single) {
      onChange(selected.has(person.id) ? [] : [person]);
      return;
    }
    onChange(selected.has(person.id) ? value.filter((p) => p.id !== person.id) : [...value, person]);
  };

  return (
    <fieldset className="rt-picker">
      <legend className="rt-legend">{label}</legend>
      <FormField
        label="Search participants by name or email"
        hint={hint ?? 'Type at least two characters.'}
        error={error}
      >
        <Input
          type="search"
          value={search}
          leading={<Search />}
          autoComplete="off"
          aria-controls={listId}
          onChange={(e) => setSearch(e.target.value)}
        />
      </FormField>
      <div id={listId} aria-live="polite" className="stack rt-stack-xs">
        {results.isFetching && <Spinner size="sm" label="Searching people" />}
        {results.data && results.data.items.length === 0 && (
          <p className="text-small text-muted">No participants match.</p>
        )}
        {results.data && results.data.items.length > 0 && (
          <ul className="rt-picker__results">
            {results.data.items.map((u) => (
              <li key={u.id}>
                <Checkbox
                  label={u.displayName}
                  description={`${u.email} · ${u.countryCode} · ${u.tier}${u.status !== 'Active' ? ` · ${u.status}` : ''}`}
                  checked={selected.has(u.id)}
                  onChange={() => toggle({ id: u.id, displayName: u.displayName, email: u.email })}
                />
              </li>
            ))}
          </ul>
        )}
      </div>
      {value.length > 0 && (
        <div className="rt-chips" role="group" aria-label="Selected people">
          {value.map((p) => (
            <span key={p.id} className="rt-chip">
              <Badge size="sm" tone="brand">
                {p.displayName}
              </Badge>
              <IconButton
                label={`Remove ${p.displayName}`}
                icon={<X />}
                size="sm"
                variant="ghost"
                onClick={() => onChange(value.filter((x) => x.id !== p.id))}
              />
            </span>
          ))}
        </div>
      )}
    </fieldset>
  );
}
