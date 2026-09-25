import { Bell, Copy, Download, Pencil, Plus, Trash2, Wallet } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Logo, LogoMark } from '@/components/brand/Logo';
import { ThemeToggle } from '@/components/ThemeToggle';
import {
  Alert,
  Avatar,
  Badge,
  BarChart,
  Button,
  Card,
  CardBody,
  CardFooter,
  CardHeader,
  Checkbox,
  ConfirmDialog,
  CopyField,
  DataTable,
  DateTime,
  Dialog,
  DropdownMenu,
  EmptyState,
  ErrorState,
  FileDrop,
  FilterBar,
  FormField,
  IconButton,
  Input,
  KeyValueList,
  LineChart,
  Money,
  PageHeader,
  Pagination,
  PasswordInput,
  ProgressBar,
  ProgressRing,
  RadioGroup,
  Select,
  Skeleton,
  Sparkline,
  Spinner,
  Stat,
  StatusBadge,
  Stepper,
  Switch,
  Tabs,
  Textarea,
  Timeline,
  Tooltip,
  useToast,
  type DataTableColumn,
  type SortState,
  type StatusKind,
} from '@/components/ui';
import { ApiError } from '@/lib/api/errors';
import './DesignSystemPage.css';

interface SampleRow {
  id: string;
  participant: string;
  campaign: string;
  status: string;
  amount: number;
  currency: string;
  submittedAt: string;
}

const now = Date.now();
const ROWS: SampleRow[] = [
  {
    id: 's1',
    participant: 'Amina Khan',
    campaign: 'Spring launch',
    status: 'UnderReview',
    amount: 12.5,
    currency: 'USD',
    submittedAt: new Date(now - 36e5).toISOString(),
  },
  {
    id: 's2',
    participant: 'Leo Martins',
    campaign: 'Eco bottles',
    status: 'Approved',
    amount: 1500,
    currency: 'JPY',
    submittedAt: new Date(now - 864e5).toISOString(),
  },
  {
    id: 's3',
    participant: 'Sara Ali',
    campaign: 'Spring launch',
    status: 'NeedsCorrection',
    amount: 4.25,
    currency: 'KWD',
    submittedAt: new Date(now - 3 * 864e5).toISOString(),
  },
  {
    id: 's4',
    participant: 'Tom Becker',
    campaign: 'City rides',
    status: 'Rejected',
    amount: 9,
    currency: 'EUR',
    submittedAt: new Date(now - 5 * 864e5).toISOString(),
  },
];

const STATUS_SAMPLES: Record<StatusKind, string[]> = {
  submission: ['Pending', 'UnderReview', 'Approved', 'NeedsCorrection', 'Rejected', 'Reversed', 'Withdrawn'],
  earning: ['PendingApproval', 'Approved', 'Scheduled', 'Paid', 'Reversed', 'Declined'],
  payout: ['Draft', 'Finalized', 'Completed', 'Cancelled'],
  payoutItem: ['Pending', 'Held', 'AwaitingPayment', 'Paid', 'Failed', 'Cancelled'],
  campaign: ['Draft', 'Scheduled', 'Active', 'Paused', 'Ended', 'Archived'],
  socialVerification: ['Unverified', 'PendingReview', 'Verified', 'Rejected'],
  ticket: ['Open', 'InProgress', 'AwaitingCustomer', 'Resolved', 'Closed'],
};

const SWATCHES = [
  ['--color-primary', 'Primary'],
  ['--color-accent', 'Accent'],
  ['--color-heading', 'Ink'],
  ['--color-surface', 'Surface'],
  ['--color-bg', 'Background'],
  ['--success-solid', 'Success'],
  ['--warning-solid', 'Warning'],
  ['--danger-solid', 'Danger'],
  ['--info-solid', 'Info'],
  ['--chart-1', 'Chart 1'],
  ['--chart-2', 'Chart 2'],
  ['--chart-3', 'Chart 3'],
];

