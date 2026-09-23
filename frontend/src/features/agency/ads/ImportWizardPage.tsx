import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Download, FileSpreadsheet } from 'lucide-react';
import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  DataTable,
  FormField,
  Input,
  PageHeader,
  RadioGroup,
  Select,
  Stepper,
  Textarea,
  type Step,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { PLATFORM_LABELS, adsKeys, useAdAccounts, type ImportPreview, type ImportResult, type ImportTemplate } from './api';
import { money } from './shared';
import './ads.css';

type Stage = 'source' | 'mapping' | 'done';

/**
 * CSV import wizard: choose the account and template (Google Ads / Meta export / generic), preview with the suggested
 * column mapping, adjust it, then import. Re-importing the same days updates rows instead of duplicating them.
 */
export function ImportWizardPage() {
  const [params] = useSearchParams();
  const accounts = useAdAccounts();
  const queryClient = useQueryClient();
  const templates = useQuery({ queryKey: adsKeys.templates(), queryFn: () => api.get<ImportTemplate[]>('/agency/ads/import/templates') });
  const [accountId, setAccountId] = useState(params.get('account') ?? '');
  const [template, setTemplate] = useState('google-ads');
  const [csv, setCsv] = useState('');
  const [fileName, setFileName] = useState('import.csv');
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [mapping, setMapping] = useState<Record<string, string>>({});
  const [allowPartial, setAllowPartial] = useState(false);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [error, setError] = useState<{ title: string; rows: string[] } | null>(null);
  const stage: Stage = result ? 'done' : preview ? 'mapping' : 'source';
  const account = accounts.data?.find((a) => a.id === accountId);

  const doPreview = useMutation({
    mutationFn: (withMapping: boolean) =>
      api.post<ImportPreview>(`/agency/ads/accounts/${accountId}/import/preview`, {
        template,
        fileName,
        csv,
        mapping: withMapping ? clean(mapping) : undefined,
      }),
    onSuccess: (p) => {
      setError(null);
      setPreview(p);
      setMapping(p.mapping);
    },
    onError: (e) => setError({ title: errorMessage(e), rows: [] }),
  });
  const doImport = useMutation({
    mutationFn: () =>
      api.post<ImportResult>(`/agency/ads/accounts/${accountId}/import`, { template, fileName, csv, mapping: clean(mapping), allowPartial }),
    onSuccess: (r) => {
      setResult(r);
      void queryClient.invalidateQueries({ queryKey: adsKeys.all });
    },
    onError: (e) => setError({ title: errorMessage(e), rows: isApiError(e) ? (e.errors?.rows ?? []) : [] }),
  });

  const steps: Step[] = [
    { id: 'source', title: 'Choose file', status: stage === 'source' ? 'current' : 'complete' },
    { id: 'mapping', title: 'Map & validate', status: stage === 'mapping' ? 'current' : stage === 'done' ? 'complete' : 'upcoming' },
    { id: 'done', title: 'Import', status: stage === 'done' ? 'complete' : 'upcoming' },
  ];

  const reset = () => {
    setPreview(null);
    setResult(null);
    setError(null);
  };

  return (
    <>
      <PageHeader
        title="Import ad metrics"
        description="Upload a Google Ads or Meta Ads Manager export (or the generic template). Imported figures are labelled Measured — they come from the platform's own report."
      />
      <Stepper steps={steps} label="Import progress" />
      {error && (
        <Alert tone="danger" title="Nothing was imported" onDismiss={() => setError(null)}>
          {error.title}
          {error.rows.length > 0 && (
            <ul className="ad-list">
              {error.rows.slice(0, 20).map((r) => (
                <li key={r}>{r}</li>
              ))}
            </ul>
          )}
        </Alert>
      )}

      {stage === 'source' && (
        <Card as="section" aria-labelledby="ad-import-source">
          <CardHeader title="1. Account, template and file" titleId="ad-import-source" />
          <CardBody className="stack">
            <FormField label="Ad account" required>
              <Select
                value={accountId}
                placeholder="Choose an account"
                onChange={(e) => setAccountId(e.target.value)}
                options={(accounts.data ?? []).map((a) => ({ value: a.id, label: `${a.clientName} — ${a.name} (${PLATFORM_LABELS[a.platform]}, ${a.currency})` }))}
              />
            </FormField>
            <RadioGroup
              legend="Template"
              value={template}
              onChange={setTemplate}
              options={(templates.data ?? []).map((t) => ({ value: t.id, label: t.name }))}
            />
            {templates.data?.find((t) => t.id === template) && (
              <div>
                <Button
                  variant="link"
                  leadingIcon={<Download />}
                  onClick={() => void api.download(templates.data!.find((t) => t.id === template)!.sampleUrl, `${template}-sample.csv`)}
                >
                  Download a sample file
                </Button>
              </div>
            )}
            <FormField label="CSV file">
              <Input
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  if (!file) return;
                  setFileName(file.name);
                  void file.text().then(setCsv);
                }}
              />
            </FormField>
            <FormField label="…or paste the CSV" optional>
              <Textarea rows={6} value={csv} onChange={(e) => setCsv(e.target.value)} />
            </FormField>
            <div>
              <Button leadingIcon={<FileSpreadsheet />} disabled={!accountId || !csv.trim()} loading={doPreview.isPending} onClick={() => doPreview.mutate(false)}>
                Preview
              </Button>
            </div>
          </CardBody>
        </Card>
      )}

      {stage === 'mapping' && preview && (
        <Card as="section" aria-labelledby="ad-import-mapping">
          <CardHeader
            title="2. Column mapping and validation"
            titleId="ad-import-mapping"
            description={`Header found on row ${preview.headerRow}. ${preview.validRows} of ${preview.rowsTotal} rows are valid${
              preview.fromDate ? ` (${preview.fromDate} to ${preview.toDate})` : ''
            }.`}
          />
          <CardBody className="stack">
            <div className="ad-mapping">
              {preview.targetFields.map((field) => (
                <FormField key={field} label={`${field}${preview.requiredFields.includes(field) ? ' (required)' : ''}`}>
                  <Select
                    value={mapping[field] ?? ''}
                    placeholder="Not in file"
                    onChange={(e) => setMapping((m) => ({ ...m, [field]: e.target.value }))}
                    options={preview.headers.filter(Boolean).map((h) => ({ value: h, label: h }))}
                  />
                </FormField>
              ))}
            </div>
            <div>
              <Button variant="secondary" loading={doPreview.isPending} onClick={() => doPreview.mutate(true)}>
                Re-check with this mapping
              </Button>
            </div>
            {preview.warnings.map((w) => (
              <Alert key={w} tone="info">
                {w}
              </Alert>
            ))}
            {preview.errors.length > 0 && (
              <section aria-labelledby="ad-import-errors">
                <h3 id="ad-import-errors" className="ad-h2">
                  {preview.errors.length} problem{preview.errors.length > 1 ? 's' : ''}
                </h3>
                <ul className="ad-list ad-error">
                  {preview.errors.map((e) => (
                    <li key={e}>{e}</li>
                  ))}
                </ul>
                <Checkbox
                  label="Import the valid rows and skip the others"
                  checked={allowPartial}
                  onChange={(e) => setAllowPartial(e.target.checked)}
                />
              </section>
            )}
            <DataTable
              caption="Preview of the first valid rows"
              showCaption
              columns={[
                { id: 'row', header: 'Row', cell: (r: ImportPreview['sample'][number]) => r.rowNumber },
                { id: 'date', header: 'Date', cell: (r: ImportPreview['sample'][number]) => r.date },
                { id: 'entity', header: 'Entity', primary: true, cell: (r: ImportPreview['sample'][number]) => `${r.entity} (${r.level})` },
                { id: 'spend', header: 'Spend', align: 'right', cell: (r: ImportPreview['sample'][number]) => money(r.spend, r.currency) },
                { id: 'clicks', header: 'Clicks', align: 'right', cell: (r: ImportPreview['sample'][number]) => r.clicks },
                { id: 'conv', header: 'Conversions', align: 'right', cell: (r: ImportPreview['sample'][number]) => r.conversions },
              ]}
              rows={preview.sample}
              getRowId={(r) => String(r.rowNumber)}
            />
            <div className="cluster">
              <Button variant="secondary" onClick={reset}>
                Back
              </Button>
              <Button
                loading={doImport.isPending}
                disabled={preview.validRows === 0 || (preview.errors.length > 0 && !allowPartial)}
                onClick={() => doImport.mutate()}
              >
                Import {preview.validRows} rows into {account?.name ?? 'the account'}
              </Button>
            </div>
          </CardBody>
        </Card>
      )}

      {stage === 'done' && result && (
        <Alert
          tone="success"
          title="Import complete"
          actions={
            <Button variant="secondary" onClick={reset}>
              Import another file
            </Button>
          }
        >
          {result.rowsImported} new rows, {result.rowsUpdated} updated (already imported), {result.rowsSkipped} skipped
          {result.fromDate ? `, covering ${result.fromDate} to ${result.toDate}` : ''}. Source label: {result.sourceLabel}.
        </Alert>
      )}
    </>
  );
}

function clean(mapping: Record<string, string>): Record<string, string> {
  return Object.fromEntries(Object.entries(mapping).filter(([, v]) => v));
}
