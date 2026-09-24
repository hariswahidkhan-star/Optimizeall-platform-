import { useQueryClient } from '@tanstack/react-query';
import { MoreHorizontal, Rocket, Save } from 'lucide-react';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  DropdownMenu,
  ErrorState,
  FormField,
  IconButton,
  Input,
  KeyValueList,
  Money,
  PageHeader,
  ProgressBar,
  Skeleton,
  StatusBadge,
  Tabs,
  useToast,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { browserTimeZone } from '@/lib/format/dates';
import { qk, useCampaign, useRewardRuleVersions } from '../api/queries';
import type {
  AdminCampaign,
  CreateCampaignRequest,
  Disclosure,
  RewardRuleSet,
  UpdateCampaignRequest,
} from '../api/types';
import { countErrors, fieldError, fieldErrorsFrom, type FieldErrorMap } from '../shared/formErrors';
import { UnsavedChangesGuard } from '../shared/UnsavedChangesGuard';
import { useCampaignActions } from '../list/campaignActions';
import { AssetsSection } from './AssetsSection';
import {
  disclosuresFromForm,
  emptyForm,
  emptyRules,
  fieldsFromForm,
  formFromCampaign,
  formsEqual,
  rulesFromSet,
  rulesToInput,
  type CampaignForm,
  type RulesForm,
} from './formModel';
import { PublishDialog } from './PublishDialog';
import { RewardPreview } from './rewards/RewardPreview';
import { RulesBuilder } from './rewards/RulesBuilder';
import { VersionHistory } from './rewards/VersionHistory';
import { CampaignRatesPanel } from '../../rates/campaign/CampaignRatesPanel';
import {
  BasicsSection,
  ContentSection,
  LandingSection,
  ScheduleSection,
  TargetingSection,
  VerificationSection,
} from './sections';
import '../campaigns.css';

/** Business error codes → the field they belong to (they carry no field key of their own). */
export const CAMPAIGN_CODE_FIELDS: Record<string, string> = {
  'campaign.invalid_dates': 'endsAt',
  'campaign.invalid_deadline': 'submissionDeadline',
  'campaign.invalid_time_zone': 'timeZone',
  'campaign.budget_currency_mismatch': 'budgetAmount',
  'campaign.budget_change_unconfirmed': 'budgetAmount',
  'campaign.budget_below_spent': 'budgetAmount',
  'campaign.disclosure_required': 'defaultDisclosureText',
  'campaign.invalid_tracking_url': 'trackingDestinationUrl',
  'campaign.invalid_image_url': 'heroImageUrl',
  'campaign.platform_required': 'platforms',
  'campaign.invalid_country': 'eligibility.countries',
  'campaign.category_not_found': 'categoryId',
  'campaign.slug_taken': 'slug',
  'campaign.title_required': 'title',
  'campaign.duplicate_disclosure': 'disclosures',
};

const TAB_FIELDS: Record<string, string[]> = {
  basics: ['title', 'slug', 'summary', 'description', 'categoryId', 'topics', 'visibility'],
  schedule: ['timeZone', 'startsAt', 'endsAt', 'submissionDeadline'],
  targeting: ['platforms', 'eligibility'],
  content: [
    'postingInstructions',
    'requiredHashtags',
    'requiredMentions',
    'defaultDisclosureText',
    'disclosures',
  ],
  rewards: ['budgetAmount', 'budgetCurrency', 'rewardRules', 'rules'],
  verification: ['minPostLiveHours', 'maxSubmissionsPerParticipant', 'requireScreenshot'],
  landing: ['landingHeadline', 'landingBody', 'heroImageUrl', 'trackingDestinationUrl', 'utmCampaign'],
};

const TAB_LABELS: Record<string, string> = {
  basics: 'Basics',
  schedule: 'Schedule',
  targeting: 'Targeting',
  content: 'Content',
  assets: 'Assets',
  rewards: 'Rewards',
  verification: 'Verification',
  landing: 'Landing & tracking',
};

export function CampaignEditorPage() {
  const { campaignId } = useParams();
  if (!campaignId) return <CampaignEditor key="new" campaign={null} />;
  return <LoadedEditor id={campaignId} />;
}

