export interface FaqItem {
  id: string;
  question: string;
  answer: string;
}

export interface FaqGroup {
  category: string;
  items: FaqItem[];
}

type RawItem = { id?: string | number; question?: string; answer?: string; category?: string | null };
type RawGroup = { category?: string | null; name?: string | null; items?: RawItem[] };

const DEFAULT_CATEGORY = 'General';

function toItem(raw: RawItem, index: number): FaqItem | null {
  if (!raw.question || !raw.answer) return null;
  return { id: String(raw.id ?? `${index}-${raw.question}`), question: raw.question, answer: raw.answer };
}

/**
 * Accepts the shapes the content endpoint may return — an array of groups `{ category, items }`, an object with
 * `groups`/`categories`/`items`, or a flat array of items carrying `category` — and returns ordered groups.
 */
export function normalizeFaqs(data: unknown): FaqGroup[] {
  if (!data) return [];
  const source = Array.isArray(data)
    ? data
    : ((data as Record<string, unknown>).groups ??
      (data as Record<string, unknown>).categories ??
      (data as Record<string, unknown>).items ??
      []);
  if (!Array.isArray(source)) return [];

  const groups = new Map<string, FaqItem[]>();
  source.forEach((entry: RawGroup & RawItem, index) => {
    if (Array.isArray(entry.items)) {
      const name = entry.category ?? entry.name ?? DEFAULT_CATEGORY;
      const items = entry.items.map(toItem).filter((i): i is FaqItem => i !== null);
      groups.set(name, [...(groups.get(name) ?? []), ...items]);
      return;
    }
    const item = toItem(entry, index);
    if (!item) return;
    const name = entry.category ?? DEFAULT_CATEGORY;
    groups.set(name, [...(groups.get(name) ?? []), item]);
  });

  return [...groups.entries()]
    .filter(([, items]) => items.length > 0)
    .map(([category, items]) => ({ category, items }));
}
