import { Checkbox, FormField, Input, RadioGroup, Textarea } from '@/components/ui';
import { PARTICIPANT_TIERS, type ParticipantTier } from '@/features/campaigns/api/types';
import type { MembershipMode, RateGroup, RateGroupInput } from '../api/types';

export interface GroupForm {
  name: string;
  description: string;
  priority: string;
  membershipMode: MembershipMode;
  autoTiers: ParticipantTier[];
  autoMinFollowers: string;
  autoMaxFollowers: string;
  autoRequireVerified: boolean;
}

export function emptyGroupForm(): GroupForm {
  return {
    name: '',
    description: '',
    priority: '0',
    membershipMode: 'Manual',
    autoTiers: [],
    autoMinFollowers: '',
    autoMaxFollowers: '',
    autoRequireVerified: true,
  };
}

export function groupFormFrom(g: RateGroup): GroupForm {
  return {
    name: g.name,
    description: g.description ?? '',
    priority: String(g.priority),
    membershipMode: g.membershipMode,
    autoTiers: g.autoTiers,
    autoMinFollowers: g.autoMinFollowers === null ? '' : String(g.autoMinFollowers),
    autoMaxFollowers: g.autoMaxFollowers === null ? '' : String(g.autoMaxFollowers),
    autoRequireVerified: g.autoRequireVerified,
  };
}

const int = (s: string) => (s.trim() === '' ? null : Math.trunc(Number(s)));

export function groupToInput(f: GroupForm): RateGroupInput {
  const auto = f.membershipMode === 'Automatic';
  return {
    name: f.name.trim(),
    description: f.description.trim() || null,
    priority: int(f.priority) ?? 0,
    membershipMode: f.membershipMode,
    autoTiers: auto ? f.autoTiers : null,
    autoMinFollowers: auto ? int(f.autoMinFollowers) : null,
    autoMaxFollowers: auto ? int(f.autoMaxFollowers) : null,
    autoRequireVerified: f.autoRequireVerified,
  };
}

export function GroupFields({
  value,
  onChange,
  errors,
  modeLocked,
}: {
  value: GroupForm;
  onChange: (v: GroupForm) => void;
  errors?: Record<string, string[]>;
  modeLocked?: boolean;
}) {
  const set = (patch: Partial<GroupForm>) => onChange({ ...value, ...patch });
  return (
    <div className="stack">
      <div className="rt-grid rt-grid--2">
        <FormField label="Name" required error={errors?.name?.[0]}>
          <Input
            value={value.name}
            maxLength={120}
            placeholder="Micro influencers"
            onChange={(e) => set({ name: e.target.value })}
          />
        </FormField>
        <FormField
          label="Priority"
          hint="When a person is in several groups with equally specific rates, the higher priority wins (−1000 to 1000)."
          error={errors?.priority?.[0]}
        >
          <Input
            type="number"
            min={-1000}
            max={1000}
            step={1}
            value={value.priority}
            onChange={(e) => set({ priority: e.target.value })}
          />
        </FormField>
      </div>
      <FormField label="Description" optional>
        <Textarea
          rows={2}
          maxLength={500}
          value={value.description}
          onChange={(e) => set({ description: e.target.value })}
        />
      </FormField>
      <RadioGroup
        legend="Membership"
        variant="cards"
        value={value.membershipMode}
        onChange={(v) => set({ membershipMode: v as MembershipMode })}
        hint={modeLocked ? 'Remove the members before switching to automatic membership.' : undefined}
        options={[
          {
            value: 'Manual',
            label: 'Manual',
            description: 'Add people one by one, in bulk or from a CSV file.',
          },
          {
            value: 'Automatic',
            label: 'Automatic (segment)',
            description:
              'Everyone matching a tier / verified-follower rule, evaluated when a post is priced.',
            disabled: modeLocked,
          },
        ]}
      />
      {value.membershipMode === 'Automatic' && (
        <fieldset className="rt-lines">
          <legend className="rt-legend">Rule</legend>
          <div className="cluster rt-cluster-sm" role="group" aria-label="Tiers">
            {PARTICIPANT_TIERS.map((t) => (
              <Checkbox
                key={t}
                label={t}
                checked={value.autoTiers.includes(t)}
                onChange={(e) =>
                  set({
                    autoTiers: e.target.checked
                      ? [...value.autoTiers, t]
                      : value.autoTiers.filter((x) => x !== t),
                  })
                }
              />
            ))}
          </div>
          <div className="rt-grid rt-grid--2">
            <FormField
              label="At least this many followers"
              optional
              hint="On the post's platform"
              error={errors?.autoMinFollowers?.[0]}
            >
              <Input
                type="number"
                min={0}
                step={1}
                value={value.autoMinFollowers}
                onChange={(e) => set({ autoMinFollowers: e.target.value })}
              />
            </FormField>
            <FormField label="Fewer than this many followers" optional error={errors?.autoMaxFollowers?.[0]}>
              <Input
                type="number"
                min={1}
                step={1}
                value={value.autoMaxFollowers}
                onChange={(e) => set({ autoMaxFollowers: e.target.value })}
              />
            </FormField>
          </div>
          <Checkbox
            label="Count verified profiles only"
            checked={value.autoRequireVerified}
            onChange={(e) => set({ autoRequireVerified: e.target.checked })}
          />
          {errors?.autoTiers?.[0] && <p className="rt-error">{errors.autoTiers[0]}</p>}
        </fieldset>
      )}
    </div>
  );
}
