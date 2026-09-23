let cached: string | null = null;

/**
 * The browser's preferred locale, validated. Some environments report tags Intl rejects (e.g. "en-US@posix"),
 * which would make every `Intl.*(undefined)` formatter throw — so formatters always pass this explicitly.
 */
export function browserLocale(): string {
  if (cached) return cached;
  const candidates =
    typeof navigator === 'undefined' ? [] : [...(navigator.languages ?? []), navigator.language];
  for (const candidate of candidates) {
    if (!candidate) continue;
    try {
      const [canonical] = Intl.getCanonicalLocales(candidate.split('@')[0]);
      if (canonical) {
        new Intl.NumberFormat(canonical);
        cached = canonical;
        return canonical;
      }
    } catch {
      // Try the next candidate.
    }
  }
  cached = 'en-US';
  return cached;
}
