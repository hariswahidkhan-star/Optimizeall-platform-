import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { HandCoins, Link2, Search } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  Dialog,
  ErrorState,
  FormField,
  Input,
  Money,
  Select,
  SkeletonText,
  Textarea,
  type DataTableColumn,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { useCampaignOptions } from '@/lib/api/campaignOptions';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { rk, usePersonRates } from '../api/queries';
import type { EffectiveRate, RateAssignment, RateExplanation } from '../api/types';
import { AssignRateDialog } from '../components/AssignRateDialog';
import { AssignmentsTable } from '../components/AssignmentsTable';
import { LevelBadge } from '../components/badges';
import {
  emptyRates,
  RateLinesEditor,
  ratesHints,
  ratesToInput,
  type RatesForm,
} from '../components/RateLinesEditor';
import { RateExplanationView } from '../components/RateExplanationView';
import { formatLabel, formatOptions, platformOptions, utcInputToIso } from '../labels';
import '../rates.css';

/**
 * The "Rates" section of a person (admin user detail): groups they are in, assignments and negotiated deals, their
 * effective rate per platform/format (optionally for one campaign) and "explain this rate".
 */
export function PersonRatesSection({ userId, displayName }: { userId: string; displayName: string }) {
  const { hasPermission } = useAuth();
  const canAssign = hasPermission(Permissions.RatesAssign);
  const canManage = hasPermission(Permissions.RatesManage);
  const [campaignId, setCampaignId] = useState('');
  const campaigns = useCampaignOptions();
  const rates = usePersonRates(userId, campaignId || undefined);
  const [dialog, setDialog] = useState<'assign' | 'custom' | null>(null);
  const [explain, setExplain] = useState<{ platform: string; format: string | null } | null>(null);

  const effectiveColumns: DataTableColumn<EffectiveRate>[] = [
    {
      id: 'post',
      header: 'Post',
      primary: true,
      cell: (r) => (
        <span>
          {r.platform} <span className="text-muted">· {formatLabel(r.format)}</span>
        </span>
      ),
    },
    {
      id: 'rate',
      header: 'Rate',
      align: 'right',
      cell: (r) => <Money amount={r.amount} currency={r.currency} />,
    },
    { id: 'level', header: 'Source', cell: (r) => <LevelBadge level={r.level} /> },
    {
      id: 'detail',
      header: 'Detail',
      hideOnMobile: true,
      cell: (r) => (
        <span className="text-small">
          {r.sourceLabel}
          {r.validTo && (
            <>
              {' '}
              · until <DateTime value={r.validTo} format="date" />
            </>
          )}
        </span>
      ),
    },
    {
      id: 'explain',
      header: <span className="visually-hidden">Explain</span>,
      cell: (r) => (
        <Button
          size="sm"
          variant="ghost"
          leadingIcon={<Search />}
          onClick={() => setExplain({ platform: r.platform, format: r.format })}
        >
          Explain
        </Button>
      ),
    },
  ];

  return (
    <Card as="section" aria-labelledby="user-rates" id="rates">
      <CardHeader
        titleId="user-rates"
        title="Rates"
        description="Groups, personal deals and the rate this person gets per post. Only staff with rates access see this."
        actions={
          <div className="cluster rt-cluster-sm">
            {canAssign && (
              <Button
                size="sm"
                variant="secondary"
                leadingIcon={<Link2 />}
                onClick={() => setDialog('assign')}
              >
                Assign card
              </Button>
            )}
            {canManage && (
              <Button size="sm" leadingIcon={<HandCoins />} onClick={() => setDialog('custom')}>
                Custom rate
              </Button>
            )}
          </div>
        }
      />
      <CardBody className="stack">
        <FormField
          label="Campaign"
          hint="Show the rates that apply in one campaign (campaign-scoped rates and the campaign's policy)."
        >
          <Select
            value={campaignId}
            options={[
              { value: '', label: 'All campaigns (global rates)' },
              ...(campaigns.data ?? []).map((c) => ({ value: c.id, label: c.title })),
            ]}
            onChange={(e) => setCampaignId(e.target.value)}
          />
        </FormField>
        {rates.isPending ? (
          <SkeletonText lines={4} />
        ) : rates.isError ? (
          <ErrorState error={rates.error} onRetry={() => void rates.refetch()} />
        ) : (
          <>
            <div>
              <h3 className="rt-h4">Groups</h3>
              {rates.data.groups.length === 0 ? (
                <p className="text-muted">Not in any rate group.</p>
              ) : (
                <ul className="rt-chips" aria-label="Rate groups">
                  {rates.data.groups.map((g) => (
                    <li key={g.id}>
                      <Link to={`/manage/rate-groups/${g.id}`} className="ui-link">
                        {g.name}
                      </Link>{' '}
                      <Badge size="sm" tone={g.membershipMode === 'Automatic' ? 'warning' : 'neutral'}>
                        {g.membershipMode === 'Automatic'
                          ? `auto: ${g.matchedPlatforms.join(', ')}`
                          : `priority ${g.priority}`}
                      </Badge>
                    </li>
                  ))}
                </ul>
              )}
            </div>
            <div>
              <h3 className="rt-h4">Effective rate per post</h3>
              {rates.data.effective.length === 0 ? (
                <p className="text-muted">No personal or group rate: campaign rates apply.</p>
              ) : (
                <DataTable
                  caption={`Effective rates of ${displayName}`}
                  columns={effectiveColumns}
                  rows={rates.data.effective}
                  getRowId={(r) => `${r.platform}-${r.format ?? 'any'}`}
                />
              )}
            </div>
            <div>
              <h3 className="rt-h4">Assignments and deals</h3>
              {rates.data.assignments.length === 0 ? (
                <p className="text-muted">No assignments.</p>
              ) : (
                <AssignmentsTable
                  assignments={rates.data.assignments as RateAssignment[]}
                  caption={`Rate assignments of ${displayName}`}
                  show="both"
                />
              )}
            </div>
          </>
        )}
      </CardBody>
      {dialog === 'assign' && (
        <AssignRateDialog onClose={() => setDialog(null)} person={{ id: userId, displayName }} />
      )}
      {dialog === 'custom' && (
        <CustomRateDialog userId={userId} displayName={displayName} onClose={() => setDialog(null)} />
      )}
      {explain && (
        <ExplainDialog
          userId={userId}
          campaignId={campaignId || undefined}
          initial={explain}
          onClose={() => setExplain(null)}
        />
      )}
    </Card>
  );
}

