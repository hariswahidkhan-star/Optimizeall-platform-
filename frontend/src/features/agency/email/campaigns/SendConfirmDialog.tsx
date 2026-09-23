import { useQueryClient } from '@tanstack/react-query';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DateTime } from '@/components/ui/DateTime';
import { KeyValueList } from '@/components/ui/KeyValueList';
import { Money } from '@/components/ui/Money';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { formatNumber } from '@/lib/format/money';
import { campaignsPath, emailKeys } from '../api/queries';
import type { Campaign, Checklist } from '../api/types';

/**
 * Sensitive send confirmation: the user types the campaign name, and the API re-checks `email.send`, the name, the
 * concurrency stamp and the checklist. Nothing is sent from the browser; the send job does the sending.
 */
export function SendConfirmDialog({
  open,
  onClose,
  campaign,
  checklist,
}: {
  open: boolean;
  onClose: () => void;
  campaign: Campaign;
  checklist: Checklist | undefined;
}) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const channel = campaign.channel === 'Email' ? 'email' : 'sms';
  const when =
    campaign.scheduleMode === 'FixedTime' && campaign.scheduledAt ? (
      <DateTime value={campaign.scheduledAt} format="datetime" withZone />
    ) : campaign.scheduleMode === 'RecipientTimeZone' ? (
      `${(campaign.scheduledLocalTime ?? '').replace('T', ' ')} in each recipient's time zone`
    ) : (
      'Immediately'
    );

  return (
    <ConfirmDialog
      open={open}
      onClose={onClose}
      title={`Send “${campaign.name}”?`}
      description="Once sending starts it can be paused or cancelled, but messages already delivered cannot be recalled."
      confirmLabel={campaign.scheduleMode === 'Immediate' ? 'Send now' : 'Schedule send'}
      confirmText={campaign.name}
      requireReason={false}
      onConfirm={async ({ reason }) => {
        await api.post(`${campaignsPath(channel)}/${campaign.id}/send`, {
          confirm: true,
          confirmName: campaign.name,
          concurrencyStamp: campaign.concurrencyStamp,
          reason: reason || null,
        });
        toast.success(checklist?.requiresClientApproval ? 'Waiting for client approval' : 'Campaign queued', 'The send job will start it on schedule.');
        void queryClient.invalidateQueries({ queryKey: emailKeys.all });
      }}
    >
      <KeyValueList
        items={[
          { label: 'Recipients (with consent)', value: formatNumber(checklist?.audienceCount ?? 0) },
          { label: 'When', value: when },
          { label: 'Speed', value: `Up to ${formatNumber(campaign.throttlePerMinute)} per minute` },
          ...(checklist?.estimatedCost != null && checklist.costCurrency
            ? [{ label: 'Estimated cost', value: <Money amount={checklist.estimatedCost} currency={checklist.costCurrency} /> }]
            : []),
          ...(checklist?.requiresClientApproval ? [{ label: 'Client approval', value: 'Required before the send job starts' }] : []),
        ]}
      />
    </ConfirmDialog>
  );
}
