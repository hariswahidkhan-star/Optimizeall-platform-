import { useMutation } from '@tanstack/react-query';
import { CheckCircle2, XCircle } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  FormField,
  Input,
  KeyValueList,
  PageHeader,
  ProgressRing,
  RadioGroup,
  Textarea,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { OnPageResult } from './api';
import { fmt } from './common';
import './seo.css';

type Mode = 'url' | 'html' | 'text';

export function OnPageAnalyzerPage() {
  const [mode, setMode] = useState<Mode>('url');
  const [source, setSource] = useState('');
  const [keyword, setKeyword] = useState('');
  const analyze = useMutation({
    mutationFn: () => api.post<OnPageResult>('/agency/seo/analyze', { keyword, [mode]: source }),
  });
  const r = analyze.data;
  const submit = (e: FormEvent) => {
    e.preventDefault();
    analyze.mutate();
  };

  return (
    <>
      <PageHeader
        title="On-page analyzer"
        description="Score a live page, raw HTML or draft copy against a target keyword: placement, density, headings, readability, links, images and schema."
        breadcrumbs={[{ label: 'SEO', to: '/agency/seo' }, { label: 'On-page analyzer' }]}
      />
      <Card>
        <CardBody>
          <form className="stack" onSubmit={submit}>
            <RadioGroup
              legend="Analyze"
              orientation="horizontal"
              value={mode}
              onChange={(v) => {
                setMode(v as Mode);
                setSource('');
              }}
              options={[
                { value: 'url', label: 'A live URL' },
                { value: 'html', label: 'Pasted HTML' },
                { value: 'text', label: 'Draft copy' },
              ]}
            />
            {mode === 'url' ? (
              <FormField label="Page URL" required hint="Fetched by our crawler (private and internal addresses are refused).">
                <Input value={source} onChange={(e) => setSource(e.target.value)} inputMode="url" placeholder="https://www.example.com/page" />
              </FormField>
            ) : (
              <FormField label={mode === 'html' ? 'HTML' : 'Copy'} required>
                <Textarea rows={8} value={source} onChange={(e) => setSource(e.target.value)} />
              </FormField>
            )}
            <FormField label="Target keyword" required>
              <Input value={keyword} onChange={(e) => setKeyword(e.target.value)} />
            </FormField>
            <div>
              <Button type="submit" loading={analyze.isPending} disabled={!source.trim() || !keyword.trim()}>
                Analyze
              </Button>
            </div>
          </form>
        </CardBody>
      </Card>
      {analyze.isError && (
        <Alert tone="danger" title="Analysis failed">
          {errorMessage(analyze.error)}
        </Alert>
      )}
      {r && (
        <section aria-label="Analysis results" className="stack seo-analysis">
          <div className="seo-grid-2">
            <Card>
              <CardHeader title="On-page score" headingLevel={2} />
              <CardBody className="seo-score">
                <ProgressRing value={r.score} max={100} label="On-page score" centerText={`${r.score}`} size={120} />
                <KeyValueList
                  layout="inline"
                  items={[
                    { label: 'Words', value: fmt.number(r.wordCount) },
                    { label: 'Keyword uses', value: `${r.keywordOccurrences} (${r.keywordDensity}%)` },
                    { label: 'Readability', value: r.fleschReadingEase === null ? '—' : `${r.fleschReadingEase} · ${r.readabilityLabel}` },
                    { label: 'Links', value: `${r.internalLinks} internal · ${r.externalLinks} external` },
                    { label: 'Images', value: `${r.images} (${r.imagesMissingAlt} without alt)` },
                  ]}
                />
                <p className="text-small text-muted">{r.readabilityNote}</p>
              </CardBody>
            </Card>
            <Card>
              <CardHeader title="Checklist" headingLevel={2} />
              <CardBody>
                <ul className="seo-checks">
                  {r.checklist.map((c) => (
                    <li key={c.key} className={c.passed ? 'is-pass' : 'is-fail'}>
                      {c.passed ? <CheckCircle2 aria-hidden="true" /> : <XCircle aria-hidden="true" />}
                      <span>
                        <span className="seo-strong">{c.label}</span>
                        <span className="visually-hidden">{c.passed ? ' — passed' : ' — needs work'}</span>
                        <span className="text-small text-muted"> — {c.detail}</span>
                      </span>
                    </li>
                  ))}
                </ul>
              </CardBody>
            </Card>
          </div>
          <div className="seo-grid-2">
            <Card>
              <CardHeader title="Heading structure" headingLevel={2} />
              <CardBody>
                {r.headings.length === 0 ? (
                  <p className="text-muted">No headings found.</p>
                ) : (
                  <ul className="seo-outline">
                    {r.headings.map((h, i) => (
                      <li key={i} style={{ paddingInlineStart: `${(h.level - 1) * 1}rem` }}>
                        <span className="seo-hx">H{h.level}</span> {h.text}
                      </li>
                    ))}
                  </ul>
                )}
              </CardBody>
            </Card>
            <Card>
              <CardHeader title="Structured data" headingLevel={2} />
              <CardBody>
                <p>
                  Found: <strong>{r.schemaTypesFound.length > 0 ? r.schemaTypesFound.join(', ') : 'none'}</strong>
                </p>
                {r.schemaSuggestions.length > 0 && (
                  <ul className="seo-suggestions">
                    {r.schemaSuggestions.map((s) => (
                      <li key={s}>{s}</li>
                    ))}
                  </ul>
                )}
              </CardBody>
            </Card>
          </div>
        </section>
      )}
    </>
  );
}
