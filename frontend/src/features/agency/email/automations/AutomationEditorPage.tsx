import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, Pause, Play, Plus, Save, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { ErrorState } from '@/components/ui/ErrorState';
import { FormField } from '@/components/ui/FormField';
import { IconButton } from '@/components/ui/IconButton';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { ScrollArea } from '@/components/ui/ScrollArea';
import { Select } from '@/components/ui/Select';
import { SkeletonText } from '@/components/ui/Skeleton';
import { Textarea } from '@/components/ui/Textarea';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import { humanize } from '@/lib/format/text';
import { EMAIL_API, emailKeys, useAutomation, useLists, useSenders, useTemplates } from '../api/queries';
import type { Automation, AutomationStepType, AutomationTrigger, EnrollmentStatus, StepConfig, StepDefinition, TriggerConfig } from '../api/types';
import { useEmailWorkspace } from '../shared/workspace';
import { AutomationStatusBadge } from './AutomationsPage';

const TRIGGERS: { value: AutomationTrigger; label: string }[] = [
  { value: 'ListSubscribed', label: 'Joins a list' },
  { value: 'NewsletterConfirmed', label: 'Confirms a newsletter sign-up' },
  { value: 'TagAdded', label: 'Gets a tag' },
  { value: 'FormSubmitted', label: 'Submits a form' },
  { value: 'DateAnniversary', label: 'Date anniversary (e.g. birthday)' },
  { value: 'CustomEvent', label: 'Custom event (e.g. cart_abandoned)' },
];

const STEP_TYPES: { value: AutomationStepType; label: string }[] = [
  { value: 'SendEmail', label: 'Send email' },
  { value: 'SendSms', label: 'Send SMS' },
  { value: 'Wait', label: 'Wait' },
  { value: 'Condition', label: 'If / else' },
  { value: 'AddTag', label: 'Add tag' },
  { value: 'RemoveTag', label: 'Remove tag' },
  { value: 'NotifyStaff', label: 'Notify staff' },
  { value: 'Exit', label: 'Exit' },
];

const CHECKS = [
  { value: 'opened', label: 'Opened an email of this journey' },
  { value: 'clicked', label: 'Clicked an email of this journey' },
  { value: 'tag', label: 'Has a tag' },
  { value: 'field', label: 'Field equals a value' },
  { value: 'event', label: 'Had a custom event' },
];

type Step = StepDefinition;

function nextKey(steps: Step[]): string {
  let n = steps.length + 1;
  while (steps.some((s) => s.key === `s${n}`)) n++;
  return `s${n}`;
}

function defaultConfig(type: AutomationStepType): StepConfig {
  switch (type) {
    case 'Wait':
      return { days: 1 };
    case 'Condition':
      return { check: 'opened' };
    default:
      return {};
  }
}

/** Journey editor: trigger, goal, sender and an ordered step graph (each step names its next step). */
export function AutomationEditorPage() {
  const { id } = useParams();
  const isNew = !id || id === 'new';
  const automation = useAutomation(isNew ? undefined : id);
  if (!isNew && automation.isPending) return <SkeletonText lines={8} />;
  if (!isNew && automation.isError) return <ErrorState error={automation.error} onRetry={() => void automation.refetch()} />;
  return <AutomationForm key={automation.data?.concurrencyStamp ?? 'new'} automation={isNew ? null : automation.data!} />;
}

