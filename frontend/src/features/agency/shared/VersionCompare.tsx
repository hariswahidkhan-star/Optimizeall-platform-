import { useId, useState } from 'react';
import { DateTime, FormField, Select } from '@/components/ui';
import type { DeliverableComment, DeliverableVersion } from './deliveryTypes';
import { VersionContent } from './deliveryUi';

interface Props {
  versions: DeliverableVersion[];
  comments: DeliverableComment[];
  /** Which file URL to use (staff: /agency/files, client: /client/orgs/{id}/files). */
  audience: 'staff' | 'client';
}

function Pane({ label, version, comments, audience, onSelect, versions }: {
  label: string;
  version: DeliverableVersion | undefined;
  comments: DeliverableComment[];
  audience: 'staff' | 'client';
  versions: DeliverableVersion[];
  onSelect: (n: number) => void;
}) {
  const id = useId();
  if (!version) return null;
  const pinned = comments.filter((c) => c.versionNumber === version.number);
  return (
    <section className="dl-compare__pane" aria-labelledby={id}>
      <h2 id={id} className="visually-hidden">
        {label}: version {version.number}
      </h2>
      <FormField label={label}>
        <Select
          value={String(version.number)}
          onChange={(e) => onSelect(Number(e.target.value))}
          options={versions.map((v) => ({ value: String(v.number), label: `Version ${v.number}` }))}
        />
      </FormField>
      <p className="dl-meta">
        <span>{version.createdBy.displayName}</span>
        <DateTime value={version.createdAt} format="both" />
      </p>
      {version.notes ? <p>{version.notes}</p> : null}
      <VersionContent
        file={version.file}
        linkUrl={version.linkUrl}
        body={version.body}
        fileUrl={version.file ? (audience === 'staff' ? version.file.staffUrl : version.file.clientUrl) : null}
        label={`Version ${version.number}`}
      />
      <h3>Comments on version {version.number}</h3>
      {pinned.length === 0 ? (
        <p className="dl-muted">No comments on this version.</p>
      ) : (
        <ul className="dl-comments" aria-label={`Comments on version ${version.number}`}>
          {pinned.map((c) => (
            <li key={c.id} className="dl-comment" data-internal={c.isInternal}>
              <div className="dl-comment__head">
                <strong>{c.author.displayName}</strong>
                {c.fromClient ? <span>(client)</span> : null}
                {c.isInternal ? <span>Internal — not visible to the client</span> : null}
                <DateTime value={c.createdAt} format="relative" />
              </div>
              {c.body}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/**
 * Side-by-side version compare: the latest version on the right, any earlier one on the left (single column on
 * phones). Comments are pinned to the version they were made on.
 */
export function VersionCompare({ versions, comments, audience }: Props) {
  const sorted = [...versions].sort((a, b) => b.number - a.number);
  const latest = sorted[0]?.number ?? 0;
  const [right, setRight] = useState<number | null>(null);
  const [left, setLeft] = useState<number | null>(null);
  if (sorted.length === 0) return <p className="dl-muted">No versions yet.</p>;
  const rightVersion = sorted.find((v) => v.number === (right ?? latest));
  const leftVersion = sorted.length > 1 ? sorted.find((v) => v.number === (left ?? sorted[1]!.number)) : undefined;
  return (
    <div className="dl-compare">
      {leftVersion ? (
        <Pane label="Compare with" version={leftVersion} comments={comments} audience={audience} versions={sorted} onSelect={setLeft} />
      ) : null}
      <Pane label="Showing" version={rightVersion} comments={comments} audience={audience} versions={sorted} onSelect={setRight} />
    </div>
  );
}