function LoadedEditor({ id }: { id: string }) {
  const query = useCampaign(id);
  if (query.isLoading) {
    return (
      <>
        <PageHeader
          title="Loading campaign…"
          breadcrumbs={[{ label: 'Campaigns', to: '/manage/campaigns' }]}
        />
        <Skeleton height={320} />
      </>
    );
  }
  if (query.isError || !query.data) {
    return (
      <>
        <PageHeader title="Campaign" breadcrumbs={[{ label: 'Campaigns', to: '/manage/campaigns' }]} />
        <ErrorState error={query.error} onRetry={() => void query.refetch()} retrying={query.isFetching} />
      </>
    );
  }
  return <CampaignEditor key={id} campaign={query.data} refetch={query.refetch} />;
}

function disclosureKey(list: Disclosure[] | { platform: unknown; countryCode: unknown; text: string }[]) {
  return JSON.stringify(list.map((d) => [d.platform ?? null, d.countryCode ?? null, d.text]));
}

interface EditorProps {
  campaign: AdminCampaign | null;
  refetch?: () => Promise<{ data?: AdminCampaign }>;
}

export function CampaignEditor({ campaign, refetch }: EditorProps) {
  const isNew = !campaign;
  const { hasPermission, user } = useAuth();
  const canRewards = hasPermission(Permissions.RewardsEdit);
  const canPublish = hasPermission(Permissions.CampaignsPublish);
  const canViewRates = hasPermission(Permissions.RatesView);
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const versionsQuery = useRewardRuleVersions(campaign?.id);
  const actions = useCampaignActions({
    onDone: (action, result) => {
      if (action === 'duplicate') navigate(`/manage/campaigns/${result.id}`);
      else adoptNextStamp.current = true;
    },
  });

  const initialZone = user?.timeZone || browserTimeZone();
  const [form, setForm] = useState<CampaignForm>(() =>
    campaign ? formFromCampaign(campaign) : emptyForm(initialZone),
  );
  const [baseline, setBaseline] = useState<CampaignForm>(form);
  const [rules, setRules] = useState<RulesForm>(() =>
    campaign ? rulesFromSet(campaign.currentRuleSet, campaign.timeZone) : emptyRules(),
  );
  const [rulesBaseline, setRulesBaseline] = useState<RulesForm>(rules);
  /** The saved version the rules editor started from (sent as baseVersion so a concurrent save is refused, not lost). */
  const [rulesVersion, setRulesVersion] = useState<number>(campaign?.currentRuleSet?.version ?? 0);
  const [stamp, setStamp] = useState(campaign?.concurrencyStamp ?? '');
  const [errors, setErrors] = useState<FieldErrorMap>({});
  const [summaryErrors, setSummaryErrors] = useState<string[]>([]);
  const [ruleServerErrors, setRuleServerErrors] = useState<string[]>([]);
  const [conflict, setConflict] = useState(false);
  const [saving, setSaving] = useState(false);
  const [budgetConfirm, setBudgetConfirm] = useState(false);
  const [rulesConfirm, setRulesConfirm] = useState(false);
  const [publishOpen, setPublishOpen] = useState(false);
  const [tab, setTab] = useState('basics');
  const [createdId, setCreatedId] = useState<string | null>(null);
  const adoptNextStamp = useRef(false);

  const archived = campaign?.status === 'Archived';
  const formDirty = !formsEqual(form, baseline);
  const rulesDirty = !formsEqual(rules, rulesBaseline);
  const dirty = !createdId && (formDirty || rulesDirty);
  const budgetChanged = !isNew && form.budgetAmount.trim() !== baseline.budgetAmount.trim();
  const ruleCurrency = isNew
    ? rules.currency
    : (campaign.currentRuleSet?.currency ?? campaign.budgetCurrency);
  const timeZone = form.timeZone || 'UTC';

  // Server data changed (refetch after our own action or someone else's edit).
  useEffect(() => {
    if (!campaign) return;
    if (!formDirty) {
      const next = formFromCampaign(campaign);
      setForm(next);
      setBaseline(next);
      setStamp(campaign.concurrencyStamp);
    } else if (adoptNextStamp.current) {
      setStamp(campaign.concurrencyStamp);
    }
    adoptNextStamp.current = false;
    if (!rulesDirty) {
      const nextRules = rulesFromSet(campaign.currentRuleSet, campaign.timeZone);
      setRules(nextRules);
      setRulesBaseline(nextRules);
      setRulesVersion(campaign.currentRuleSet?.version ?? 0);
    }
    // Only react to new server data, not to local edits.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [campaign]);

  // Navigate to the edit page once the new campaign is saved and the guard is released.
  useEffect(() => {
    if (createdId) navigate(`/manage/campaigns/${createdId}`, { replace: true });
  }, [createdId, navigate]);

  const set = (patch: Partial<CampaignForm>) => setForm((f) => ({ ...f, ...patch }));

  const applyError = (err: unknown) => {
    const mapped = fieldErrorsFrom(err, { ...CAMPAIGN_CODE_FIELDS, 'reward.invalid_rules': 'rules' });
    setErrors(mapped);
    const messages = Object.values(mapped).flat();
    setSummaryErrors(messages.length > 0 ? messages : [errorMessage(err)]);
    if (isApiError(err) && err.code === 'reward.invalid_rules') {
      setRuleServerErrors(Object.values(err.errors ?? {}).flat());
    }
    const firstTab = Object.keys(TAB_FIELDS).find((t) => countErrors(mapped, TAB_FIELDS[t]!) > 0);
    if (firstTab) setTab(firstTab);
  };

  const resetFromServer = (data: AdminCampaign) => {
    const next = formFromCampaign(data);
    setForm(next);
    setBaseline(next);
    setStamp(data.concurrencyStamp);
    const nextRules = rulesFromSet(data.currentRuleSet, data.timeZone);
    setRules(nextRules);
    setRulesBaseline(nextRules);
    setRulesVersion(data.currentRuleSet?.version ?? 0);
  };

  const create = async () => {
    const body: CreateCampaignRequest = {
      ...fieldsFromForm(form, rules.currency),
      slug: form.slugTouched ? form.slug.trim() || null : null,
      rewardRules: rulesToInput(rules, timeZone),
    };
    const created = await api.post<AdminCampaign>('/admin/campaigns', body);
    const disclosures = disclosuresFromForm(form.disclosures);
    if (disclosures.length > 0) {
      try {
        await api.put(`/admin/campaigns/${created.id}/disclosures`, { disclosures });
      } catch (err) {
        toast.error('Campaign created, but the disclosure overrides were not saved', errorMessage(err));
      }
    }
    queryClient.setQueryData(qk.campaign(created.id), created);
    await queryClient.invalidateQueries({ queryKey: qk.campaigns() });
    toast.success('Draft campaign created', created.title);
    setCreatedId(created.id);
  };

  const update = async (reason?: string) => {
    if (!campaign) return;
    const body: UpdateCampaignRequest = {
      ...fieldsFromForm(form, ruleCurrency),
      slug: form.slug.trim() || null,
      concurrencyStamp: stamp,
      ...(reason ? { confirm: true, reason } : {}),
    };
    let saved: AdminCampaign;
    try {
      saved = await api.put<AdminCampaign>(`/admin/campaigns/${campaign.id}`, body);
    } catch (err) {
      if (isApiError(err) && err.status === 409 && err.code === 'concurrency.conflict') {
        setConflict(true);
        return;
      }
      throw err;
    }
    // The campaign itself is saved from here on: keep its new stamp even if the disclosure overrides are refused
    // below, or the next save would be reported as someone else's change.
    setStamp(saved.concurrencyStamp);
    queryClient.setQueryData(qk.campaign(campaign.id), saved);
    const disclosures = disclosuresFromForm(form.disclosures);
    if (disclosureKey(disclosures) !== disclosureKey(saved.disclosures)) {
      const list = await api.put<Disclosure[]>(`/admin/campaigns/${campaign.id}/disclosures`, {
        disclosures,
      });
      saved = { ...saved, disclosures: list };
    }
    queryClient.setQueryData(qk.campaign(campaign.id), saved);
    void queryClient.invalidateQueries({ queryKey: qk.campaigns() });
    const next = formFromCampaign(saved);
    setForm(next);
    setBaseline(next);
    setStamp(saved.concurrencyStamp);
    toast.success('Campaign saved');
  };

  const save = async (reason?: string) => {
    setSaving(true);
    setErrors({});
    setSummaryErrors([]);
    setRuleServerErrors([]);
    try {
      if (isNew) await create();
      else await update(reason);
    } catch (err) {
      applyError(err);
      if (reason) throw err;
    } finally {
      setSaving(false);
    }
  };

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (saving || archived) return;
    if (budgetChanged) {
      setBudgetConfirm(true);
      return;
    }
    void save();
  };

  const saveRules = async ({ reason }: { reason: string }) => {
    if (!campaign) return;
    setRuleServerErrors([]);
    try {
      const created = await api.post<RewardRuleSet>(`/admin/campaigns/${campaign.id}/reward-rules`, {
        ...rulesToInput(rules, timeZone),
        reason,
        confirm: true,
        baseVersion: rulesVersion,
      });
      toast.success(`Reward rules saved as version ${created.version}`);
      const nextRules = rulesFromSet(created, timeZone);
      setRules(nextRules);
      setRulesBaseline(nextRules);
      setRulesVersion(created.version);
      adoptNextStamp.current = true;
      await queryClient.invalidateQueries({ queryKey: qk.campaign(campaign.id) });
    } catch (err) {
      if (isApiError(err)) setRuleServerErrors(Object.values(err.errors ?? {}).flat());
      throw err;
    }
  };

  const reloadLatest = async () => {
    if (!refetch) return;
    const result = await refetch();
    if (result.data) resetFromServer(result.data);
    setConflict(false);
    setErrors({});
    setSummaryErrors([]);
    toast.info('Loaded the latest version', 'Your unsaved changes were discarded.');
  };

  const tabBadge = (id: string) => {
    const count = countErrors(errors, TAB_FIELDS[id] ?? []);
    return count > 0 ? (
      <>
        {count}
        <span className="visually-hidden"> {count === 1 ? 'error' : 'errors'}</span>
      </>
    ) : undefined;
  };

  const readOnly = archived || saving;
  const versions = versionsQuery.data ?? [];
  const sectionProps = { form, set, errors, disabled: readOnly };
  const budgetDisabled = readOnly || (!isNew && !canRewards);
  const rulesDisabled = archived || !canRewards;

  const rewardsTab = (
    <div className="stack">
      {!isNew && (
        <Card flat>
          <CardBody className="stack mg-stack-sm">
            <KeyValueList
              layout="inline"
              items={[
                {
                  label: 'Spent',
                  value: <Money amount={campaign.spent} currency={campaign.budgetCurrency} />,
                },
                {
                  label: 'Budget',
                  value:
                    campaign.budgetAmount !== null ? (
                      <Money amount={campaign.budgetAmount} currency={campaign.budgetCurrency} />
                    ) : (
                      'No budget'
                    ),
                },
                {
                  label: 'Remaining',
                  value:
                    campaign.budgetRemaining !== null ? (
                      <Money amount={campaign.budgetRemaining} currency={campaign.budgetCurrency} />
                    ) : (
                      '—'
                    ),
                },
              ]}
            />
            {campaign.budgetAmount !== null && campaign.budgetAmount > 0 && (
              <ProgressBar
                label="Budget used"
                value={Math.min(campaign.spent, campaign.budgetAmount)}
                max={campaign.budgetAmount}
                tone={campaign.spent >= campaign.budgetAmount ? 'accent' : 'primary'}
              />
            )}
          </CardBody>
        </Card>
      )}
      <FormField
        label={`Campaign budget (${ruleCurrency})`}
        optional
        hint={
          !isNew && !canRewards
            ? 'Changing the budget needs the rewards.edit permission.'
            : isNew
              ? 'Blank = no budget limit. Earnings stop once the budget is used.'
              : 'Changing the budget asks for a reason (audited). Blank = no budget limit.'
        }
        error={fieldError(errors, 'budgetAmount') ?? fieldError(errors, 'budgetCurrency')}
      >
        <Input
          type="number"
          min={0.01}
          step="any"
          inputMode="decimal"
          value={form.budgetAmount}
          disabled={budgetDisabled}
          onChange={(e) => set({ budgetAmount: e.target.value })}
        />
      </FormField>
      <Card as="section" aria-labelledby="rules-title">
        <CardHeader
          titleId="rules-title"
          headingLevel={2}
          title="Reward rules"
          description={
            isNew
              ? 'These become version 1 when you create the campaign.'
              : `Current: ${campaign.currentRuleSet ? `version ${campaign.currentRuleSet.version}` : 'none'}. Saving creates a new version.`
          }
          actions={
            !isNew ? (
              <div className="cluster mg-cluster-sm">
                {rulesDirty && (
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={rulesDisabled}
                    onClick={() => {
                      setRules(rulesBaseline);
                      setRuleServerErrors([]);
                    }}
                  >
                    Discard rule changes
                  </Button>
                )}
                <Button
                  size="sm"
                  leadingIcon={<Save />}
                  disabled={rulesDisabled || !rulesDirty}
                  title={!canRewards ? 'Requires the rewards.edit permission' : undefined}
                  onClick={() => setRulesConfirm(true)}
                >
                  Save as new version
                </Button>
              </div>
            ) : undefined
          }
        />
        <CardBody className="stack">
          {!canRewards && (
            <Alert tone="info">
              You can view the reward rules, but changing them needs the rewards.edit permission.
            </Alert>
          )}
          <RulesBuilder
            value={rules}
            onChange={setRules}
            timeZone={timeZone}
            disabled={rulesDisabled}
            currencyLocked={!isNew && (campaign.submissions.total ?? 0) > 0}
            serverErrors={[...ruleServerErrors, ...(fieldError(errors, 'rules') ?? [])].filter(
              (m, i, a) => a.indexOf(m) === i,
            )}
          />
        </CardBody>
      </Card>
      {isNew ? (
        <Alert tone="info" title="Payout preview">
          Save the draft to preview payouts with the server&apos;s reward engine.
        </Alert>
      ) : (
        <RewardPreview
          campaignId={campaign.id}
          draft={rulesToInput(rules, timeZone)}
          versions={versions}
          timeZone={timeZone}
          defaultPlatform={form.platforms[0]}
        />
      )}
      {!isNew && canViewRates && (
        <CampaignRatesPanel campaignId={campaign.id} campaignTitle={campaign.title} platforms={form.platforms} />
      )}
      {!isNew && <VersionHistory versions={versions} loading={versionsQuery.isLoading} />}
    </div>
  );

  const tabs = [
    { id: 'basics', content: <BasicsSection {...sectionProps} /> },
    { id: 'schedule', content: <ScheduleSection {...sectionProps} /> },
    { id: 'targeting', content: <TargetingSection {...sectionProps} /> },
    { id: 'content', content: <ContentSection {...sectionProps} /> },
    {
      id: 'assets',
      content: isNew ? (
        <Alert tone="info" title="Assets">
          Create the draft first, then add captions, links and images here.
        </Alert>
      ) : (
        <AssetsSection campaignId={campaign.id} assets={campaign.assets} disabled={archived} />
      ),
    },
    { id: 'rewards', content: rewardsTab },
    { id: 'verification', content: <VerificationSection {...sectionProps} /> },
    {
      id: 'landing',
      content: (
        <LandingSection
          {...sectionProps}
          publicLandingUrl={
            campaign?.publicLandingPath ? `${window.location.origin}${campaign.publicLandingPath}` : undefined
          }
        />
      ),
    },
  ].map((t) => ({ ...t, label: TAB_LABELS[t.id]!, badge: tabBadge(t.id) }));

  const menu = campaign ? actions.menuItems(campaign) : [];
  const title = isNew ? 'New campaign' : campaign.title;

  return (
    <>
      <PageHeader
        title={title}
        breadcrumbs={[
          { label: 'Campaigns', to: '/manage/campaigns' },
          { label: isNew ? 'New' : campaign.title },
        ]}
        meta={campaign ? <StatusBadge kind="campaign" status={campaign.status} /> : undefined}
        actions={
          <div className="cluster mg-cluster-sm">
            {campaign?.status === 'Draft' && (
              <Button
                variant="highlight"
                leadingIcon={<Rocket />}
                disabled={!canPublish}
                title={!canPublish ? 'Requires the campaigns.publish permission' : undefined}
                onClick={() => setPublishOpen(true)}
              >
                Publish
              </Button>
            )}
            <Button
              type="submit"
              form="campaign-form"
              leadingIcon={<Save />}
              loading={saving}
              disabled={archived || (isNew && !canRewards) || (!isNew && !formDirty)}
            >
              {isNew ? 'Create draft' : 'Save changes'}
            </Button>
            {campaign && menu.length > 0 && (
              <DropdownMenu
                trigger={
                  <IconButton label="More campaign actions" icon={<MoreHorizontal />} variant="secondary" />
                }
                items={menu}
              />
            )}
          </div>
        }
      />

      <div className="stack">
        {isNew && !canRewards && (
          <Alert tone="warning" title="You can't create campaigns">
            Creating a campaign also sets its first reward rules, which needs the rewards.edit permission.
          </Alert>
        )}
        {archived && <Alert tone="info">This campaign is archived and can no longer be changed.</Alert>}
        {conflict && (
          <Alert
            tone="danger"
            role="alert"
            title="Someone else changed this campaign"
            actions={
              <>
                <Button size="sm" onClick={() => void reloadLatest()}>
                  Reload latest version
                </Button>
                <Button size="sm" variant="ghost" onClick={() => setConflict(false)}>
                  Keep my edits
                </Button>
              </>
            }
          >
            Your changes were not saved because the campaign was updated after you opened it. Reload to see
            the latest version (your unsaved edits will be discarded), or copy your edits first.
          </Alert>
        )}
        {summaryErrors.length > 0 && (
          <Alert tone="danger" role="alert" title="The campaign could not be saved">
            <ul className="mg-list">
              {summaryErrors.map((m) => (
                <li key={m}>{m}</li>
              ))}
            </ul>
          </Alert>
        )}
        {!isNew && rulesDirty && tab !== 'rewards' && (
          <Alert tone="warning">
            Reward rules have unsaved changes. Save them as a new version on the Rewards tab.
          </Alert>
        )}
        {/* The header's save button submits this form; sections are not nested in it because the payout preview
            has a form of its own. */}
        <form id="campaign-form" onSubmit={onSubmit} noValidate aria-label="Campaign details" hidden />
        <Tabs label="Campaign sections" tabs={tabs} value={tab} onValueChange={setTab} />
      </div>

      <UnsavedChangesGuard when={dirty} />
      {actions.dialog}
      <ConfirmDialog
        open={budgetConfirm}
        onClose={() => setBudgetConfirm(false)}
        onConfirm={({ reason }) => save(reason)}
        title="Change the campaign budget?"
        description={`The budget changes from ${baseline.budgetAmount || 'no limit'} to ${form.budgetAmount || 'no limit'} ${ruleCurrency}. Budget changes are audited.`}
        confirmLabel="Save with new budget"
        requireReason
        reasonMinLength={5}
        reasonHint="Recorded in the audit log (at least 5 characters)."
      />
      <ConfirmDialog
        open={rulesConfirm}
        onClose={() => setRulesConfirm(false)}
        onConfirm={saveRules}
        title="Save reward rules as a new version?"
        description="The new version applies to submissions created from now on. Existing submissions keep their original version and approved earnings are never changed."
        confirmLabel="Save new version"
        requireReason
        reasonMinLength={5}
        reasonLabel="Reason for the change"
        reasonHint="Required and recorded in the audit log (at least 5 characters)."
      />
      {campaign && (
        <PublishDialog
          open={publishOpen}
          onClose={() => setPublishOpen(false)}
          campaign={campaign}
          unsavedChanges={dirty}
          onPublished={() => {
            adoptNextStamp.current = true;
          }}
        />
      )}
    </>
  );
}
