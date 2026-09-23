import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, Radar, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  Badge,
  Button,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  PageHeader,
  Pagination,
  Switch,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { describeReviewError, humanError } from '../api/errors';
import { reviewApi, reviewKeys } from '../api/reviewApi';
import type { LiveCheckItem } from '../api/types';
import { ActionError, ExternalLink } from '../components/common';
import { workspacePath } from '../hooks/useReviewActions';

function ConfirmLiveDialog({ item, onClose }: { item: LiveCheckItem | null; onClose: () => void }) {
  const [note, setNote] = useState('');
  const toast = useToast();
  const queryClient = useQueryClient();
  const confirm = useMutation({
    mutationFn: (id: string) =>
      reviewApi.liveCheck(id, { result: 'ConfirmedLive', note: note.trim() || undefined }),
    onSuccess: () => {
      toast.success('Post confirmed live', 'Pending earnings were approved.');
      void queryClient.invalidateQueries({ queryKey: reviewKeys.all });
      setNote('');
      onClose();
    },
    onError: (error) => toast.error(describeReviewError(error).title, describeReviewError(error).description),
  });
  return (
    <Dialog
      open={!!item}
      onClose={() => {
        confirm.reset();
        onClose();
      }}
      dismissible={!confirm.isPending}
      icon={<CheckCircle2 />}
      tone="success"
      title="Confirm the post is still live"
      description={item ? `${item.campaign.title} · ${item.participant.displayName}` : undefined}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={confirm.isPending}>
            Cancel
          </Button>
          <Button loading={confirm.isPending} onClick={() => item && confirm.mutate(item.submissionId)}>
            Confirm live
          </Button>
        </>
      }
    >
      <div className="stack">
        <p>
          Only confirm after checking that the post is public and unchanged. Pending earnings are approved.
        </p>
        {confirm.isError && (
          <ActionError
            error={confirm.error}
            onRefresh={() => {
              confirm.reset();
              onClose();
              void queryClient.invalidateQueries({ queryKey: reviewKeys.all });
            }}
          />
        )}
        <FormField label="Note" optional hint="Kept on the submission’s timeline.">
          <Textarea rows={2} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

export function LiveChecksPage() {
  const [params, setParams] = useSearchParams();
  const showUpcoming = params.get('upcoming') === '1';
  const page = Math.max(1, Number(params.get('page')) || 1);
  const [opened, setOpened] = useState<Set<string>>(() => new Set());
  const [confirming, setConfirming] = useState<LiveCheckItem | null>(null);
  const [removing, setRemoving] = useState<LiveCheckItem | null>(null);
  const [listError, setListError] = useState<unknown>(null);
  const toast = useToast();
  const queryClient = useQueryClient();

  const list = useQuery({
    queryKey: reviewKeys.liveChecks(!showUpcoming, page),
    queryFn: ({ signal }) => reviewApi.liveChecks(!showUpcoming, page, signal),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
  });

  const remove = useMutation({
    mutationFn: ({ id, note }: { id: string; note: string }) =>
      reviewApi.liveCheck(id, { result: 'Removed', note }),
    onSuccess: () => {
      toast.success('Marked as removed', 'The submission was reversed and its earnings cancelled.');
      void queryClient.invalidateQueries({ queryKey: reviewKeys.all });
    },
  });

  const markOpened = (id: string) => setOpened((prev) => new Set(prev).add(id));

  const columns: DataTableColumn<LiveCheckItem>[] = [
    {
      id: 'post',
      header: 'Post',
      primary: true,
      cell: (item) => (
        <div className="rv-cell-main">
          <Link to={workspacePath(item.submissionId)} className="ui-link">
            {item.campaign.title}
          </Link>
          <span className="text-small text-muted">
            {item.platform} · {item.participant.displayName}
          </span>
        </div>
      ),
    },
    {
      id: 'link',
      header: 'Live post',
      cell: (item) => (
        <span className="rv-inline">
          <ExternalLink href={item.postUrl} onOpen={() => markOpened(item.submissionId)}>
            Open post
          </ExternalLink>
          {opened.has(item.submissionId) && (
            <Badge size="sm" tone="info">
              Opened
            </Badge>
          )}
        </span>
      ),
    },
    { id: 'posted', header: 'Posted', cell: (item) => <DateTime value={item.postedAt} format="both" /> },
    {
      id: 'due',
      header: 'Due',
      cell: (item) => (
        <span className="rv-inline">
          {item.dueAt ? <DateTime value={item.dueAt} format="relative" /> : '—'}
          <Badge size="sm" tone={item.isDue ? 'warning' : 'neutral'}>
            {item.isDue ? 'Due' : 'Not yet due'}
          </Badge>
        </span>
      ),
    },
    {
      id: 'actions',
      header: <span className="visually-hidden">Actions</span>,
      align: 'right',
      cell: (item) => {
        const wasOpened = opened.has(item.submissionId);
        return (
          <div className="rv-row-actions">
            <Button
              size="sm"
              leadingIcon={<CheckCircle2 />}
              disabled={!wasOpened || !item.isDue}
              onClick={() => setConfirming(item)}
              aria-label={`Confirm live: ${item.campaign.title} by ${item.participant.displayName}`}
            >
              Confirm live
            </Button>
            <Button
              size="sm"
              variant="danger"
              leadingIcon={<Trash2 />}
              disabled={!wasOpened}
              onClick={() => setRemoving(item)}
              aria-label={`Mark removed: ${item.campaign.title} by ${item.participant.displayName}`}
            >
              Mark removed
            </Button>
          </div>
        );
      },
    },
  ];

  const data = list.data;

  return (
    <>
      <PageHeader
        title="Live checks"
        description="Approved posts must stay public for the campaign’s required hours. Open each post, then confirm it is live or mark it removed (removal reverses the earnings)."
        breadcrumbs={[{ label: 'Review', to: '/review' }, { label: 'Live checks' }]}
      />
      <div className="stack">
        <Switch
          checked={showUpcoming}
          onCheckedChange={(checked) => {
            const next = new URLSearchParams(params);
            if (checked) next.set('upcoming', '1');
            else next.delete('upcoming');
            next.delete('page');
            setParams(next, { replace: true });
          }}
          label="Include checks that aren’t due yet"
        />
        <p className="text-small text-muted">
          Open the post first — the actions unlock once you’ve looked at it.
        </p>
        {listError !== null && <ActionError error={listError} onDismiss={() => setListError(null)} />}
        {list.isError && !data ? (
          <ErrorState error={list.error} onRetry={() => void list.refetch()} retrying={list.isFetching} />
        ) : (
          <DataTable
            caption="Live checks"
            columns={columns}
            rows={data?.items ?? []}
            getRowId={(item) => item.submissionId}
            loading={list.isPending}
            emptyState={
              <EmptyState
                icon={<Radar />}
                headingLevel={2}
                title="No live checks due"
                description="Approved posts with a minimum live duration appear here when their check is due."
              />
            }
          />
        )}
        {data && data.total > data.pageSize && (
          <Pagination
            page={data.page}
            pageSize={data.pageSize}
            total={data.total}
            onPageChange={(p) => {
              const next = new URLSearchParams(params);
              next.set('page', String(p));
              setParams(next);
            }}
          />
        )}
      </div>

      <ConfirmLiveDialog item={confirming} onClose={() => setConfirming(null)} />
      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        tone="danger"
        title="Mark this post as removed?"
        description="The submission is reversed and all its earnings are cancelled. The participant is notified."
        confirmLabel="Mark removed & reverse"
        requireReason
        reasonMinLength={5}
        reasonLabel="What did you find?"
        reasonHint="At least 5 characters, e.g. “Post deleted” or “Account set to private”."
        onConfirm={async ({ reason }) => {
          if (!removing) return;
          try {
            await remove.mutateAsync({ id: removing.submissionId, note: reason });
          } catch (error) {
            if (describeReviewError(error).refresh) setListError(error);
            throw humanError(error);
          }
        }}
      >
        {removing && (
          <p>
            <strong>{removing.campaign.title}</strong> · {removing.participant.displayName} ·{' '}
            {removing.platform}
          </p>
        )}
      </ConfirmDialog>
    </>
  );
}
