import { CalendarClock, CheckCircle2, Mail, MessageSquare, Phone, StickyNote } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Badge, Button, DateTime, EmptyState, ErrorState, FormField, Input, Select, Skeleton, Textarea, useToast } from '@/components/ui';
import { billingErrorMessage } from '@/features/agency/billing/lib';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useActivities, useAssignees, useCompleteActivity, useSaveActivity } from '../api/hooks';
import type { Activity, ActivityType } from '../api/types';
import { ACTIVITY_OPTIONS, localToIso } from '../lib';

const ICONS: Record<ActivityType, typeof StickyNote> = {
  Note: StickyNote,
  Call: Phone,
  Meeting: CalendarClock,
  Email: Mail,
  Task: CheckCircle2,
};

export function ActivityItem({ activity, showLinks }: { activity: Activity; showLinks?: boolean }) {
  const Icon = ICONS[activity.type] ?? MessageSquare;
  const complete = useCompleteActivity();
  const toast = useToast();
  const { hasPermission } = useAuth();
  const isTask = activity.type === 'Task';
  return (
    <li className="crm-card">
      <div className="crm-row">
        <span className="crm-row">
          <Icon aria-hidden="true" width={16} height={16} />
          <span className="bill-strong">{activity.subject}</span>
          {activity.isSystem && <Badge tone="neutral">System</Badge>}
          {isTask && activity.completedAt && <Badge tone="success">Done</Badge>}
          {activity.isOverdue && <Badge tone="danger">Overdue</Badge>}
        </span>
        <span className="crm-muted">
          <DateTime value={activity.createdAt} format="relative" />
        </span>
      </div>
      {activity.body && <p className="crm-pre">{activity.body}</p>}
      <p className="crm-muted">
        {activity.type}
        {activity.dueAt && (
          <>
            {' '}· due <DateTime value={activity.dueAt} />
          </>
        )}
        {activity.occursAt && activity.type !== 'Task' && (
          <>
            {' '}· <DateTime value={activity.occursAt} />
          </>
        )}
        {activity.assignee && ` · ${activity.assignee.displayName}`}
        {showLinks && activity.dealId && (
          <>
            {' '}·{' '}
            <Link className="ui-link" to={`/agency/crm/deals/${activity.dealId}`}>
              {activity.dealTitle}
            </Link>
          </>
        )}
      </p>
      {isTask && hasPermission(Permissions.CrmManage) && (
        <div>
          <Button
            size="sm"
            variant="secondary"
            loading={complete.isPending}
            onClick={async () => {
              try {
                await complete.mutateAsync({ id: activity.id, completed: !activity.completedAt, concurrencyStamp: activity.concurrencyStamp });
              } catch (error) {
                toast.error('Couldn’t update the task', billingErrorMessage(error));
              }
            }}
          >
            {activity.completedAt ? 'Reopen' : 'Mark done'}
            <span className="visually-hidden">: {activity.subject}</span>
          </Button>
        </div>
      )}
    </li>
  );
}

/** Timeline of notes, calls, meetings, emails and tasks for a deal, contact or company, with a quick-add form. */
export function ActivityPanel({ dealId, contactId, companyId }: { dealId?: string; contactId?: string; companyId?: string }) {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.CrmManage);
  const query = useActivities({ dealId, contactId, companyId, pageSize: 100 });
  const save = useSaveActivity();
  const assignees = useAssignees();
  const toast = useToast();
  const [type, setType] = useState<ActivityType>('Note');
  const [subject, setSubject] = useState('');
  const [body, setBody] = useState('');
  const [when, setWhen] = useState('');
  const [assignee, setAssignee] = useState('');
  const needsWhen = type === 'Task' || type === 'Meeting';

  return (
    <div className="stack">
      {canManage && (
        <form
          className="stack"
          aria-label="Log activity"
          onSubmit={async (e) => {
            e.preventDefault();
            if (!subject.trim() || (needsWhen && !when)) return;
            try {
              await save.mutateAsync({
                type,
                subject: subject.trim(),
                body: body.trim() || null,
                dealId: dealId ?? null,
                contactId: contactId ?? null,
                companyId: companyId ?? null,
                dueAt: type === 'Task' ? localToIso(when) : null,
                occursAt: type !== 'Task' && when ? localToIso(when) : null,
                assigneeUserId: assignee || null,
              });
              setSubject('');
              setBody('');
              setWhen('');
              toast.success(`${type} added`);
            } catch (error) {
              toast.error('Couldn’t add the activity', billingErrorMessage(error));
            }
          }}
        >
          <div className="crm-grid">
            <FormField label="Type">
              <Select value={type} options={ACTIVITY_OPTIONS} onChange={(e) => setType(e.target.value as ActivityType)} />
            </FormField>
            <FormField label="Subject" required>
              <Input value={subject} maxLength={200} onChange={(e) => setSubject(e.target.value)} />
            </FormField>
            <FormField label={type === 'Task' ? 'Due' : 'When'} required={needsWhen} optional={!needsWhen}>
              <Input type="datetime-local" value={when} onChange={(e) => setWhen(e.target.value)} />
            </FormField>
            {needsWhen && (
              <FormField label="Assignee">
                <Select
                  value={assignee}
                  options={[{ value: '', label: 'Me' }, ...(assignees.data ?? []).map((u) => ({ value: u.id, label: u.displayName }))]}
                  onChange={(e) => setAssignee(e.target.value)}
                />
              </FormField>
            )}
          </div>
          <FormField label="Details" optional>
            <Textarea rows={2} maxLength={10000} value={body} onChange={(e) => setBody(e.target.value)} />
          </FormField>
          <div>
            <Button type="submit" size="sm" loading={save.isPending} disabled={!subject.trim() || (needsWhen && !when)}>
              Add {type.toLowerCase()}
            </Button>
          </div>
        </form>
      )}
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !query.data ? (
        <Skeleton height="6rem" />
      ) : query.data.items.length === 0 ? (
        <EmptyState compact headingLevel={3} title="No activity yet" />
      ) : (
        <ul className="crm-list" aria-label="Activity timeline">
          {query.data.items.map((a) => (
            <ActivityItem key={a.id} activity={a} />
          ))}
        </ul>
      )}
    </div>
  );
}
