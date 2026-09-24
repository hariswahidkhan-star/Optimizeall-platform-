import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CalendarDays, CheckCircle2, MessageSquareWarning } from 'lucide-react';
import { useEffect, useMemo, useState, type ReactElement } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  LineChart,
  PageHeader,
  Select,
  Skeleton,
  Stat,
  Textarea,
  Timeline,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { formatMoney, formatNumber } from '@/lib/format/money';
import {
  NETWORK_LABELS,
  type CalendarResponse,
  type Media,
  type Post,
  type PostSummary,
  type SocialKpis,
  type SeriesPoint,
  type TopPost,
} from '@/features/agency/social/api';
import { PostPreview } from '@/features/agency/social/PostPreview';
import { NetworkChip, PostStatusBadge, SourceLabel, percent } from '@/features/agency/social/shared';
import '@/features/agency/social/social.css';

export interface Organization {
  id: string;
  name: string;
  currency: string;
  timeZone: string;
  role: 'Viewer' | 'Approver' | 'Billing' | 'Owner';
  canApprove: boolean;
}

interface AdsSummary {
  reportingCurrency: string;
  totals: {
    spend: number;
    impressions: number;
    clicks: number;
    conversions: number;
    conversionValue: number;
  };
  kpis: { cpa: number | null; roas: number | null; ctr: number | null };
  platforms: {
    platform: string;
    totals: { spend: number; conversions: number };
    kpis: { roas: number | null; cpa: number | null };
    sourceLabel: string;
  }[];
  fxMissing: string[];
  sourceLabel: string;
}

interface Performance {
  social: SocialKpis;
  socialSeries: SeriesPoint[];
  topPosts: TopPost[];
  ads: AdsSummary;
}

const clientKeys = {
  orgs: ['client-social', 'orgs'] as const,
  calendar: (org: string, from: string) => ['client-social', 'calendar', org, from] as const,
  approvals: (org: string) => ['client-social', 'approvals', org] as const,
  post: (id: string) => ['client-social', 'post', id] as const,
  performance: (org: string, from: string, to: string) =>
    ['client-social', 'performance', org, from, to] as const,
};

/** The organization the client user is looking at (first one by default; switchable when they belong to several). */
function useOrganization(): {
  org: Organization | undefined;
  picker: ReactElement | null;
  loading: boolean;
  error: unknown;
} {
  const orgs = useQuery({
    queryKey: clientKeys.orgs,
    queryFn: () => api.get<Organization[]>('/client/social/organizations'),
  });
  const [selected, setSelected] = useState<string | undefined>();
  const list = orgs.data ?? [];
  const org = list.find((o) => o.id === selected) ?? list[0];
  const picker =
    list.length > 1 ? (
      <FormField label="Organization">
        <Select
          value={org?.id ?? ''}
          onChange={(e) => setSelected(e.target.value)}
          options={list.map((o) => ({ value: o.id, label: o.name }))}
        />
      </FormField>
    ) : null;
  return { org, picker, loading: orgs.isLoading, error: orgs.error };
}

function Gate({
  state,
  children,
}: {
  state: ReturnType<typeof useOrganization>;
  children: (org: Organization) => ReactElement;
}) {
  if (state.loading) return <Skeleton height="16rem" />;
  if (state.error) return <ErrorState error={state.error} />;
  if (!state.org)
    return (
      <EmptyState
        icon={<CalendarDays />}
        title="No organization"
        description="Your account is not linked to a client organization yet."
      />
    );
  return children(state.org);
}

// ---------------------------------------------------------------- calendar preview

export function ClientSocialCalendarPage() {
  const state = useOrganization();
  const [month, setMonth] = useState(() => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
  });
  return (
    <>
      <PageHeader
        title="Social calendar"
        description="Posts planned, awaiting your approval, scheduled and published."
      />
      <div className="sm-toolbar">
        {state.picker}
        <FormField label="Month">
          <Input type="month" value={month} onChange={(e) => e.target.value && setMonth(e.target.value)} />
        </FormField>
      </div>
      <Gate state={state}>{(org) => <CalendarList org={org} month={month} />}</Gate>
    </>
  );
}

