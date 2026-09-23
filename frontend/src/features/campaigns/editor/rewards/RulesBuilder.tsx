import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  FormField,
  IconButton,
  Input,
  Select,
} from '@/components/ui';
import { useSupportedCurrencies } from '@/lib/api/meta';
import type { RewardRuleType } from '../../api/types';
import { platformOptions, ruleTypeLabel, tierOptions } from '../../shared/labels';
import { newRule, ruleHints, type RuleRow, type RulesForm } from '../formModel';

export interface RulesBuilderProps {
  value: RulesForm;
  onChange: (next: RulesForm) => void;
  /** Campaign time zone the rule windows are entered in. */
  timeZone: string;
  disabled?: boolean;
  /** Currency cannot change once the campaign has submissions. */
  currencyLocked?: boolean;
  /** Messages from the server's `reward.invalid_rules` response. */
  serverErrors?: string[];
}

const ADDABLE: { type: RewardRuleType; description: string }[] = [
  {
    type: 'RateOverride',
    description: 'Replaces the base rate for matching posts (platform, country, tier, window).',
  },
  { type: 'TimeLimitedBonus', description: 'Adds an amount to posts made inside a time window.' },
  { type: 'FirstPostBonus', description: "Adds an amount to a participant's first approved post." },
  { type: 'QualityBonus', description: 'Maximum a reviewer may award for an outstanding post.' },
];

const APPROVAL_OPTIONS = [
  { value: 'Automatic', label: 'Automatic' },
  { value: 'ManualApproval', label: 'Needs finance approval' },
];

/**
 * Reward rules editor. The client only shows hints (exactly one base rate, override conditions, windows); the
 * server validates when the version is saved or previewed.
 */
