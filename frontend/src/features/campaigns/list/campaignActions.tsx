import { useQueryClient } from '@tanstack/react-query';
import { Archive, ArchiveRestore, Copy, Pause, Pencil, Play, Square } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { ConfirmDialog, useToast, type MenuEntry } from '@/components/ui';
import { api } from '@/lib/api/client';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { qk } from '../api/queries';
import type { AdminCampaign, CampaignStatus } from '../api/types';

export type CampaignAction = 'pause' | 'resume' | 'end' | 'archive' | 'unarchive' | 'duplicate';

interface ActionTarget {
  id: string;
  title: string;
  status: CampaignStatus;
}

interface ActionSpec {
  title: (t: ActionTarget) => string;
  description: string;
  confirmLabel: string;
  tone: 'danger' | 'primary';
  requireReason: boolean;
  success: string;
}

const SPECS: Record<CampaignAction, ActionSpec> = {
  pause: {
    title: (t) => `Pause “${t.title}”?`,
    description: 'Participants can no longer submit posts until you resume it. Pending reviews continue.',
    confirmLabel: 'Pause campaign',
    tone: 'primary',
    requireReason: true,
    success: 'Campaign paused',
  },
  resume: {
    title: (t) => `Resume “${t.title}”?`,
    description: 'The campaign opens for submissions again (or returns to Scheduled if it has not started).',
    confirmLabel: 'Resume campaign',
    tone: 'primary',
    requireReason: false,
    success: 'Campaign resumed',
  },
  end: {
    title: (t) => `End “${t.title}”?`,
    description:
      'Ending closes the campaign for good: no new submissions. Pending submissions can still be reviewed.',
    confirmLabel: 'End campaign',
    tone: 'danger',
    requireReason: true,
    success: 'Campaign ended',
  },
  archive: {
    title: (t) => `Archive “${t.title}”?`,
    description: 'Archived campaigns are read-only and hidden from participants.',
    confirmLabel: 'Archive campaign',
    tone: 'danger',
    requireReason: false,
    success: 'Campaign archived',
  },
  unarchive: {
    title: (t) => `Restore “${t.title}”?`,
    description:
      'The campaign leaves the archive: a campaign that was never published returns to Draft, a published one to Ended.',
    confirmLabel: 'Restore campaign',
    tone: 'primary',
    requireReason: false,
    success: 'Campaign restored',
  },
  duplicate: {
    title: (t) => `Duplicate “${t.title}”?`,
    description:
      'Creates a draft copy with the same content, assets, disclosures and the latest reward rules (as version 1).',
    confirmLabel: 'Duplicate',
    tone: 'primary',
    requireReason: false,
    success: 'Draft copy created',
  },
};

export function allowedActions(status: CampaignStatus): CampaignAction[] {
  const actions: CampaignAction[] = [];
  if (status === 'Active' || status === 'Scheduled') actions.push('pause');
  if (status === 'Paused') actions.push('resume');
  if (status === 'Active' || status === 'Paused' || status === 'Scheduled') actions.push('end');
  if (status === 'Draft' || status === 'Ended') actions.push('archive');
  if (status === 'Archived') actions.push('unarchive');
  actions.push('duplicate');
  return actions;
}

const ICONS: Record<CampaignAction, ReactNode> = {
  pause: <Pause />,
  resume: <Play />,
  end: <Square />,
  archive: <Archive />,
  unarchive: <ArchiveRestore />,
  duplicate: <Copy />,
};

const LABELS: Record<CampaignAction, string> = {
  pause: 'Pause…',
  resume: 'Resume…',
  end: 'End…',
  archive: 'Archive…',
  unarchive: 'Restore from archive…',
  duplicate: 'Duplicate…',
};

/**
 * Status actions (pause/resume/end/archive/restore/duplicate) with their confirmation dialogs. Pause and End require a
 * reason (audited). Returns menu entries for a campaign and the dialog element to render once.
 */
export function useCampaignActions(
  options: { onDone?: (action: CampaignAction, result: AdminCampaign) => void } = {},
) {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.CampaignsManage);
  const queryClient = useQueryClient();
  const toast = useToast();
  const navigate = useNavigate();
  const [pending, setPending] = useState<{ action: CampaignAction; target: ActionTarget } | null>(null);

  const menuItems = (target: ActionTarget, extra: { edit?: string } = {}): MenuEntry[] => [
    ...(extra.edit
      ? [{ id: 'edit', label: 'Edit', icon: <Pencil />, to: extra.edit } satisfies MenuEntry]
      : []),
    ...allowedActions(target.status).map((action): MenuEntry => ({
      id: action,
      label: LABELS[action],
      icon: ICONS[action],
      danger: SPECS[action].tone === 'danger',
      disabled: !canManage,
      onSelect: () => setPending({ action, target }),
    })),
  ];

  const spec = pending ? SPECS[pending.action] : null;

  const run = async ({ reason }: { reason: string }) => {
    if (!pending) return;
    const { action, target } = pending;
    const body = action === 'pause' || action === 'end' ? { reason } : undefined;
    const result = await api.post<AdminCampaign>(`/admin/campaigns/${target.id}/${action}`, body);
    toast.success(SPECS[action].success, action === 'duplicate' ? result.title : undefined);
    await queryClient.invalidateQueries({ queryKey: qk.all });
    options.onDone?.(action, result);
    if (action === 'duplicate' && !options.onDone) navigate(`/manage/campaigns/${result.id}`);
  };

  const dialog = (
    <ConfirmDialog
      open={!!pending}
      onClose={() => setPending(null)}
      onConfirm={run}
      title={pending && spec ? spec.title(pending.target) : ''}
      description={spec?.description}
      confirmLabel={spec?.confirmLabel}
      tone={spec?.tone}
      requireReason={spec?.requireReason}
      reasonMinLength={5}
      reasonHint="Recorded in the audit log (at least 5 characters)."
    />
  );

  return {
    menuItems,
    dialog,
    open: (action: CampaignAction, target: ActionTarget) => setPending({ action, target }),
  };
}