function StepEditor({
  step,
  index,
  steps,
  onChange,
  onRemove,
  onMove,
  templates,
}: {
  step: Step;
  index: number;
  steps: Step[];
  onChange: (step: Step) => void;
  onRemove: () => void;
  onMove: (delta: number) => void;
  templates: { value: string; label: string }[];
}) {
  const c = step.config;
  const set = (patch: Partial<StepConfig>) => onChange({ ...step, config: { ...c, ...patch } });
  const targets = [{ value: '', label: 'End of journey' }, ...steps.filter((s) => s.key !== step.key).map((s) => ({ value: s.key, label: `${s.key} · ${humanize(s.type)}` }))];
  const num = (v: string) => (v === '' ? null : Math.max(0, Number(v)));
  const title = `Step ${step.key}`;
  return (
    <li className={`journey-step${step.type === 'Condition' ? ' journey-step--condition' : step.type === 'Wait' ? ' journey-step--wait' : ''}`}>
      <fieldset className="stack">
        <legend className="email-section-title">
          {title} · {STEP_TYPES.find((t) => t.value === step.type)?.label}
        </legend>
        <div className="cluster">
          <IconButton label={`Move ${title} up`} icon={<ArrowUp />} size="sm" variant="ghost" disabled={index === 0} onClick={() => onMove(-1)} />
          <IconButton label={`Move ${title} down`} icon={<ArrowDown />} size="sm" variant="ghost" disabled={index === steps.length - 1} onClick={() => onMove(1)} />
          <IconButton label={`Remove ${title}`} icon={<Trash2 />} size="sm" variant="ghost" onClick={onRemove} />
        </div>
        <FormField label="Action">
          <Select
            value={step.type}
            options={STEP_TYPES}
            onChange={(e) => {
              const type = e.target.value as AutomationStepType;
              onChange({ ...step, type, config: defaultConfig(type), altNext: type === 'Condition' ? step.altNext : null });
            }}
          />
        </FormField>
        {step.type === 'SendEmail' && (
          <>
            <FormField label="Template" required>
              <Select value={c.templateId ?? ''} placeholder="Choose a template" options={templates} onChange={(e) => set({ templateId: e.target.value || null })} />
            </FormField>
            <FormField label="Subject override" optional>
              <Input value={c.subject ?? ''} onChange={(e) => set({ subject: e.target.value || null })} maxLength={200} />
            </FormField>
          </>
        )}
        {step.type === 'SendSms' && (
          <FormField label="SMS text" required hint="Sent only to contacts with SMS consent, outside quiet hours. Include “Reply STOP to opt out”.">
            <Textarea rows={3} value={c.body ?? ''} onChange={(e) => set({ body: e.target.value })} maxLength={1600} />
          </FormField>
        )}
        {step.type === 'Wait' && (
          <div className="cluster">
            <FormField label="Days">
              <Input type="number" min={0} max={365} value={c.days ?? ''} onChange={(e) => set({ days: num(e.target.value) })} />
            </FormField>
            <FormField label="Hours">
              <Input type="number" min={0} max={23} value={c.hours ?? ''} onChange={(e) => set({ hours: num(e.target.value) })} />
            </FormField>
            <FormField label="Then until (local time)" optional hint="HH:mm in the contact's time zone">
              <Input type="time" value={c.untilTime ?? ''} onChange={(e) => set({ untilTime: e.target.value || null })} />
            </FormField>
          </div>
        )}
        {step.type === 'Condition' && (
          <>
            <FormField label="Check">
              <Select value={c.check ?? 'opened'} options={CHECKS} onChange={(e) => set({ check: e.target.value })} />
            </FormField>
            {(c.check === 'opened' || c.check === 'clicked') && (
              <FormField label="Which email" optional>
                <Select
                  value={c.stepKey ?? ''}
                  options={[{ value: '', label: 'Any email of this journey' }, ...steps.filter((s) => s.type === 'SendEmail').map((s) => ({ value: s.key, label: `Step ${s.key}` }))]}
                  onChange={(e) => set({ stepKey: e.target.value || null })}
                />
              </FormField>
            )}
            {c.check === 'tag' && (
              <FormField label="Tag" required>
                <Input value={c.tag ?? ''} onChange={(e) => set({ tag: e.target.value })} maxLength={50} />
              </FormField>
            )}
            {c.check === 'field' && (
              <div className="cluster">
                <FormField label="Field" required hint="e.g. country or custom.plan">
                  <Input value={c.field ?? ''} onChange={(e) => set({ field: e.target.value })} maxLength={60} />
                </FormField>
                <FormField label="Value">
                  <Input value={c.value ?? ''} onChange={(e) => set({ value: e.target.value })} maxLength={200} />
                </FormField>
              </div>
            )}
            {c.check === 'event' && (
              <FormField label="Event name" required>
                <Input value={c.eventName ?? ''} onChange={(e) => set({ eventName: e.target.value })} maxLength={64} />
              </FormField>
            )}
          </>
        )}
        {(step.type === 'AddTag' || step.type === 'RemoveTag') && (
          <FormField label="Tag" required>
            <Input value={c.tag ?? ''} onChange={(e) => set({ tag: e.target.value })} maxLength={50} />
          </FormField>
        )}
        {step.type === 'NotifyStaff' && (
          <FormField label="Message to the workspace's staff" required>
            <Textarea rows={2} value={c.message ?? ''} onChange={(e) => set({ message: e.target.value })} maxLength={500} />
          </FormField>
        )}
        {step.type !== 'Exit' && (
          <div className="cluster">
            <FormField label={step.type === 'Condition' ? 'If yes, go to' : 'Then go to'}>
              <Select value={step.next ?? ''} options={targets} onChange={(e) => onChange({ ...step, next: e.target.value || null })} />
            </FormField>
            {step.type === 'Condition' && (
              <FormField label="If no, go to">
                <Select value={step.altNext ?? ''} options={targets} onChange={(e) => onChange({ ...step, altNext: e.target.value || null })} />
              </FormField>
            )}
          </div>
        )}
      </fieldset>
    </li>
  );
}

