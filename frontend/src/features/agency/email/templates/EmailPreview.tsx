import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Alert } from '@/components/ui/Alert';
import { RadioGroup } from '@/components/ui/RadioGroup';
import { Spinner } from '@/components/ui/Spinner';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { formatBytes } from '@/lib/format/text';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { EMAIL_API } from '../api/queries';
import type { EmailDesign, RenderResult } from '../api/types';

/**
 * Server-rendered preview with sample data, on a desktop or mobile width. The HTML is shown in a sandboxed iframe
 * (no scripts, no same-origin access).
 */
export function EmailPreview({
  clientId,
  subject,
  previewText,
  design,
}: {
  clientId: string | null;
  subject: string;
  previewText?: string | null;
  design: EmailDesign;
}) {
  const [device, setDevice] = useState<'desktop' | 'mobile'>('desktop');
  const input = useDebouncedValue({ clientId, subject, previewText, design }, 400);
  const render = useQuery({
    queryKey: ['email', 'render', input],
    queryFn: ({ signal }) =>
      api.post<RenderResult>(`${EMAIL_API}/templates/render`, {
        clientAccountId: input.clientId,
        subject: input.subject,
        previewText: input.previewText,
        design: input.design,
      }, { signal }),
    placeholderData: (previous) => previous,
  });

  return (
    <section className="email-preview" aria-labelledby="email-preview-title">
      <div className="cluster" style={{ justifyContent: 'space-between' }}>
        <h2 id="email-preview-title" className="email-section-title">
          Preview
        </h2>
        {render.isFetching && <Spinner label="Rendering preview" />}
      </div>
      <RadioGroup
        legend="Preview width"
        orientation="horizontal"
        value={device}
        onChange={(v) => setDevice(v as 'desktop' | 'mobile')}
        options={[
          { value: 'desktop', label: 'Desktop' },
          { value: 'mobile', label: 'Mobile (375 px)' },
        ]}
      />
      {render.isError && <Alert tone="danger" title="Preview failed">{errorMessage(render.error)}</Alert>}
      {render.data && (
        <>
          <p className="email-muted">
            Subject: <strong>{render.data.subject || '(no subject)'}</strong> · {formatBytes(render.data.sizeBytes)}
          </p>
          {render.data.errors.length > 0 && (
            <Alert tone="danger" title="Fix before sending">
              <ul>
                {render.data.errors.map((e) => (
                  <li key={e}>{e}</li>
                ))}
              </ul>
            </Alert>
          )}
          {render.data.warnings.length > 0 && (
            <Alert tone="warning" title="Recommendations">
              <ul>
                {render.data.warnings.map((w) => (
                  <li key={w}>{w}</li>
                ))}
              </ul>
            </Alert>
          )}
          <div className="email-preview__frame-wrap">
            <iframe
              title={`Email preview (${device})`}
              className={device === 'mobile' ? 'email-preview__frame email-preview__frame--mobile' : 'email-preview__frame'}
              sandbox=""
              srcDoc={render.data.html}
            />
          </div>
          <details>
            <summary>Plain-text version</summary>
            <pre style={{ whiteSpace: 'pre-wrap' }}>{render.data.text}</pre>
          </details>
        </>
      )}
    </section>
  );
}
