import { Download, ExternalLink, FileText, Film } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { ProtectedImage } from '@/components/ProtectedImage';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { Badge, Button, type Tone } from '@/components/ui';
import { api } from '@/lib/api/client';
import { humanize } from '@/lib/format/text';
import type {
  ClientStatus,
  DeliverableStatus,
  DeliveryFile,
  HealthLevel,
  KpiMeasurement,
  ProjectStatus,
  TaskStatus,
} from './deliveryTypes';
import './delivery-shared.css';

// ---------------------------------------------------------------- labels & badges

const deliverableTones: Record<DeliverableStatus, Tone> = {
  Draft: 'neutral',
  InternalReview: 'info',
  ClientReview: 'warning',
  ChangesRequested: 'danger',
  Approved: 'success',
  Published: 'brand',
};

const deliverableLabels: Record<DeliverableStatus, string> = {
  Draft: 'Draft',
  InternalReview: 'Internal review',
  ClientReview: 'Awaiting client',
  ChangesRequested: 'Changes requested',
  Approved: 'Approved',
  Published: 'Published',
};

/** Client-facing wording for the same states (the tone is shared). */
const deliverableClientLabels: Partial<Record<DeliverableStatus, string>> = {
  ClientReview: 'Awaiting your review',
  ChangesRequested: 'Changes in progress',
};

export function deliverableStatusLabel(status: DeliverableStatus, audience: 'staff' | 'client' = 'staff'): string {
  return (audience === 'client' && deliverableClientLabels[status]) || deliverableLabels[status];
}

export function DeliverableStatusBadge({ status, audience = 'staff' }: { status: DeliverableStatus; audience?: 'staff' | 'client' }) {
  return (
    <Badge tone={deliverableTones[status]} dot>
      {deliverableStatusLabel(status, audience)}
    </Badge>
  );
}

const taskTones: Record<TaskStatus, Tone> = {
  Todo: 'neutral',
  InProgress: 'info',
  InReview: 'warning',
  Blocked: 'danger',
  Done: 'success',
};

export const taskStatusLabel = (s: TaskStatus) => (s === 'Todo' ? 'To do' : s === 'InProgress' ? 'In progress' : s === 'InReview' ? 'In review' : s);

export function TaskStatusBadge({ status }: { status: TaskStatus }) {
  return (
    <Badge tone={taskTones[status]} size="sm">
      {taskStatusLabel(status)}
    </Badge>
  );
}

const projectTones: Record<ProjectStatus, Tone> = {
  Planning: 'info',
  Active: 'success',
  OnHold: 'warning',
  Completed: 'brand',
  Cancelled: 'neutral',
};

export function ProjectStatusBadge({ status }: { status: ProjectStatus }) {
  return <Badge tone={projectTones[status]}>{status === 'OnHold' ? 'On hold' : status}</Badge>;
}

const clientTones: Record<ClientStatus, Tone> = { Onboarding: 'info', Active: 'success', Paused: 'warning', Churned: 'neutral' };

export function ClientStatusBadge({ status }: { status: ClientStatus }) {
  return <Badge tone={clientTones[status]}>{status}</Badge>;
}

const healthTones: Record<HealthLevel, Tone> = { Green: 'success', Amber: 'warning', Red: 'danger' };

/** Red/amber/green with a text label (never color alone). */
export function HealthBadge({ level, score }: { level: HealthLevel; score?: number }) {
  return (
    <Badge tone={healthTones[level]} dot>
      {level}
      {score !== undefined ? ` · ${score}` : ''}
    </Badge>
  );
}

const measurementText: Record<KpiMeasurement, { label: string; tone: Tone; help: string }> = {
  Measured: { label: 'Measured', tone: 'success', help: 'Reported by a connected platform' },
  Estimated: { label: 'Estimated', tone: 'warning', help: 'Modelled or projected, not directly measured' },
  Manual: { label: 'Manual', tone: 'neutral', help: 'Entered by the agency' },
};

