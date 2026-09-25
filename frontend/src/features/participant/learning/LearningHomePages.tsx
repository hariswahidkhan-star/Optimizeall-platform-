import { Award, BookOpenCheck, Clock, Download, ExternalLink, GraduationCap, Library, Linkedin, PlayCircle, Share2 } from 'lucide-react';
import { Link, useParams } from 'react-router-dom';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { CopyField } from '@/components/ui/CopyField';
import { DashboardCell, DashboardGrid, StatGrid } from '@/components/ui/Dashboard';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { PageHeader } from '@/components/ui/PageHeader';
import { ProgressRing } from '@/components/ui/Progress';
import { Skeleton } from '@/components/ui/Skeleton';
import { Stat } from '@/components/ui/Stat';
import { Badge } from '@/components/ui/Badge';
import {
  formatMinutes,
  useLearningDashboard,
  useMyCertificate,
  type EnrolmentCard,
  type MyCertificate,
} from '@/features/learning/api';
import { MyCatalog } from '@/features/learning/components/CatalogBrowser';
import { BadgeImage, CategoryTag, CourseCard } from '@/features/learning/components/CourseCard';
import '@/features/learning/learning.css';

export const learningPaths = {
  home: '/app/learning',
  catalog: '/app/learning/catalog',
  course: (slug: string) => `/app/learning/courses/${encodeURIComponent(slug)}`,
  lesson: (slug: string, lesson: string) =>
    `/app/learning/courses/${encodeURIComponent(slug)}/lessons/${encodeURIComponent(lesson)}`,
  exam: (slug: string) => `/app/learning/courses/${encodeURIComponent(slug)}/exam`,
  attempt: (id: string) => `/app/learning/attempts/${encodeURIComponent(id)}`,
  certificate: (id: string) => `/app/learning/certificates/${encodeURIComponent(id)}`,
};

export function ContinueCard({ item, headingLevel = 2 }: { item: EnrolmentCard; headingLevel?: 2 | 3 }) {
  const Heading = `h${headingLevel}` as const;
  const to = item.nextLessonSlug ? learningPaths.lesson(item.course.slug, item.nextLessonSlug) : learningPaths.course(item.course.slug);
  return (
    <Card className="lx-continue">
      <CardBody>
        <div className="lx-continue__row">
          <ProgressRing value={item.progressPercent} label={`${item.course.title}: ${item.progressPercent}% complete`} size={76} />
          <div className="lx-continue__text">
            <p className="lx-continue__kicker">Continue where you left off</p>
            <Heading className="lx-continue__title">
              <Link to={learningPaths.course(item.course.slug)}>{item.course.title}</Link>
            </Heading>
            <p className="lx-muted">
              {item.completedLessons} of {item.lessonCount} lessons
              {item.nextLessonTitle ? ` · Next: ${item.nextLessonTitle}` : ''}
            </p>
          </div>
          <ButtonLink to={item.examUnlocked && !item.passed ? learningPaths.exam(item.course.slug) : to} leadingIcon={<PlayCircle aria-hidden="true" />}>
            {item.examUnlocked && !item.passed ? 'Take the assessment' : 'Resume'}
          </ButtonLink>
        </div>
      </CardBody>
    </Card>
  );
}

function EnrolmentRow({ item }: { item: EnrolmentCard }) {
  return (
    <li className="lx-row">
      <ProgressRing value={item.progressPercent} label={`${item.course.title}: ${item.progressPercent}% complete`} size={48} strokeWidth={5} />
      <div className="lx-row__text">
        <Link to={learningPaths.course(item.course.slug)} className="lx-row__title">
          {item.course.title}
        </Link>
        <span className="lx-muted">
          {item.completedLessons}/{item.lessonCount} lessons
          {item.bestScore !== null ? ` · best score ${item.bestScore}%` : ''}
        </span>
      </div>
      <CategoryTag category={item.course.category} />
    </li>
  );
}

export function CertificateRow({ cert }: { cert: MyCertificate }) {
  return (
    <li className="lx-row">
      <BadgeImage src={cert.links.badgeImageUrl} size={48} />
      <div className="lx-row__text">
        <Link to={learningPaths.certificate(cert.id)} className="lx-row__title">
          {cert.badgeName}
        </Link>
        <span className="lx-muted">
          {cert.courseTitle} · <DateTime value={cert.issuedAt} format="date" />
        </span>
      </div>
      {cert.revoked ? <Badge tone="danger">Revoked</Badge> : <Badge tone="success">Valid</Badge>}
    </li>
  );
}

