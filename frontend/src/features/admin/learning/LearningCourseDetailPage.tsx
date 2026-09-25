import '@/features/learning/learning.css';
import { Pencil } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { StatGrid } from '@/components/ui/Dashboard';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { Skeleton } from '@/components/ui/Skeleton';
import { Stat } from '@/components/ui/Stat';
import { Switch } from '@/components/ui/Switch';
import { Tabs } from '@/components/ui/Tabs';
import { useToast } from '@/components/ui/toastContext';
import { Permissions } from '@/lib/auth/permissions';
import { CATEGORY_LABELS } from '@/features/learning/api';
import { ExportCsvButton, QueryError, useCan } from '../shared/common';
import { adminErrorMessage } from '../shared/errors';
import {
  uploadMedia,
  useAdminCourse,
  useCourseMutations,
  useCourseVersion,
  useLearners,
  useQuestionStats,
  type AdminCourseDetail,
} from './api';

export function LearningCourseDetailPage() {
  const { courseId = '' } = useParams();
  const q = useAdminCourse(courseId);
  const canManage = useCan(Permissions.LearningManage);
  const [tab, setTab] = useState('overview');

  if (q.isPending) return <Skeleton height={400} />;
  if (q.isError) return <QueryError error={q.error} onRetry={() => void q.refetch()} />;
  const c = q.data.summary;
  return (
    <>
      <PageHeader
        title={c.title}
        description={`${CATEGORY_LABELS[c.category]} · ${c.level} · ${c.lessonCount} lessons · /learn/${c.slug}`}
        breadcrumbs={[{ label: 'Learning', to: '/admin/learning' }, { label: c.title }]}
        meta={
          <>
            <Badge tone={c.status === 'Published' ? 'success' : 'neutral'}>{c.status}</Badge>
            <Badge>{c.origin === 'Pack' ? `Course pack${q.data.packFile ? ` · ${q.data.packFile}` : ''}` : 'Staff-authored'}</Badge>
          </>
        }
        actions={
          <>
            <ExportCsvButton path={`/admin/learning/courses/${c.id}/results.csv`} fileName={`learning-results-${c.slug}.csv`} label="Export results" />
            {canManage && (
              <ButtonLink to="edit" leadingIcon={<Pencil />}>
                Edit course
              </ButtonLink>
            )}
          </>
        }
      />
      {c.packUpdateAvailable && (
        <Alert tone="warning" title="A newer course pack version is available">
          The shipped course pack was updated after staff published an edited version, so it was stored but not published. Compare the
          versions below and publish the one you want.
        </Alert>
      )}
      <Tabs
        label="Course sections"
        value={tab}
        onValueChange={setTab}
        tabs={[
          { id: 'overview', label: 'Overview', content: <Overview detail={q.data} canManage={canManage} /> },
          { id: 'questions', label: 'Question analytics', content: <Questions courseId={c.id} enabled={tab === 'questions'} /> },
          { id: 'learners', label: 'Learners', content: <Learners courseId={c.id} enabled={tab === 'learners'} /> },
          ...(canManage ? [{ id: 'videos', label: 'Videos', content: <Videos detail={q.data} enabled={tab === 'videos'} /> }] : []),
        ]}
      />
    </>
  );
}

