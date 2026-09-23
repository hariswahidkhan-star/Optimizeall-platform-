import { Card, CardBody, CardHeader, EmptyState } from '@/components/ui';
import { formatBytes } from '@/lib/format/text';
import type { BrandKit } from './deliveryTypes';
import { FilePreview } from './deliveryUi';

function Items({ title, items }: { title: string; items: string[] }) {
  return (
    <Card as="section" aria-label={title}>
      <CardHeader title={title} headingLevel={3} />
      <CardBody>
        {items.length === 0 ? (
          <p className="dl-muted">Not set yet.</p>
        ) : (
          <ul>
            {items.map((i) => (
              <li key={i}>{i}</li>
            ))}
          </ul>
        )}
      </CardBody>
    </Card>
  );
}

/** Read-only brand kit (client portal and staff view). */
export function BrandKitView({ kit, audience }: { kit: BrandKit; audience: 'staff' | 'client' }) {
  return (
    <div className="dl-page">
      <div className="dl-grid dl-grid--wide">
        <Card as="section" aria-label="Colours">
          <CardHeader title="Colours" headingLevel={3} />
          <CardBody>
            {kit.colors.length === 0 ? (
              <p className="dl-muted">Not set yet.</p>
            ) : (
              <ul className="dl-list" aria-label="Brand colours">
                {kit.colors.map((c) => (
                  <li key={c.hex + c.name} className="dl-row">
                    <span className="dl-swatch" style={{ background: c.hex }} aria-hidden="true" />
                    <span>
                      {c.name} <code>{c.hex}</code>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </CardBody>
        </Card>
        <Items title="Fonts" items={kit.fonts} />
        <Card as="section" aria-label="Tone of voice">
          <CardHeader title="Tone of voice" headingLevel={3} />
          <CardBody>
            <p className="dl-report__body">{kit.toneOfVoice ?? 'Not set yet.'}</p>
          </CardBody>
        </Card>
        <Items title="Key messages" items={kit.keyMessages} />
        <Items title="Do" items={kit.dos} />
        <Items title="Don't" items={kit.donts} />
        <Items title="Competitors" items={kit.competitors} />
        <Card as="section" aria-label="Audience personas">
          <CardHeader title="Audience personas" headingLevel={3} />
          <CardBody>
            {kit.personas.length === 0 ? (
              <p className="dl-muted">Not set yet.</p>
            ) : (
              <dl>
                {kit.personas.map((p) => (
                  <div key={p.name}>
                    <dt>
                      <strong>{p.name}</strong>
                    </dt>
                    <dd>{p.description}</dd>
                  </div>
                ))}
              </dl>
            )}
          </CardBody>
        </Card>
      </div>
      <section aria-labelledby="brand-assets-heading" className="dl-page">
        <h3 id="brand-assets-heading">Assets</h3>
        {kit.assets.length === 0 ? (
          <EmptyState compact title="No brand assets yet" description="Logos, guidelines and photography appear here." />
        ) : (
          <ul className="dl-grid" aria-label="Brand assets">
            {kit.assets.map((a) => (
              <li key={a.id} className="dl-compare__pane">
                <strong>{a.label}</strong>
                <span className="dl-meta">
                  {a.kind} · {formatBytes(a.sizeBytes)}
                </span>
                <FilePreview
                  file={{ id: a.fileId, fileName: a.fileName, contentType: a.contentType, sizeBytes: a.sizeBytes, createdAt: a.createdAt, staffUrl: a.staffUrl, clientUrl: a.clientUrl }}
                  url={audience === 'staff' ? a.staffUrl : a.clientUrl}
                  alt={a.label}
                />
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