/** "My learning": stats, continue, in-progress courses, recommendations and certificates. */
export function LearningHomePage() {
  const q = useLearningDashboard();
  return (
    <div className="pp-page ui-dash lx-page">
      <PageHeader
        eyebrow="Optimize All Academy"
        title="My learning"
        description="Free courses in sales, marketing, SEO and AI — with certificates you can add to LinkedIn."
        actions={
          <ButtonLink to={learningPaths.catalog} variant="secondary" leadingIcon={<Library aria-hidden="true" />}>
            Browse all courses
          </ButtonLink>
        }
      />
      {q.isPending && (
        <div aria-busy="true" className="stack">
          <span className="visually-hidden" role="status">
            Loading your learning…
          </span>
          <Skeleton height={96} radius="var(--radius-xl)" />
          <Skeleton height={220} radius="var(--radius-xl)" />
        </div>
      )}
      {q.isError && (
        <Card flat>
          <ErrorState error={q.error} title="Your learning isn’t available right now" onRetry={() => void q.refetch()} />
        </Card>
      )}
      {q.data && (
        <>
          <StatGrid strip>
            <Stat label="Courses in progress" value={q.data.stats.inProgress} measurement="Count" icon={<BookOpenCheck />} />
            <Stat label="Courses completed" value={q.data.stats.completed} measurement="Count" icon={<GraduationCap />} />
            <Stat label="Certificates" value={q.data.stats.certificates} measurement="Count" icon={<Award />} />
            <Stat label="Time learned" value={formatMinutes(q.data.stats.minutesLearned)} measurement="Measured" icon={<Clock />} />
          </StatGrid>

          {q.data.continue && <ContinueCard item={q.data.continue} />}

          {q.data.stats.enrolled === 0 && (
            <Card flat>
              <EmptyState
                icon={<GraduationCap />}
                title="Start your first free course"
                description="Pick a course below. Every course is free and ends with a certificate you can share."
                action={<ButtonLink to={learningPaths.catalog}>Browse courses</ButtonLink>}
              />
            </Card>
          )}

          <DashboardGrid>
            <DashboardCell span={7}>
              <Card as="section" aria-labelledby="inprogress-heading">
                <CardHeader title="In progress" titleId="inprogress-heading" />
                <CardBody flush>
                  {q.data.inProgress.length === 0 ? (
                    <p className="lx-pad lx-muted">Nothing in progress. Enrol in a course to start.</p>
                  ) : (
                    <ul className="lx-rows">
                      {q.data.inProgress.map((e) => (
                        <EnrolmentRow key={e.course.id} item={e} />
                      ))}
                    </ul>
                  )}
                </CardBody>
              </Card>
            </DashboardCell>
            <DashboardCell span={5}>
              <Card as="section" aria-labelledby="certs-heading">
                <CardHeader title="Certificates" titleId="certs-heading" />
                <CardBody flush>
                  {q.data.certificates.length === 0 ? (
                    <p className="lx-pad lx-muted">Pass a course’s final assessment to earn its certificate.</p>
                  ) : (
                    <ul className="lx-rows">
                      {q.data.certificates.map((c) => (
                        <CertificateRow key={c.id} cert={c} />
                      ))}
                    </ul>
                  )}
                </CardBody>
              </Card>
            </DashboardCell>
          </DashboardGrid>

          {q.data.recommended.length > 0 && (
            <section aria-labelledby="rec-heading" className="stack">
              <h2 id="rec-heading" className="ui-dash-head">
                Recommended for you
              </h2>
              <ul className="lx-grid">
                {q.data.recommended.map((c) => (
                  <li key={c.id}>
                    <CourseCard course={c} to={learningPaths.course(c.slug)} />
                  </li>
                ))}
              </ul>
            </section>
          )}

          {q.data.completed.length > 0 && (
            <Card as="section" aria-labelledby="done-heading">
              <CardHeader title="Completed" titleId="done-heading" />
              <CardBody flush>
                <ul className="lx-rows">
                  {q.data.completed.map((e) => (
                    <EnrolmentRow key={e.course.id} item={e} />
                  ))}
                </ul>
              </CardBody>
            </Card>
          )}
        </>
      )}
    </div>
  );
}

