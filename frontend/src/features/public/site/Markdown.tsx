import clsx from 'clsx';
import { Fragment, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ScrollArea } from '@/components/ui/ScrollArea';
import { isExternalHref, isInternalHref } from '@/lib/safeHref';
import { usePartnerLinkRules } from '../partners/PartnerLinksContext';
import { applyPartnerLink, type PartnerLinkRule } from '../partners/partnerLinks';

/**
 * Safe Markdown renderer for CMS content. It parses a practical Markdown subset (headings, paragraphs, lists,
 * blockquotes, fenced code, tables, rules, links, images, bold/italic/code) straight into React elements — there is no
 * HTML string and no `dangerouslySetInnerHTML`, so raw HTML in the source is shown as text. Links and images only render
 * for safe destinations (app paths, http(s), mailto, in-page anchors); anything else degrades to plain text.
 */

export interface Heading {
  id: string;
  text: string;
  level: number;
}

export type Block =
  | { kind: 'heading'; level: number; text: string }
  | { kind: 'paragraph'; text: string }
  | { kind: 'list'; ordered: boolean; items: string[] }
  | { kind: 'quote'; text: string }
  | { kind: 'code'; lang: string; text: string }
  | { kind: 'table'; header: string[]; rows: string[][] }
  | { kind: 'rule' };

export function slugifyHeading(text: string): string {
  return (
    text
      .toLowerCase()
      .replace(/[`*_~[\]()]/g, '')
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 80) || 'section'
  );
}

const splitRow = (line: string) =>
  line
    .trim()
    .replace(/^\||\|$/g, '')
    .split('|')
    .map((cell) => cell.trim());

export function parseMarkdown(source: string): Block[] {
  const lines = source.replace(/\r\n?/g, '\n').split('\n');
  const blocks: Block[] = [];
  let i = 0;
  const isBlank = (l: string) => l.trim() === '';
  const startsBlock = (l: string) =>
    /^\s{0,3}(#{1,6}\s|>|```|~~~|[-*+]\s|\d+[.)]\s|(-{3,}|\*{3,}|_{3,})\s*$)/.test(l) || /^\s*\|/.test(l) || /^\s*(```|~~~)/.test(l);

  while (i < lines.length) {
    const line = lines[i];
    if (isBlank(line)) {
      i++;
      continue;
    }
    const fence = /^\s*(`{3,}|~{3,})\s*([\w-]*)/.exec(line);
    if (fence) {
      // As in the API's sanitizer (CommonMark): only a line made of the same fence character, at least as long as the
      // opening fence, closes the block. "```js" inside a block is code, not a closing fence.
      const closing = new RegExp(`^\\s*${fence[1][0] === '`' ? '`' : '~'}{${fence[1].length},}\\s*$`);
      const body: string[] = [];
      i++;
      while (i < lines.length && !closing.test(lines[i])) body.push(lines[i++]);
      i++;
      blocks.push({ kind: 'code', lang: fence[2], text: body.join('\n') });
      continue;
    }
    const heading = /^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$/.exec(line);
    if (heading) {
      blocks.push({ kind: 'heading', level: heading[1].length, text: heading[2] });
      i++;
      continue;
    }
    if (/^\s{0,3}(-{3,}|\*{3,}|_{3,})\s*$/.test(line)) {
      blocks.push({ kind: 'rule' });
      i++;
      continue;
    }
    if (/^\s*\|/.test(line) && i + 1 < lines.length && /^\s*\|?\s*:?-{3,}/.test(lines[i + 1])) {
      const header = splitRow(line);
      i += 2;
      const rows: string[][] = [];
      while (i < lines.length && /^\s*\|/.test(lines[i])) rows.push(splitRow(lines[i++]));
      blocks.push({ kind: 'table', header, rows });
      continue;
    }
    if (/^\s{0,3}>/.test(line)) {
      const body: string[] = [];
      while (i < lines.length && /^\s{0,3}>/.test(lines[i])) body.push(lines[i++].replace(/^\s{0,3}>\s?/, ''));
      blocks.push({ kind: 'quote', text: body.join('\n') });
      continue;
    }
    const listItem = /^\s{0,3}([-*+]|\d+[.)])\s+(.*)$/.exec(line);
    if (listItem) {
      const ordered = /\d/.test(listItem[1]);
      const items: string[] = [];
      while (i < lines.length) {
        const m = /^\s{0,3}([-*+]|\d+[.)])\s+(.*)$/.exec(lines[i]);
        if (m && /\d/.test(m[1]) === ordered) {
          items.push(m[2]);
          i++;
        } else if (!isBlank(lines[i]) && /^\s{2,}\S/.test(lines[i]) && items.length > 0) {
          items[items.length - 1] += ` ${lines[i].trim()}`;
          i++;
        } else break;
      }
      blocks.push({ kind: 'list', ordered, items });
      continue;
    }
    const body: string[] = [];
    while (i < lines.length && !isBlank(lines[i]) && !(body.length > 0 && startsBlock(lines[i]))) body.push(lines[i++]);
    blocks.push({ kind: 'paragraph', text: body.join('\n') });
  }
  return blocks;
}

