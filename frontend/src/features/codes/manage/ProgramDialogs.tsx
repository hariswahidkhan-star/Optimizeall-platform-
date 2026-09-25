import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Alert, Button, Dialog, FormField, Input, RadioGroup, Select, Textarea } from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { ApiError, errorMessage } from '@/lib/api/errors';
import { useRateGroups } from '@/features/rates/api/queries';
import { PeoplePicker, type PickedPerson } from '@/features/rates/components/PeoplePicker';
import '@/features/rates/rates.css';
import { invalidateCodes } from '../api/queries';
import type { CodePayoutType, CodeProgram } from '../api/types';
import {
  PayoutFields,
  payoutFrom,
  payoutToInput,
  ProgramDetailsFields,
  programDetailsFrom,
  programDetailsToInput,
} from './ProgramForms';

export function EditProgramDialog({ program, onClose }: { program: CodeProgram; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const [details, setDetails] = useState(() => programDetailsFrom(program));
  const save = useMutation({
    mutationFn: () =>
      api.put<CodeProgram>(`/admin/code-programs/${program.id}`, {
        ...programDetailsToInput(details),
        concurrencyStamp: program.concurrencyStamp,
      }),
    onSuccess: async () => {
      toast.success('Program saved');
      await invalidateCodes(client);
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={`Edit ${program.name}`}
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} loading={save.isPending}>
            Save
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
        <ProgramDetailsFields
          value={details}
          onChange={setDetails}
          errors={save.error instanceof ApiError ? save.error.errors : undefined}
          currencyLocked={program.stats.pendingSales + program.stats.approvedSales > 0}
        />
      </div>
    </Dialog>
  );
}

export function EditPayoutDialog({ program, onClose }: { program: CodeProgram; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const [payout, setPayout] = useState(() => payoutFrom(program));
  const [reason, setReason] = useState('');
  const save = useMutation({
    mutationFn: () =>
      api.put<CodeProgram>(`/admin/code-programs/${program.id}/payout`, {
        ...payoutToInput(payout),
        reason: reason.trim(),
        confirm: true,
        concurrencyStamp: program.concurrencyStamp,
      }),
    onSuccess: async (p) => {
      toast.success(
        'Payout rules saved',
        `Version ${p.payoutVersion} applies to sales approved from now on.`,
      );
      await invalidateCodes(client);
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="Change payout rules"
      description="Money-affecting and audited. Sales already approved keep their commission; the new rules price every sale approved from now on."
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={reason.trim().length < 5}>
            Save payout rules
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
        <PayoutFields
          value={payout}
          onChange={setPayout}
          currency={program.currency}
          errors={save.error instanceof ApiError ? save.error.errors : undefined}
        />
        <FormField label="Reason" required hint="Kept in the audit log.">
          <Textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

export function OverrideDialog({ program, onClose }: { program: CodeProgram; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const [target, setTarget] = useState<'Person' | 'Group'>('Group');
  const [people, setPeople] = useState<PickedPerson[]>([]);
  const [groupId, setGroupId] = useState('');
  const [type, setType] = useState<CodePayoutType>('PercentOfNet');
  const [amount, setAmount] = useState('');
  const [reason, setReason] = useState('');
  const groups = useRateGroups({ mode: 'Manual', page: 1, pageSize: 100 });
  const save = useMutation({
    mutationFn: () =>
      api.post<CodeProgram>(`/admin/code-programs/${program.id}/overrides`, {
        target,
        userId: target === 'Person' ? people[0]?.id : null,
        groupId: target === 'Group' ? groupId : null,
        payoutType: type,
        flatAmount: type === 'FlatPerSale' ? Number(amount) : null,
        percent: type === 'PercentOfNet' ? Number(amount) : null,
        reason: reason.trim(),
      }),
    onSuccess: async () => {
      toast.success('Payout override added');
      await invalidateCodes(client);
      onClose();
    },
  });
  const ready =
    (target === 'Person' ? people.length === 1 : !!groupId) &&
    Number(amount) > 0 &&
    reason.trim().length >= 5;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="Add a payout override"
      description="A negotiated rate for one person or a rate group in this program. A person’s override beats a group’s; the group with the higher priority wins between groups. Tier bonuses still apply."
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={!ready}>
            Add override
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && (
          <Alert tone="danger" title="Not added">
            {errorMessage(save.error)}
          </Alert>
        )}
        <RadioGroup
          legend="For"
          orientation="horizontal"
          value={target}
          onChange={(v) => setTarget(v as 'Person' | 'Group')}
          options={[
            { value: 'Group', label: 'A rate group' },
            { value: 'Person', label: 'One person' },
          ]}
        />
        {target === 'Person' ? (
          <PeoplePicker label="Person" single value={people} onChange={setPeople} />
        ) : (
          <FormField label="Rate group" required>
            <Select
              value={groupId}
              onChange={(e) => setGroupId(e.target.value)}
              placeholder="Choose a group"
              options={(groups.data?.items ?? []).map((g) => ({ value: g.id, label: g.name }))}
            />
          </FormField>
        )}
        <div className="dc-form-grid">
          <FormField label="Payout">
            <Select
              value={type}
              onChange={(e) => setType(e.target.value as CodePayoutType)}
              options={[
                { value: 'PercentOfNet', label: 'Percent of net' },
                { value: 'FlatPerSale', label: `Flat per sale (${program.currency})` },
              ]}
            />
          </FormField>
          <FormField label={type === 'PercentOfNet' ? 'Percent' : `Amount (${program.currency})`} required>
            <Input
              type="number"
              min={0}
              step="any"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
            />
          </FormField>
        </div>
        <FormField label="Reason" required hint="Kept in the audit log.">
          <Textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}
