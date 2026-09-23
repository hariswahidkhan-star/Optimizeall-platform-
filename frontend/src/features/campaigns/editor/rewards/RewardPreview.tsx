import { useMutation } from '@tanstack/react-query';
import { Calculator } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  DataTable,
  type DataTableColumn,
  FormField,
  Input,
  Money,
  Select,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import type {
  ParticipantTier,
  RewardPreviewRequest,
  RewardLine,
  RewardQuote,
  RewardRuleSet,
  RewardRuleSetInput,
  SocialPlatform,
} from '../../api/types';
import { capLabel, platformOptions, ruleTypeLabel, tierOptions, toNumberOrNull } from '../../shared/labels';
import { zonedInputToIso } from '../../shared/zonedTime';

export interface RewardPreviewProps {
  campaignId: string;
  /** Rules currently in the editor (priced as a draft). */
  draft: RewardRuleSetInput;
  /** Saved versions, newest first. */
  versions: RewardRuleSet[];
  timeZone: string;
  defaultPlatform?: SocialPlatform;
}

interface Scenario {
  source: string; // 'draft' | version number
  platform: SocialPlatform;
  countryCode: string;
  tier: ParticipantTier;
  postedAt: string;
  isFirstApprovedPost: boolean;
  earnedToday: string;
  earnedThisWeek: string;
  earnedInCampaign: string;
  qualityBonusRequested: string;
  campaignBudgetRemaining: string;
}

