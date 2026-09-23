import { Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { FormField, Input, Select } from '@/components/ui';
import { useCampaignOptions, type CampaignOption } from '@/lib/api/campaignOptions';
import { humanize } from '@/lib/format/text';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';

export interface CampaignPickerProps {
  value: string;
  onChange: (campaignId: string) => void;
}

const optionLabel = (c: CampaignOption) => `${c.title} (${humanize(c.status)})`;

/**
 * Searchable campaign select for list filters: "Find campaign" narrows `GET /campaigns/options` on the server (the
 * newest 500 without a search) and the select picks one. The chosen campaign stays listed while the search changes.
 */
export function CampaignPicker({ value, onChange }: CampaignPickerProps) {
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search, 300);
  const options = useCampaignOptions(debounced);
  const [selected, setSelected] = useState<CampaignOption | null>(null);

  const choices = useMemo(() => {
    const list = (options.data ?? []).map((c) => ({ value: c.id, label: optionLabel(c) }));
    if (value && !list.some((o) => o.value === value))
      list.unshift({
        value,
        label: selected?.id === value ? optionLabel(selected) : 'Selected campaign',
      });
    return list;
  }, [options.data, value, selected]);

  return (
    <>
      <FormField label="Find campaign">
        <Input
          type="search"
          size="sm"
          value={search}
          leading={<Search />}
          placeholder="Title or slug"
          autoComplete="off"
          onChange={(e) => setSearch(e.target.value)}
        />
      </FormField>
      <FormField label="Campaign">
        <Select
          size="sm"
          value={value}
          placeholder={options.isPending ? 'Loading campaigns…' : 'All campaigns'}
          options={choices}
          onChange={(e) => {
            const id = e.target.value;
            setSelected(options.data?.find((c) => c.id === id) ?? null);
            onChange(id);
          }}
        />
      </FormField>
    </>
  );
}
