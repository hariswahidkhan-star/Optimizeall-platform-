import type { LinkHandling, Preset } from './api';

/**
 * Instant, client-side character counting for the composer (the server re-validates with the same rules and is
 * authoritative). Mirrors `Domain/SocialMedia/PostValidator`: X uses its weighted count (Latin/general punctuation
 * code points = 1, everything else = 2, an emoji sequence = 2, every URL = 23); other networks count
 * user-perceived characters.
 */

const URL_PATTERN = /https?:\/\/[^\s]+/gi;
const HASHTAG_PATTERN = /(?<![\w&])#[\p{L}\p{N}_]+/gu;

function isLight(cp: number): boolean {
  return (
    (cp >= 0 && cp <= 4351) || (cp >= 8192 && cp <= 8205) || (cp >= 8208 && cp <= 8223) || (cp >= 8242 && cp <= 8247)
  );
}

function isEmoji(cp: number): boolean {
  return cp >= 0x1f000 || cp === 0x200d || cp === 0xfe0f || (cp >= 0x2600 && cp <= 0x27bf);
}

function graphemes(text: string): string[] {
  if (typeof Intl !== 'undefined' && 'Segmenter' in Intl) {
    const segmenter = new Intl.Segmenter(undefined, { granularity: 'grapheme' });
    return Array.from(segmenter.segment(text), (s) => s.segment);
  }
  return Array.from(text);
}

function weighted(segment: string): number {
  let total = 0;
  for (const g of graphemes(segment)) {
    const cps = Array.from(g).map((c) => c.codePointAt(0) ?? 0);
    if (cps.length > 1 && cps.some(isEmoji)) {
      total += 2;
      continue;
    }
    for (const cp of cps) total += isLight(cp) ? 1 : 2;
  }
  return total;
}

export function textLength(text: string, urlWeight: number | null): number {
  if (!text) return 0;
  if (urlWeight == null) return graphemes(text).length;
  let total = 0;
  let last = 0;
  for (const match of text.matchAll(URL_PATTERN)) {
    const index = match.index ?? 0;
    total += weighted(text.slice(last, index)) + urlWeight;
    last = index + match[0].length;
  }
  return total + weighted(text.slice(last));
}

export function normalizeHashtag(tag: string): string {
  const cleaned = tag.trim().replace(/^#+/, '').replace(/[^\p{L}\p{N}_]/gu, '');
  return cleaned ? `#${cleaned}` : '';
}

/** Body + hashtags not already present + the link when the network puts links in the text. */
export function composeText(text: string, hashtags: string[], link: string | null | undefined, handling: LinkHandling): string {
  let result = text.trimEnd();
  const existing = new Set((text.match(HASHTAG_PATTERN) ?? []).map((h) => h.toLowerCase()));
  const extra = hashtags
    .map(normalizeHashtag)
    .filter((h) => h.length > 1)
    .filter((h) => {
      const key = h.toLowerCase();
      if (existing.has(key)) return false;
      existing.add(key);
      return true;
    });
  if (extra.length > 0) result += (result ? '\n\n' : '') + extra.join(' ');
  if (handling === 'InText' && link && link.trim() && !text.includes(link)) result += (result ? ' ' : '') + link.trim();
  return result;
}

export function countHashtags(text: string): number {
  return (text.match(HASHTAG_PATTERN) ?? []).length;
}

export interface Counter {
  length: number;
  max: number;
  remaining: number;
  over: boolean;
  hashtags: number;
  finalText: string;
}

export function countFor(preset: Preset, text: string, hashtags: string[], link: string | null | undefined): Counter {
  const finalText = composeText(text, hashtags, link, preset.linkHandling);
  const length = textLength(finalText, preset.urlWeight);
  return {
    length,
    max: preset.maxTextLength,
    remaining: preset.maxTextLength - length,
    over: length > preset.maxTextLength,
    hashtags: countHashtags(finalText),
    finalText,
  };
}