const enrollmentTones: Record<EnrollmentStatus, string> = { Active: 'In progress', Completed: 'Completed', Exited: 'Exited', Failed: 'Failed' };

function Enrollments({ automationId }: { automationId: string }) {
  const enrollments = useQuery({
    queryKey: [...emailKeys.automation(automationId), 'enrollments'],
    queryFn: ({ signal }) =>
      api.get<{ id: string; subscriberId: string; email: string | null; status: EnrollmentStatus; currentStepKey: string | null; enteredAt: string; nextRunAt: string; finishedAt: string | null; exitReason: string | null }[]>(
        `${EMAIL_API}/automations/${automationId}/enrollments`,
        { signal },
      ),
  });
  if (enrollments.isError) return <ErrorState error={enrollments.error} onRetry={() => void enrollments.refetch()} />;
  return (
    <DataTable
      caption="Latest 100 enrollments"
      rows={enrollments.data ?? []}
      getRowId={(e) => e.id}
      loading={enrollments.isPending}
      columns={[
        { id: 'contact', header: 'Contact', primary: true, cell: (e) => e.email ?? e.subscriberId },
        { id: 'status', header: 'Status', cell: (e) => enrollmentTones[e.status] },
        { id: 'step', header: 'Current step', cell: (e) => e.currentStepKey ?? '—' },
        { id: 'entered', header: 'Entered', hideOnMobile: true, cell: (e) => <DateTime value={e.enteredAt} format="datetime" /> },
        { id: 'reason', header: 'Exit reason', hideOnMobile: true, cell: (e) => (e.exitReason ? humanize(e.exitReason) : '—') },
      ]}
    />
  );
}

