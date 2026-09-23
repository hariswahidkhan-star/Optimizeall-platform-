import { X } from 'lucide-react';
import { useState, type KeyboardEvent } from 'react';
import { Badge, IconButton, Input } from '@/components/ui';

export interface TagInputProps {
  /** Accessible name of the text box (use together with a FormField label, or on its own). */
  id?: string;
  value: string[];
  onChange: (value: string[]) => void;
  placeholder?: string;
  /** Normalizes each entry (e.g. upper-case country codes). Return '' to reject. */
  normalize?: (raw: string) => string;
  max?: number;
  disabled?: boolean;
  /** Noun for the remove buttons, e.g. "topic". */
  itemLabel?: string;
  invalid?: boolean;
  'aria-describedby'?: string;
}

/**
 * Tags as removable chips plus a text box. Enter or comma adds; Backspace on an empty box removes the last tag.
 * Pasting "a, b, c" adds several at once.
 */
export function TagInput({
  id,
  value,
  onChange,
  placeholder = 'Type and press Enter',
  normalize = (raw) => raw.trim(),
  max,
  disabled,
  itemLabel = 'item',
  invalid,
  'aria-describedby': describedBy,
}: TagInputProps) {
  const [text, setText] = useState('');

  const add = (raw: string) => {
    const incoming = raw.split(/[,\n]/).map(normalize).filter(Boolean);
    if (incoming.length === 0) return;
    const next = [...value];
    for (const item of incoming) {
      if (max && next.length >= max) break;
      if (!next.some((v) => v.toLowerCase() === item.toLowerCase())) next.push(item);
    }
    onChange(next);
    setText('');
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter' || event.key === ',') {
      event.preventDefault();
      add(text);
    } else if (event.key === 'Backspace' && text === '' && value.length > 0) {
      onChange(value.slice(0, -1));
    }
  };

  return (
    <div className="mg-tags">
      {value.length > 0 && (
        <ul className="mg-tags__list" aria-label={`Selected ${itemLabel}s`}>
          {value.map((tag) => (
            <li key={tag}>
              <Badge tone="brand">
                <span className="mg-tags__chip">
                  {tag}
                  <IconButton
                    size="sm"
                    variant="ghost"
                    label={`Remove ${itemLabel} ${tag}`}
                    icon={<X />}
                    disabled={disabled}
                    onClick={() => onChange(value.filter((v) => v !== tag))}
                  />
                </span>
              </Badge>
            </li>
          ))}
        </ul>
      )}
      <Input
        id={id}
        value={text}
        placeholder={placeholder}
        disabled={disabled || (max !== undefined && value.length >= max)}
        invalid={invalid}
        aria-describedby={describedBy}
        onChange={(e) => {
          const v = e.target.value;
          if (v.includes(',')) add(v);
          else setText(v);
        }}
        onKeyDown={onKeyDown}
        onBlur={() => add(text)}
      />
    </div>
  );
}
