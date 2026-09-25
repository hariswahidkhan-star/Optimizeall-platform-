import { ArrowRight } from 'lucide-react';
import { Link, useParams } from 'react-router-dom';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card } from '@/components/ui/Card';
import { ErrorState } from '@/components/ui/ErrorState';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton } from '@/components/ui/Skeleton';
import { useMyPath, useMyPaths } from '@/features/learning/api';
import { PathDetailView, PathGrid } from '@/features/learning/components/PathViews';
import '@/features/learning/learning.css';
import '@/features/learning/academy.css';
import { learningPaths } from './LearningHomePages';

/** /app/learning/paths: every learning path with the learner's progress. */
export function LearningPathsPage() {
  const q = useMyPaths();
  const progress = new Map(q.data?.map((p) => [p.card.slug, p.progress]) ?? []);
  return (
    <div className="pp-page ui-dash lx-page">
      <PageHeader
        eyebrow="Optimize All Academy"
        title="Learning paths"
        description="Step-by-step routes of free courses towards a role or goal — a certificate for every course you pass."
      />
      {q.isPending && (
        <div className="lx-path-grid" aria-busy="true">
          {[1, 2, 3, 4].map((n) => (
            <Skeleton key={n} height={300} radius="var(--radius-xl)" />
          ))}
        </div>
      )}
      {q.isError && (
        <Card flat>
          <ErrorState error={q.error} title="Learning paths aren’t available right now" onRetry={() => void q.refetch()} />
        </Card>
      )}
      {q.data && <PathGrid paths={q.data.map((p) => p.card)} linkFor={learningPaths.path} progressFor={(s) => progress.get(s)} headingLevel={2} />}
    </div>
  );
}

/** /app/learning/paths/:pathSlug: the path's timeline with the learner's progress; courses open in the portal. */
export function LearningPathPage() {
  const { pathSlug = '' } = useParams();
  const q = useMyPath(pathSlug);
  if (q.isPending) return <Skeleton height={420} radius="var(--radius-xl)" />;
  if (q.isError)
    return (
      <Card flat>
        <ErrorState error={q.error} title="This learning path isn’t available" onRetry={() => void q.refetch()} />
      </Card>
    );
  const { path, progress } = q.data;
  const next = path.courses.find((c) => c.course.slug === (progress.nextCourseSlug ?? path.courses[0]?.course.slug))?.course;
  return (
    <div className="pp-page lx-page">
      <nav aria-label="Breadcrumb" className="ui-breadcrumbs lx-crumbs">
        <ol>
          <li>
            <Link to={learningPaths.home}>My learning</Link>
          </li>
          <li aria-hidden="true">/</li>
          <li>
            <Link to={learningPaths.paths}>Learning paths</Link>
          </li>
        </ol>
      </nav>
      <PathDetailView
        path={path}
        progress={progress}
        courseLink={learningPaths.course}
        actions={
          next && (
            <ButtonLink to={learningPaths.course(next.slug)} size="lg" trailingIcon={<ArrowRight aria-hidden="true" />} className="lx-cta-glow">
              {progress.started ? `Continue: ${next.title}` : `Start with ${next.title}`}
            </ButtonLink>
          )
        }
      />
    </div>
  );
}
