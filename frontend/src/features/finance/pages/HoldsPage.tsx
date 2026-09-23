import { PauseCircle, PlayCircle } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Alert,
  Button,
  Card,
  CardBody,
  DataTable,
  DateTime,
  EmptyState,
  FilterBar,
  FormField,
  PageHeader,
  Pagination,
  Tabs,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { useCreateHold, useHolds, useReleaseHold } from '../api/hooks';
import type { CreateHoldResponse, PayoutHold } from '../api/types';
import { Muted, PersonCell, QueryError } from '../components/common';
import { FormDialog } from '../components/FormDialog';
import { UserPicker, type PickedUser } from '../components/UserPicker';
import { GUID_RE } from '../lib/format';
import { useCan } from '../lib/useCan';

function PlaceHoldDialog({
  open,
  onClose,
  onPlaced,
}: {
  open: boolean;
  onClose: () => void;
  onPlaced: (result: CreateHoldResponse) => void;
}) {
  const create = useCreateHold();
  const [user, setUser] = useState<PickedUser | null>(null);
  const [reason, setReason] = useState('');
  const [touched, setTouched] = useState(false);
  useEffect(() => {
    if (open) {
      setUser(null);
      setReason('');
      setTouched(false);
    }
  }, [open]);
  const userError = !user || !GUID_RE.test(user.id) ? 'Choose a participant or paste a valid user id.' : null;
  const reasonError = reason.trim().length < 5 ? 'Give a reason of at least 5 characters.' : null;
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      sensitive
      title="Place a payout hold"
      description="Stops the participant’s earnings from being paid until the hold is released. Their pending items in draft batches are held automatically. The participant sees a neutral message, never the reason."
      submitLabel="Place hold"
      onSubmit={async () => {
        setTouched(true);
        if (userError || reasonError) return false;
        const result = await create.mutateAsync({ userId: user!.id, reason: reason.trim() });
        onPlaced(result);
      }}
    >
      <UserPicker value={user} onChange={setUser} error={touched ? userError : null} />
      <FormField
        label="Reason"
        hint="Internal; recorded in the audit log."
        required
        error={touched ? reasonError : null}
      >
        <Textarea rows={3} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

function ReleaseHoldDialog({ hold, onClose }: { hold: PayoutHold | null; onClose: () => void }) {
  const release = useReleaseHold();
  const toast = useToast();
  const [note, setNote] = useState('');
  useEffect(() => {
    if (hold) setNote('');
  }, [hold]);
  return (
    <FormDialog
      open={!!hold}
      onClose={onClose}
      title={hold ? `Release the hold on ${hold.user.displayName}?` : 'Release hold'}
      description="Their earnings can be included in the next batch again. Items held in existing draft batches stay held until you release them there."
      submitLabel="Release hold"
      onSubmit={async () => {
        if (!hold) return;
        await release.mutateAsync({ id: hold.id, note: note.trim() });
        toast.success('Hold released');
      }}
    >
      {hold && <p className="text-small">Reason: {hold.reason}</p>}
      <FormField label="Release note" optional>
        <Textarea rows={2} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

export function HoldsPage() {
  const can = useCan();
  const [tab, setTab] = useState<'active' | 'released'>('active');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [placing, setPlacing] = useState(false);
  const [releasing, setReleasing] = useState<PayoutHold | null>(null);
  const [placed, setPlaced] = useState<CreateHoldResponse | null>(null);
  const query = useHolds({ active: tab === 'active', search, page, pageSize }, can.hold);

  const columns: DataTableColumn<PayoutHold>[] = [
    {
      id: 'user',
      header: 'Participant',
      primary: true,
      cell: (h) => <PersonCell user={h.user} link={can.viewLedger} />,
    },
    { id: 'reason', header: 'Reason', cell: (h) => h.reason },
    { id: 'created', header: 'Placed', cell: (h) => <DateTime value={h.createdAt} /> },
    ...(tab === 'released'
      ? [
          { id: 'released', header: 'Released', cell: (h: PayoutHold) => <DateTime value={h.releasedAt} /> },
          { id: 'note', header: 'Release note', cell: (h: PayoutHold) => h.releaseNote ?? <Muted>—</Muted> },
        ]
      : [
          {
            id: 'action',
            header: 'Action',
            cell: (h: PayoutHold) => (
              <Button
                size="sm"
                variant="secondary"
                leadingIcon={<PlayCircle />}
                onClick={() => setReleasing(h)}
              >
                Release<span className="visually-hidden"> hold on {h.user.displayName}</span>
              </Button>
            ),
          },
        ]),
  ];

  if (!can.hold) {
    return (
      <>
        <PageHeader title="Holds" />
        <Alert tone="warning" title="No access">
          Managing payout holds needs the “payout holds” permission. Items held inside a batch are listed on
          the batch review screen.
        </Alert>
      </>
    );
  }

  const list = (
    <div className="stack">
      <FilterBar
        search={search}
        onSearchChange={(v) => {
          setSearch(v);
          setPage(1);
        }}
        searchPlaceholder="Participant name or email"
        searchLabel="Search holds"
      />
      {query.isError ? (
        <QueryError error={query.error} onRetry={() => query.refetch()} />
      ) : (
        <>
          <DataTable
            caption={tab === 'active' ? 'Active payout holds' : 'Released payout holds'}
            columns={columns}
            rows={query.data?.items ?? []}
            getRowId={(h) => h.id}
            loading={query.isPending}
            emptyState={
              <EmptyState
                compact
                headingLevel={3}
                title={tab === 'active' ? 'No active holds' : 'No released holds'}
              />
            }
          />
          {query.data && query.data.total > 0 && (
            <Pagination
              page={page}
              pageSize={pageSize}
              total={query.data.total}
              onPageChange={setPage}
              onPageSizeChange={(s) => {
                setPageSize(s);
                setPage(1);
              }}
              label="Holds pages"
            />
          )}
        </>
      )}
    </div>
  );

  return (
    <>
      <PageHeader
        title="Holds"
        description="A payout hold stops a participant from being paid (for example during an investigation). Items in a single batch can also be held from the batch review screen."
        actions={
          <Button leadingIcon={<PauseCircle />} onClick={() => setPlacing(true)}>
            Place hold
          </Button>
        }
      />
      <div className="stack fin-page">
        {placed && (
          <Alert
            tone="success"
            role="status"
            title={`Hold placed on ${placed.hold.user.displayName}`}
            onDismiss={() => setPlaced(null)}
          >
            {placed.heldDraftItemIds.length > 0
              ? `${placed.heldDraftItemIds.length} pending ${placed.heldDraftItemIds.length === 1 ? 'item' : 'items'} in draft batches ${placed.heldDraftItemIds.length === 1 ? 'was' : 'were'} held automatically.`
              : 'No draft batch items needed holding.'}
            {placed.awaitingPaymentItemIds.length > 0 && (
              <>
                {' '}
                <strong>
                  {placed.awaitingPaymentItemIds.length}{' '}
                  {placed.awaitingPaymentItemIds.length === 1 ? 'item is' : 'items are'} already awaiting
                  payment
                </strong>{' '}
                in finalized batches — decide on {placed.awaitingPaymentItemIds.length === 1 ? 'it' : 'them'}{' '}
                manually (don’t pay, or mark failed). Open the{' '}
                <Link to="/finance/batches?status=Finalized" className="ui-link">
                  finalized batches
                </Link>
                .
              </>
            )}
            <ul className="fin-plain-list text-small fin-mono">
              {[...placed.heldDraftItemIds, ...placed.awaitingPaymentItemIds].map((id) => (
                <li key={id}>{id}</li>
              ))}
            </ul>
          </Alert>
        )}
        <Card>
          <CardBody>
            <Tabs
              label="Hold status"
              value={tab}
              onValueChange={(id) => {
                setTab(id as 'active' | 'released');
                setPage(1);
              }}
              tabs={[
                { id: 'active', label: 'Active', content: list },
                { id: 'released', label: 'Released', content: list },
              ]}
            />
          </CardBody>
        </Card>
      </div>
      <PlaceHoldDialog open={placing} onClose={() => setPlacing(false)} onPlaced={setPlaced} />
      <ReleaseHoldDialog hold={releasing} onClose={() => setReleasing(null)} />
    </>
  );
}
