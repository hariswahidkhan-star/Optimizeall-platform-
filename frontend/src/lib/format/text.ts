import { browserLocale } from './locale';

/** "UnderReview" → "Under review", "PENDING_APPROVAL" → "Pending approval", "social-accounts" → "Social accounts". */
export function humanize(value: string): string {
  const spaced = value
    .replace(/[_-]+/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .trim()
    .toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/** Up to two initials from a display name or email. */
export function initials(name: string): string {
  const source = name.includes('@') ? name.split('@')[0]!.replace(/[._-]+/g, ' ') : name;
  const words = source.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  const first = words[0]!.charAt(0);
  const last = words.length > 1 ? words[words.length - 1]!.charAt(0) : '';
  return (first + last).toUpperCase();
}

/** "1 post" / "3 posts" — pass the plural when it isn't singular + "s". */
export function pluralize(count: number, singular: string, plural = `${singular}s`): string {
  return `${new Intl.NumberFormat(browserLocale()).format(count)} ${count === 1 ? singular : plural}`;
}

export function truncate(value: string, max: number): string {
  if (value.length <= max) return value;
  return `${value.slice(0, Math.max(0, max - 1)).trimEnd()}…`;
}

/** First name for greetings ("Ada Lovelace" → "Ada"). */
export function firstName(displayName: string): string {
  return displayName.trim().split(/\s+/)[0] ?? displayName;
}

/** Formats bytes as "1.2 MB". */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unit]}`;
}
