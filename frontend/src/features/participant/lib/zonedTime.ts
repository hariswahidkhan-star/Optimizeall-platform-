/**
 * Wall-clock ↔ UTC conversion in an IANA time zone (the participant's profile zone), for date/time inputs.
 * `<input type="datetime-local">` yields "YYYY-MM-DDTHH:mm" with no zone; we interpret it in the user's zone and send
 * the API a UTC instant.
 */

function isValidZone(timeZone: string | undefined): timeZone is string {
  if (!timeZone) return false;
  try {
    new Intl.DateTimeFormat('en', { timeZone });
    return true;
  } catch {
    return false;
  }
}

/** Offset (ms) of `timeZone` from UTC at the given instant: local = utc + offset. */
function zoneOffsetMs(instant: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(new Date(instant));
  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value ?? 0);
  const asUtc = Date.UTC(
    get('year'),
    get('month') - 1,
    get('day'),
    get('hour'),
    get('minute'),
    get('second'),
  );
  return asUtc - Math.floor(instant / 1000) * 1000;
}

const LOCAL_RE = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2})(?::(\d{2}))?)?$/;

/**
 * "2026-09-23T14:05" in `timeZone` → ISO UTC string ("2026-09-23T09:05:00.000Z" for Asia/Karachi).
 * Returns null for malformed input. Date-only values mean local midnight.
 */
export function zonedLocalToUtcIso(local: string, timeZone?: string): string | null {
  const match = LOCAL_RE.exec(local.trim());
  if (!match) return null;
  const [, y, mo, d, h = '0', mi = '0', s = '0'] = match;
  const wall = Date.UTC(Number(y), Number(mo) - 1, Number(d), Number(h), Number(mi), Number(s));
  if (Number.isNaN(wall)) return null;
  if (!isValidZone(timeZone)) {
    // Browser zone fallback.
    const date = new Date(Number(y), Number(mo) - 1, Number(d), Number(h), Number(mi), Number(s));
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }
  // Two passes handle DST transitions (the offset at the guess can differ from the offset at the result).
  let utc = wall - zoneOffsetMs(wall, timeZone);
  utc = wall - zoneOffsetMs(utc, timeZone);
  return new Date(utc).toISOString();
}

const pad = (n: number) => String(n).padStart(2, '0');

/** UTC instant → "YYYY-MM-DDTHH:mm" wall time in `timeZone` (for datetime-local inputs). */
export function utcToZonedLocal(value: string | Date, timeZone?: string): string {
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  if (!isValidZone(timeZone)) {
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }
  const shifted = new Date(date.getTime() + zoneOffsetMs(date.getTime(), timeZone));
  return `${shifted.getUTCFullYear()}-${pad(shifted.getUTCMonth() + 1)}-${pad(shifted.getUTCDate())}T${pad(shifted.getUTCHours())}:${pad(shifted.getUTCMinutes())}`;
}

/** "YYYY-MM-DD" + n days (calendar arithmetic, zone independent). */
export function addDays(dateKey: string, days: number): string {
  const [y, m, d] = dateKey.split('-').map(Number);
  const date = new Date(Date.UTC(y ?? 1970, (m ?? 1) - 1, (d ?? 1) + days));
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
}

/** Whole days from now until an instant (ceil; 0 when past). */
export function daysUntil(value: string | Date, now: Date = new Date()): number {
  const target = value instanceof Date ? value : new Date(value);
  const diff = target.getTime() - now.getTime();
  return diff <= 0 ? 0 : Math.ceil(diff / 86_400_000);
}