/** The measurement label shown next to every report number. */
export function MeasurementLabel({ measurement }: { measurement: KpiMeasurement }) {
  const m = measurementText[measurement];
  return (
    <Badge tone={m.tone} size="sm" title={m.help}>
      {m.label}
    </Badge>
  );
}

// ---------------------------------------------------------------- formatting

export function formatMinutes(minutes: number): string {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  if (h === 0) return `${m}m`;
  return m === 0 ? `${h}h` : `${h}h ${m}m`;
}

/** `yyyy-MM-dd` (a calendar date, not an instant) → "23 Sep 2026" without time-zone shifts. */
export function formatDateOnly(value: string | null | undefined, options: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short', year: 'numeric' }): string {
  if (!value) return '—';
  const [y, m, d] = value.split('-').map(Number);
  return new Intl.DateTimeFormat(undefined, { ...options, timeZone: 'UTC' }).format(new Date(Date.UTC(y!, (m ?? 1) - 1, d ?? 1)));
}

export function todayIso(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

export const labelOf = (value: string) => humanize(value);

export function formatKpi(value: number | null, unit: string | null): string {
  if (value === null || value === undefined) return '—';
  const n = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value);
  if (!unit) return n;
  if (unit === '%') return `${n}%`;
  if (/^[A-Z]{3}$/.test(unit)) {
    try {
      return new Intl.NumberFormat(undefined, { style: 'currency', currency: unit, maximumFractionDigits: 2 }).format(value);
    } catch {
      return `${n} ${unit}`;
    }
  }
  return `${n}${unit.startsWith('/') ? unit : ` ${unit}`}`;
}

// ---------------------------------------------------------------- files

/**
 * Preview of a private delivery file: images inline (fetched with the session), PDFs/videos open in a new tab from a
 * blob URL. `url` is the staff or client URL of the file.
 */
export function FilePreview({ file, url, alt }: { file: DeliveryFile; url: string; alt: string }) {
  const [busy, setBusy] = useState(false);
  if (file.contentType.startsWith('image/'))
    return <ProtectedImage src={url} alt={alt} className="dl-file-preview__img" />;
  const open = async () => {
    setBusy(true);
    try {
      const blob = await api.blob(url);
      const objectUrl = URL.createObjectURL(blob);
      window.open(objectUrl, '_blank', 'noopener');
      setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="dl-file-preview__doc">
      {file.contentType === 'video/mp4' ? <Film aria-hidden="true" /> : <FileText aria-hidden="true" />}
      <span className="dl-file-preview__name">{file.fileName}</span>
      <Button variant="secondary" size="sm" leadingIcon={<Download aria-hidden="true" />} loading={busy} onClick={() => void open()}>
        Open {file.contentType === 'application/pdf' ? 'PDF' : file.contentType === 'video/mp4' ? 'video' : 'file'}
      </Button>
    </div>
  );
}

/** A version's content: file, external link and/or inline text. */
export function VersionContent({
  file,
  linkUrl,
  body,
  fileUrl,
  label,
}: {
  file: DeliveryFile | null;
  linkUrl: string | null;
  body: string | null;
  fileUrl: string | null;
  label: string;
}) {
  const parts: ReactNode[] = [];
  if (file && fileUrl) parts.push(<FilePreview key="file" file={file} url={fileUrl} alt={label} />);
  if (linkUrl)
    parts.push(
      <p key="link" className="dl-version__link">
        <ExternalLink aria-hidden="true" size={16} /> <SafeExternalLink href={linkUrl}>{linkUrl}</SafeExternalLink>
      </p>,
    );
  if (body)
    parts.push(
      <pre key="body" className="dl-version__body">
        {body}
      </pre>,
    );
  return <div className="dl-version">{parts.length > 0 ? parts : <p className="text-muted">No content.</p>}</div>;
}
