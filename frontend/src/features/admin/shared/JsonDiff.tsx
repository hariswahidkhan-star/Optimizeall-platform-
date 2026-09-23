import clsx from 'clsx';
import { useId } from 'react';

export interface DiffLine {
  text: string;
  changed: boolean;
}

type Json = unknown;

function isPlainObject(value: Json): value is Record<string, Json> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function same(a: Json, b: Json): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

/**
 * Pretty-prints `value` (2-space JSON) line by line, marking a line as changed when the value at that path is
 * missing from or different in `other`. Objects are compared key by key so only the changed leaves light up.
 */
export function diffLines(
  value: Json,
  other: Json,
  hasOther = other !== undefined,
  indent = 0,
  prefix = '',
  comma = '',
): DiffLine[] {
  const pad = '  '.repeat(indent);
  if (isPlainObject(value) && hasOther && isPlainObject(other)) {
    const keys = Object.keys(value);
    if (keys.length === 0) return [{ text: `${pad}${prefix}{}${comma}`, changed: !same(value, other) }];
    return [
      { text: `${pad}${prefix}{`, changed: false },
      ...keys.flatMap((key, i) =>
        diffLines(
          value[key],
          other[key],
          Object.prototype.hasOwnProperty.call(other, key),
          indent + 1,
          `${JSON.stringify(key)}: `,
          i < keys.length - 1 ? ',' : '',
        ),
      ),
      { text: `${pad}}${comma}`, changed: false },
    ];
  }
  const changed = !hasOther || !same(value, other);
  const pretty = (JSON.stringify(value, null, 2) ?? 'null').split('\n');
  return pretty.map((line, i) => ({
    text: `${i === 0 ? pad + prefix : pad}${line}${i === pretty.length - 1 ? comma : ''}`,
    changed,
  }));
}

/** Dotted paths whose values differ between before and after (top-level keys, nested for objects). */
export function changedPaths(before: Json, after: Json, base = ''): string[] {
  if (isPlainObject(before) && isPlainObject(after)) {
    const keys = [...new Set([...Object.keys(before), ...Object.keys(after)])];
    return keys.flatMap((key) => {
      const path = base ? `${base}.${key}` : key;
      const inBefore = Object.prototype.hasOwnProperty.call(before, key);
      const inAfter = Object.prototype.hasOwnProperty.call(after, key);
      if (!inBefore || !inAfter) return [path];
      return changedPaths(before[key], after[key], path);
    });
  }
  return same(before, after) ? [] : [base || '(value)'];
}

function Panel({ title, value, other }: { title: string; value: Json; other: Json }) {
  const id = useId();
  const empty = value === null || value === undefined;
  const lines = empty ? [] : diffLines(value, other, other !== null && other !== undefined);
  return (
    <figure className="admin-diff__panel" aria-labelledby={id}>
      <figcaption id={id} className="admin-diff__title">
        {title}
      </figcaption>
      {empty ? (
        <p className="admin-diff__empty">No data recorded.</p>
      ) : (
        // A scrollable region must be focusable so keyboard users can scroll it.
        // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
        <pre className="admin-diff__code" tabIndex={0}>
          <code>
            {lines.map((line, i) => (
              <span
                key={i}
                className={clsx('admin-diff__line', line.changed && 'admin-diff__line--changed')}
                data-changed={line.changed || undefined}
              >
                {line.text}
                {line.changed && <span className="visually-hidden"> (changed)</span>}
                {'\n'}
              </span>
            ))}
          </code>
        </pre>
      )}
    </figure>
  );
}

/** Side-by-side before/after JSON with changed keys highlighted, plus a list of what changed. */
export function JsonDiff({ before, after }: { before: Json; after: Json }) {
  const paths =
    before === null || before === undefined || after === null || after === undefined
      ? []
      : changedPaths(before, after);
  return (
    <div className="admin-diff">
      {paths.length > 0 && (
        <p className="admin-diff__summary">
          <strong>Changed:</strong>{' '}
          {paths.map((p, i) => (
            <span key={p}>
              <code className="admin-diff__key">{p}</code>
              {i < paths.length - 1 ? ', ' : ''}
            </span>
          ))}
        </p>
      )}
      <div className="admin-diff__panels">
        <Panel title="Before" value={before} other={after} />
        <Panel title="After" value={after} other={before} />
      </div>
    </div>
  );
}
