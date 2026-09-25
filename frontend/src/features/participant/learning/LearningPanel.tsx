import { GraduationCap } from 'lucide-react';
import { Link } from 'react-router-dom';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ProgressRing } from '@/components/ui/Progress';
import { useLearningDashboard } from '@/features/learning/api';
import { BadgeImage, CourseCard } from '@/features/learning/components/CourseCard';
import '@/features/learning/learning.css';
import { ContinueCard, learningPaths } from './LearningHomePages';

/**
 * The "Learning" panel of the participant home: continue where you left off, progress rings of courses in progress,
 * earned certificates and recommended courses. Hidden while loading or when the API fails (the home page stays usable).
 */
export function LearningPanel() {
  const q = useLearningDashboard();
  if (!q.data) return null;
  const d = q.data;
  const others = d.inProgress.filter((e) => e.course.id !== d.continue?.course.id).slice(0, 3);
  return (
    <section aria-labelledby="home-learning-heading" className="stack">
      <div className="lx-panel-head">
        <h2 id="home-learning-heading" className="ui-dash-head">
          Learning
        </h2>
        <Link to={learningPaths.home}>My learning</Link>
      </div>
      {d.continue && <ContinueCard item={d.continue} headingLevel={3} />}
      {(others.length > 0 || d.certificates.length > 0) && (
        <Card className="lx-home-panel">
          <CardHeader title="Your progress" headingLevel={3} />
          <CardBody>
            {others.length > 0 && (
              <ul className="lx-home-rings" aria-label="Courses in progress">
                {others.map((e) => (
                  <li key={e.course.id}>
                    <ProgressRing value={e.progressPercent} label={`${e.course.title}: ${e.progressPercent}% complete`} size={52} strokeWidth={5} />
                    <Link to={learningPaths.course(e.course.slug)}>{e.course.title}</Link>
                  </li>
                ))}
              </ul>
            )}
            {d.certificates.length > 0 && (
              <ul className="lx-home-rings" aria-label="Certificates">
                {d.certificates.slice(0, 3).map((c) => (
                  <li key={c.id}>
                    <BadgeImage src={c.links.badgeImageUrl} size={52} />
                    <Link to={learningPaths.certificate(c.id)}>{c.badgeName}</Link>
                  </li>
                ))}
              </ul>
            )}
          </CardBody>
        </Card>
      )}
      {d.recommended.length > 0 && (
        <>
          <h3 className="lx-subhead">{d.stats.enrolled === 0 ? 'Start a free course' : 'Recommended courses'}</h3>
          <ul className="lx-grid">
            {d.recommended.slice(0, 3).map((c) => (
              <li key={c.id}>
                <CourseCard course={c} to={learningPaths.course(c.slug)} headingLevel={3} />
              </li>
            ))}
          </ul>
        </>
      )}
      {d.stats.enrolled === 0 && d.recommended.length === 0 && (
        <Card flat>
          <CardBody>
            <ButtonLink to={learningPaths.catalog} leadingIcon={<GraduationCap aria-hidden="true" />}>
              Browse free courses
            </ButtonLink>
          </CardBody>
        </Card>
      )}
    </section>
  );
}