/** Live quote from `POST /admin/campaigns/{id}/reward-rules/preview` for a hypothetical approved post. */
export function RewardPreview({
  campaignId,
  draft,
  versions,
  timeZone,
  defaultPlatform,
}: RewardPreviewProps) {
  const [scenario, setScenario] = useState<Scenario>({
    source: 'draft',
    platform: defaultPlatform ?? 'Instagram',
    countryCode: 'US',
    tier: 'Standard',
    postedAt: '',
    isFirstApprovedPost: false,
    earnedToday: '0',
    earnedThisWeek: '0',
    earnedInCampaign: '0',
    qualityBonusRequested: '',
    campaignBudgetRemaining: '',
  });
  const set = (patch: Partial<Scenario>) => setScenario((s) => ({ ...s, ...patch }));

  const preview = useMutation({
    mutationFn: (body: RewardPreviewRequest) =>
      api.post<RewardQuote>(`/admin/campaigns/${campaignId}/reward-rules/preview`, body),
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const body: RewardPreviewRequest = {
      platform: scenario.platform,
      countryCode: scenario.countryCode.trim().toUpperCase() || 'US',
      tier: scenario.tier,
      postedAt: scenario.postedAt ? zonedInputToIso(scenario.postedAt, timeZone) : null,
      isFirstApprovedPost: scenario.isFirstApprovedPost,
      earnedToday: toNumberOrNull(scenario.earnedToday) ?? 0,
      earnedThisWeek: toNumberOrNull(scenario.earnedThisWeek) ?? 0,
      earnedInCampaign: toNumberOrNull(scenario.earnedInCampaign) ?? 0,
      qualityBonusRequested: toNumberOrNull(scenario.qualityBonusRequested),
      campaignBudgetRemaining: toNumberOrNull(scenario.campaignBudgetRemaining),
      ...(scenario.source === 'draft' ? { draft } : { ruleSetVersion: Number(scenario.source) }),
    };
    preview.mutate(body);
  };

  const quote = preview.data;
  const error = preview.error;
  const serverRuleErrors = isApiError(error) ? Object.values(error.errors ?? {}).flat() : [];

  return (
    <Card as="section" aria-labelledby="reward-preview-title" className="mg-preview">
      <CardHeader
        titleId="reward-preview-title"
        headingLevel={3}
        title="Preview a payout"
        description="Prices one hypothetical approved post with the server's reward engine."
      />
      <CardBody className="stack">
        <form className="stack" onSubmit={submit} noValidate>
          <FormField label="Price with">
            <Select
              value={scenario.source}
              onChange={(e) => set({ source: e.target.value })}
              options={[
                { value: 'draft', label: 'Rules in the editor (unsaved)' },
                ...versions.map((v) => ({
                  value: String(v.version),
                  label: `Saved version ${v.version}${v.isCurrent ? ' (current)' : ''}`,
                })),
              ]}
            />
          </FormField>
          <div className="mg-grid mg-grid--3">
            <FormField label="Platform">
              <Select
                value={scenario.platform}
                options={platformOptions}
                onChange={(e) => set({ platform: e.target.value as SocialPlatform })}
              />
            </FormField>
            <FormField label="Country">
              <Input
                value={scenario.countryCode}
                maxLength={2}
                onChange={(e) => set({ countryCode: e.target.value.toUpperCase() })}
              />
            </FormField>
            <FormField label="Tier">
              <Select
                value={scenario.tier}
                options={tierOptions}
                onChange={(e) => set({ tier: e.target.value as ParticipantTier })}
              />
            </FormField>
          </div>
          <FormField label="Posted at" optional hint={`In ${timeZone}. Blank = now.`}>
            <Input
              type="datetime-local"
              value={scenario.postedAt}
              onChange={(e) => set({ postedAt: e.target.value })}
            />
          </FormField>
          <div className="mg-grid mg-grid--3">
            <FormField label="Earned today">
              <Input
                type="number"
                min={0}
                step="any"
                value={scenario.earnedToday}
                onChange={(e) => set({ earnedToday: e.target.value })}
              />
            </FormField>
            <FormField label="Earned this week">
              <Input
                type="number"
                min={0}
                step="any"
                value={scenario.earnedThisWeek}
                onChange={(e) => set({ earnedThisWeek: e.target.value })}
              />
            </FormField>
            <FormField label="Earned in campaign">
              <Input
                type="number"
                min={0}
                step="any"
                value={scenario.earnedInCampaign}
                onChange={(e) => set({ earnedInCampaign: e.target.value })}
              />
            </FormField>
          </div>
          <div className="mg-grid mg-grid--2">
            <FormField label="Quality bonus requested" optional>
              <Input
                type="number"
                min={0}
                step="any"
                value={scenario.qualityBonusRequested}
                onChange={(e) => set({ qualityBonusRequested: e.target.value })}
              />
            </FormField>
            <FormField
              label="Budget remaining"
              optional
              hint="Blank = the campaign's actual remaining budget"
            >
              <Input
                type="number"
                min={0}
                step="any"
                value={scenario.campaignBudgetRemaining}
                onChange={(e) => set({ campaignBudgetRemaining: e.target.value })}
              />
            </FormField>
          </div>
          <Checkbox
            label="First approved post in this campaign"
            checked={scenario.isFirstApprovedPost}
            onChange={(e) => set({ isFirstApprovedPost: e.target.checked })}
          />
          <div>
            <Button type="submit" leadingIcon={<Calculator />} loading={preview.isPending}>
              Calculate
            </Button>
          </div>
        </form>

        <div aria-live="polite" className="stack">
          {error && (
            <Alert tone="danger" title={errorMessage(error)} role="alert">
              {serverRuleErrors.length > 0 && (
                <ul className="mg-list">
                  {serverRuleErrors.map((m) => (
                    <li key={m}>{m}</li>
                  ))}
                </ul>
              )}
            </Alert>
          )}
          {quote && !error && <QuoteView quote={quote} />}
        </div>
      </CardBody>
    </Card>
  );
}

export function QuoteView({ quote }: { quote: RewardQuote }) {
  const columns: DataTableColumn<RewardLine & { index: number }>[] = [
    {
      id: 'line',
      header: 'Line',
      primary: true,
      cell: (line) => (
        <span className="stack mg-stack-xs">
          <span>{line.label || ruleTypeLabel(line.type)}</span>
          <span className="cluster mg-cluster-sm">
            <Badge size="sm">{ruleTypeLabel(line.type)}</Badge>
            {line.requiresApproval && (
              <Badge size="sm" tone="warning">
                Needs approval
              </Badge>
            )}
          </span>
        </span>
      ),
    },
    {
      id: 'uncapped',
      header: 'Before caps',
      align: 'right',
      cell: (line) => <Money amount={line.uncappedAmount} currency={quote.currency} />,
    },
    {
      id: 'amount',
      header: 'Amount',
      align: 'right',
      cell: (line) => <Money amount={line.amount} currency={quote.currency} />,
    },
  ];
  return (
    <div className="stack mg-quote" data-testid="reward-quote">
      <p className="text-small text-muted">
        {quote.ruleSetVersion ? `Version ${quote.ruleSetVersion}` : 'Unsaved draft'} · {quote.ruleSetSummary}
      </p>
      <DataTable
        caption="Reward lines"
        columns={columns}
        rows={quote.lines.map((line, index) => ({ ...line, index }))}
        getRowId={(line) => `${line.ruleId}-${line.index}`}
        emptyState={<p className="text-muted">No reward lines apply to this post.</p>}
      />
      <p className="mg-total">
        <span>Total</span>
        <strong>
          <Money amount={quote.total} currency={quote.currency} />
        </strong>
      </p>
      {quote.appliedCaps.length > 0 ? (
        <Alert tone="info" title="Caps applied">
          <ul className="mg-list">
            {quote.appliedCaps.map((cap) => (
              <li key={cap}>{capLabel(cap)}</li>
            ))}
          </ul>
        </Alert>
      ) : (
        <p className="text-small text-muted">No caps were applied.</p>
      )}
    </div>
  );
}