function Overview({ detail, canManage }: { detail: AdminCourseDetail; canManage: boolean }) {
  const c = detail.summary;
  const m = useCourseMutations(c.id);
  const toast = useToast();
  const [featured, setFeatured] = useState(c.isFeatured);
  const [sortOrder, setSortOrder] = useState(String(c.sortOrder));
  const onError = (e: unknown) => toast.error('The change wasn’t saved', adminErrorMessage(e));
  return (
    <div className="stack">
      <StatGrid strip>
        <Stat label="Enrolled" value={c.enrolments} measurement="Count" />
        <Stat label="Completion rate" value={`${c.completionRate}%`} measurement="Measured" hint={`${c.completions} passed`} />
        <Stat label="Exam attempts" value={c.attempts} measurement="Count" />
        <Stat label="Pass rate" value={c.attempts ? `${c.passRate}%` : '–'} measurement="Measured" />
        <Stat label="Average score" value={c.averageScore !== null ? `${c.averageScore}%` : '–'} measurement="Measured" />
        <Stat label="Valid certificates" value={c.certificates} measurement="Count" />
      </StatGrid>
      <Card as="section" aria-labelledby="versions-heading">
        <CardHeader
          title="Versions"
          titleId="versions-heading"
          description="Versions are immutable. Edits and pack updates add a version; publish the one learners should see."
          actions={
            canManage && c.status === 'Published' ? (
              <Button
                variant="secondary"
                loading={m.unpublish.isPending}
                onClick={() => m.unpublish.mutate({ concurrencyStamp: c.concurrencyStamp }, { onError, onSuccess: () => toast.success('Course unpublished') })}
              >
                Unpublish
              </Button>
            ) : undefined
          }
        />
        <CardBody flush>
          <DataTable
            caption="Course versions"
            rows={detail.versions}
            getRowId={(v) => v.id}
            columns={[
              {
                id: 'number',
                header: 'Version',
                primary: true,
                cell: (v) => (
                  <span>
                    v{v.number} {v.isPublished && <Badge tone="success">Published</Badge>} {v.isLatest && !v.isPublished && <Badge>Latest</Badge>}
                  </span>
                ),
              },
              { id: 'source', header: 'Source', cell: (v) => (v.source === 'Pack' ? `Course pack v${v.packVersion}` : `Staff${v.createdBy ? ` · ${v.createdBy}` : ''}`) },
              { id: 'note', header: 'Note', cell: (v) => v.note ?? '' },
              { id: 'created', header: 'Created', cell: (v) => <DateTime value={v.createdAt} /> },
              {
                id: 'actions',
                header: <span className="visually-hidden">Actions</span>,
                cell: (v) =>
                  canManage && !(v.isPublished && c.status === 'Published') ? (
                    <Button
                      size="sm"
                      variant="secondary"
                      loading={m.publish.isPending && m.publish.variables?.versionId === v.id}
                      onClick={() =>
                        m.publish.mutate({ versionId: v.id, concurrencyStamp: c.concurrencyStamp }, { onError, onSuccess: () => toast.success(`Version ${v.number} published`) })
                      }
                    >
                      Publish v{v.number}
                    </Button>
                  ) : null,
              },
            ]}
          />
        </CardBody>
      </Card>
      {canManage && (
        <Card as="section" aria-labelledby="catalog-heading">
          <CardHeader title="Catalog placement" titleId="catalog-heading" />
          <CardBody className="stack">
            <Switch checked={featured} onCheckedChange={setFeatured} label="Featured" description="Shown first in the catalog and on the website home page." />
            <FormField label="Sort order" hint="Lower numbers come first.">
              <Input type="number" min={0} max={10000} value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} />
            </FormField>
            <div>
              <Button
                loading={m.settings.isPending}
                onClick={() =>
                  m.settings.mutate(
                    { isFeatured: featured, sortOrder: Number(sortOrder) || 0, concurrencyStamp: c.concurrencyStamp },
                    { onError, onSuccess: () => toast.success('Catalog placement saved') },
                  )
                }
              >
                Save placement
              </Button>
            </div>
          </CardBody>
        </Card>
      )}
    </div>
  );
}

