import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { Alert, Button, Card, CardBody, CardHeader, ErrorState, FormField, Skeleton, Textarea } from '@/components/ui';
import type { NpsStatus } from '@/features/agency/shared/deliveryTypes';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { ClientShell } from './ClientShell';
import { clientKeys } from './useClientOrg';

/** A 0–10 or 1–5 scale as a radio group (keyboard: arrow keys). */
export function ScorePicker({ legend, min, max, value, onChange, lowLabel, highLabel }: {
  legend: string;
  min: number;
  max: number;
  value: number | null;
  onChange: (v: number) => void;
  lowLabel: string;
  highLabel: string;
}) {
  const name = useId();
  return (
    <fieldset className="dl-form">
      <legend>{legend}</legend>
      <div className="cc-scale">
        {Array.from({ length: max - min + 1 }, (_, i) => min + i).map((n) => (
          <label key={n} className="cc-scale__option">
            <input type="radio" name={name} value={n} checked={value === n} onChange={() => onChange(n)} />
            <span>{n}</span>
          </label>
        ))}
      </div>
      <span className="dl-meta">
        <span>
          {min} = {lowLabel}
        </span>
        <span>
          {max} = {highLabel}
        </span>
      </span>
    </fieldset>
  );
}

export function NpsSurvey({ base, orgId, period }: { base: string; orgId: string; period: string }) {
  const qc = useQueryClient();
  const [score, setScore] = useState<number | null>(null);
  const [comment, setComment] = useState('');
  const submit = useMutation({
    mutationFn: () => api.post<NpsStatus>(`${base}/feedback/nps`, { score, comment: comment || null }),
    onSuccess: (s) => {
      qc.setQueryData(clientKeys.part(orgId, 'nps'), s);
      void qc.invalidateQueries({ queryKey: clientKeys.part(orgId, 'home') });
    },
  });
  if (submit.isSuccess) return <Alert tone="success" title="Thank you!">Your feedback helps us improve.</Alert>;
  return (
    <Card as="section" aria-label="Quarterly survey">
      <CardHeader title={`Quick survey (${period})`} headingLevel={2} description="Takes 20 seconds. Your account team sees your answer." />
      <CardBody>
        <form
          className="dl-form"
          onSubmit={(e) => {
            e.preventDefault();
            if (score !== null) submit.mutate();
          }}
        >
          {submit.error ? <Alert tone="danger">{errorMessage(submit.error)}</Alert> : null}
          <ScorePicker
            legend="How likely are you to recommend us to a friend or colleague?"
            min={0}
            max={10}
            value={score}
            onChange={setScore}
            lowLabel="not at all likely"
            highLabel="extremely likely"
          />
          <FormField label="What's the main reason for your score?" optional>
            <Textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} maxLength={2000} />
          </FormField>
          <div className="dl-row">
            <Button type="submit" disabled={score === null} loading={submit.isPending}>
              Send feedback
            </Button>
          </div>
        </form>
      </CardBody>
    </Card>
  );
}

function Feedback({ base, orgId }: { base: string; orgId: string }) {
  const nps = useQuery({
    queryKey: clientKeys.part(orgId, 'nps'),
    queryFn: ({ signal }) => api.get<NpsStatus>(`${base}/feedback/nps`, { signal }),
  });
  if (nps.isPending) return <Skeleton height={160} />;
  if (nps.isError) return <ErrorState error={nps.error} />;
  if (!nps.data.due)
    return (
      <Alert tone="success" title="Thanks for your feedback">
        You answered this quarter's survey ({nps.data.period}) with {nps.data.myScore}/10. You can also rate each deliverable once you approve it.
      </Alert>
    );
  return <NpsSurvey base={base} orgId={orgId} period={nps.data.period} />;
}

export function FeedbackPage() {
  return (
    <ClientShell title="Feedback" description="Tell us how we're doing.">
      {({ base, org }) => <Feedback key={org.clientId} base={base} orgId={org.clientId} />}
    </ClientShell>
  );
}
