import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Alert, Button, Dialog, FormField, Input, RadioGroup, Select, Textarea } from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { useCampaignOptions } from '@/lib/api/campaignOptions';
import { ApiError, errorMessage } from '@/lib/api/errors';
import { rk, useActiveCardOptions, useGroupOptions } from '../api/queries';
import type { AssignmentTarget, RateAssignment } from '../api/types';
import { utcInputToIso } from '../labels';
import { PeoplePicker, type PickedPerson } from './PeoplePicker';

export interface AssignRateDialogProps {
  onClose: () => void;
  /** Fixed card (card detail page); otherwise the user picks an active card. */
  card?: { id: string; name: string };
  /** Fixed person (Rates section of a user) or group (group detail page). */
  person?: PickedPerson;
  group?: { id: string; name: string };
  /** Pre-selected campaign scope (campaign editor). */
  campaign?: { id: string; title: string };
}

/**
 * Assigns a rate card to one person or a rate group, for every campaign or one campaign, optionally within a window
 * (UTC). Overlapping assignments for the same target and scope are refused by the server (409).
 */
export function AssignRateDialog({ onClose, card, person, group, campaign }: AssignRateDialogProps) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [target, setTarget] = useState<AssignmentTarget>(group ? 'Group' : 'Person');
  const [cardId, setCardId] = useState(card?.id ?? '');
  const [people, setPeople] = useState<PickedPerson[]>(person ? [person] : []);
  const [groupId, setGroupId] = useState(group?.id ?? '');
  const [scope, setScope] = useState<string>(campaign ? campaign.id : '');
  const [validFrom, setValidFrom] = useState('');
  const [validTo, setValidTo] = useState('');
  const [reason, setReason] = useState('');
  const cards = useActiveCardOptions(!card);
  const groups = useGroupOptions(!group && !person && target === 'Group');
  const campaigns = useCampaignOptions('', { enabled: !campaign });

  const save = useMutation({
    mutationFn: () =>
      api.post<RateAssignment>('/admin/rate-assignments', {
        rateCardId: cardId,
        target,
        userId: target === 'Person' ? people[0]?.id : null,
        groupId: target === 'Group' ? groupId : null,
        campaignId: scope || null,
        validFrom: utcInputToIso(validFrom),
        validTo: utcInputToIso(validTo),
        reason: reason.trim(),
      }),
    onSuccess: async (a) => {
      toast.success('Rate assigned', `${a.card.name} → ${a.person?.displayName ?? a.group?.name ?? ''}`);
      await queryClient.invalidateQueries({ queryKey: rk.all });
      onClose();
    },
  });
  const err = save.error instanceof ApiError ? save.error : null;
  const valid =
    !!cardId && reason.trim().length >= 5 && (target === 'Person' ? people.length === 1 : !!groupId);
  const fixedTarget = !!person || !!group;

  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="Assign a rate card"
      description={card ? `Card: ${card.name}` : 'Choose the card, who it applies to and where.'}
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={!valid}>
            Assign
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && (
          <Alert tone="danger" title="Not assigned">
            {errorMessage(save.error)}
          </Alert>
        )}
        {!card && (
          <FormField label="Rate card" required error={err?.fieldError('rateCardId')}>
            <Select
              value={cardId}
              placeholder={cards.isLoading ? 'Loading…' : 'Choose an active card'}
              options={(cards.data?.items ?? []).map((c) => ({
                value: c.id,
                label: `${c.name} (${c.currency}, v${c.currentVersion})`,
              }))}
              onChange={(e) => setCardId(e.target.value)}
            />
          </FormField>
        )}
        {!fixedTarget && (
          <RadioGroup
            legend="Applies to"
            value={target}
            onChange={(v) => setTarget(v as AssignmentTarget)}
            orientation="horizontal"
            options={[
              { value: 'Person', label: 'One person' },
              { value: 'Group', label: 'A rate group' },
            ]}
          />
        )}
        {target === 'Person' && !person && (
          <PeoplePicker
            label="Person"
            single
            value={people}
            onChange={setPeople}
            error={err?.fieldError('userId')}
          />
        )}
        {target === 'Person' && person && (
          <p>
            Person: <strong>{person.displayName}</strong>
          </p>
        )}
        {target === 'Group' && !group && (
          <FormField label="Rate group" required error={err?.fieldError('groupId')}>
            <Select
              value={groupId}
              placeholder="Choose a group"
              options={(groups.data?.items ?? []).map((g) => ({
                value: g.id,
                label: `${g.name}${g.membershipMode === 'Automatic' ? ' (automatic)' : ''} · priority ${g.priority}`,
              }))}
              onChange={(e) => setGroupId(e.target.value)}
            />
          </FormField>
        )}
        {group && (
          <p>
            Group: <strong>{group.name}</strong>
          </p>
        )}
        {campaign ? (
          <p>
            Campaign: <strong>{campaign.title}</strong> (campaign-scoped rates outrank rates for every
            campaign)
          </p>
        ) : (
          <FormField label="Where" hint="Campaign-scoped rates outrank rates that apply to every campaign.">
            <Select
              value={scope}
              options={[
                { value: '', label: 'Every campaign' },
                ...(campaigns.data ?? [])
                  .filter((c) => c.status !== 'Archived' && c.status !== 'Ended')
                  .map((c) => ({ value: c.id, label: `${c.title} (${c.status})` })),
              ]}
              onChange={(e) => setScope(e.target.value)}
            />
          </FormField>
        )}
        <div className="rt-grid rt-grid--2">
          <FormField
            label="Valid from (UTC)"
            optional
            hint="Blank = now. Evaluated at the post time."
            error={err?.fieldError('validFrom')}
          >
            <Input type="datetime-local" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} />
          </FormField>
          <FormField
            label="Valid until (UTC)"
            optional
            hint="Blank = no end (e.g. a negotiated deal that expires)."
            error={err?.fieldError('validTo')}
          >
            <Input type="datetime-local" value={validTo} onChange={(e) => setValidTo(e.target.value)} />
          </FormField>
        </div>
        <FormField label="Reason" required hint="At least 5 characters, e.g. the deal reference. Audited.">
          <Textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}