function Questions({ courseId, enabled }: { courseId: string; enabled: boolean }) {
  const q = useQuestionStats(courseId, enabled);
  if (q.isError) return <QueryError error={q.error} onRetry={() => void q.refetch()} />;
  return (
    <Card>
      <CardBody className="stack">
        <p className="text-muted text-small">
          Difficulty is the share of graded answers that were correct. Discrimination compares attempts that passed with those that didn’t
          (points): low or negative values flag questions to review.
        </p>
        <DataTable
          caption="Question analytics"
          rows={q.data ?? []}
          loading={q.isPending}
          getRowId={(r) => r.questionId}
          defaultSort={{ id: 'correct', desc: false }}
          columns={[
            {
              id: 'question',
              header: 'Question',
              primary: true,
              cell: (r) => (
                <div className="admin-cell-stack">
                  <span>{r.question}</span>
                  <span className="text-small text-muted">
                    <code>{r.questionId}</code> · {r.module} · {r.difficulty} · {r.type}
                  </span>
                </div>
              ),
            },
            { id: 'answered', header: 'Answered', align: 'right', sortable: true, sortValue: (r) => r.answered, cell: (r) => r.answered },
            {
              id: 'correct',
              header: '% correct',
              align: 'right',
              sortable: true,
              sortValue: (r) => r.percentCorrect ?? -1,
              cell: (r) => (r.percentCorrect !== null ? `${r.percentCorrect}%` : '–'),
            },
            {
              id: 'disc',
              header: 'Discrimination',
              align: 'right',
              sortable: true,
              sortValue: (r) => r.discrimination ?? -999,
              cell: (r) => (r.discrimination !== null ? `${r.discrimination > 0 ? '+' : ''}${r.discrimination} pts` : '–'),
            },
          ]}
        />
      </CardBody>
    </Card>
  );
}

function Learners({ courseId, enabled }: { courseId: string; enabled: boolean }) {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const q = useLearners(courseId, { page, pageSize: 25, search: search || undefined }, enabled);
  if (q.isError) return <QueryError error={q.error} onRetry={() => void q.refetch()} />;
  return (
    <Card>
      <CardBody className="stack">
        <FormField label="Search learners" hideLabel>
          <Input type="search" placeholder="Name or email…" value={search} onChange={(e) => (setSearch(e.target.value), setPage(1))} />
        </FormField>
        <DataTable
          caption="Learners"
          rows={q.data?.items ?? []}
          loading={q.isPending}
          getRowId={(r) => r.userId}
          columns={[
            {
              id: 'name',
              header: 'Learner',
              primary: true,
              cell: (r) => (
                <div className="admin-cell-stack">
                  <Link className="ui-link" to={`/admin/users/${r.userId}`}>
                    {r.displayName}
                  </Link>
                  <span className="text-small text-muted">{r.email}</span>
                </div>
              ),
            },
            { id: 'progress', header: 'Progress', align: 'right', cell: (r) => `${r.completedLessons}/${r.lessonCount} (${r.progressPercent}%)` },
            { id: 'attempts', header: 'Attempts', align: 'right', cell: (r) => r.attempts },
            { id: 'best', header: 'Best score', align: 'right', cell: (r) => (r.bestScore !== null ? `${r.bestScore}%` : '–') },
            {
              id: 'status',
              header: 'Result',
              cell: (r) =>
                r.certificateId ? (
                  <Badge tone={r.certificateRevoked ? 'danger' : 'success'}>{r.certificateRevoked ? 'Certificate revoked' : 'Certified'}</Badge>
                ) : r.passed ? (
                  <Badge tone="success">Passed</Badge>
                ) : (
                  <Badge>In progress</Badge>
                ),
            },
            { id: 'activity', header: 'Last activity', cell: (r) => (r.lastActivityAt ? <DateTime value={r.lastActivityAt} format="relative" /> : '–') },
          ]}
        />
        {q.data && <Pagination page={page} pageSize={25} total={q.data.total} onPageChange={setPage} label="Learner pages" />}
      </CardBody>
    </Card>
  );
}