/** The shallowest heading level used in a document ('#' = 1). */
function topLevel(blocks: readonly Block[]): number {
  const levels = blocks.flatMap((b) => (b.kind === 'heading' ? [b.level] : []));
  return levels.length > 0 ? Math.min(...levels) : 1;
}

/** Maps a Markdown heading to an HTML level so the document's shallowest heading renders at `minLevel`. */
function headingLevel(level: number, top: number, minLevel: number): number {
  return Math.min(6, level - top + minLevel);
}

/** Headings (h2/h3 in the rendered page) for a table of contents. */
export function extractHeadings(source: string, maxLevel = 3): Heading[] {
  const used = new Map<string, number>();
  return parseMarkdown(source)
    .filter((b): b is Extract<Block, { kind: 'heading' }> => b.kind === 'heading')
    .map((b, _i, all) => ({ level: headingLevel(b.level, topLevel(all), 2), text: plain(b.text), id: uniqueId(slugifyHeading(b.text), used) }))
    .filter((h) => h.level <= maxLevel);
}

function uniqueId(id: string, used: Map<string, number>): string {
  const n = used.get(id) ?? 0;
  used.set(id, n + 1);
  return n === 0 ? id : `${id}-${n}`;
}

function plain(text: string): string {
  return text.replace(/!?\[([^\]]*)\]\([^)]*\)/g, '$1').replace(/[*_`~]/g, '');
}

function isSafeImage(src: string): boolean {
  return (isInternalHref(src) && src.startsWith('/api/v1/files/')) || (isExternalHref(src) && src.startsWith('https://'));
}

function isSafeLink(href: string): boolean {
  return isInternalHref(href) || isExternalHref(href) || /^#[\w-]+$/.test(href) || /^mailto:[^\s@]+@[^\s@]+$/i.test(href);
}

const INLINE =
  /(`+)([^`]+?)\1|!\[([^\]]*)\]\(\s*([^)\s]+)(?:\s+"[^"]*")?\s*\)|\[([^\]]+)\]\(\s*([^)\s]+)(?:\s+"([^"]*)")?\s*\)|\*\*([^*]+?)\*\*|__([^_]+?)__|\*([^*\s][^*]*?)\*|(?<![\w])_([^_\s][^_]*?)_(?![\w])| {2,}\n/g;

/**
 * Renders inline Markdown. Links to an active partner's website (`partnerLinks`, see partners/partnerLinks.ts) are
 * partnership links: they get `rel="sponsored noopener"`, a new tab and the partner's UTM tags.
 */
export function renderInline(text: string, keyPrefix = 'i', partnerLinks: readonly PartnerLinkRule[] = []): ReactNode[] {
  const out: ReactNode[] = [];
  let last = 0;
  let n = 0;
  for (const m of text.matchAll(INLINE)) {
    const index = m.index ?? 0;
    if (index > last) out.push(text.slice(last, index));
    const key = `${keyPrefix}-${n++}`;
    if (m[1]) out.push(<code key={key}>{m[2]}</code>);
    else if (m[4] !== undefined) {
      out.push(isSafeImage(m[4]) ? <img key={key} src={m[4]} alt={m[3]} loading="lazy" decoding="async" /> : m[3]);
    } else if (m[6] !== undefined) {
      const label = renderInline(m[5], key, partnerLinks);
      const href = m[6];
      const partner = isSafeLink(href) ? applyPartnerLink(href, partnerLinks) : null;
      if (!isSafeLink(href)) out.push(<Fragment key={key}>{label}</Fragment>);
      else if (partner)
        out.push(
          <a key={key} href={partner.href} title={m[7]} target={partner.target} rel={partner.rel} data-partner={partner.partnerSlug}>
            {label}
            <span className="visually-hidden"> (opens in a new tab)</span>
          </a>,
        );
      else if (href.startsWith('/') && isInternalHref(href))
        out.push(
          <Link key={key} to={href} title={m[7]}>
            {label}
          </Link>,
        );
      else if (href.startsWith('#') || href.startsWith('mailto:'))
        out.push(
          <a key={key} href={href} title={m[7]}>
            {label}
          </a>,
        );
      else
        out.push(
          <a key={key} href={href} title={m[7]} target="_blank" rel="noopener noreferrer">
            {label}
            <span className="visually-hidden"> (opens in a new tab)</span>
          </a>,
        );
    } else if (m[8] !== undefined || m[9] !== undefined)
      out.push(<strong key={key}>{renderInline(m[8] ?? m[9], key, partnerLinks)}</strong>);
    else if (m[10] !== undefined || m[11] !== undefined)
      out.push(<em key={key}>{renderInline(m[10] ?? m[11], key, partnerLinks)}</em>);
    else out.push(<br key={key} />);
    last = index + m[0].length;
  }
  if (last < text.length) out.push(text.slice(last));
  return out;
}

export interface MarkdownProps {
  source: string | null | undefined;
  className?: string;
  /**
   * HTML level of the document's shallowest heading (default 2, i.e. under the page's h1), so authors may start at
   * '#' or '##' and the page outline never skips a level.
   */
  minLevel?: number;
  /**
   * Rendered once inside the text (e.g. the blog's inline partner unit): before the second top-level heading, or after
   * the third block of a long text without headings. Short texts get none (see `interludeIndex`).
   */
  interlude?: ReactNode;
}

/** Where `interlude` goes: the index of the block it precedes, or -1. */
export function interludeIndex(blocks: readonly Block[]): number {
  const top = topLevel(blocks);
  const headings = blocks.map((b, i) => (b.kind === 'heading' && b.level === top ? i : -1)).filter((i) => i >= 0);
  if (headings.length >= 2) return headings[1];
  if (headings.length === 1 && headings[0] >= 2) return headings[0];
  return blocks.length >= 5 ? 3 : -1;
}

export function Markdown({ source, className, minLevel = 2, interlude }: MarkdownProps) {
  const partnerLinks = usePartnerLinkRules();
  if (!source) return null;
  const used = new Map<string, number>();
  const blocks = parseMarkdown(source);
  const top = topLevel(blocks);
  const interludeAt = interlude ? interludeIndex(blocks) : -1;
  const renderBlock = (block: Block, key: string): ReactNode => {
    switch (block.kind) {
      case 'heading': {
        const level = headingLevel(block.level, top, minLevel);
        const Tag = `h${level}` as 'h2';
        return (
          <Tag key={key} id={uniqueId(slugifyHeading(block.text), used)}>
            {renderInline(block.text, key, partnerLinks)}
          </Tag>
        );
      }
      case 'paragraph':
        return <p key={key}>{renderInline(block.text, key, partnerLinks)}</p>;
      case 'list': {
        const Tag = block.ordered ? 'ol' : 'ul';
        return (
          <Tag key={key}>
            {block.items.map((item, j) => (
              <li key={j}>{renderInline(item, `${key}-${j}`, partnerLinks)}</li>
            ))}
          </Tag>
        );
      }
      case 'quote':
        return (
          <blockquote key={key}>
            <Markdown source={block.text} minLevel={minLevel} className="site-prose--nested" />
          </blockquote>
        );
      case 'code':
        return (
          <pre key={key}>
            <code>{block.text}</code>
          </pre>
        );
      case 'table':
        return (
          <ScrollArea key={key} className="site-prose__table" label="Table">
            <table>
              <thead>
                <tr>
                  {block.header.map((cell, j) => (
                    <th key={j} scope="col">
                      {renderInline(cell, `${key}-h${j}`, partnerLinks)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {block.rows.map((row, r) => (
                  <tr key={r}>
                    {row.map((cell, j) => (
                      <td key={j}>{renderInline(cell, `${key}-${r}-${j}`, partnerLinks)}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </ScrollArea>
        );
      case 'rule':
        return <hr key={key} />;
      default:
        return null;
    }
  };
  return (
    <div className={clsx('site-prose', className)}>
      {blocks.map((block, index) => {
        const key = `b${index}`;
        const node = renderBlock(block, key);
        return index === interludeAt ? (
          <Fragment key={key}>
            {interlude}
            {node}
          </Fragment>
        ) : (
          node
        );
      })}
    </div>
  );
}
