import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Play, Square } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Alert, Button, FormField, Input, Select, Skeleton } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { TimeEntry } from '../shared/deliveryTypes';
import { dk, useProjectOptions, useTimer } from './api';

function elapsed(startedAt: string, now: number): string {
  const total = Math.max(0, Math.floor((now - new Date(startedAt).getTime()) / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${h}:${pad(m)}:${pad(s)}`;
}

function RunningClock({ startedAt }: { startedAt: string }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, []);
  return (
    <span className="dl-timer__clock" role="timer" aria-label="Elapsed time">
      {elapsed(startedAt, now)}
    </span>
  );
}

/** One running timer per user (enforced by the API). Start with a project and note; stop to log the time. */
export function TimerWidget() {
  const qc = useQueryClient();
  const timer = useTimer();
  const [projectId, setProjectId] = useState('');
  const [note, setNote] = useState('');
  const projects = useProjectOptions(undefined, timer.data === null);
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: dk.timer });
    void qc.invalidateQueries({ queryKey: dk.dashboard });
    void qc.invalidateQueries({ queryKey: ['delivery', 'week'] });
  };
  const start = useMutation({
    mutationFn: () => api.post<TimeEntry>('/agency/time/timer/start', { projectId, note: note || null, billable: true }),
    onSuccess: (entry) => {
      qc.setQueryData(dk.timer, entry);
      setNote('');
      refresh();
    },
  });
  const stop = useMutation({
    mutationFn: () => api.post<TimeEntry>('/agency/time/timer/stop', {}),
    onSuccess: () => {
      qc.setQueryData(dk.timer, null);
      refresh();
    },
  });

  if (timer.isPending) return <Skeleton height={48} />;
  const running = timer.data;
  if (running)
    return (
      <div className="dl-timer" aria-live="polite">
        <RunningClock startedAt={running.startedAt ?? running.date} />
        <span className="dl-list__main">
          <span className="dl-list__title">{running.projectName}</span>
          <span className="dl-meta">
            {running.clientName}
            {running.note ? ` · ${running.note}` : ''}
          </span>
        </span>
        <Button variant="danger" leadingIcon={<Square aria-hidden="true" />} loading={stop.isPending} onClick={() => stop.mutate()}>
          Stop timer
        </Button>
        {stop.error ? <Alert tone="danger">{errorMessage(stop.error)}</Alert> : null}
      </div>
    );

  return (
    <form
      className="dl-form"
      onSubmit={(e) => {
        e.preventDefault();
        if (projectId) start.mutate();
      }}
    >
      <div className="dl-form__row">
        <FormField label="Project" required>
          <Select
            value={projectId}
            onChange={(e) => setProjectId(e.target.value)}
            placeholder="Choose a project…"
            options={(projects.data ?? []).map((p) => ({ value: p.id, label: `${p.clientName} — ${p.name}` }))}
          />
        </FormField>
        <FormField label="What are you working on?" optional>
          <Input value={note} onChange={(e) => setNote(e.target.value)} maxLength={1000} />
        </FormField>
      </div>
      <div className="dl-row">
        <Button type="submit" leadingIcon={<Play aria-hidden="true" />} loading={start.isPending} disabled={!projectId}>
          Start timer
        </Button>
      </div>
      {start.error ? <Alert tone="danger">{errorMessage(start.error)}</Alert> : null}
    </form>
  );
}
