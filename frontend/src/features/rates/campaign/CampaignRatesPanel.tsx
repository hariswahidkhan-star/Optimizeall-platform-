import { useMutation } from '@tanstack/react-query';
import { Calculator, Link2 } from 'lucide-react';
import { useState } from 'react';
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  EmptyState,
  ErrorState,
  FormField,
  Select,
  SkeletonText,
  Stat,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useCampaignRates } from '../api/queries';
import type { RateExplanation } from '../api/types';
import { AssignRateDialog } from '../components/AssignRateDialog';
import { AssignmentsTable } from '../components/AssignmentsTable';
import { PeoplePicker, type PickedPerson } from '../components/PeoplePicker';
import { RateExplanationView } from '../components/RateExplanationView';
import { formatOptions, platformOptions } from '../labels';
import '../rates.css';

/**
 * Campaign editor panel: which person-level rates apply to this campaign (scoped and global assignments), currency
 * problems that would block publishing, and a price simulator ("price for person X on platform Y").
 */
export function CampaignRatesPanel({
  campaignId,
  campaignTitle,
  platforms,
}: {
  campaignId: string;
  campaignTitle: string;
  platforms: string[];
}) {
  const { hasPermission } = useAuth();
  const canAssign = hasPermission(Permissions.RatesAssign);
  const rates = useCampaignRates(campaignId);
  const [assigning, setAssigning] = useState(false);
  const [person, setPerson] = useState<PickedPerson[]>([]);
  const [platform, setPlatform] = useState(platforms[0] ?? 'Instagram');
  const [format, setFormat] = useState('');
  const [firstPost, setFirstPost] = useState(false);
  const simulate = useMutation({
    mutationFn: () =>
      api.post<RateExplanation>(`/admin/campaigns/${campaignId}/rates/simulate`, {
        userId: person[0]?.id,
        platform,
        format: format || null,
        isFirstApprovedPost: firstPost,
      }),
  });

  return (
    <Card as="section" aria-labelledby="campaign-rates-title">
      <CardHeader
        titleId="campaign-rates-title"
        headingLevel={2}
        title="Personal & group rates"
        description="Rate cards assigned to people or groups replace the post rate above for them, in precedence order. Caps and the budget always apply."
        actions={
          canAssign ? (
            <Button size="sm" variant="secondary" leadingIcon={<Link2 />} onClick={() => setAssigning(true)}>
              Assign for this campaign
            </Button>
          ) : undefined
        }
      />
      <CardBody className="stack">
        {rates.isPending ? (
          <SkeletonText lines={3} />
        ) : rates.isError ? (
          <ErrorState error={rates.error} onRetry={() => void rates.refetch()} />
        ) : (
          <>
            {rates.data.personalRatesMode === 'CampaignRatesOnly' && (
              <Alert tone="info" title="Campaign rates only">
                This campaign ignores personal and group rates: everyone is paid the rules above.
              </Alert>
            )}
            {rates.data.fxProblems.length > 0 && (
              <Alert tone="danger" title="Missing exchange rates">
                Rates in {rates.data.fxProblems.join(', ')} can't be converted to {rates.data.currency}.
                Publishing is blocked until finance adds the exchange rate.
              </Alert>
            )}
            <div className="grid-auto rt-stat-grid">
              <Stat
                label="Rates for this campaign"
                value={String(rates.data.campaignAssignments.filter((a) => a.isActive).length)}
                measurement="Count"
              />
              <Stat
                label="Rates for every campaign"
                value={String(rates.data.globalAssignments.length)}
                measurement="Count"
              />
              <Stat
                label="People with a personal rate"
                value={String(rates.data.peopleWithPersonalRates)}
                measurement="Count"
              />
            </div>
            {rates.data.campaignAssignments.length === 0 && rates.data.globalAssignments.length === 0 ? (
              <EmptyState
                headingLevel={3}
                title="No personal or group rates"
                description="Everyone is paid the campaign rules."
              />
            ) : (
              <AssignmentsTable
                assignments={[...rates.data.campaignAssignments, ...rates.data.globalAssignments]}
                caption="Personal and group rates that can apply in this campaign"
              />
            )}
          </>
        )}

        <fieldset className="rt-lines">
          <legend className="rt-legend">
            <Calculator aria-hidden="true" className="rt-icon" /> Price simulator
          </legend>
          <PeoplePicker label="Person" single value={person} onChange={setPerson} />
          <div className="rt-grid rt-grid--3">
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
            <Checkbox
              label="First approved post"
              checked={firstPost}
              onChange={(e) => setFirstPost(e.target.checked)}
            />
          </div>
          <div>
            <Button
              leadingIcon={<Calculator />}
              disabled={person.length === 0}
              loading={simulate.isPending}
              onClick={() => simulate.mutate()}
            >
              Price this post
            </Button>
          </div>
          {simulate.isError && (
            <Alert tone="danger" title="Could not price">
              {errorMessage(simulate.error)}
            </Alert>
          )}
          {simulate.data && <RateExplanationView explanation={simulate.data} />}
        </fieldset>
      </CardBody>
      {assigning && (
        <AssignRateDialog
          onClose={() => setAssigning(false)}
          campaign={{ id: campaignId, title: campaignTitle }}
        />
      )}
    </Card>
  );
}
