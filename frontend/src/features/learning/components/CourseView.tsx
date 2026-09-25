import clsx from 'clsx';
import {
  Award,
  BadgeCheck,
  Check,
  CheckCircle2,
  ChevronDown,
  Clock,
  FileText,
  PlayCircle,
  RotateCcw,
  Timer,
} from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode, type RefObject } from 'react';
import { Link } from 'react-router-dom';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { PartnerLinksProvider } from '@/features/public/partners/PartnerLinksContext';
import { Markdown } from '@/features/public/site/Markdown';
import { courseKeywords, formatMinutes, type CourseDetail } from '../api';
import { LearnSlot } from './LearnSlot';
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

const PHONE_QUERY = '(max-width: 767px)';

/**
 * True on phones once the hero's actions have scrolled out of view: the course page then shows a compact sticky bar with
 * the same actions, so enrolling / resuming is always one tap away. Never on wider screens (the actions stay near), and
 * never where the browser lacks IntersectionObserver.
 */
function useStickyActions(target: RefObject<HTMLElement | null>): boolean {
  const [show, setShow] = useState(false);
  useEffect(() => {
    const el = target.current;
    if (!el || typeof IntersectionObserver === 'undefined' || typeof window.matchMedia !== 'function') return;
    const phone = window.matchMedia(PHONE_QUERY);
    let visible = true;
    const update = () => setShow(phone.matches && !visible);
    const observer = new IntersectionObserver(([entry]) => {
      // Only once the actions are above the viewport (scrolled past), not while the page is still loading below them.
      visible = entry!.isIntersecting || entry!.boundingClientRect.top > 0;
      update();
    });
    observer.observe(el);
    phone.addEventListener?.('change', update);
    return () => {
      observer.disconnect();
      phone.removeEventListener?.('change', update);
    };
  }, [target]);
  return show;
}

/** Course page layout shared by the public academy and the participant portal. */
export function CourseView({
  course,
  lessonLink,
  courseLink,
  actions,
  progress,
  completedLessons,
  aside,
}: CourseViewProps) {
  const card = course.card;
  const actionsRef = useRef<HTMLDivElement>(null);
  const sticky = useStickyActions(actionsRef);
  // The learner's next lesson: the first one (in course order) not completed yet.
  const nextLesson = completedLessons
    ? course.modules.flatMap((m) => m.lessons).find((l) => !completedLessons.has(l.slug))?.slug
    : undefined;
  return (
    // Partner links in lesson/course Markdown get rel="sponsored" + UTM tags in the portals too (the public site
    // chrome already provides the rules).
    <PartnerLinksProvider>
      <div className={clsx('lx-course', categoryClass(card.category), sticky && 'has-sticky-cta')}>
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
            <div className="lx-hero__actions" ref={actionsRef}>
              {actions}
            </div>
            {progress}
          </div>
          <figure className="lx-hero__badge">
            <div className="lx-hero__badge-stage">
              <BadgeImage src={course.badge.imageUrl} alt={`${course.badge.name} badge`} size={200} />
            </div>
            <figcaption className="lx-hero__badge-caption">
              <span className="lx-hero__badge-kicker">Earn the badge</span>
              <span className="lx-hero__badge-name">{course.badge.name}</span>
            </figcaption>
          </figure>
        </section>

        <div className="lx-course__grid">
          <div className="lx-course__main">
            <Card as="section" aria-labelledby="about-heading">
              <CardHeader title="About this course" titleId="about-heading" />
              <CardBody className="lx-about">
                <Markdown source={course.description} minLevel={3} />
              </CardBody>
            </Card>

            <Card as="section" aria-labelledby="outcomes-heading">
              <CardHeader title="What you’ll be able to do" titleId="outcomes-heading" />
              <CardBody>
                <ul className="lx-outcomes">
                  {course.outcomes.map((o) => (
                    <li key={o}>
                      <span className="lx-outcomes__check" aria-hidden="true">
                        <Check />
                      </span>
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

            <Card as="section" aria-labelledby="syllabus-heading" className="lx-syllabus-card">
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
                      nextLesson={nextLesson}
                      defaultOpen={nextLesson ? m.lessons.some((l) => l.slug === nextLesson) : i === 0}
                    />
                  ))}
                </ol>
              </CardBody>
            </Card>
            {/* Partner slot (course page). */}
            <LearnSlot
              slot="learn.course"
              keywords={courseKeywords(card.skills, card.category)}
              categories={[card.category]}
            />
          </div>

          <aside className="lx-course__aside" aria-label="Certificate and assessment">
            <Card as="section" aria-labelledby="cert-heading" className="lx-cert-preview">
              <CardHeader title="Your certificate" titleId="cert-heading" headingLevel={2} />
              <CardBody>
                <div className="lx-cert-mini" aria-hidden="true">
                  <div className="lx-cert-mini__rail">
                    <BadgeImage src={course.badge.imageUrl} size={64} />
                  </div>
                  <div className="lx-cert-mini__text">
                    <p className="lx-cert-mini__kicker">Certificate of achievement</p>
                    <p className="lx-cert-mini__name">Your name</p>
                    <p className="lx-cert-mini__course">{card.title}</p>
                  </div>
                </div>
                <p>
                  Earn the <strong>{course.badge.name}</strong> badge: {course.badge.description}
                </p>
                <p className="lx-muted">{course.badge.criteria}</p>
                <p className="lx-muted lx-cert-preview__perks">
                  <BadgeCheck aria-hidden="true" />
                  <span>
                    Verifiable online, downloadable as PDF, shareable as an Open Badge and addable to your LinkedIn
                    profile.
                  </span>
                </p>
              </CardBody>
            </Card>
            <Card as="section" aria-labelledby="exam-heading">
              <CardHeader title="Final assessment" titleId="exam-heading" />
              <CardBody>
                <ul className="lx-facts">
                  <li>
                    <FileText aria-hidden="true" /> {course.exam.questionCount} questions drawn from a larger
                    pool
                  </li>
                  <li>
                    <Timer aria-hidden="true" /> {course.exam.timeLimitMinutes} minutes
                  </li>
                  <li>
                    <Award aria-hidden="true" /> Pass mark {course.exam.passingScore}%
                  </li>
                  <li>
                    <RotateCcw aria-hidden="true" /> Up to {course.exam.maxAttemptsPerDay} attempts per 24 hours
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
        {sticky && (
          <div className="lx-sticky-cta" role="region" aria-label="Course actions">
            <p className="lx-sticky-cta__title">{card.title}</p>
            <div className="lx-sticky-cta__actions">{actions}</div>
          </div>
        )}
      </div>
    </PartnerLinksProvider>
  );
}

