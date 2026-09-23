import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Award, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  DataTable,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Select,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import { humanize } from '@/lib/format/text';
import { qk } from '../api/queries';
import {
  ACHIEVEMENT_CRITERIA,
  type Achievement,
  type AchievementCriterion,
  type AchievementInput,
} from '../api/types';
import { fieldError, fieldErrorsFrom, type FieldErrorMap } from '../shared/formErrors';
import '../campaigns.css';

const CRITERION_HELP: Record<AchievementCriterion, string> = {
  ApprovedSubmissions: 'Number of approved posts',
  CampaignsCompleted: 'Campaigns with an approved post',
  TotalEarnedSettlement: 'Total earned, in the settlement currency',
  QualifiedReferrals: 'Referrals that qualified',
  PlatformsUsed: 'Distinct platforms with an approved post',
};

export function AchievementsPage() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<Achievement | 'new' | null>(null);
  const [removing, setRemoving] = useState<Achievement | null>(null);
  const query = useQuery({
    queryKey: qk.achievements(),
    queryFn: () => api.get<Achievement[]>('/marketing/achievements'),
  });

  const columns: DataTableColumn<Achievement>[] = [
    {
      id: 'name',
      header: 'Achievement',
      primary: true,
      cell: (a) => (
        <span className="stack mg-stack-xs">
          <span className="mg-strong">{a.name}</span>
          <span className="text-small text-muted">
            <code>{a.key}</code> · {a.description}
          </span>
        </span>
      ),
    },
    {
      id: 'criterion',
      header: 'Awarded when',
      cell: (a) => `${CRITERION_HELP[a.criterion] ?? humanize(a.criterion)} ≥ ${formatNumber(a.threshold)}`,
    },
    { id: 'order', header: 'Order', align: 'right', cell: (a) => a.sortOrder },
    { id: 'awarded', header: 'Awarded', align: 'right', cell: (a) => formatNumber(a.awardedCount) },
    {
      id: 'active',
      header: 'Status',
      cell: (a) => (a.isActive ? <Badge tone="success">Active</Badge> : <Badge>Inactive</Badge>),
    },
  ];

  return (
    <>
      <PageHeader
        title="Achievements"
        description="Badges participants earn automatically. Awarded achievements can be deactivated but not deleted."
        actions={
          <Button leadingIcon={<Plus />} onClick={() => setEditing('new')}>
            New achievement
          </Button>
        }
      />
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <DataTable
          caption="Achievements"
          columns={columns}
          rows={query.data ?? []}
          getRowId={(a) => a.id}
          rowLabel={(a) => a.name}
          loading={query.isLoading}
          rowActions={(a) => [
            { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditing(a) },
            {
              id: 'delete',
              label: a.awardedCount > 0 ? 'Delete (awarded — deactivate instead)' : 'Delete…',
              icon: <Trash2 />,
              danger: true,
              disabled: a.awardedCount > 0,
              onSelect: () => setRemoving(a),
            },
          ]}
          emptyState={<EmptyState icon={<Award />} headingLevel={2} title="No achievements yet" />}
        />
      )}
      {editing && (
        <AchievementDialog
          achievement={editing === 'new' ? null : editing}
          nextOrder={((query.data ?? []).reduce((m, a) => Math.max(m, a.sortOrder), 0) || 0) + 10}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void queryClient.invalidateQueries({ queryKey: qk.achievements() });
          }}
        />
      )}
      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        tone="danger"
        title={removing ? `Delete “${removing.name}”?` : ''}
        description="Only achievements nobody has earned can be deleted."
        confirmLabel="Delete achievement"
        onConfirm={async () => {
          if (!removing) return;
          await api.delete(`/marketing/achievements/${removing.id}`);
          toast.success('Achievement deleted');
          await queryClient.invalidateQueries({ queryKey: qk.achievements() });
        }}
      />
    </>
  );
}