export function ExplainDialog({
  userId,
  campaignId,
  initial,
  onClose,
}: {
  userId: string;
  campaignId?: string;
  initial: { platform: string; format: string | null };
  onClose: () => void;
}) {
  const [platform, setPlatform] = useState(initial.platform);
  const [format, setFormat] = useState(initial.format ?? '');
  const explain = useQuery({
    queryKey: [...rk.person(userId, campaignId), 'explain', platform, format],
    queryFn: ({ signal }) =>
      api.get<RateExplanation>(`/admin/users/${userId}/rates/explain`, {
        query: { platform, format: format || undefined, campaignId },
        signal,
      }),
  });
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="Explain this rate"
      footer={<Button onClick={onClose}>Close</Button>}
    >
      <div className="stack">
        <div className="rt-grid rt-grid--2">
          <FormField label="Platform">
            <Select
              value={platform}
              options={platformOptions}
              onChange={(e) => setPlatform(e.target.value)}
            />
          </FormField>
          <FormField label="Format">
            <Select
              value={format}
              placeholder="Any / unknown"
              options={formatOptions}
              onChange={(e) => setFormat(e.target.value)}
            />
          </FormField>
        </div>
        {explain.isPending ? (
          <SkeletonText lines={5} />
        ) : explain.isError ? (
          <ErrorState error={explain.error} onRetry={() => void explain.refetch()} />
        ) : (
          <RateExplanationView explanation={explain.data} />
        )}
      </div>
    </Dialog>
  );
}

function CustomRateDialog({
  userId,
  displayName,
  onClose,
}: {
  userId: string;
  displayName: string;
  onClose: () => void;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const campaigns = useCampaignOptions();
  const [rates, setRates] = useState<RatesForm>(emptyRates());
  const [campaignId, setCampaignId] = useState('');
  const [validFrom, setValidFrom] = useState('');
  const [validTo, setValidTo] = useState('');
  const [reason, setReason] = useState('');
  const hints = ratesHints(rates);
  const save = useMutation({
    mutationFn: () =>
      api.post<RateAssignment>(`/admin/users/${userId}/custom-rates`, {
        ...ratesToInput(rates),
        campaignId: campaignId || null,
        validFrom: utcInputToIso(validFrom),
        validTo: utcInputToIso(validTo),
        reason: reason.trim(),
      }),
    onSuccess: async () => {
      toast.success('Custom rate saved', displayName);
      await queryClient.invalidateQueries({ queryKey: rk.all });
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={`Custom rate for ${displayName}`}
      description="A negotiated deal for this person only. It outranks group rates at the same scope; caps and the campaign budget still apply."
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button
            onClick={() => save.mutate()}
            loading={save.isPending}
            disabled={reason.trim().length < 5 || hints.length > 0}
          >
            Save custom rate
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && (
          <Alert tone="danger" title="Not saved">
            {errorMessage(save.error)}
          </Alert>
        )}
        <FormField
          label="Where"
          hint="A deal for one campaign outranks every rate that applies to all campaigns."
        >
          <Select
            value={campaignId}
            options={[
              { value: '', label: 'Every campaign' },
              ...(campaigns.data ?? [])
                .filter((c) => c.status !== 'Archived' && c.status !== 'Ended')
                .map((c) => ({ value: c.id, label: c.title })),
            ]}
            onChange={(e) => setCampaignId(e.target.value)}
          />
        </FormField>
        <RateLinesEditor value={rates} onChange={setRates} idPrefix="custom" />
        {hints.length > 0 && (
          <Alert tone="warning" title="Check the rates">
            <ul className="rt-list">
              {hints.map((h) => (
                <li key={h}>{h}</li>
              ))}
            </ul>
          </Alert>
        )}
        <div className="rt-grid rt-grid--2">
          <FormField label="Valid from (UTC)" optional>
            <Input type="datetime-local" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} />
          </FormField>
          <FormField label="Valid until (UTC)" optional hint="The deal's expiry; participants see it.">
            <Input type="datetime-local" value={validTo} onChange={(e) => setValidTo(e.target.value)} />
          </FormField>
        </div>
        <FormField label="Reason / deal reference" required hint="At least 5 characters. Audited.">
          <Textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}
