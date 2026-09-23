import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/Checkbox';
import { Dialog } from '@/components/ui/Dialog';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { Select } from '@/components/ui/Select';
import { Stepper } from '@/components/ui/Stepper';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import { EMAIL_API, emailKeys } from '../api/queries';
import type { ImportPreview, ImportResult } from '../api/types';

const MAX_BYTES = 10 * 1024 * 1024;

const TARGET_LABELS: Record<string, string> = {
  email: 'Email',
  phone: 'Phone (E.164)',
  first_name: 'First name',
  last_name: 'Last name',
  language: 'Language',
  country: 'Country',
  time_zone: 'Time zone',
  tags: 'Tags (separated by ;)',
  ignore: 'Do not import',
};

type Step = 'file' | 'map' | 'result';

/** CSV import wizard: upload → map columns → attest consent → per-row results. */
export function ImportDialog({ open, onClose, listId }: { open: boolean; onClose: () => void; listId: string }) {
  const queryClient = useQueryClient();
  const [step, setStep] = useState<Step>('file');
  const [fileName, setFileName] = useState('');
  const [csv, setCsv] = useState('');
  const [fileError, setFileError] = useState<string | null>(null);
  const [mapping, setMapping] = useState<Record<string, string>>({});
  const [tags, setTags] = useState('');
  const [consentSource, setConsentSource] = useState('');
  const [attest, setAttest] = useState(false);
  const [smsConsent, setSmsConsent] = useState(false);

  const preview = useMutation({
    mutationFn: (text: string) => api.post<ImportPreview>(`${EMAIL_API}/lists/${listId}/imports/preview`, { csv: text }),
    onSuccess: (p) => {
      setMapping(p.suggestedMapping);
      setStep('map');
    },
  });

  const start = useMutation({
    mutationFn: () =>
      api.post<ImportResult>(`${EMAIL_API}/lists/${listId}/imports`, {
        fileName,
        csv,
        mapping,
        tags: tags.split(',').map((t) => t.trim()).filter(Boolean),
        confirmConsent: attest,
        consentSource,
        grantSmsConsent: smsConsent,
      }),
    onSuccess: () => {
      setStep('result');
      void queryClient.invalidateQueries({ queryKey: emailKeys.all });
    },
  });

  const onFile = async (file: File | undefined) => {
    setFileError(null);
    if (!file) return;
    if (file.size > MAX_BYTES) {
      setFileError('The file is larger than 10 MB. Split it into smaller files.');
      return;
    }
    const text = await file.text();
    setFileName(file.name);
    setCsv(text);
    preview.mutate(text);
  };

  const targets = preview.data?.targets ?? Object.keys(TARGET_LABELS);
  const options = [
    ...targets.map((t) => ({ value: t, label: TARGET_LABELS[t] ?? t })),
    ...(preview.data?.headers ?? [])
      .map((h) => mapping[h])
      .filter((v): v is string => !!v && v.startsWith('custom.'))
      .map((v) => ({ value: v, label: `Custom field: ${v.slice(7)}` })),
  ].filter((o, i, all) => all.findIndex((x) => x.value === o.value) === i);
  const mappedIdentity = Object.values(mapping).some((v) => v === 'email' || v === 'phone');
  const result = start.data;
  const startErrors = isApiError(start.error) ? Object.values(start.error.errors ?? {}).flat() : [];

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="lg"
      title="Import contacts"
      description="CSV with a header row (UTF-8). Existing contacts are updated; unsubscribed, bounced or suppressed addresses are never re-subscribed."
      footer={
        step === 'map' ? (
          <>
            <Button variant="secondary" onClick={() => setStep('file')}>
              Back
            </Button>
            <Button onClick={() => start.mutate()} loading={start.isPending} disabled={!attest || !consentSource.trim() || !mappedIdentity}>
              Import {formatNumber(preview.data?.totalRows ?? 0)} rows
            </Button>
          </>
        ) : (
          <Button variant="secondary" onClick={onClose}>
            Close
          </Button>
        )
      }
    >
      <div className="stack">
        <Stepper
          label="Import steps"
          steps={[
            { id: 'file', title: 'Upload', status: step === 'file' ? 'current' : 'complete' },
            { id: 'map', title: 'Map & attest', status: step === 'map' ? 'current' : step === 'result' ? 'complete' : 'upcoming' },
            { id: 'result', title: 'Results', status: step === 'result' ? 'current' : 'upcoming' },
          ]}
        />
        {step === 'file' && (
          <>
            <FormField label="CSV file" hint="Up to 10 MB. Large files are processed in the background." error={fileError}>
              <Input type="file" accept=".csv,text/csv" onChange={(e) => void onFile(e.target.files?.[0])} />
            </FormField>
            {preview.isPending && <p className="email-muted">Reading the file…</p>}
            {preview.isError && <Alert tone="danger">{errorMessage(preview.error)}</Alert>}
          </>
        )}
        {step === 'map' && preview.data && (
          <>
            <p>
              {formatNumber(preview.data.totalRows)} rows in <strong>{fileName}</strong>. Map each column:
            </p>
            {preview.data.headers.map((header, i) => (
              <FormField
                key={header}
                label={header}
                hint={preview.data.sampleRows[0] ? `Example: ${preview.data.sampleRows[0][i] ?? ''}` : undefined}
              >
                <Select value={mapping[header] ?? 'ignore'} options={options} onChange={(e) => setMapping({ ...mapping, [header]: e.target.value })} />
              </FormField>
            ))}
            {!mappedIdentity && <Alert tone="warning">Map a column to Email or Phone.</Alert>}
            <FormField label="Add tags to every imported contact" optional hint="Comma-separated">
              <Input value={tags} onChange={(e) => setTags(e.target.value)} />
            </FormField>
            <FormField label="How was consent collected?" required hint="Recorded on every contact's consent history, e.g. “Checkout opt-in box, Shopify, 2024–2026”.">
              <Input value={consentSource} onChange={(e) => setConsentSource(e.target.value)} maxLength={200} />
            </FormField>
            <Checkbox
              checked={attest}
              onChange={(e) => setAttest(e.target.checked)}
              label="I confirm every contact in this file gave consent to receive marketing from this sender, and that the consent can be evidenced."
            />
            <Checkbox checked={smsConsent} onChange={(e) => setSmsConsent(e.target.checked)} label="Their consent also covered SMS (for rows with a phone number)." />
            {start.isError && (
              <Alert tone="danger" title="Import rejected">
                {startErrors.length > 0 ? startErrors.join(' ') : errorMessage(start.error)}
              </Alert>
            )}
          </>
        )}
        {step === 'result' && result && (
          <>
            <Alert tone={result.failed > 0 ? 'warning' : 'success'} title={result.status === 'Completed' ? 'Import finished' : 'Import running in the background'}>
              {formatNumber(result.created)} created · {formatNumber(result.updated)} updated · {formatNumber(result.skipped)} skipped ·{' '}
              {formatNumber(result.failed)} failed
            </Alert>
            {result.errors.length > 0 && (
              <table className="ui-table">
                <caption>Rows with problems</caption>
                <thead>
                  <tr>
                    <th scope="col">Row</th>
                    <th scope="col">Problem</th>
                  </tr>
                </thead>
                <tbody>
                  {result.errors.map((e) => (
                    <tr key={`${e.row}-${e.message}`}>
                      <td>{e.row}</td>
                      <td>{e.message}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </>
        )}
      </div>
    </Dialog>
  );
}
