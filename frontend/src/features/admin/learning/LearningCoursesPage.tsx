import '@/features/learning/learning.css';
import { useQueryClient } from '@tanstack/react-query';
import { Award, GraduationCap, Plus } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Badge } from '@/components/ui/Badge';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { FilterBar } from '@/components/ui/FilterBar';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { useToast } from '@/components/ui/toastContext';
import { Permissions } from '@/lib/auth/permissions';
import { CATEGORIES, CATEGORY_LABELS } from '@/features/learning/api';
import { useCan, QueryError } from '../shared/common';
import { adminErrorMessage } from '../shared/errors';
import { useListParams } from '../shared/useListParams';
import { revokeCertificate, useAdminCertificates, useAdminCourses, type AdminCertificate } from './api';

export function LearningCoursesPage() {
  const list = useListParams(['category', 'status']);
  const canManage = useCan(Permissions.LearningManage);
  const query = { search: list.search, ...list.filters, page: list.page, pageSize: list.pageSize };
  const courses = useAdminCourses(query);
  return (
    <>
      <PageHeader
        title="Learning"
        description="Courses, enrolments, pass rates and certificates of the Optimize All Academy."
        actions={
          <>
            <ButtonLink to="certificates" variant="secondary" leadingIcon={<Award />}>
              Certificates
            </ButtonLink>
            {canManage && (
              <ButtonLink to="new" leadingIcon={<Plus />}>
                New course
              </ButtonLink>
            )}
          </>
        }
      />
      <Card>
        <CardBody className="stack">
          <FilterBar
            search={list.search}
            onSearchChange={(search) => list.update({ search })}
            searchPlaceholder="Course title or slug…"
            searchLabel="Search courses"
            filters={[
              { id: 'category', label: 'Category', options: CATEGORIES.map((c) => ({ value: c, label: CATEGORY_LABELS[c] })) },
              { id: 'status', label: 'Status', options: [{ value: 'Published', label: 'Published' }, { value: 'Draft', label: 'Draft' }] },
            ]}
            values={list.filters}
            onFilterChange={(id, value) => list.update({ [id]: value })}
            onReset={list.reset}
          />
          {courses.isError ? (
            <QueryError error={courses.error} onRetry={() => void courses.refetch()} />
          ) : (
            <>
              <DataTable
                caption="Courses"
                rows={courses.data?.items ?? []}
                loading={courses.isPending}
                getRowId={(c) => c.id}
                columns={[
                  {
                    id: 'title',
                    header: 'Course',
                    primary: true,
                    cell: (c) => (
                      <div className="admin-cell-stack">
                        <Link className="ui-link" to={`courses/${c.id}`}>
                          {c.title}
                        </Link>
                        <span className="text-small text-muted">
                          {CATEGORY_LABELS[c.category]} · {c.level} · {c.lessonCount} lessons
                        </span>
                      </div>
                    ),
                  },
                  {
                    id: 'status',
                    header: 'Status',
                    cell: (c) => (
                      <div className="admin-cell-stack">
                        <Badge tone={c.status === 'Published' ? 'success' : 'neutral'}>{c.status}</Badge>
                        {c.packUpdateAvailable && <Badge tone="warning">Pack update</Badge>}
                        {c.isFeatured && <Badge tone="brand">Featured</Badge>}
                      </div>
                    ),
                  },
                  {
                    id: 'version',
                    header: 'Version',
                    cell: (c) => (
                      <span className="text-small">
                        v{c.publishedVersionNumber ?? '–'}
                        {c.latestVersionNumber !== c.publishedVersionNumber ? ` (latest v${c.latestVersionNumber})` : ''} ·{' '}
                        {c.origin === 'Pack' ? 'pack' : 'staff'}
                      </span>
                    ),
                  },
                  { id: 'enrolments', header: 'Enrolled', align: 'right', cell: (c) => c.enrolments },
                  { id: 'completion', header: 'Completion', align: 'right', cell: (c) => `${c.completionRate}%` },
                  { id: 'attempts', header: 'Attempts', align: 'right', cell: (c) => c.attempts },
                  { id: 'pass', header: 'Pass rate', align: 'right', cell: (c) => (c.attempts ? `${c.passRate}%` : '–') },
                  { id: 'avg', header: 'Avg score', align: 'right', cell: (c) => (c.averageScore !== null ? `${c.averageScore}%` : '–') },
                  { id: 'certs', header: 'Certificates', align: 'right', cell: (c) => c.certificates },
                ]}
                emptyState={<EmptyState icon={<GraduationCap />} headingLevel={2} title="No courses match" description="Try clearing the filters." />}
              />
              {courses.data && (
                <Pagination
                  page={list.page}
                  pageSize={list.pageSize}
                  total={courses.data.total}
                  onPageChange={(page) => list.update({ page }, false)}
                  onPageSizeChange={(pageSize) => list.update({ pageSize })}
                  label="Course pages"
                />
              )}
            </>
          )}
        </CardBody>
      </Card>
    </>
  );
}

