import { formatBytes } from '@/lib/format/text';

export const IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/webp'] as const;
export const DEFAULT_MAX_BYTES = 8 * 1024 * 1024;

const TYPE_NAMES: Record<string, string> = {
  'image/png': 'PNG',
  'image/jpeg': 'JPEG',
  'image/webp': 'WebP',
};

export function describeTypes(accept: readonly string[]): string {
  const names = accept.map((t) => TYPE_NAMES[t] ?? t);
  if (names.length <= 1) return names.join('');
  return `${names.slice(0, -1).join(', ')} or ${names[names.length - 1]}`;
}

/** Returns a user-facing error, or null when the file is acceptable. */
export function validateFile(file: File, accept: readonly string[], maxBytes: number): string | null {
  if (accept.length > 0 && !accept.includes(file.type)) {
    return `“${file.name}” isn’t supported. Upload a ${describeTypes(accept)} image.`;
  }
  if (file.size > maxBytes) {
    return `“${file.name}” is ${formatBytes(file.size)}. The limit is ${formatBytes(maxBytes)}.`;
  }
  if (file.size === 0) return `“${file.name}” is empty.`;
  return null;
}
