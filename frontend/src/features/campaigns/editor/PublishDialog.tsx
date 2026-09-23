import { useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, Circle, Rocket } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Alert, Button, DateTime, Dialog, useToast } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { qk } from '../api/queries';
import type { AdminCampaign } from '../api/types';
import { readiness } from './formModel';

export interface PublishDialogProps {
  open: boolean;
  onClose: () => void;
  campaign: AdminCampaign;
  /** The editor has unsaved changes (the checklist reflects the saved campaign). */
  unsavedChanges?: boolean;
  onPublished?: (campaign: AdminCampaign) => void;
}

/**
 * Publish confirmation with a readiness checklist mirroring the server's `campaign.incomplete` validation. The
 * server remains the authority: any problems it reports are listed as returned.
 */
export function PublishDialog({ open, onClose, campaign, unsavedChanges, onPublished }: PublishDialogProps) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [serverProblems, setServerProblems] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setServerProblems([]);
      setError(null);
    }
  }, [open]);

  const items = readiness(campaign);
  const ready = items.every((i) => i.ok);
  const startsInFuture = new Date(campaign.startsAt).getTime() > Date.now();

  const publish = async () => {
    setBusy(true);
    setError(null);
    setServerProblems([]);
    try {
      const result = await api.post<AdminCampaign>(`/admin/campaigns/${campaign.id}/publish`);
      queryClient.setQueryData(qk.campaign(campaign.id), result);
      await queryClient.invalidateQueries({ queryKey: qk.all });
      toast.success(
        result.status === 'Scheduled' ? 'Campaign scheduled' : 'Campaign published',
        result.title,
      );
      onPublished?.(result);
      onClose();
    } catch (err) {
      setError(errorMessage(err));
      if (isApiError(err)) setServerProblems(Object.values(err.errors ?? {}).flat());
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Publish campaign"
      description={
        startsInFuture ? (
          <>
            It will be <strong>Scheduled</strong> and go live on{' '}
            <DateTime value={campaign.startsAt} timeZone={campaign.timeZone} withZone />.
          </>
        ) : (
          'It goes live immediately and eligible participants can start submitting.'
        )
      }
      icon={<Rocket />}
      dismissible={!busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button variant="highlight" onClick={() => void publish()} loading={busy} disabled={!ready}>
            {startsInFuture ? 'Schedule campaign' : 'Publish now'}
          </Button>
        </>
      }
    >
      <div className="stack">
        {unsavedChanges && (
          <Alert tone="warning">
            You have unsaved changes. The checklist reflects the last saved version.
          </Alert>
        )}
        <ul className="mg-checklist" aria-label="Readiness checklist">
          {items.map((item) => (
            <li key={item.id} data-ok={item.ok}>
              {item.ok ? (
                <CheckCircle2 aria-hidden="true" className="mg-ok" />
              ) : (
                <Circle aria-hidden="true" className="mg-missing" />
              )}
              <span>
                {item.label}
                <span className="visually-hidden">{item.ok ? ': done' : ': missing'}</span>
              </span>
            </li>
          ))}
        </ul>
        {!ready && <p className="text-small text-muted">Complete every item and save before publishing.</p>}
        {error && (
          <Alert tone="danger" role="alert" title={error}>
            {serverProblems.length > 0 && (
              <ul className="mg-list">
                {serverProblems.map((p) => (
                  <li key={p}>{p}</li>
                ))}
              </ul>
            )}
          </Alert>
        )}
      </div>
    </Dialog>
  );
}