function Videos({ detail, enabled }: { detail: AdminCourseDetail; enabled: boolean }) {
  const latest = detail.versions.find((v) => v.isLatest);
  const doc = useCourseVersion(detail.summary.id, enabled ? latest?.id : undefined);
  if (doc.isPending) return <Skeleton height={200} />;
  if (doc.isError) return <QueryError error={doc.error} onRetry={() => void doc.refetch()} />;
  const videoLessons = doc.data.document.modules.flatMap((m) => m.lessons).filter((l) => l.type === 'video');
  if (videoLessons.length === 0)
    return (
      <Card>
        <CardBody>
          <p>This course has no video lessons. Change a lesson’s type to video in the course editor to add one.</p>
        </CardBody>
      </Card>
    );
  return (
    <div className="stack">
      <p className="text-muted">
        Produce each video from its narration script (ElevenLabs voice, HeyGen avatar), then upload the MP4 (up to 50 MB), a poster image and
        WebVTT captions, or paste https URLs. Saving creates a new version of the latest content.
      </p>
      {videoLessons.map((l) => (
        <VideoForm key={l.slug} detail={detail} lessonSlug={l.slug} title={l.title} script={l.video?.script ?? ''} initial={l.video} />
      ))}
    </div>
  );
}

function VideoForm({
  detail,
  lessonSlug,
  title,
  script,
  initial,
}: {
  detail: AdminCourseDetail;
  lessonSlug: string;
  title: string;
  script: string;
  initial: { src: string | null; poster: string | null; captions: string | null } | null;
}) {
  const m = useCourseMutations(detail.summary.id);
  const toast = useToast();
  const [src, setSrc] = useState(initial?.src ?? '');
  const [poster, setPoster] = useState(initial?.poster ?? '');
  const [captions, setCaptions] = useState(initial?.captions ?? '');
  const [publish, setPublish] = useState(detail.summary.status === 'Published');
  const [uploading, setUploading] = useState<string | null>(null);

  const upload = async (file: File | undefined, set: (url: string) => void, field: string) => {
    if (!file) return;
    setUploading(field);
    try {
      const stored = await uploadMedia(file);
      set(stored.url);
      toast.success('Uploaded', file.name);
    } catch (e) {
      toast.error('Upload failed', adminErrorMessage(e));
    } finally {
      setUploading(null);
    }
  };

  return (
    <Card as="section" aria-label={`Video for ${title}`}>
      <CardHeader title={title} headingLevel={3} description={`Lesson: ${lessonSlug}`} />
      <CardBody className="stack">
        <details>
          <summary>Narration script</summary>
          <p className="text-small">{script}</p>
        </details>
        {(
          [
            ['Video (MP4 or https URL)', src, setSrc, 'src'],
            ['Poster image', poster, setPoster, 'poster'],
            ['Captions (WebVTT)', captions, setCaptions, 'captions'],
          ] as const
        ).map(([label, value, set, field]) => (
          <FormField key={field} label={label} hint={uploading === field ? 'Uploading…' : undefined}>
            <Input value={value} onChange={(e) => set(e.target.value)} placeholder="/api/v1/files/… or https://…" />
          </FormField>
        ))}
        <div className="lx-actions">
          {(
            [
              ['Upload video', setSrc, 'video/mp4', 'src'],
              ['Upload poster', setPoster, 'image/png,image/jpeg,image/webp', 'poster'],
              ['Upload captions', setCaptions, '.vtt,text/vtt', 'captions'],
            ] as const
          ).map(([label, set, accept, field]) => (
            <label key={field} className="ui-button ui-button--secondary ui-button--sm">
              {label}
              <input
                type="file"
                accept={accept}
                className="visually-hidden"
                onChange={(e) => void upload(e.target.files?.[0], set, field)}
              />
            </label>
          ))}
        </div>
        <Switch checked={publish} onCheckedChange={setPublish} label="Publish the new version right away" />
        <div>
          <Button
            loading={m.video.isPending}
            onClick={() =>
              m.video.mutate(
                {
                  lessonSlug,
                  src: src || null,
                  poster: poster || null,
                  captions: captions || null,
                  publish,
                  concurrencyStamp: detail.summary.concurrencyStamp,
                },
                {
                  onSuccess: () => toast.success('Video saved', publish ? 'Published as a new version.' : 'Saved as a new draft version.'),
                  onError: (e) => toast.error('The video wasn’t saved', adminErrorMessage(e)),
                },
              )
            }
          >
            Save video
          </Button>
        </div>
      </CardBody>
    </Card>
  );
}
