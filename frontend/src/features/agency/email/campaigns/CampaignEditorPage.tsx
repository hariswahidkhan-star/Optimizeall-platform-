import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BarChart3, Pause, Play, Save, Send, XCircle } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { ErrorState } from '@/components/ui/ErrorState';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { RadioGroup } from '@/components/ui/RadioGroup';
import { Select } from '@/components/ui/Select';
import { SkeletonText } from '@/components/ui/Skeleton';
import { Switch } from '@/components/ui/Switch';
import { Textarea } from '@/components/ui/Textarea';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { campaignsPath, emailKeys, useCampaign, useChecklist, useLists, useSegments, useSenders, useTemplates } from '../api/queries';
import type { Campaign, EmailDesign, ScheduleMode, Template } from '../api/types';
import { EMAIL_API } from '../api/queries';
import { ApprovalBadge, CampaignStatusBadge, ChecklistView, toLocalInput } from '../shared/ui';
import { useEmailWorkspace } from '../shared/workspace';
import { BlockEditor, MERGE_TAG_HELP, emptyDesign } from '../templates/BlockEditor';
import { EmailPreview } from '../templates/EmailPreview';
import { TestSendDialog } from '../templates/TestSendDialog';
import { SendConfirmDialog } from './SendConfirmDialog';

type ChannelKind = 'email' | 'sms';

export function CampaignEditorPage({ channel = 'email' }: { channel?: ChannelKind }) {
  const { id } = useParams();
  const isNew = !id || id === 'new';
  const campaign = useCampaign(isNew ? undefined : id, channel);
  if (!isNew && campaign.isPending) return <SkeletonText lines={8} />;
  if (!isNew && campaign.isError) return <ErrorState error={campaign.error} onRetry={() => void campaign.refetch()} />;
  return <CampaignForm key={campaign.data?.concurrencyStamp ?? 'new'} channel={channel} campaign={isNew ? null : campaign.data!} />;
}

interface SmsSegmentsResult {
  encoding: string;
  characters: number;
  segments: number;
  remaining: number;
}

function SmsCounter({ text }: { text: string }) {
  const debounced = useDebouncedValue(text, 300);
  const result = useQuery({
    queryKey: ['email', 'sms-segments', debounced],
    queryFn: ({ signal }) => api.post<SmsSegmentsResult>(`${EMAIL_API}/sms/campaigns/segments`, { text: debounced }, { signal }),
    placeholderData: (prev) => prev,
  });
  if (!result.data) return null;
  return (
    <p className="email-muted" aria-live="polite">
      {result.data.characters} {result.data.encoding === 'Gsm7' ? 'GSM-7' : 'UCS-2 (Unicode)'} characters · {result.data.segments} segment
      {result.data.segments === 1 ? '' : 's'} · {result.data.remaining} left in this segment
    </p>
  );
}