function CalendarList({ org, month }: { org: Organization; month: string }) {
  const [y, m] = month.split('-').map(Number);
  const from = new Date(y!, m! - 1, 1).toISOString();
  const to = new Date(y!, m!, 1).toISOString();
  const query = useQuery({
    queryKey: clientKeys.calendar(org.id, from),
    queryFn: () =>
      api.get<CalendarResponse>('/client/social/calendar', { query: { clientId: org.id, from, to } }),
  });
  const columns: DataTableColumn<PostSummary>[] = [
    {
      id: 'when',
      header: 'When',
      primary: true,
      cell: (p) => (p.scheduledAt ? <DateTime value={p.scheduledAt} format="datetime" /> : '—'),
    },
    { id: 'title', header: 'Post', cell: (p) => p.title },
    {
      id: 'preview',
      header: 'Text',
      hideOnMobile: true,
      cell: (p) => <span className="sm-muted">{p.preview}</span>,
    },
    {
      id: 'networks',
      header: 'Networks',
      cell: (p) => (
        <span className="sm-network-list">
          {p.networks.map((n) => (
            <NetworkChip key={n} network={n} />
          ))}
        </span>
      ),
    },
    { id: 'status', header: 'Status', cell: (p) => <PostStatusBadge status={p.status} /> },
  ];
  if (query.isError) return <ErrorState error={query.error} />;
  return (
    <div className="stack">
      <DataTable
        caption="Posts this month"
        columns={columns}
        rows={query.data?.posts ?? []}
        getRowId={(p) => p.id}
        loading={query.isLoading}
        emptyState={
          <EmptyState compact icon={<CalendarDays />} headingLevel={2} title="Nothing planned this month" />
        }
      />
      {(query.data?.awarenessDays.length ?? 0) > 0 && (
        <section aria-labelledby="client-awareness" className="stack">
          <h2 id="client-awareness" className="sm-h3">
            Holidays & awareness days
          </h2>
          <ul className="sm-issues">
            {query.data!.awarenessDays.map((d) => (
              <li key={`${d.date}-${d.name}`}>
                {d.date}: {d.name}
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

// ---------------------------------------------------------------- approvals

export function ClientApprovalsPage() {
  const state = useOrganization();
  return (
    <>
      <PageHeader
        title="Approvals"
        description="Review posts before they are scheduled. Approve them, or request changes with a note for the team."
      />
      <div className="sm-toolbar">{state.picker}</div>
      <Gate state={state}>{(org) => <ApprovalQueue org={org} />}</Gate>
    </>
  );
}

function ApprovalQueue({ org }: { org: Organization }) {
  const query = useQuery({
    queryKey: clientKeys.approvals(org.id),
    queryFn: () => api.get<PostSummary[]>('/client/social/approvals', { query: { clientId: org.id } }),
  });
  const [open, setOpen] = useState<string | null>(null);
  if (query.isError) return <ErrorState error={query.error} />;
  if (query.isLoading) return <Skeleton height="10rem" />;
  const posts = query.data ?? [];
  return (
    <div className="stack">
      {!org.canApprove && (
        <Alert tone="info" title="View only">
          Your role ({org.role}) can see posts but not approve them. Ask an Approver or Owner in your
          organization.
        </Alert>
      )}
      {posts.length === 0 ? (
        <EmptyState
          icon={<CheckCircle2 />}
          title="You're all caught up"
          description="No posts are waiting for your approval."
        />
      ) : (
        <ul
          className="stack"
          style={{ listStyle: 'none', padding: 0, margin: 0 }}
          aria-label="Posts awaiting approval"
        >
          {posts.map((p) => (
            <li key={p.id}>
              <Card as="article" aria-labelledby={`appr-${p.id}`}>
                <CardHeader
                  title={p.title}
                  titleId={`appr-${p.id}`}
                  description={
                    p.scheduledAt ? `Planned for ${new Date(p.scheduledAt).toLocaleString()}` : undefined
                  }
                  actions={
                    <Button variant="secondary" onClick={() => setOpen(p.id)}>
                      Review
                    </Button>
                  }
                />
                <CardBody>
                  <p className="sm-muted">{p.preview}</p>
                  <span className="sm-network-list">
                    {p.networks.map((n) => (
                      <NetworkChip key={n} network={n} />
                    ))}
                  </span>
                </CardBody>
              </Card>
            </li>
          ))}
        </ul>
      )}
      {open && <ReviewDialog postId={open} org={org} onClose={() => setOpen(null)} />}
    </div>
  );
}

function ReviewDialog({ postId, org, onClose }: { postId: string; org: Organization; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const post = useQuery({
    queryKey: clientKeys.post(postId),
    queryFn: () => api.get<Post>(`/client/social/posts/${postId}`),
  });
  const media = useQuery({
    queryKey: ['client-social', 'media', postId],
    queryFn: () => api.get<Media[]>('/client/social/media', { query: { postId } }),
  });
  const [comment, setComment] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [variantIndex, setVariantIndex] = useState(0);
  useEffect(() => setVariantIndex(0), [postId]);
  const decide = useMutation({
    mutationFn: (kind: 'approve' | 'request-changes') =>
      api.post<Post>(`/client/social/posts/${postId}/${kind}`, {
        comment: comment || undefined,
        concurrencyStamp: post.data?.concurrencyStamp,
      }),
    onSuccess: (_, kind) => {
      toast.success(kind === 'approve' ? 'Post approved' : 'Changes requested');
      void queryClient.invalidateQueries({ queryKey: ['client-social'] });
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  const mediaMap = useMemo(() => new Map((media.data ?? []).map((m) => [m.id, m])), [media.data]);
  const p = post.data;
  const v = p?.variants[variantIndex];
  return (
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title={p?.title ?? 'Review post'}
      footer={
        org.canApprove && p?.allowedActions.includes('clientApprove') ? (
          <>
            <Button
              variant="secondary"
              leadingIcon={<MessageSquareWarning />}
              disabled={!comment.trim()}
              onClick={() => decide.mutate('request-changes')}
            >
              Request changes
            </Button>
            <Button
              variant="highlight"
              leadingIcon={<CheckCircle2 />}
              loading={decide.isPending}
              onClick={() => decide.mutate('approve')}
            >
              Approve
            </Button>
          </>
        ) : (
          <Button variant="secondary" onClick={onClose}>
            Close
          </Button>
        )
      }
    >
      {post.isError && <ErrorState error={post.error} />}
      {!p ? (
        <Skeleton height="12rem" />
      ) : (
        <div className="stack">
          {error && <Alert tone="danger">{error}</Alert>}
          {p.variants.length > 1 && (
            <FormField label="Network">
              <Select
                value={String(variantIndex)}
                onChange={(e) => setVariantIndex(Number(e.target.value))}
                options={p.variants.map((x, i) => ({
                  value: String(i),
                  label: `${NETWORK_LABELS[x.network]} @${x.profileHandle}`,
                }))}
              />
            </FormField>
          )}
          {v && (
            <PostPreview
              network={v.network}
              displayName={v.profileName}
              handle={v.profileHandle}
              text={v.text}
              title={v.title}
              hashtags={v.hashtags}
              link={v.effectiveLink}
              media={v.mediaIds.map((id) => mediaMap.get(id)).filter((m): m is Media => !!m)}
              altTexts={v.altTexts}
              preset={undefined}
            />
          )}
          {p.comments.length > 0 && (
            <Timeline
              label="Comments"
              items={p.comments.map((c) => ({
                id: c.id,
                title: c.isResolved ? `${c.authorName} · addressed` : c.authorName,
                description: c.body,
                timestamp: c.createdAt,
                tone: c.isResolved ? ('success' as const) : undefined,
              }))}
            />
          )}
          {org.canApprove && (
            <FormField label="Comment" hint="Required when requesting changes">
              <Textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} />
            </FormField>
          )}
        </div>
      )}
    </Dialog>
  );
}

// ---------------------------------------------------------------- performance

export function ClientPerformancePage() {
  const state = useOrganization();
  const [days, setDays] = useState('30');
  return (
    <>
      <PageHeader
        title="Social & ads performance"
        description="Results across your social profiles and ad accounts. Each figure shows whether it was measured or entered manually."
      />
      <div className="sm-toolbar">
        {state.picker}
        <FormField label="Period">
          <Select
            value={days}
            onChange={(e) => setDays(e.target.value)}
            options={[
              { value: '7', label: 'Last 7 days' },
              { value: '30', label: 'Last 30 days' },
              { value: '90', label: 'Last 90 days' },
            ]}
          />
        </FormField>
      </div>
      <Gate state={state}>{(org) => <PerformanceBody org={org} days={Number(days)} />}</Gate>
    </>
  );
}

function PerformanceBody({ org, days }: { org: Organization; days: number }) {
  const to = new Date(Date.now() - 86_400_000).toISOString().slice(0, 10);
  const from = new Date(Date.now() - days * 86_400_000).toISOString().slice(0, 10);
  const query = useQuery({
    queryKey: clientKeys.performance(org.id, from, to),
    queryFn: () =>
      api.get<Performance>('/client/social/performance', { query: { clientId: org.id, from, to } }),
  });
  if (query.isError) return <ErrorState error={query.error} />;
  if (!query.data) return <Skeleton height="16rem" />;
  const { social, socialSeries, topPosts, ads } = query.data;
  const cur = ads.reportingCurrency;
  const m = social.sourceLabel === 'Measured' ? 'Measured' : 'Estimated';
  return (
    <div className="stack">
      <section aria-labelledby="perf-social" className="stack">
        <div className="cluster">
          <h2 id="perf-social" className="sm-h2">
            Social
          </h2>
          <SourceLabel label={social.sourceLabel} />
        </div>
        <div className="sm-grid-stats">
          <Stat label="Impressions" value={formatNumber(social.totals.impressions)} measurement={m} />
          <Stat label="Engagements" value={formatNumber(social.totals.engagements)} measurement={m} />
          <Stat label="Engagement rate" value={percent(social.totals.engagementRate, 2)} measurement={m} />
          <Stat
            label="Followers growth"
            value={
              social.totals.followersGrowth == null
                ? '—'
                : formatNumber(social.totals.followersGrowth, { signDisplay: 'exceptZero' })
            }
            measurement={m}
          />
          <Stat label="Posts published" value={formatNumber(social.postsPublished)} measurement="Count" />
        </div>
        {socialSeries.length > 1 && (
          <LineChart
            title="Impressions per day"
            description="Daily impressions across your social profiles."
            labels={socialSeries.map((s) => s.date)}
            series={[{ id: 'imp', label: 'Impressions', values: socialSeries.map((s) => s.impressions) }]}
            valueFormatter={(v) => formatNumber(v)}
          />
        )}
        {topPosts.length > 0 && (
          <DataTable
            caption="Top posts"
            columns={[
              {
                id: 'post',
                header: 'Post',
                primary: true,
                cell: (p: TopPost) => (
                  <span>
                    <NetworkChip network={p.network} /> {p.title ?? 'Post'}
                  </span>
                ),
              },
              {
                id: 'eng',
                header: 'Engagements',
                align: 'right',
                cell: (p: TopPost) => formatNumber(p.engagements),
              },
              {
                id: 'er',
                header: 'Eng. rate',
                align: 'right',
                cell: (p: TopPost) => percent(p.engagementRate),
              },
              {
                id: 'link',
                header: 'Link',
                cell: (p: TopPost) => (p.url ? <SafeExternalLink href={p.url}>View</SafeExternalLink> : '—'),
              },
            ]}
            rows={topPosts}
            getRowId={(p) => `${p.profileHandle}-${p.publishedAt}-${p.title}`}
          />
        )}
      </section>
      <section aria-labelledby="perf-ads" className="stack">
        <div className="cluster">
          <h2 id="perf-ads" className="sm-h2">
            Paid ads
          </h2>
          <SourceLabel label={ads.sourceLabel} />
        </div>
        {ads.fxMissing.length > 0 && (
          <Alert tone="warning">
            Some spend could not be converted to {cur} yet ({ads.fxMissing.join(', ')}).
          </Alert>
        )}
        <div className="sm-grid-stats">
          <Stat label="Ad spend" value={formatMoney(ads.totals.spend, cur)} measurement="Measured" />
          <Stat
            label="Conversions"
            value={formatNumber(ads.totals.conversions, { maximumFractionDigits: 1 })}
            measurement="Measured"
          />
          <Stat
            label="Cost per acquisition"
            value={ads.kpis.cpa == null ? '—' : formatMoney(ads.kpis.cpa, cur)}
            measurement="Measured"
          />
          <Stat
            label="Return on ad spend"
            value={ads.kpis.roas == null ? '—' : `${ads.kpis.roas.toFixed(2)}×`}
            measurement="Measured"
          />
        </div>
        {ads.platforms.length > 0 && (
          <ul className="cluster" style={{ listStyle: 'none', padding: 0 }}>
            {ads.platforms.map((p) => (
              <li key={p.platform}>
                <Badge tone="brand">
                  {p.platform}: {formatMoney(p.totals.spend, cur)} · ROAS{' '}
                  {p.kpis.roas == null ? '—' : p.kpis.roas.toFixed(2)}
                </Badge>
              </li>
            ))}
          </ul>
        )}
      </section>
      <p className="sm-muted">{social.definitions}</p>
    </div>
  );
}
