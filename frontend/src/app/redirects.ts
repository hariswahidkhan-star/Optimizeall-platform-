function hasControlOrBackslash(value: string): boolean {
  for (let i = 0; i < value.length; i += 1) {
    const code = value.charCodeAt(i);
    if (code < 0x20 || code === 0x7f || value[i] === '\\') return true;
  }
  return false;
}

/**
 * Validates a `next` redirect target: only same-origin absolute paths are allowed. Rejects protocol-relative URLs
 * (`//evil.com`), backslash tricks (`/\evil.com`), schemes and control characters (open-redirect defence).
 */
export function safeNextPath(next: string | null | undefined): string | null {
  if (!next) return null;
  const value = next.trim();
  if (!value.startsWith('/') || value.startsWith('//')) return null;
  if (hasControlOrBackslash(value)) return null;
  try {
    const url = new URL(value, 'https://app.invalid');
    if (url.origin !== 'https://app.invalid') return null;
    return url.pathname + url.search + url.hash;
  } catch {
    return null;
  }
}