function AutomationForm({ automation }: { automation: Automation | null }) {
  const { clientId, key } = useEmailWorkspace();
  const workspaceId = automation ? automation.clientAccountId : clientId;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const toast = useToast();
  const lists = useLists(workspaceId);
  const templates = useTemplates(workspaceId);
  const senders = useSenders(workspaceId);
  const [name, setName] = useState(automation?.name ?? '');
  const [description, setDescription] = useState(automation?.description ?? '');
  const [trigger, setTrigger] = useState<AutomationTrigger>(automation?.trigger ?? 'ListSubscribed');
  const [triggerConfig, setTriggerConfig] = useState<TriggerConfig>(automation?.triggerConfig ?? {});
  const [reentry, setReentry] = useState(automation?.reentry ?? 'Never');
  const [cooldown, setCooldown] = useState(automation?.reentryCooldownDays ?? 0);
  const [goalKind, setGoalKind] = useState(automation?.goal?.kind ?? '');
  const [goalValue, setGoalValue] = useState(automation?.goal?.tag ?? automation?.goal?.eventName ?? '');
  const [senderId, setSenderId] = useState(automation?.senderProfileId ?? '');
  const [steps, setSteps] = useState<Step[]>(
    automation?.steps.map(({ key: k, type, config, next, altNext }) => ({ key: k, type, config, next, altNext })) ?? [
      { key: 's1', type: 'SendEmail', config: {}, next: null },
    ],
  );
  const [archiveOpen, setArchiveOpen] = useState(false);
  const stats = new Map((automation?.steps ?? []).map((s) => [s.key, s.stats]));

  const body = () => ({
    clientAccountId: workspaceId,
    name,
    description: description || null,
    trigger,
    triggerConfig,
    reentry,
    reentryCooldownDays: cooldown,
    goal: goalKind ? { kind: goalKind, tag: goalKind === 'tag_added' ? goalValue : null, eventName: goalKind === 'event' ? goalValue : null } : null,
    senderProfileId: senderId || null,
    entryStepKey: steps[0]?.key ?? null,
    steps,
    concurrencyStamp: automation?.concurrencyStamp,
  });

  const save = useMutation({
    mutationFn: () => (automation ? api.put<Automation>(`${EMAIL_API}/automations/${automation.id}`, body()) : api.post<Automation>(`${EMAIL_API}/automations`, body())),
    onSuccess: (saved) => {
      toast.success('Journey saved');
      void queryClient.invalidateQueries({ queryKey: emailKeys.automations(key) });
      queryClient.setQueryData(emailKeys.automation(saved.id), saved);
      if (!automation) navigate(`../${saved.id}`, { relative: 'path' });
    },
  });

  const status = useMutation({
    mutationFn: (action: 'activate' | 'pause' | 'archive') => api.post<Automation>(`${EMAIL_API}/automations/${automation!.id}/${action}`),
    onSuccess: (saved, action) => {
      toast.success(action === 'activate' ? 'Journey activated' : action === 'pause' ? 'Journey paused' : 'Journey archived');
      void queryClient.invalidateQueries({ queryKey: emailKeys.automations(key) });
      if (action === 'archive') navigate('/agency/email/automations');
      else queryClient.setQueryData(emailKeys.automation(saved.id), saved);
    },
  });

  const errorOf = (e: unknown) => (isApiError(e) && e.errors ? Object.values(e.errors).flat() : [errorMessage(e)]);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    save.mutate();
  };

  const addStep = () => {
    const k = nextKey(steps);
    setSteps((prev) => {
      const last = prev[prev.length - 1];
      const linked = last && !last.next && last.type !== 'Exit' ? [...prev.slice(0, -1), { ...last, next: k }] : prev;
      return [...linked, { key: k, type: 'Wait', config: defaultConfig('Wait'), next: null }];
    });
  };
  const removeStep = (k: string) =>
    setSteps((prev) =>
      prev.filter((s) => s.key !== k).map((s) => ({ ...s, next: s.next === k ? null : s.next, altNext: s.altNext === k ? null : s.altNext })),
    );
  const moveStep = (index: number, delta: number) =>
    setSteps((prev) => {
      const next = [...prev];
      const [item] = next.splice(index, 1);
      next.splice(index + delta, 0, item!);
      return next;
    });

  const isActive = automation?.status === 'Active';
  const title = automation ? automation.name : 'New journey';

  return (
    <>
      <PageHeader
        title={title}
        breadcrumbs={[{ label: 'Journeys', to: '/agency/email/automations' }, { label: automation ? automation.name : 'New' }]}
        meta={automation && <AutomationStatusBadge status={automation.status} />}
        actions={
          automation && (
            <>
              {isActive ? (
                <Button variant="secondary" leadingIcon={<Pause />} loading={status.isPending} onClick={() => status.mutate('pause')}>
                  Pause
                </Button>
              ) : (
                <Button leadingIcon={<Play />} loading={status.isPending} onClick={() => status.mutate('activate')}>
                  Activate
                </Button>
              )}
              <Button variant="ghost" onClick={() => setArchiveOpen(true)}>
                Archive
              </Button>
            </>
          )
        }
      />
      {automation && (
        <p className="email-muted">
          {formatNumber(automation.active)} in progress · {formatNumber(automation.completed)} completed · {formatNumber(automation.exited)} exited
        </p>
      )}
      {isActive && <Alert tone="info">This journey is live. Saved changes apply to contacts when they reach the changed steps.</Alert>}
      {status.isError && (
        <Alert tone="danger" title="The journey could not change status">
          <ul>
            {errorOf(status.error).map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
        </Alert>
      )}
      <form className="stack" onSubmit={submit} aria-label="Journey">
        <Card>
          <CardHeader title="Trigger & goal" />
          <CardBody className="stack">
            <FormField label="Name" required>
              <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={150} />
            </FormField>
            <FormField label="Description" optional>
              <Textarea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} maxLength={1000} />
            </FormField>
            <FormField label="Starts when a contact…">
              <Select value={trigger} options={TRIGGERS} onChange={(e) => setTrigger(e.target.value as AutomationTrigger)} />
            </FormField>
            {(trigger === 'ListSubscribed' || trigger === 'NewsletterConfirmed' || trigger === 'FormSubmitted') && (
              <FormField label="List" required={trigger === 'ListSubscribed'} optional={trigger !== 'ListSubscribed'}>
                <Select
                  value={triggerConfig.listId ?? ''}
                  placeholder="Any list"
                  options={(lists.data ?? []).map((l) => ({ value: l.id, label: l.name }))}
                  onChange={(e) => setTriggerConfig({ ...triggerConfig, listId: e.target.value || null })}
                />
              </FormField>
            )}
            {trigger === 'TagAdded' && (
              <FormField label="Tag" required>
                <Input value={triggerConfig.tag ?? ''} onChange={(e) => setTriggerConfig({ ...triggerConfig, tag: e.target.value })} maxLength={50} />
              </FormField>
            )}
            {trigger === 'DateAnniversary' && (
              <FormField label="Custom date field" required hint="Values in yyyy-MM-dd, e.g. birthday">
                <Input value={triggerConfig.dateField ?? ''} onChange={(e) => setTriggerConfig({ ...triggerConfig, dateField: e.target.value })} maxLength={40} />
              </FormField>
            )}
            {trigger === 'CustomEvent' && (
              <FormField label="Event name" required hint="Sent by your site or the conversions API, e.g. cart_abandoned">
                <Input value={triggerConfig.eventName ?? ''} onChange={(e) => setTriggerConfig({ ...triggerConfig, eventName: e.target.value })} maxLength={64} />
              </FormField>
            )}
            <div className="cluster">
              <FormField label="Re-entry">
                <Select
                  value={reentry}
                  options={[
                    { value: 'Never', label: 'A contact enters once' },
                    { value: 'AfterExit', label: 'Again after finishing' },
                  ]}
                  onChange={(e) => setReentry(e.target.value as 'Never' | 'AfterExit')}
                />
              </FormField>
              {reentry === 'AfterExit' && (
                <FormField label="Cooldown (days)">
                  <Input type="number" min={0} max={3650} value={cooldown} onChange={(e) => setCooldown(Number(e.target.value) || 0)} />
                </FormField>
              )}
            </div>
            <div className="cluster">
              <FormField label="Goal (exit when met)" optional>
                <Select
                  value={goalKind}
                  options={[
                    { value: '', label: 'No goal' },
                    { value: 'purchased', label: 'Makes a purchase' },
                    { value: 'tag_added', label: 'Gets a tag' },
                    { value: 'event', label: 'Has a custom event' },
                  ]}
                  onChange={(e) => setGoalKind(e.target.value)}
                />
              </FormField>
              {(goalKind === 'tag_added' || goalKind === 'event') && (
                <FormField label={goalKind === 'event' ? 'Goal event' : 'Goal tag'} required>
                  <Input value={goalValue} onChange={(e) => setGoalValue(e.target.value)} maxLength={64} />
                </FormField>
              )}
            </div>
            <FormField label="Sender" hint="Required for email steps; must be verified before activation.">
              <Select
                value={senderId}
                placeholder="Choose a sender"
                options={(senders.data ?? []).map((s) => ({ value: s.id, label: `${s.fromName} <${s.fromEmail}>${s.verified ? '' : ' (unverified)'}` }))}
                onChange={(e) => setSenderId(e.target.value)}
              />
            </FormField>
          </CardBody>
        </Card>
        <Card>
          <CardHeader title="Steps" description="The first step runs when a contact enters. Each step names what happens next; journeys cannot loop." />
          <CardBody className="stack">
            <ol className="journey-steps">
              {steps.map((step, index) => (
                <StepEditor
                  key={step.key}
                  step={step}
                  index={index}
                  steps={steps}
                  templates={(templates.data ?? []).map((t) => ({ value: t.id, label: t.name }))}
                  onChange={(s) => setSteps((prev) => prev.map((p) => (p.key === step.key ? s : p)))}
                  onRemove={() => removeStep(step.key)}
                  onMove={(delta) => moveStep(index, delta)}
                />
              ))}
            </ol>
            {automation && steps.some((s) => stats.has(s.key)) && (
              <ScrollArea className="ui-table-wrap" label="Step results">
                <table className="ui-table">
                  <caption>Step results</caption>
                  <thead>
                    <tr>
                      <th scope="col">Step</th>
                      <th scope="col">Runs</th>
                      <th scope="col">Sent</th>
                      <th scope="col">Opened</th>
                      <th scope="col">Clicked</th>
                      <th scope="col">Skipped</th>
                      <th scope="col">Failed</th>
                    </tr>
                  </thead>
                  <tbody>
                    {automation.steps.map((s) => (
                      <tr key={s.key}>
                        <th scope="row">{s.key}</th>
                        <td>{formatNumber(s.stats.runs)}</td>
                        <td>{formatNumber(s.stats.sent)}</td>
                        <td>{formatNumber(s.stats.opened)}</td>
                        <td>{formatNumber(s.stats.clicked)}</td>
                        <td>{formatNumber(s.stats.skipped)}</td>
                        <td>{formatNumber(s.stats.failed)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </ScrollArea>
            )}
            <div className="cluster">
              <Button type="button" variant="secondary" leadingIcon={<Plus />} onClick={addStep} disabled={steps.length >= 50}>
                Add step
              </Button>
            </div>
          </CardBody>
        </Card>
        {save.isError && (
          <Alert tone="danger" title="The journey was not saved">
            <ul>
              {errorOf(save.error).map((m) => (
                <li key={m}>{m}</li>
              ))}
            </ul>
          </Alert>
        )}
        <div className="cluster">
          <Button type="submit" leadingIcon={<Save />} loading={save.isPending}>
            Save journey
          </Button>
        </div>
      </form>
      {automation && (
        <Card>
          <CardHeader title="Contacts in this journey" />
          <CardBody>
            <Enrollments automationId={automation.id} />
          </CardBody>
        </Card>
      )}
      <ConfirmDialog
        open={archiveOpen}
        onClose={() => setArchiveOpen(false)}
        tone="danger"
        title="Archive this journey?"
        description="Contacts in progress stop at their current step. Archived journeys are hidden."
        confirmLabel="Archive"
        onConfirm={async () => {
          await status.mutateAsync('archive');
        }}
      />
    </>
  );
}