function SyllabusModule({
  index,
  module,
  lessonLink,
  completed,
  nextLesson,
  defaultOpen,
}: {
  index: number;
  module: CourseDetail['modules'][number];
  lessonLink: (slug: string) => string;
  completed?: ReadonlySet<string>;
  nextLesson?: string;
  defaultOpen: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const panelId = `module-${module.slug}`;
  const minutes = module.lessons.reduce((sum, l) => sum + l.durationMinutes, 0);
  const done = completed ? module.lessons.filter((l) => completed.has(l.slug)).length : 0;
  const allDone = completed !== undefined && done === module.lessons.length && done > 0;
  return (
    <li className={clsx('lx-syllabus__module', allDone && 'is-done')}>
      <h3 className="lx-syllabus__heading">
        <button
          type="button"
          aria-expanded={open}
          aria-controls={panelId}
          onClick={() => setOpen(!open)}
          className="lx-syllabus__toggle"
        >
          <span className="lx-syllabus__num" aria-hidden="true">
            {allDone ? <Check /> : index + 1}
          </span>
          <span className="lx-syllabus__title">
            <span className="lx-syllabus__name">{module.title}</span>
            <span className="lx-syllabus__meta">
              {module.lessons.length} lessons · {formatMinutes(minutes)}
              {completed ? ` · ${done}/${module.lessons.length} done` : ''}
            </span>
            {completed && (
              <span className="lx-syllabus__bar" aria-hidden="true">
                <span style={{ width: `${(done / Math.max(1, module.lessons.length)) * 100}%` }} />
              </span>
            )}
          </span>
          <ChevronDown aria-hidden="true" className="lx-syllabus__chevron" />
        </button>
      </h3>
      <div id={panelId} hidden={!open} className="lx-syllabus__panel">
        <p className="lx-syllabus__summary">{module.summary}</p>
        <ol className="lx-syllabus__lessons">
          {module.lessons.map((l) => {
            const isDone = completed?.has(l.slug) ?? false;
            const isNext = !isDone && l.slug === nextLesson;
            return (
              <li key={l.slug} className={clsx('lx-lesson-row', isDone && 'is-done', isNext && 'is-current')}>
                <span className="lx-lesson-row__icon" aria-hidden="true">
                  {isDone ? <CheckCircle2 /> : l.type === 'Video' ? <PlayCircle /> : <FileText />}
                </span>
                <span className="lx-lesson-row__main">
                  <Link to={lessonLink(l.slug)} className="lx-lesson-row__link">
                    {l.title}
                  </Link>
                  {isNext && <span className="lx-lesson-row__next">Up next</span>}
                </span>
                <span className="lx-lesson-row__dur">
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