function Section({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <section className="ds-section" aria-labelledby={`ds-${id}`}>
      <h2 id={`ds-${id}`} className="ds-section__title">
        {title}
      </h2>
      <div className="ds-section__body">{children}</div>
    </section>
  );
}

/** Living catalogue of the design system (development and staging only). */
export function DesignSystemPage() {
  const toast = useToast();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [switchOn, setSwitchOn] = useState(true);
  const [radio, setRadio] = useState('biweekly');
  const [file, setFile] = useState<File | null>(null);
  const [page, setPage] = useState(3);
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [selected, setSelected] = useState<string[]>([]);
  const [sort, setSort] = useState<SortState | null>({ id: 'submittedAt', desc: true });

  const columns: DataTableColumn<SampleRow>[] = [
    {
      id: 'participant',
      header: 'Participant',
      cell: (r) => r.participant,
      sortable: true,
      sortValue: (r) => r.participant,
      primary: true,
    },
    {
      id: 'campaign',
      header: 'Campaign',
      cell: (r) => r.campaign,
      sortable: true,
      sortValue: (r) => r.campaign,
    },
    { id: 'status', header: 'Status', cell: (r) => <StatusBadge kind="submission" status={r.status} /> },
    {
      id: 'amount',
      header: 'Reward',
      align: 'right',
      cell: (r) => <Money amount={r.amount} currency={r.currency} />,
      sortable: true,
      sortValue: (r) => r.amount,
    },
    {
      id: 'submittedAt',
      header: 'Submitted',
      cell: (r) => <DateTime value={r.submittedAt} format="relative" />,
      sortable: true,
      sortValue: (r) => r.submittedAt,
      nowrap: true,
    },
  ];

  return (
    <div className="container ds">
      <PageHeader
        eyebrow="Internal"
        title="Design system"
        description="Tokens and components of the Optimize All UI. Toggle the theme to review both palettes."
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Design system' }]}
        actions={<ThemeToggle />}
      />

      <Section id="brand" title="Brand">
        <div className="ds-row ds-row--center">
          <Logo variant="mark" size={56} />
          <Logo variant="horizontal" size={40} />
          <Logo variant="stacked" size={96} tagline />
          <div className="ds-dark-panel">
            <Logo variant="stacked" size={80} tagline tone="onDark" />
          </div>
          <LogoMark size={24} title="Optimize All" />
        </div>
      </Section>

      <Section id="colors" title="Color tokens">
        <ul className="ds-swatches">
          {SWATCHES.map(([token, label]) => (
            <li key={token}>
              <span className="ds-swatch" style={{ background: `var(${token})` }} />
              <span className="ds-swatch__label">{label}</span>
              <code>{token}</code>
            </li>
          ))}
        </ul>
      </Section>

      <Section id="type" title="Typography">
        <p className="ds-display">Display — Get paid to share</p>
        <h1>Heading 1</h1>
        <h2>Heading 2</h2>
        <h3>Heading 3</h3>
        <h4>Heading 4</h4>
        <p>Body — Share company-approved content from your established accounts.</p>
        <p className="text-small text-muted">Small muted text for hints and metadata.</p>
        <p className="eyebrow">Eyebrow label</p>
        <p className="tabular">Tabular numerals: 1,234.50 · 98,760.00</p>
      </Section>

      <Section id="buttons" title="Buttons">
        <div className="ds-row">
          <Button>Primary</Button>
          <Button variant="highlight">Highlight</Button>
          <Button variant="secondary">Secondary</Button>
          <Button variant="ghost">Ghost</Button>
          <Button variant="danger" leadingIcon={<Trash2 />}>
            Delete
          </Button>
          <Button variant="link">Link button</Button>
          <Button loading>Saving</Button>
          <Button disabled>Disabled</Button>
          <Button size="sm" leadingIcon={<Plus />}>
            Small
          </Button>
          <Button size="lg">Large</Button>
          <IconButton label="Edit" icon={<Pencil />} />
          <IconButton label="Download" variant="secondary" icon={<Download />} />
          <Tooltip content="Copies the tracking link">
            <IconButton label="Copy link" variant="secondary" icon={<Copy />} />
          </Tooltip>
        </div>
      </Section>

      <Section id="badges" title="Badges & statuses">
        <div className="ds-row">
          <Badge>Neutral</Badge>
          <Badge tone="brand">Brand</Badge>
          <Badge tone="info">Info</Badge>
          <Badge tone="success" dot>
            Success
          </Badge>
          <Badge tone="warning">Warning</Badge>
          <Badge tone="danger">Danger</Badge>
        </div>
        {(Object.keys(STATUS_SAMPLES) as StatusKind[]).map((kind) => (
          <div key={kind} className="ds-row">
            <code className="ds-kind">{kind}</code>
            {STATUS_SAMPLES[kind].map((s) => (
              <StatusBadge key={s} kind={kind} status={s} />
            ))}
          </div>
        ))}
      </Section>

      <Section id="forms" title="Form controls">
        <div className="ds-grid">
          <FormField label="Email" hint="We never share your email." required>
            <Input type="email" placeholder="name@example.com" />
          </FormField>
          <FormField label="Password" error="Use at least 10 characters.">
            <PasswordInput defaultValue="short" />
          </FormField>
          <FormField label="Country">
            <Select
              placeholder="Choose…"
              options={[
                { value: 'GB', label: 'United Kingdom' },
                { value: 'US', label: 'United States' },
              ]}
            />
          </FormField>
          <FormField label="Notes" optional>
            <Textarea placeholder="Anything reviewers should know" />
          </FormField>
        </div>
        <div className="ds-grid">
          <Checkbox label="Email me about new campaigns" description="You can unsubscribe at any time." />
          <Switch
            checked={switchOn}
            onCheckedChange={setSwitchOn}
            label="Payout notifications"
            description="Email when a payout is sent."
          />
          <RadioGroup
            legend="Payout frequency"
            value={radio}
            onChange={setRadio}
            orientation="horizontal"
            options={[
              { value: 'weekly', label: 'Weekly', disabled: true },
              { value: 'biweekly', label: 'Biweekly' },
              { value: 'monthly', label: 'Monthly' },
            ]}
          />
        </div>
        <FileDrop label="Screenshot of your post" value={file} onChange={setFile} required />
        <CopyField label="Your referral link" value="https://optimizeall.app/r/AMINA42" />
      </Section>

      <Section id="feedback" title="Feedback">
        <div className="stack">
          <Alert tone="info" title="Heads up">
            Reviews usually take less than 48 hours.
          </Alert>
          <Alert tone="success" title="Post approved" onDismiss={() => toast.info('Dismissed')}>
            Earnings were added to your next payout.
          </Alert>
          <Alert
            tone="warning"
            title="Needs correction"
            actions={
              <Button size="sm" variant="secondary">
                Fix it
              </Button>
            }
          >
            Add the #ad disclosure to your caption and resubmit.
          </Alert>
          <Alert tone="danger" title="Rejected">
            This post was published from an account that isn’t verified.
          </Alert>
          <div className="ds-row">
            <Button variant="secondary" onClick={() => toast.success('Saved', 'Your changes are live.')}>
              Success toast
            </Button>
            <Button variant="secondary" onClick={() => toast.error('Couldn’t save', 'Please try again.')}>
              Error toast
            </Button>
            <Button variant="secondary" onClick={() => toast.info('Heads up')}>
              Info toast
            </Button>
            <Spinner />
            <Skeleton width={120} height={16} />
          </div>
        </div>
      </Section>

      <Section id="tabs" title="Tabs">
        <Tabs
          label="Submission details"
          tabs={[
            { id: 'overview', label: 'Overview', content: <p>Post link, screenshot and campaign rules.</p> },
            {
              id: 'history',
              label: 'History',
              badge: 3,
              content: <p>Every status change with who made it.</p>,
            },
            { id: 'notes', label: 'Reviewer notes', content: <p>Internal notes, visible to staff only.</p> },
          ]}
        />
        <Tabs
          label="Earnings period"
          className="ui-tabs--segmented"
          tabs={[
            { id: 'week', label: 'This week', content: <p>Approved earnings for the current week.</p> },
            { id: 'month', label: 'This month', content: <p>Approved earnings for the current month.</p> },
            { id: 'all', label: 'All time', content: <p>Everything you have earned so far.</p> },
          ]}
        />
      </Section>

      <Section id="overlays" title="Overlays">
        <div className="ds-row">
          <Button onClick={() => setDialogOpen(true)}>Open dialog</Button>
          <Button variant="danger" onClick={() => setConfirmOpen(true)}>
            Finalize batch…
          </Button>
          <DropdownMenu
            align="start"
            trigger={<Button variant="secondary">Actions</Button>}
            items={[
              { id: 'edit', label: 'Edit', icon: <Pencil /> },
              { id: 'copy', label: 'Duplicate', icon: <Copy /> },
              { type: 'separator', id: 'sep' },
              { id: 'delete', label: 'Delete', icon: <Trash2 />, danger: true },
            ]}
          />
        </div>
        <Dialog
          open={dialogOpen}
          onClose={() => setDialogOpen(false)}
          title="Submit proof"
          description="Paste your post link and attach a screenshot."
          footer={
            <>
              <Button variant="secondary" onClick={() => setDialogOpen(false)}>
                Cancel
              </Button>
              <Button onClick={() => setDialogOpen(false)}>Submit</Button>
            </>
          }
        >
          <FormField label="Post URL">
            <Input type="url" placeholder="https://" />
          </FormField>
        </Dialog>
        <ConfirmDialog
          open={confirmOpen}
          onClose={() => setConfirmOpen(false)}
          onConfirm={({ reason }) => {
            toast.success('Batch finalized', `Reason: ${reason}`);
          }}
          title="Finalize payout batch?"
          description="Finalizing freezes the batch for payment. This can’t be undone."
          tone="danger"
          confirmLabel="Finalize batch"
          requireReason
          confirmText="FINALIZE"
        />
      </Section>

      <Section id="data" title="Data display">
        <div className="ds-stats">
          <Stat
            label="Approved earnings"
            value={<Money amount={1284.5} currency="USD" />}
            measurement="Measured"
            delta={{ value: 0.124, label: 'vs last period' }}
            icon={<Wallet />}
          />
          <Stat
            label="Estimated reach"
            value="48.2K"
            measurement="Estimated"
            delta={{ value: -0.03, label: 'vs last period' }}
          />
          <Stat
            label="Approved posts"
            value={312}
            measurement="Count"
            hint={
              <Sparkline values={[3, 5, 4, 8, 7, 9, 12]} label="Approved posts, last 7 days: trending up" />
            }
          />
        </div>
        <div className="ds-grid">
          <Card>
            <CardHeader
              title="Card title"
              description="Supporting description"
              actions={<IconButton label="Notifications" icon={<Bell />} />}
            />
            <CardBody>
              <KeyValueList
                items={[
                  { label: 'Participant', value: 'Amina Khan' },
                  { label: 'Reward', value: <Money amount={4.25} currency="KWD" /> },
                  { label: 'Submitted', value: <DateTime value={ROWS[0]!.submittedAt} format="both" /> },
                  { label: 'Status', value: <StatusBadge kind="submission" status="UnderReview" /> },
                ]}
              />
            </CardBody>
            <CardFooter>
              <Button variant="secondary">Reject</Button>
              <Button>Approve</Button>
            </CardFooter>
          </Card>
          <Card>
            <CardHeader title="Onboarding" headingLevel={3} />
            <CardBody className="stack">
              <Stepper
                label="Onboarding"
                steps={[
                  { id: 'verify', title: 'Verify your email', status: 'complete' },
                  {
                    id: 'connect',
                    title: 'Connect a social account',
                    description: 'Established accounts only.',
                    status: 'current',
                    action: <Button size="sm">Connect</Button>,
                  },
                  { id: 'payout', title: 'Add payout details', status: 'upcoming' },
                ]}
              />
              <ProgressBar label="Profile complete" value={2} max={3} valueText="2 of 3 steps" />
              <div className="ds-row">
                <ProgressRing value={68} label="Campaign budget used" />
                <Avatar name="Amina Khan" size={44} />
                <Avatar name="leo.martins@example.com" size={32} />
              </div>
            </CardBody>
          </Card>
        </div>
        <Timeline
          label="Submission history"
          items={[
            { id: '1', title: 'Submitted', timestamp: ROWS[2]!.submittedAt, tone: 'neutral' },
            {
              id: '2',
              title: 'Needs correction',
              description: 'Disclosure missing from caption.',
              actor: 'Reviewed by Priya',
              timestamp: ROWS[1]!.submittedAt,
              tone: 'warning',
            },
            { id: '3', title: 'Approved', timestamp: ROWS[0]!.submittedAt, tone: 'success' },
          ]}
        />
      </Section>

      <Section id="table" title="Data table">
        <FilterBar
          search={search}
          onSearchChange={setSearch}
          searchPlaceholder="Search participants"
          filters={[
            {
              id: 'status',
              label: 'Status',
              options: [
                { value: 'UnderReview', label: 'Under review' },
                { value: 'Approved', label: 'Approved' },
              ],
            },
          ]}
          values={filters}
          onFilterChange={(id, value) => setFilters((f) => ({ ...f, [id]: value }))}
          onReset={() => {
            setSearch('');
            setFilters({});
          }}
        />
        <DataTable
          caption="Recent submissions"
          columns={columns}
          rows={ROWS.filter(
            (r) =>
              (!search || r.participant.toLowerCase().includes(search.toLowerCase())) &&
              (!filters.status || r.status === filters.status),
          )}
          getRowId={(r) => r.id}
          rowLabel={(r) => `${r.participant} — ${r.campaign}`}
          sort={sort}
          onSortChange={setSort}
          selectable
          selectedIds={selected}
          onSelectionChange={setSelected}
          bulkActions={() => <Button size="sm">Assign to me</Button>}
          rowActions={() => [
            { id: 'open', label: 'Open' },
            { id: 'reject', label: 'Reject', danger: true },
          ]}
        />
        <Pagination
          page={page}
          pageSize={25}
          total={480}
          onPageChange={setPage}
          onPageSizeChange={() => undefined}
        />
      </Section>

      <Section id="charts" title="Charts">
        <div className="ds-grid">
          <Card>
            <CardHeader title="Approved posts per week" headingLevel={3} />
            <CardBody>
              <BarChart
                title="Approved posts per week"
                description="Approved posts rose from 18 to 41 over six weeks."
                data={[
                  { label: 'W1', value: 18 },
                  { label: 'W2', value: 22 },
                  { label: 'W3', value: 27 },
                  { label: 'W4', value: 25 },
                  { label: 'W5', value: 36 },
                  { label: 'W6', value: 41 },
                ]}
              />
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Submissions vs approvals" headingLevel={3} />
            <CardBody>
              <LineChart
                title="Submissions vs approvals"
                description="Approvals track submissions with a stable approval rate around 80%."
                labels={['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']}
                series={[
                  { id: 'sub', label: 'Submitted', values: [40, 52, 48, 61, 58, 30, 25] },
                  { id: 'app', label: 'Approved', values: [31, 43, 39, 50, 47, 24, 20] },
                ]}
              />
            </CardBody>
          </Card>
        </div>
      </Section>

      <Section id="states" title="Empty & error states">
        <div className="ds-grid">
          <Card flat>
            <EmptyState
              headingLevel={3}
              title="No submissions yet"
              description="Share a campaign and submit your post to see it here."
              action={<Button>Browse campaigns</Button>}
            />
          </Card>
          <Card flat>
            <ErrorState
              headingLevel={3}
              error={
                new ApiError({
                  status: 500,
                  code: 'server.error',
                  title: 'An unexpected error occurred.',
                  traceId: '0HN7ABC123:00000001',
                })
              }
              onRetry={() => toast.info('Retrying…')}
            />
          </Card>
        </div>
      </Section>
    </div>
  );
}