function CampaignForm({ channel, campaign }: { channel: ChannelKind; campaign: Campaign | null }) {
  const { clientId } = useEmailWorkspace();
  const workspaceId = campaign ? campaign.clientAccountId : clientId;
  const { hasPermission } = useAuth();
  const canSend = hasPermission(Permissions.EmailSend);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const toast = useToast();
  const lists = useLists(workspaceId);
  const segments = useSegments(workspaceId);
  const senders = useSenders(workspaceId);
  const templates = useTemplates(workspaceId);
  const checklist = useChecklist(campaign?.id, channel);
  const editable = !campaign || campaign.status === 'Draft';
  const base = channel === 'sms' ? '/agency/sms' : '/agency/email/campaigns';

  const [name, setName] = useState(campaign?.name ?? '');
  const [smsChannel, setSmsChannel] = useState<'Sms' | 'WhatsApp'>(campaign?.channel === 'WhatsApp' ? 'WhatsApp' : 'Sms');
  const [listId, setListId] = useState(campaign?.listId ?? '');
  const [segmentId, setSegmentId] = useState(campaign?.segmentId ?? '');
  const [senderId, setSenderId] = useState(campaign?.senderProfileId ?? '');
  const [subject, setSubject] = useState(campaign?.subject ?? '');
  const [previewText, setPreviewText] = useState(campaign?.previewText ?? '');
  const [design, setDesign] = useState<EmailDesign>(campaign?.design?.blocks ? campaign.design : emptyDesign());
  const [smsBody, setSmsBody] = useState(campaign?.smsBody ?? 'Hi {{first_name|there}}, … Reply STOP to opt out');
  const [waTemplate, setWaTemplate] = useState(campaign?.whatsAppTemplateName ?? '');
  const [waLanguage, setWaLanguage] = useState(campaign?.whatsAppTemplateLanguage ?? 'en');
  const [waParams, setWaParams] = useState((campaign?.whatsAppParameters ?? []).join('\n'));
  const [scheduleMode, setScheduleMode] = useState<ScheduleMode>(campaign?.scheduleMode ?? 'Immediate');
  const [scheduledAt, setScheduledAt] = useState(toLocalInput(campaign?.scheduledAt));
  const [localTime, setLocalTime] = useState(campaign?.scheduledLocalTime ?? '');
  const [throttle, setThrottle] = useState(String(campaign?.throttlePerMinute ?? 600));
  const [windowStart, setWindowStart] = useState(campaign?.sendWindowStartHour?.toString() ?? '');
  const [windowEnd, setWindowEnd] = useState(campaign?.sendWindowEndHour?.toString() ?? '');
  const [abTest, setAbTest] = useState(campaign?.type === 'AbTest');
  const variantB = campaign?.variants.find((v) => v.key === 'B');
  const [bSubject, setBSubject] = useState(variantB?.subject ?? '');
  const [abPercent, setAbPercent] = useState(String(campaign?.abTestPercent ?? 20));
  const [abMetric, setAbMetric] = useState(campaign?.abWinnerMetric ?? 'OpenRate');
  const [abWait, setAbWait] = useState(String(campaign?.abWaitHours ?? 4));
  const [templateToApply, setTemplateToApply] = useState('');
  const [sendOpen, setSendOpen] = useState(false);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [testOpen, setTestOpen] = useState(false);

  const body = () => ({
    clientAccountId: workspaceId,
    name,
    channel: channel === 'email' ? 'Email' : smsChannel,
    type: channel === 'email' && abTest ? 'AbTest' : 'Regular',
    listId: listId || null,
    segmentId: segmentId || null,
    senderProfileId: senderId || null,
    subject,
    previewText: previewText || null,
    design: channel === 'email' ? design : undefined,
    smsBody: channel === 'sms' ? smsBody : null,
    whatsAppTemplateName: smsChannel === 'WhatsApp' ? waTemplate : null,
    whatsAppTemplateLanguage: smsChannel === 'WhatsApp' ? waLanguage : null,
    whatsAppParameters: smsChannel === 'WhatsApp' ? waParams.split('\n').map((p) => p.trim()).filter(Boolean) : null,
    scheduleMode,
    scheduledAt: scheduleMode === 'FixedTime' && scheduledAt ? new Date(scheduledAt).toISOString() : null,
    scheduledLocalTime: scheduleMode === 'RecipientTimeZone' ? localTime : null,
    throttlePerMinute: Number(throttle) || undefined,
    sendWindowStartHour: windowStart === '' ? null : Number(windowStart),
    sendWindowEndHour: windowEnd === '' ? null : Number(windowEnd),
    abTestPercent: Number(abPercent) || 20,
    abWinnerMetric: abMetric,
    abWaitHours: Number(abWait) || 4,
    variants: abTest ? [{ key: 'A' }, { key: 'B', subject: bSubject || null }] : null,
    concurrencyStamp: campaign?.concurrencyStamp,
  });

  const save = useMutation({
    mutationFn: () => (campaign ? api.put<Campaign>(`${campaignsPath(channel)}/${campaign.id}`, body()) : api.post<Campaign>(campaignsPath(channel), body())),
    onSuccess: (saved) => {
      toast.success('Campaign saved');
      void queryClient.invalidateQueries({ queryKey: emailKeys.all });
      if (!campaign) navigate(`${base}/${saved.id}`);
    },
  });

  const applyTemplate = useMutation({
    mutationFn: (templateId: string) => api.get<Template>(`${EMAIL_API}/templates/${templateId}`),
    onSuccess: (t) => {
      setDesign(t.design);
      if (!subject) setSubject(t.subject);
      if (!previewText && t.previewText) setPreviewText(t.previewText);
      toast.info('Template applied', 'Its content replaced the current design (not saved yet).');
    },
  });

  const action = useMutation({
    mutationFn: ({ verb, reason }: { verb: 'pause' | 'resume' | 'cancel' | 'unschedule'; reason?: string }) =>
      api.post<Campaign>(`${campaignsPath(channel)}/${campaign!.id}/${verb}`, { concurrencyStamp: campaign!.concurrencyStamp, reason }),
    onSuccess: (updated) => {
      toast.success(`Campaign ${updated.status.toLowerCase()}`);
      void queryClient.invalidateQueries({ queryKey: emailKeys.all });
    },
    onError: (e) => toast.error('Action failed', errorMessage(e)),
  });

  const fieldErrors = isApiError(save.error) ? Object.values(save.error.errors ?? {}).flat() : [];
  const submit = (event: FormEvent) => {
    event.preventDefault();
    save.mutate();
  };

  return (
    <>
      <PageHeader
        title={campaign ? campaign.name : channel === 'sms' ? 'New SMS / WhatsApp campaign' : 'New email campaign'}
        breadcrumbs={[{ label: channel === 'sms' ? 'SMS & WhatsApp' : 'Campaigns', to: base }, { label: campaign ? campaign.name : 'New' }]}
        meta={
          campaign && (
            <span className="cluster">
              <CampaignStatusBadge status={campaign.status} />
              <ApprovalBadge status={campaign.approvalStatus} />
            </span>
          )
        }
        actions={
          <>
            {campaign && campaign.status !== 'Draft' && (
              <ButtonLink to={`${base}/${campaign.id}/report`} variant="secondary" leadingIcon={<BarChart3 />}>
                Report
              </ButtonLink>
            )}
            {campaign && channel === 'email' && (
              <Button variant="secondary" onClick={() => setTestOpen(true)}>
                Send test
              </Button>
            )}
            {editable && (
              <Button type="submit" form="campaign-form" variant={campaign ? 'secondary' : 'primary'} leadingIcon={<Save />} loading={save.isPending}>
                Save draft
              </Button>
            )}
            {campaign?.status === 'Draft' && canSend && (
              <Button leadingIcon={<Send />} onClick={() => setSendOpen(true)} disabled={!checklist.data?.canSend}>
                Review & send
              </Button>
            )}
          </>
        }
      />

      {campaign?.pauseReason && campaign.status === 'Paused' && <Alert tone="warning" title="Paused">{campaign.pauseReason}</Alert>}
      {campaign?.approvalStatus === 'Rejected' && campaign.approvalNote && (
        <Alert tone="danger" title="The client requested changes">
          {campaign.approvalNote}
        </Alert>
      )}
      {save.isError && (
        <Alert tone="danger" title="Could not save the campaign">
          {fieldErrors.length > 0 ? (
            <ul>
              {fieldErrors.map((e) => (
                <li key={e}>{e}</li>
              ))}
            </ul>
          ) : (
            errorMessage(save.error)
          )}
        </Alert>
      )}
      {campaign && !canSend && campaign.status === 'Draft' && (
        <Alert tone="info">Sending needs the email.send permission. Save the draft and ask an administrator to send it.</Alert>
      )}

      {campaign && campaign.status !== 'Draft' && canSend && (
        <Card>
          <CardHeader title="Sending controls" />
          <CardBody className="cluster">
            {(campaign.status === 'Scheduled' || campaign.status === 'Sending') && (
              <Button variant="secondary" leadingIcon={<Pause />} loading={action.isPending} onClick={() => action.mutate({ verb: 'pause' })}>
                Pause
              </Button>
            )}
            {campaign.status === 'Paused' && (
              <Button variant="secondary" leadingIcon={<Play />} loading={action.isPending} onClick={() => action.mutate({ verb: 'resume' })}>
                Resume
              </Button>
            )}
            {campaign.status === 'Scheduled' && !campaign.sendStartedAt && (
              <Button variant="secondary" loading={action.isPending} onClick={() => action.mutate({ verb: 'unschedule' })}>
                Back to draft
              </Button>
            )}
            {['Scheduled', 'Sending', 'Paused'].includes(campaign.status) && (
              <Button variant="danger" leadingIcon={<XCircle />} onClick={() => setCancelOpen(true)}>
                Cancel campaign
              </Button>
            )}
          </CardBody>
        </Card>
      )}

      <div className="email-two-col">
        <form id="campaign-form" onSubmit={submit} className="stack" aria-label="Campaign settings">
          <fieldset disabled={!editable} className="stack" style={{ border: 0, padding: 0, margin: 0 }}>
            <Card>
              <CardHeader title="Basics" />
              <CardBody className="stack">
                <FormField label="Campaign name" required hint="Internal name; typed back to confirm the send.">
                  <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={150} />
                </FormField>
                {channel === 'sms' && (
                  <RadioGroup
                    legend="Channel"
                    orientation="horizontal"
                    value={smsChannel}
                    onChange={(v) => setSmsChannel(v as 'Sms' | 'WhatsApp')}
                    options={[
                      { value: 'Sms', label: 'SMS (Twilio)' },
                      { value: 'WhatsApp', label: 'WhatsApp template' },
                    ]}
                  />
                )}
                <FormField label="List" hint="Only subscribed contacts with consent for this channel receive it.">
                  <Select value={listId} placeholder="No list (segment only)" options={(lists.data ?? []).map((l) => ({ value: l.id, label: l.name }))} onChange={(e) => setListId(e.target.value)} />
                </FormField>
                <FormField label="Segment" optional>
                  <Select value={segmentId} placeholder="Everyone on the list" options={(segments.data ?? []).map((s) => ({ value: s.id, label: s.name }))} onChange={(e) => setSegmentId(e.target.value)} />
                </FormField>
              </CardBody>
            </Card>

            {channel === 'email' ? (
              <Card>
                <CardHeader title="Email" />
                <CardBody className="stack">
                  <FormField label="From (verified sender)" required>
                    <Select
                      value={senderId}
                      placeholder="Choose a sender"
                      options={(senders.data ?? []).map((s) => ({ value: s.id, label: `${s.fromName} <${s.fromEmail}>${s.verified ? '' : ' — not verified'}` }))}
                      onChange={(e) => setSenderId(e.target.value)}
                    />
                  </FormField>
                  <FormField label="Subject line" required hint={MERGE_TAG_HELP}>
                    <Input value={subject} onChange={(e) => setSubject(e.target.value)} maxLength={200} />
                  </FormField>
                  <FormField label="Preview text" optional>
                    <Input value={previewText} onChange={(e) => setPreviewText(e.target.value)} maxLength={200} />
                  </FormField>
                  <div className="cluster">
                    <FormField label="Start from a template">
                      <Select
                        value={templateToApply}
                        placeholder="Choose a template"
                        options={(templates.data ?? []).map((t) => ({ value: t.id, label: t.isGlobal ? `${t.name} (library)` : t.name }))}
                        onChange={(e) => setTemplateToApply(e.target.value)}
                      />
                    </FormField>
                    <Button variant="secondary" size="sm" disabled={!templateToApply} loading={applyTemplate.isPending} onClick={() => applyTemplate.mutate(templateToApply)}>
                      Apply template
                    </Button>
                  </div>
                  <Switch
                    checked={abTest}
                    onCheckedChange={setAbTest}
                    label="A/B test the subject line"
                    description="Send two versions to a test cohort, then the winner to everyone else."
                  />
                  {abTest && (
                    <div className="stack">
                      <FormField label="Variant B subject line" required>
                        <Input value={bSubject} onChange={(e) => setBSubject(e.target.value)} maxLength={200} />
                      </FormField>
                      <FormField label="Test cohort (%)" hint="5–50% of the audience, split evenly between A and B.">
                        <Input type="number" min={5} max={50} value={abPercent} onChange={(e) => setAbPercent(e.target.value)} />
                      </FormField>
                      <FormField label="Pick the winner by">
                        <Select
                          value={abMetric}
                          options={[
                            { value: 'OpenRate', label: 'Open rate (machine opens excluded)' },
                            { value: 'ClickRate', label: 'Click rate' },
                          ]}
                          onChange={(e) => setAbMetric(e.target.value as Campaign['abWinnerMetric'])}
                        />
                      </FormField>
                      <FormField label="Wait before picking (hours)">
                        <Input type="number" min={1} max={72} value={abWait} onChange={(e) => setAbWait(e.target.value)} />
                      </FormField>
                    </div>
                  )}
                </CardBody>
              </Card>
            ) : smsChannel === 'Sms' ? (
              <Card>
                <CardHeader title="Message" />
                <CardBody className="stack">
                  <FormField label="SMS text" required hint="Include “Reply STOP to opt out”. Emoji and non-Latin characters switch to UCS-2 (70 characters per segment).">
                    <Textarea rows={4} value={smsBody} onChange={(e) => setSmsBody(e.target.value)} maxLength={1600} />
                  </FormField>
                  <SmsCounter text={smsBody} />
                </CardBody>
              </Card>
            ) : (
              <Card>
                <CardHeader title="WhatsApp template" />
                <CardBody className="stack">
                  <FormField label="Approved template name" required>
                    <Input value={waTemplate} onChange={(e) => setWaTemplate(e.target.value)} maxLength={100} />
                  </FormField>
                  <FormField label="Language code">
                    <Input value={waLanguage} onChange={(e) => setWaLanguage(e.target.value)} maxLength={12} />
                  </FormField>
                  <FormField label="Body parameters" hint="One per line, in template order ({{1}}, {{2}}…). Merge tags allowed.">
                    <Textarea rows={3} value={waParams} onChange={(e) => setWaParams(e.target.value)} />
                  </FormField>
                </CardBody>
              </Card>
            )}

            <Card>
              <CardHeader title="Schedule & speed" />
              <CardBody className="stack">
                <RadioGroup
                  legend="When to send"
                  value={scheduleMode}
                  onChange={(v) => setScheduleMode(v as ScheduleMode)}
                  options={[
                    { value: 'Immediate', label: 'As soon as it is confirmed' },
                    { value: 'FixedTime', label: 'At a fixed time' },
                    { value: 'RecipientTimeZone', label: "At a local time in each recipient's time zone" },
                  ]}
                />
                {scheduleMode === 'FixedTime' && (
                  <FormField label="Send at (your time)">
                    <Input type="datetime-local" value={scheduledAt} onChange={(e) => setScheduledAt(e.target.value)} />
                  </FormField>
                )}
                {scheduleMode === 'RecipientTimeZone' && (
                  <FormField label="Local send time" hint="E.g. 09:00 on the day, in every recipient's own time zone.">
                    <Input type="datetime-local" value={localTime} onChange={(e) => setLocalTime(e.target.value)} />
                  </FormField>
                )}
                <FormField label="Messages per minute">
                  <Input type="number" min={1} max={100000} value={throttle} onChange={(e) => setThrottle(e.target.value)} />
                </FormField>
                <div className="cluster">
                  <FormField label="Send window from (hour)" optional>
                    <Input type="number" min={0} max={23} value={windowStart} onChange={(e) => setWindowStart(e.target.value)} />
                  </FormField>
                  <FormField label="until (hour)" optional>
                    <Input type="number" min={0} max={23} value={windowEnd} onChange={(e) => setWindowEnd(e.target.value)} />
                  </FormField>
                </div>
              </CardBody>
            </Card>

            {channel === 'email' && editable && <BlockEditor design={design} onChange={setDesign} />}
          </fieldset>
        </form>

        <div className="stack">
          {campaign && (
            <Card>
              <CardHeader title="Pre-send checklist" description="Blocking items must be fixed before sending." />
              <CardBody>
                {checklist.isError ? (
                  <ErrorState error={checklist.error} compact onRetry={() => void checklist.refetch()} />
                ) : checklist.data ? (
                  <ChecklistView items={checklist.data.items} />
                ) : (
                  <SkeletonText lines={5} />
                )}
              </CardBody>
            </Card>
          )}
          {channel === 'email' && <EmailPreview clientId={workspaceId} subject={subject} previewText={previewText} design={design} />}
        </div>
      </div>

      {campaign && (
        <SendConfirmDialog open={sendOpen} onClose={() => setSendOpen(false)} campaign={campaign} checklist={checklist.data} />
      )}
      {campaign && (
        <ConfirmDialog
          open={cancelOpen}
          onClose={() => setCancelOpen(false)}
          title="Cancel this campaign?"
          description="Messages not yet sent are cancelled. This cannot be undone."
          tone="danger"
          confirmLabel="Cancel campaign"
          requireReason
          onConfirm={({ reason }) => action.mutateAsync({ verb: 'cancel', reason }).then(() => undefined)}
        />
      )}
      {campaign && channel === 'email' && (
        <TestSendDialog open={testOpen} onClose={() => setTestOpen(false)} path={`${campaignsPath(channel)}/${campaign.id}/test`} clientId={workspaceId} />
      )}
    </>
  );
}