export function LearningCatalogPage() {
  return (
    <div className="pp-page ui-dash lx-page">
      <PageHeader
        eyebrow="Optimize All Academy"
        title="All courses"
        description="Free, practical courses with certificates. Filter by topic, level and length."
        breadcrumbs={[{ label: 'My learning', to: learningPaths.home }, { label: 'All courses' }]}
      />
      <MyCatalog linkFor={learningPaths.course} />
    </div>
  );
}

/** One of my certificates: preview, PDF download, LinkedIn "Add to profile" and "Share", verification link, Open Badge. */
export function LearningCertificatePage() {
  const { id = '' } = useParams();
  const q = useMyCertificate(id);
  return (
    <div className="pp-page ui-dash lx-page">
      {q.isPending && <Skeleton height={320} radius="var(--radius-xl)" />}
      {q.isError && (
        <Card flat>
          <ErrorState error={q.error} title="This certificate isn’t available" />
        </Card>
      )}
      {q.data && (
        <>
          <PageHeader
            eyebrow="Certificate"
            title={q.data.badgeName}
            description={q.data.courseTitle}
            breadcrumbs={[{ label: 'My learning', to: learningPaths.home }, { label: q.data.badgeName }]}
            meta={
              q.data.revoked ? (
                <Badge tone="danger">Revoked</Badge>
              ) : (
                <Badge tone="success">Valid · issued <DateTime value={q.data.issuedAt} format="date" /></Badge>
              )
            }
          />
          <DashboardGrid>
            <DashboardCell span={7}>
              <Card>
                <CardBody>
                  <img
                    className="lx-cert-image"
                    src={q.data.links.imageUrl}
                    alt={`Certificate ${q.data.verificationCode}: ${q.data.badgeName}, ${q.data.courseTitle}`}
                  />
                </CardBody>
              </Card>
            </DashboardCell>
            <DashboardCell span={5}>
              <Card as="section" aria-labelledby="share-heading">
                <CardHeader title="Download and share" titleId="share-heading" />
                <CardBody className="stack">
                  {!q.data.revoked && (
                    <div className="lx-actions">
                      <a className="ui-button ui-button--primary ui-button--md" href={q.data.links.pdfUrl} download>
                        <Download aria-hidden="true" /> Download PDF
                      </a>
                      <a
                        className="ui-button ui-button--secondary ui-button--md"
                        href={q.data.links.linkedInAddToProfileUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        data-testid="linkedin-add"
                      >
                        <Linkedin aria-hidden="true" /> Add to LinkedIn profile
                        <span className="visually-hidden"> (opens in a new tab)</span>
                      </a>
                      <a
                        className="ui-button ui-button--secondary ui-button--md"
                        href={q.data.links.linkedInShareUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        data-testid="linkedin-share"
                      >
                        <Share2 aria-hidden="true" /> Share on LinkedIn
                        <span className="visually-hidden"> (opens in a new tab)</span>
                      </a>
                    </div>
                  )}
                  <CopyField label="Verification link" value={q.data.links.verificationUrl} />
                  <CopyField label="Credential ID" value={q.data.verificationCode} />
                  <ul className="lx-links">
                    <li>
                      <Link to={`/verify/certificates/${q.data.id}`}>Open the public verification page</Link>
                    </li>
                    <li>
                      <a href={q.data.links.openBadgeAssertionUrl} target="_blank" rel="noopener noreferrer">
                        Open Badge 2.0 assertion (JSON) <ExternalLink aria-hidden="true" className="lx-inline-icon" />
                        <span className="visually-hidden"> (opens in a new tab)</span>
                      </a>
                    </li>
                    <li>
                      <a href={q.data.links.imageUrl} download>
                        Download as image (SVG)
                      </a>
                    </li>
                  </ul>
                  {q.data.skills.length > 0 && (
                    <>
                      <h3 className="lx-subhead">Skills</h3>
                      <ul className="lx-skills">
                        {q.data.skills.map((s) => (
                          <li key={s}>{s}</li>
                        ))}
                      </ul>
                    </>
                  )}
                </CardBody>
              </Card>
            </DashboardCell>
          </DashboardGrid>
        </>
      )}
    </div>
  );
}
