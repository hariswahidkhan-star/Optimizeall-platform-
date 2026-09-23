/**
 * Conversions between UTC instants and wall-clock values (`YYYY-MM-DDTHH:mm`, as used by
 * `<input type="datetime-local">`) in an IANA time zone. Pure Intl, no dependencies.
 */

function validZone(timeZone: string | undefined): string {
  if (!timeZone) return 'UTC';
  try {
    new Intl.DateTimeFormat('en', { timeZone });
    return timeZone;
  } catch {
    return 'UTC';
  }
}

function zonedParts(date: Date, timeZone: string) {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: validZone(timeZone),
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(date);
  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value ?? 0);
  return {
    year: get('year'),
    month: get('month'),
    day: get('day'),
    hour: get('hour') % 24,
    minute: get('minute'),
    second: get('second'),
  };
}

/** Offset of the zone from UTC at an instant, in ms (e.g. +5h for Asia/Karachi). */
export function zoneOffsetMs(date: Date, timeZone: string): number {
  const p = zonedParts(date, timeZone);
  const asUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second);
  return asUtc - Math.floor(date.getTime() / 1000) * 1000;
}

const pad = (n: number, width = 2) => String(n).padStart(width, '0');

/** UTC ISO instant → `YYYY-MM-DDTHH:mm` wall clock in the zone ('' for empty/invalid input). */
export function isoToZonedInput(iso: string | null | undefined, timeZone: string): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const p = zonedParts(date, timeZone);
  return `${pad(p.year, 4)}-${pad(p.month)}-${pad(p.day)}T${pad(p.hour)}:${pad(p.minute)}`;
}

/** `YYYY-MM-DDTHH:mm` wall clock in the zone → UTC ISO instant (null for empty/invalid input). */
export function zonedInputToIso(value: string, timeZone: string): string | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(value);
  if (!match) return null;
  const [, y, mo, d, h, mi] = match.map(Number) as [number, number, number, number, number, number];
  const guess = Date.UTC(y, mo - 1, d, h, mi);
  const offset = zoneOffsetMs(new Date(guess), timeZone);
  let result = guess - offset;
  const second = zoneOffsetMs(new Date(result), timeZone);
  if (second !== offset) result = guess - second;
  return new Date(result).toISOString();
}

/** Every IANA zone this runtime knows (falls back to a short list on old engines). */
export function timeZoneOptions(): string[] {
  try {
    const zones = Intl.supportedValuesOf('timeZone');
    return zones.includes('UTC') ? zones : ['UTC', ...zones];
  } catch {
    return [
      'UTC',
      'Europe/London',
      'Europe/Berlin',
      'Asia/Dubai',
      'Asia/Karachi',
      'Asia/Kolkata',
      'America/New_York',
    ];
  }
}