export function LearningCertificatesPage() {
  const list = useListParams(['revoked']);
  const canCertify = useCan(Permissions.LearningCertify);
  const query = { search: list.search, ...list.filters, page: list.page, pageSize: list.pageSize };
  const certs = useAdminCertificates(query);
  const [revoking, setRevoking] = useState<AdminCertificate | null>(null);
  const toast = useToast();
  const qc = useQueryClient();

  return (
    <>
      <PageHeader
        title="Certificates"
        description="Every certificate issued by the academy. Revocation is audited and shows on the public verification page."
        breadcrumbs={[{ label: 'Learning', to: '/admin/learning' }, { label: 'Certificates' }]}
      />
      <Card>
        <CardBody className="stack">
          <FilterBar
            search={list.search}
            onSearchChange={(search) => list.update({ search })}
            searchPlaceholder="Holder, email, code or course…"
            searchLabel="Search certificates"
            filters={[{ id: 'revoked', label: 'Status', options: [{ value: 'false', label: 'Valid' }, { value: 'true', label: 'Revoked' }] }]}
            values={list.filters}
            onFilterChange={(id, value) => list.update({ [id]: value })}
            onReset={list.reset}
          />
          {certs.isError ? (
            <QueryError error={certs.error} onRetry={() => void certs.refetch()} />
          ) : (
            <>
              <DataTable
                caption="Certificates"
                rows={certs.data?.items ?? []}
                loading={certs.isPending}
                getRowId={(c) => c.id}
                rowLabel={(c) => `${c.verificationCode} ${c.holderName}`}
                rowActions={
                  canCertify
                    ? (c) =>
                        c.revokedAt
                          ? []
                          : [{ id: 'revoke', label: 'Revoke certificate', onSelect: () => setRevoking(c), danger: true }]
                    : undefined
                }
                columns={[
                  {
                    id: 'holder',
                    header: 'Holder',
                    primary: true,
                    cell: (c) => (
                      <div className="admin-cell-stack">
                        <span>{c.holderName}</span>
                        <span className="text-small text-muted">{c.email}</span>
                      </div>
                    ),
                  },
                  {
                    id: 'course',
                    header: 'Course',
                    cell: (c) => (
                      <Link className="ui-link" to={`/admin/learning/courses/${c.courseId}`}>
                        {c.courseTitle}
                      </Link>
                    ),
                  },
                  {
                    id: 'code',
                    header: 'Credential ID',
                    cell: (c) => (
                      <Link className="ui-link" to={`/verify/certificates/${c.id}`}>
                        <code>{c.verificationCode}</code>
                      </Link>
                    ),
                  },
                  { id: 'score', header: 'Score', align: 'right', cell: (c) => (c.score !== null ? `${c.score}%` : c.manual ? 'Manual' : '–') },
                  { id: 'issued', header: 'Issued', cell: (c) => <DateTime value={c.issuedAt} format="date" /> },
                  {
                    id: 'status',
                    header: 'Status',
                    cell: (c) =>
                      c.revokedAt ? (
                        <Badge tone="danger" title={c.revocationReason ?? undefined}>
                          Revoked
                        </Badge>
                      ) : (
                        <Badge tone="success">Valid</Badge>
                      ),
                  },
                ]}
                emptyState={<EmptyState icon={<Award />} headingLevel={2} title="No certificates match" />}
              />
              {certs.data && (
                <Pagination
                  page={list.page}
                  pageSize={list.pageSize}
                  total={certs.data.total}
                  onPageChange={(page) => list.update({ page }, false)}
                  onPageSizeChange={(pageSize) => list.update({ pageSize })}
                  label="Certificate pages"
                />
              )}
            </>
          )}
        </CardBody>
      </Card>
      <ConfirmDialog
        open={!!revoking}
        onClose={() => setRevoking(null)}
        tone="danger"
        title={`Revoke ${revoking?.verificationCode ?? ''}?`}
        description={`${revoking?.holderName ?? ''} will be notified and the verification page will show the certificate as revoked. This cannot be undone.`}
        confirmLabel="Revoke certificate"
        requireReason
        reasonLabel="Reason (kept in the audit log)"
        reasonMinLength={3}
        onConfirm={async ({ reason }) => {
          if (!revoking) return;
          try {
            await revokeCertificate(revoking.id, { reason, confirm: true, concurrencyStamp: revoking.concurrencyStamp });
          } catch (e) {
            throw new Error(adminErrorMessage(e));
          }
          toast.success('Certificate revoked', revoking.verificationCode);
          setRevoking(null);
          void qc.invalidateQueries({ queryKey: ['admin', 'learning'] });
        }}
      />
    </>
  );
}
