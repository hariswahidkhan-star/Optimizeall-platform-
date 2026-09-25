import clsx from 'clsx';
import { Award, CheckCircle2, ChevronDown, Circle, Clock, FileText, PlayCircle, Target, Timer } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { Markdown } from '@/features/public/site/Markdown';
import { formatMinutes, type CourseDetail } from '../api';
import { BadgeImage, CategoryTag, categoryClass, LevelTag } from './CourseCard';

export interface CourseViewProps {
  course: CourseDetail;
  /** Link to a lesson (public or portal route). */
  lessonLink: (lessonSlug: string) => string;
  /** Link to a prerequisite course. */
  courseLink: (slug: string) => string;
  /** Primary actions in the hero (enrol, continue, sign-up CTA). */
  actions: ReactNode;
  /** Progress panel under the hero actions (portal). */
  progress?: ReactNode;
  completedLessons?: ReadonlySet<string>;
  /** Extra content in the side column (exam panel). */
  aside?: ReactNode;
}

/** Course page layout shared by the public academy and the participant portal. */
export function CourseView({ course, lessonLink, courseLink, actions, progress, completedLessons, aside }: CourseViewProps) {
  const card = course.card;
  return (
    <div className={clsx('lx-course', categoryClass(card.category))}>
      <section className="lx-hero" aria-labelledby="course-title">
        <div className="lx-hero__text">
          <p className="lx-hero__eyebrow">
            <CategoryTag category={card.category} /> <span>Optimize All Academy · Free course</span>
          </p>
          <h1 id="course-title" className="lx-hero__title">
            {card.title}
          </h1>
          <p className="lx-hero__subtitle">{card.subtitle}</p>
          <ul className="lx-hero__facts" aria-label="Course facts">
            <li>
              <LevelTag level={card.level} />
            </li>
            <li>
              <Clock aria-hidden="true" /> {formatMinutes(card.estimatedMinutes)}
            </li>
            <li>
              <FileText aria-hidden="true" /> {card.lessonCount} lessons in {card.moduleCount} modules
            </li>
            <li>
              <Award aria-hidden="true" /> Certificate + LinkedIn badge
            </li>
          </ul>
          <div className="lx-hero__actions">{actions}</div>
          {progress}
        </div>
        <div className="lx-hero__badge">
          <BadgeImage src={course.badge.imageUrl} alt={`${course.badge.name} badge`} size={180} />
        </div>
      </section>

      <div className="lx-course__grid">
        <div className="lx-course__main">
          <Card as="section" aria-labelledby="about-heading">
            <CardHeader title="About this course" titleId="about-heading" />
            <CardBody>
              <Markdown source={course.description} minLevel={3} />
            </CardBody>
          </Card>

          <Card as="section" aria-labelledby="outcomes-heading">
            <CardHeader title="What you’ll be able to do" titleId="outcomes-heading" />
            <CardBody>
              <ul className="lx-outcomes">
                {course.outcomes.map((o) => (
                  <li key={o}>
                    <Target aria-hidden="true" />
                    <span>{o}</span>
                  </li>
                ))}
              </ul>
              {course.card.skills.length > 0 && (
                <>
                  <h3 className="lx-subhead">Skills</h3>
                  <ul className="lx-skills" aria-label="Skills">
                    {course.card.skills.map((s) => (
                      <li key={s}>{s}</li>
                    ))}
                  </ul>
                </>
              )}
            </CardBody>
          </Card>

          <Card as="section" aria-labelledby="syllabus-heading">
            <CardHeader
              title="Syllabus"
              titleId="syllabus-heading"
              description={`${course.modules.length} modules · ${card.lessonCount} lessons · final assessment`}
            />
            <CardBody flush>
              <ol className="lx-syllabus">
                {course.modules.map((m, i) => (
                  <SyllabusModule
                    key={m.slug}
                    index={i}
                    module={m}
                    lessonLink={lessonLink}
                    completed={completedLessons}
                    defaultOpen={i === 0}
                  />
                ))}
              </ol>
            </CardBody>
          </Card>
        </div>

        <aside className="lx-course__aside" aria-label="Certificate and assessment">
          <Card as="section" aria-labelledby="cert-heading" className="lx-cert-preview">
            <CardHeader title="Your certificate" titleId="cert-heading" headingLevel={2} />
            <CardBody>
              <div className="lx-cert-mini" aria-hidden="true">
                <BadgeImage src={course.badge.imageUrl} size={72} />
                <div>
                  <p className="lx-cert-mini__kicker">Certificate of achievement</p>
                  <p className="lx-cert-mini__name">Your name</p>
                  <p className="lx-cert-mini__course">{card.title}</p>
                </div>
              </div>
              <p>
                Earn the <strong>{course.badge.name}</strong> badge: {course.badge.description}
              </p>
              <p className="lx-muted">{course.badge.criteria}</p>
              <p className="lx-muted">
                Verifiable online, downloadable as PDF, shareable as an Open Badge and addable to your LinkedIn profile.
              </p>
            </CardBody>
          </Card>
          <Card as="section" aria-labelledby="exam-heading">
            <CardHeader title="Final assessment" titleId="exam-heading" />
            <CardBody>
              <ul className="lx-facts">
                <li>
                  <FileText aria-hidden="true" /> {course.exam.questionCount} questions drawn from a larger pool
                </li>
                <li>
                  <Timer aria-hidden="true" /> {course.exam.timeLimitMinutes} minutes
                </li>
                <li>
                  <Award aria-hidden="true" /> Pass mark {course.exam.passingScore}%
                </li>
                <li>
                  <Circle aria-hidden="true" /> Up to {course.exam.maxAttemptsPerDay} attempts per 24 hours
                </li>
              </ul>
            </CardBody>
          </Card>
          {aside}
          {course.prerequisites.length > 0 && (
            <Card as="section" aria-labelledby="prereq-heading">
              <CardHeader title="Recommended first" titleId="prereq-heading" />
              <CardBody>
                <ul className="lx-links">
                  {course.prerequisites.map((p) => (
                    <li key={p.slug}>
                      <Link to={courseLink(p.slug)}>{p.title}</Link>
                    </li>
                  ))}
                </ul>
              </CardBody>
            </Card>
          )}
        </aside>
      </div>
    </div>
  );
}

