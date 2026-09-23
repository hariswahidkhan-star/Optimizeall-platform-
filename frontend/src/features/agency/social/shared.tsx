import { useSearchParams } from 'react-router-dom';
import { Badge, FormField, Select, type Tone } from '@/components/ui';
import { NETWORK_LABELS, STATUS_LABELS, useSocialClients, type ClientOption, type PostStatus, type SocialNetwork } from './api';

export const STATUS_TONES: Record<PostStatus, Tone> = {
  Draft: 'neutral',
  InternalReview: 'info',
  ClientApproval: 'warning',
  Approved: 'brand',
  Scheduled: 'info',
  Publishing: 'warning',
  Published: 'success',
  Failed: 'danger',
};

export function PostStatusBadge({ status }: { status: PostStatus }) {
  return (
    <Badge tone={STATUS_TONES[status]} dot>
      {STATUS_LABELS[status]}
    </Badge>
  );
}

const NETWORK_SHORT: Record<SocialNetwork, string> = {
  Facebook: 'FB',
  Instagram: 'IG',
  X: 'X',
  LinkedIn: 'IN',
  TikTok: 'TT',
  YouTube: 'YT',
  Pinterest: 'PI',
  GoogleBusiness: 'GBP',
};

/** Compact network chip; the full name is the accessible label. */
export function NetworkChip({ network }: { network: SocialNetwork }) {
  return (
    <span className={`sm-network sm-network--${network.toLowerCase()}`} title={NETWORK_LABELS[network]}>
      <span aria-hidden="true">{NETWORK_SHORT[network]}</span>
      <span className="visually-hidden">{NETWORK_LABELS[network]}</span>
    </span>
  );
}

/** The selected client lives in the URL (?client=…) so views are shareable and survive reloads. */
export function useClientParam(): [string | undefined, (id: string | undefined) => void] {
  const [params, setParams] = useSearchParams();
  const value = params.get('client') ?? undefined;
  const set = (id: string | undefined) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (id) next.set('client', id);
        else next.delete('client');
        return next;
      },
      { replace: true },
    );
  return [value, set];
}

export function ClientPicker({
  value,
  onChange,
  allowAll = false,
  label = 'Client',
  clients: provided,
}: {
  value: string | undefined;
  onChange: (id: string | undefined) => void;
  allowAll?: boolean;
  label?: string;
  clients?: ClientOption[];
}) {
  const query = useSocialClients();
  const clients = provided ?? query.data ?? [];
  return (
    <FormField label={label} className="sm-client-picker">
      <Select
        value={value ?? ''}
        onChange={(e) => onChange(e.target.value || undefined)}
        placeholder={allowAll ? 'All clients' : 'Choose a client'}
        options={clients.map((c) => ({ value: c.id, label: c.name }))}
      />
    </FormField>
  );
}

/** Measured (API or verified import) vs Manual — shown next to every metric. */
export function SourceLabel({ label }: { label: string }) {
  const tone: Tone = label === 'Measured' ? 'success' : label === 'Manual' ? 'warning' : 'neutral';
  return (
    <Badge tone={tone} size="sm" title="Measured = network API or a file exported from the network; Manual = typed in by staff">
      {label}
    </Badge>
  );
}

export function percent(value: number | null | undefined, digits = 1): string {
  if (value == null) return '—';
  return `${(value * 100).toFixed(digits)}%`;
}

export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function fromLocalInput(value: string): string | null {
  if (!value) return null;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}