export function RulesBuilder({
  value,
  onChange,
  timeZone,
  disabled,
  currencyLocked,
  serverErrors = [],
}: RulesBuilderProps) {
  const hints = ruleHints(value);
  const setHints = hints.filter((h) => h.rowKey === null);
  const set = (patch: Partial<RulesForm>) => onChange({ ...value, ...patch });
  const currencyOptions = useSupportedCurrencies(value.currency).options;
  const setRule = (key: string, patch: Partial<RuleRow>) =>
    set({ rules: value.rules.map((r) => (r.key === key ? { ...r, ...patch } : r)) });
  const move = (index: number, delta: number) => {
    const next = [...value.rules];
    const target = index + delta;
    if (target < 0 || target >= next.length) return;
    [next[index], next[target]] = [next[target]!, next[index]!];
    set({ rules: next });
  };
  const hasSingle = (type: RewardRuleType) => value.rules.some((r) => r.type === type);

  return (
    <div className="stack">
      <div className="mg-grid mg-grid--4">
        <FormField
          label="Currency"
          hint={
            currencyLocked ? 'Locked: the campaign already has submissions.' : 'Every amount below uses it.'
          }
        >
          <Select
            value={value.currency}
            options={currencyOptions}
            disabled={disabled || currencyLocked}
            onChange={(e) => set({ currency: e.target.value })}
          />
        </FormField>
        <FormField label="Daily cap per participant" optional hint="Blank = no cap">
          <Input
            type="number"
            min={0}
            step="any"
            inputMode="decimal"
            value={value.dailyCap}
            disabled={disabled}
            onChange={(e) => set({ dailyCap: e.target.value })}
          />
        </FormField>
        <FormField label="Weekly cap per participant" optional hint="Monday-based, campaign time zone">
          <Input
            type="number"
            min={0}
            step="any"
            inputMode="decimal"
            value={value.weeklyCap}
            disabled={disabled}
            onChange={(e) => set({ weeklyCap: e.target.value })}
          />
        </FormField>
        <FormField label="Campaign cap per participant" optional>
          <Input
            type="number"
            min={0}
            step="any"
            inputMode="decimal"
            value={value.campaignCap}
            disabled={disabled}
            onChange={(e) => set({ campaignCap: e.target.value })}
          />
        </FormField>
      </div>

      <div role="status" aria-live="polite" className="stack mg-stack-sm">
        {setHints.length > 0 && (
          <Alert tone="warning" title="Check the rules">
            <ul className="mg-list">
              {setHints.map((h) => (
                <li key={h.message}>{h.message}</li>
              ))}
            </ul>
          </Alert>
        )}
      </div>
      {serverErrors.length > 0 && (
        <Alert tone="danger" title="The server rejected these rules" role="alert">
          <ul className="mg-list">
            {serverErrors.map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
        </Alert>
      )}

      <ol className="mg-rules" aria-label="Reward rules">
        {value.rules.map((rule, index) => (
          <RuleCard
            key={rule.key}
            rule={rule}
            index={index}
            count={value.rules.length}
            currency={value.currency}
            timeZone={timeZone}
            disabled={disabled}
            hints={hints.filter((h) => h.rowKey === rule.key).map((h) => h.message)}
            onChange={(patch) => setRule(rule.key, patch)}
            onRemove={() => set({ rules: value.rules.filter((r) => r.key !== rule.key) })}
            onMove={(delta) => move(index, delta)}
          />
        ))}
      </ol>

      <div className="mg-add-rules">
        <p className="mg-hint">Add a rule</p>
        <div className="cluster">
          {!hasSingle('BaseRate') && (
            <Button
              size="sm"
              variant="secondary"
              leadingIcon={<Plus />}
              disabled={disabled}
              onClick={() => set({ rules: [newRule('BaseRate'), ...value.rules] })}
            >
              Base rate
            </Button>
          )}
          {ADDABLE.map(({ type, description }) => {
            const single = type === 'FirstPostBonus' || type === 'QualityBonus';
            return (
              <Button
                key={type}
                size="sm"
                variant="secondary"
                leadingIcon={<Plus />}
                title={description}
                disabled={disabled || (single && hasSingle(type))}
                onClick={() => set({ rules: [...value.rules, newRule(type)] })}
              >
                {ruleTypeLabel(type)}
              </Button>
            );
          })}
        </div>
      </div>
    </div>
  );
}

function RuleCard({
  rule,
  index,
  count,
  currency,
  timeZone,
  disabled,
  hints,
  onChange,
  onRemove,
  onMove,
}: {
  rule: RuleRow;
  index: number;
  count: number;
  currency: string;
  timeZone: string;
  disabled?: boolean;
  hints: string[];
  onChange: (patch: Partial<RuleRow>) => void;
  onRemove: () => void;
  onMove: (delta: number) => void;
}) {
  const title = ruleTypeLabel(rule.type);
  const conditional = rule.type === 'RateOverride' || rule.type === 'TimeLimitedBonus';
  const isBonus =
    rule.type === 'TimeLimitedBonus' || rule.type === 'FirstPostBonus' || rule.type === 'QualityBonus';
  const amountLabel = rule.type === 'QualityBonus' ? `Maximum amount (${currency})` : `Amount (${currency})`;
  const hintId = `${rule.key}-hints`;

  return (
    <li>
      <Card flat className="mg-rule" aria-describedby={hints.length > 0 ? hintId : undefined}>
        <CardHeader
          headingLevel={3}
          title={
            <span className="cluster mg-cluster-sm">
              {title}
              {rule.type === 'BaseRate' && <Badge tone="brand">Required, exactly one</Badge>}
            </span>
          }
          actions={
            <div className="cluster mg-cluster-sm">
              <IconButton
                size="sm"
                variant="ghost"
                label={`Move ${title} rule ${index + 1} up`}
                icon={<ArrowUp />}
                disabled={disabled || index === 0}
                onClick={() => onMove(-1)}
              />
              <IconButton
                size="sm"
                variant="ghost"
                label={`Move ${title} rule ${index + 1} down`}
                icon={<ArrowDown />}
                disabled={disabled || index === count - 1}
                onClick={() => onMove(1)}
              />
              <IconButton
                size="sm"
                variant="ghost"
                label={`Remove ${title} rule ${index + 1}`}
                icon={<Trash2 />}
                disabled={disabled}
                onClick={onRemove}
              />
            </div>
          }
        />
        <CardBody className="stack">
          {hints.length > 0 && (
            <Alert tone="warning" id={hintId}>
              {hints.join(' ')}
            </Alert>
          )}
          <div className="mg-grid mg-grid--3">
            <FormField label={amountLabel} required>
              <Input
                type="number"
                min={0}
                step="any"
                inputMode="decimal"
                value={rule.amount}
                disabled={disabled}
                onChange={(e) => onChange({ amount: e.target.value })}
              />
            </FormField>
            <FormField label="Label" optional hint="Shown to participants and on ledger lines">
              <Input
                value={rule.label}
                maxLength={150}
                disabled={disabled}
                onChange={(e) => onChange({ label: e.target.value })}
              />
            </FormField>
            {isBonus && (
              <FormField label="Approval" hint="Manual bonuses stay pending until finance approves them">
                <Select
                  value={rule.approvalMode}
                  options={APPROVAL_OPTIONS}
                  disabled={disabled}
                  onChange={(e) => onChange({ approvalMode: e.target.value as RuleRow['approvalMode'] })}
                />
              </FormField>
            )}
            {rule.type === 'RateOverride' && (
              <FormField label="Priority" hint="Breaks ties between equally specific overrides">
                <Input
                  type="number"
                  min={-1000}
                  max={1000}
                  step={1}
                  value={rule.priority}
                  disabled={disabled}
                  onChange={(e) => onChange({ priority: e.target.value })}
                />
              </FormField>
            )}
          </div>
          {conditional && (
            <>
              <div className="mg-grid mg-grid--3">
                <FormField label="Platform" optional>
                  <Select
                    value={rule.platform}
                    placeholder="Any platform"
                    options={[{ value: '', label: 'Any platform' }, ...platformOptions]}
                    disabled={disabled}
                    onChange={(e) => onChange({ platform: e.target.value })}
                  />
                </FormField>
                <FormField label="Country" optional hint="Two-letter code, e.g. PK">
                  <Input
                    value={rule.countryCode}
                    maxLength={2}
                    autoCapitalize="characters"
                    disabled={disabled}
                    onChange={(e) => onChange({ countryCode: e.target.value.toUpperCase() })}
                  />
                </FormField>
                <FormField label="Tier" optional>
                  <Select
                    value={rule.tier}
                    options={[{ value: '', label: 'Any tier' }, ...tierOptions]}
                    disabled={disabled}
                    onChange={(e) => onChange({ tier: e.target.value })}
                  />
                </FormField>
              </div>
              <div className="mg-grid mg-grid--2">
                <FormField
                  label="Valid from"
                  optional={rule.type === 'RateOverride'}
                  required={rule.type === 'TimeLimitedBonus'}
                  hint={`In ${timeZone}; inclusive`}
                >
                  <Input
                    type="datetime-local"
                    value={rule.validFrom}
                    disabled={disabled}
                    onChange={(e) => onChange({ validFrom: e.target.value })}
                  />
                </FormField>
                <FormField
                  label="Valid to"
                  optional={rule.type === 'RateOverride'}
                  required={rule.type === 'TimeLimitedBonus'}
                  hint={`In ${timeZone}; exclusive`}
                >
                  <Input
                    type="datetime-local"
                    value={rule.validTo}
                    disabled={disabled}
                    onChange={(e) => onChange({ validTo: e.target.value })}
                  />
                </FormField>
              </div>
            </>
          )}
        </CardBody>
      </Card>
    </li>
  );
}