function AchievementDialog({
  achievement,
  nextOrder,
  onClose,
  onSaved,
}: {
  achievement: Achievement | null;
  nextOrder: number;
  onClose: () => void;
  onSaved: () => void;
}) {
  const toast = useToast();
  const [form, setForm] = useState({
    key: achievement?.key ?? '',
    name: achievement?.name ?? '',
    description: achievement?.description ?? '',
    icon: achievement?.icon ?? '',
    criterion: (achievement?.criterion ?? 'ApprovedSubmissions') as AchievementCriterion,
    threshold: achievement ? String(achievement.threshold) : '1',
    sortOrder: String(achievement?.sortOrder ?? nextOrder),
    isActive: achievement?.isActive ?? true,
  });
  const [errors, setErrors] = useState<FieldErrorMap>({});
  const [formError, setFormError] = useState<string | null>(null);
  const set = (patch: Partial<typeof form>) => setForm((f) => ({ ...f, ...patch }));

  const save = useMutation({
    mutationFn: (body: AchievementInput) =>
      achievement
        ? api.put<Achievement>(`/marketing/achievements/${achievement.id}`, body)
        : api.post<Achievement>('/marketing/achievements', body),
    onSuccess: (a) => {
      toast.success(achievement ? 'Achievement saved' : 'Achievement created', a.name);
      onSaved();
    },
    onError: (err) => {
      const mapped = fieldErrorsFrom(err, { 'achievement.key_taken': 'key', 'achievement.awarded': 'key' });
      setErrors(mapped);
      setFormError(Object.keys(mapped).length ? null : errorMessage(err));
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const local: FieldErrorMap = {};
    if (!/^[a-z0-9][a-z0-9-]{1,59}$/.test(form.key))
      local.key = ['Use 2–60 lower-case letters, digits and dashes.'];
    if (!form.name.trim()) local.name = ['Enter a name.'];
    if (!form.description.trim()) local.description = ['Enter a description.'];
    if (!(Number(form.threshold) > 0)) local.threshold = ['The threshold must be greater than 0.'];
    setErrors(local);
    if (Object.keys(local).length) return;
    save.mutate({
      key: form.key,
      name: form.name.trim(),
      description: form.description.trim(),
      icon: form.icon.trim() || null,
      criterion: form.criterion,
      threshold: Number(form.threshold),
      sortOrder: Number(form.sortOrder) || 0,
      isActive: form.isActive,
    });
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title={achievement ? 'Edit achievement' : 'New achievement'}
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form="achievement-form" loading={save.isPending}>
            {achievement ? 'Save achievement' : 'Create achievement'}
          </Button>
        </>
      }
    >
      <form id="achievement-form" className="stack" onSubmit={submit} noValidate>
        {formError && (
          <Alert tone="danger" role="alert">
            {formError}
          </Alert>
        )}
        {achievement && achievement.awardedCount > 0 && (
          <Alert tone="info">
            Awarded to {achievement.awardedCount} participant(s): the key can no longer change.
          </Alert>
        )}
        <div className="mg-grid mg-grid--2">
          <FormField
            label="Key"
            required
            hint="Lower-case letters, digits and dashes"
            error={fieldError(errors, 'key')}
          >
            <Input
              value={form.key}
              maxLength={60}
              spellCheck={false}
              disabled={!!achievement && achievement.awardedCount > 0}
              onChange={(e) => set({ key: e.target.value.toLowerCase() })}
            />
          </FormField>
          <FormField label="Name" required error={fieldError(errors, 'name')}>
            <Input value={form.name} maxLength={100} onChange={(e) => set({ name: e.target.value })} />
          </FormField>
        </div>
        <FormField label="Description" required error={fieldError(errors, 'description')}>
          <Textarea
            value={form.description}
            rows={2}
            maxLength={500}
            onChange={(e) => set({ description: e.target.value })}
          />
        </FormField>
        <div className="mg-grid mg-grid--2">
          <FormField label="Criterion" required error={fieldError(errors, 'criterion')}>
            <Select
              value={form.criterion}
              options={ACHIEVEMENT_CRITERIA.map((c) => ({ value: c, label: CRITERION_HELP[c] }))}
              onChange={(e) => set({ criterion: e.target.value as AchievementCriterion })}
            />
          </FormField>
          <FormField label="Threshold" required error={fieldError(errors, 'threshold')}>
            <Input
              type="number"
              min={0}
              step="any"
              value={form.threshold}
              onChange={(e) => set({ threshold: e.target.value })}
            />
          </FormField>
          <FormField
            label="Icon"
            optional
            hint="A lucide icon name, e.g. badge-check"
            error={fieldError(errors, 'icon')}
          >
            <Input
              value={form.icon}
              maxLength={50}
              onChange={(e) => set({ icon: e.target.value.toLowerCase() })}
            />
          </FormField>
          <FormField label="Sort order" error={fieldError(errors, 'sortOrder')}>
            <Input
              type="number"
              min={0}
              step={1}
              value={form.sortOrder}
              onChange={(e) => set({ sortOrder: e.target.value })}
            />
          </FormField>
        </div>
        <Checkbox
          label="Active"
          checked={form.isActive}
          onChange={(e) => set({ isActive: e.target.checked })}
        />
      </form>
    </Dialog>
  );
}