function SyllabusModule({
  index,
  module,
  lessonLink,
  completed,
  defaultOpen,
}: {
  index: number;
  module: CourseDetail['modules'][number];
  lessonLink: (slug: string) => string;
  completed?: ReadonlySet<string>;
  defaultOpen: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const panelId = `module-${module.slug}`;
  const minutes = module.lessons.reduce((sum, l) => sum + l.durationMinutes, 0);
  const done = completed ? module.lessons.filter((l) => completed.has(l.slug)).length : 0;
  return (
    <li className="lx-syllabus__module">
      <h3 className="lx-syllabus__heading">
        <button type="button" aria-expanded={open} aria-controls={panelId} onClick={() => setOpen(!open)} className="lx-syllabus__toggle">
          <span className="lx-syllabus__num" aria-hidden="true">
            {index + 1}
          </span>
          <span className="lx-syllabus__title">
            {module.title}
            <span className="lx-syllabus__meta">
              {module.lessons.length} lessons · {formatMinutes(minutes)}
              {completed ? ` · ${done}/${module.lessons.length} done` : ''}
            </span>
          </span>
          <ChevronDown aria-hidden="true" className="lx-syllabus__chevron" />
        </button>
      </h3>
      <div id={panelId} hidden={!open}>
        <p className="lx-syllabus__summary">{module.summary}</p>
        <ol className="lx-syllabus__lessons">
          {module.lessons.map((l) => {
            const isDone = completed?.has(l.slug);
            return (
              <li key={l.slug}>
                {isDone ? (
                  <CheckCircle2 aria-hidden="true" className="lx-done" />
                ) : l.type === 'Video' ? (
                  <PlayCircle aria-hidden="true" />
                ) : (
                  <FileText aria-hidden="true" />
                )}
                <Link to={lessonLink(l.slug)}>{l.title}</Link>
                <span className="lx-syllabus__dur">
                  {l.type === 'Video' ? 'Video · ' : ''}
                  {l.durationMinutes} min
                  {isDone ? <span className="visually-hidden"> (completed)</span> : null}
                </span>
              </li>
            );
          })}
        </ol>
      </div>
    </li>
  );
}
