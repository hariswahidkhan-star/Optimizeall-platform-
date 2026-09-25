import '../academy.css';
import clsx from 'clsx';
import { ArrowRight, Award, BookOpen, Check, Clock, Compass, Layers, Lock, Route, Target, Users } from 'lucide-react';
import type { CSSProperties, ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ProgressBar, ProgressRing } from '@/components/ui/Progress';
import { Markdown } from '@/features/public/site/Markdown';
import { formatHours, type PathCard as PathCardData, type PathDetail, type PathProgress } from '../api';
import { BadgeImage, CategoryTag, categoryClass, LevelTag } from './CourseCard';
import { stagger, useReveal } from './Motion';

/** The dominant category of a path (its first course's), for the art tint. */
function pathCategoryClass(path: PathCardData): string {
  return path.categories[0] ? categoryClass(path.categories[0]) : 'lx-cat--platform';
}

/** A stack of up to four overlapping course badges ("badges you earn"). */
export function BadgeStack({ badges, size = 44, max = 4 }: { badges: PathCardData['badges']; size?: number; max?: number }) {
  const shown = badges.slice(0, max);
  const more = badges.length - shown.length;
  return (
    <span className="lx-badge-stack" aria-hidden="true">
      {shown.map((b) => (
        <BadgeImage key={b.courseSlug} src={b.imageUrl} size={size} className="lx-badge-stack__img" />
      ))}
      {more > 0 && (
        <span className="lx-badge-stack__more" style={{ inlineSize: size, blockSize: size }}>
          +{more}
        </span>
      )}
    </span>
  );
}

export interface PathCardProps {
  path: PathCardData;
  to: string;
  headingLevel?: 2 | 3;
  progress?: PathProgress | null;
}

/** A learning path card: tinted art with the badge stack, title, subtitle, level / courses / hours, and progress. */
export function PathCard({ path, to, headingLevel = 3, progress }: PathCardProps) {
  const Heading = `h${headingLevel}` as const;
  return (
    <article className={clsx('lx-path-card', pathCategoryClass(path))}>
      <div className="lx-path-card__art">
        <span className="lx-path-card__kicker">
          <Route aria-hidden="true" /> Learning path
        </span>
        <BadgeStack badges={path.badges} />
        {progress?.started && (
          <ProgressRing
            value={progress.progressPercent}
            label={`${path.title}: ${progress.progressPercent}% complete`}
            size={52}
            strokeWidth={5}
            className="lx-path-card__ring"
          />
        )}
      </div>
      <div className="lx-path-card__body">
        <Heading className="lx-path-card__title">
          <Link to={to} className="lx-path-card__link">
            {path.title}
          </Link>
        </Heading>
        <p className="lx-path-card__subtitle">{path.subtitle}</p>
        <ul className="lx-path-card__meta" aria-label="Path details">
          <li>
            <LevelTag level={path.level} />
          </li>
          <li>
            <Layers aria-hidden="true" /> {path.courseCount} courses
          </li>
          <li>
            <Clock aria-hidden="true" /> {formatHours(path.totalMinutes)}
          </li>
          <li>
            <Award aria-hidden="true" /> {path.badges.length} badges
          </li>
        </ul>
      </div>
      <div className="lx-path-card__foot">
        {progress?.started ? (
          <span className="lx-path-card__status">
            {progress.completedCourses} of {progress.courseCount} courses completed
          </span>
        ) : (
          <span className="lx-path-card__status">
            <span className="lx-free">Free</span> Certificate for every course
          </span>
        )}
        <ArrowRight aria-hidden="true" className="lx-path-card__arrow" />
      </div>
    </article>
  );
}

/** A grid of path cards with reveal-on-scroll. */
export function PathGrid({
  paths,
  linkFor,
  progressFor,
  headingLevel = 3,
  label = 'Learning paths',
}: {
  paths: PathCardData[];
  linkFor: (slug: string) => string;
  progressFor?: (slug: string) => PathProgress | null | undefined;
  headingLevel?: 2 | 3;
  label?: string;
}) {
  const ref = useReveal<HTMLUListElement>(paths.length);
  return (
    <ul className="lx-path-grid lx-reveal" aria-label={label} ref={ref}>
      {paths.map((p, i) => (
        <li key={p.slug} style={stagger(i)}>
          <PathCard path={p} to={linkFor(p.slug)} headingLevel={headingLevel} progress={progressFor?.(p.slug)} />
        </li>
      ))}
    </ul>
  );
}

export interface PathDetailViewProps {
  path: PathDetail;
  progress?: PathProgress | null;
  courseLink: (slug: string) => string;
  /** Hero actions (start / continue / sign up). */
  actions: ReactNode;
  /** Shown under the hero (e.g. sign-up call to action). */
  aside?: ReactNode;
}

/**
 * A learning path page: hero (title, promise, facts, overall progress, badge row), then the ordered course timeline
 * — each step with its badge, what it covers, the learner's progress and a link — and the path's outcomes/audience.
 */
export function PathDetailView({ path, progress, courseLink, actions, aside }: PathDetailViewProps) {
  const card = path.card;
  const timelineRef = useReveal<HTMLOListElement>(path.courses.length);
  const byCourse = new Map(progress?.courses.map((c) => [c.slug, c]) ?? []);
  const doneCount = progress?.completedCourses ?? 0;
  // Fill of the timeline's spine: up to the last passed course.
  const lastDone = path.courses.reduce((last, c, i) => (byCourse.get(c.course.slug)?.passed ? i : last), -1);
  const fill = path.courses.length <= 1 ? 0 : Math.max(0, lastDone) / (path.courses.length - 1);
  return (
    <div className={clsx('lx-path', pathCategoryClass(card))}>
      <section className="lx-path-hero" aria-labelledby="path-title">
        <div className="lx-path-hero__glow" aria-hidden="true" />
        <div className="lx-path-hero__text">
          <p className="lx-hero__eyebrow">
            <span className="lx-tag">
              <Route aria-hidden="true" className="lx-inline-icon" /> Learning path
            </span>{' '}
            <span>Optimize All Academy · Free</span>
          </p>
          <h1 id="path-title" className="lx-hero__title">
            {card.title}
          </h1>
          <p className="lx-hero__subtitle">{card.subtitle}</p>
          <ul className="lx-hero__facts" aria-label="Path facts">
            <li>
              <LevelTag level={card.level} />
            </li>
            <li>
              <Layers aria-hidden="true" /> {card.courseCount} courses in order
            </li>
            <li>
              <BookOpen aria-hidden="true" /> {card.lessonCount} lessons
            </li>
            <li>
              <Clock aria-hidden="true" /> {formatHours(card.totalMinutes)} in total
            </li>
            <li>
              <Award aria-hidden="true" /> {card.badges.length} certificates
            </li>
          </ul>
          <div className="lx-hero__actions">{actions}</div>
          {progress?.started && (
            <div className="lx-hero__progress">
              <ProgressBar
                value={progress.progressPercent}
                label="Path progress"
                valueText={`${doneCount} of ${progress.courseCount} courses completed (${progress.progressPercent}%)`}
              />
            </div>
          )}
        </div>
        <figure className="lx-path-hero__badges">
          <div className="lx-path-hero__badge-grid">
            {card.badges.slice(0, 6).map((b, i) => {
              const earned = byCourse.get(b.courseSlug)?.passed ?? false;
              return (
                <span key={b.courseSlug} className={clsx('lx-path-hero__badge', earned && 'is-earned')} style={stagger(i)}>
                  <BadgeImage src={b.imageUrl} size={72} alt="" />
                </span>
              );
            })}
          </div>
          <figcaption className="lx-hero__badge-caption">
            <span className="lx-hero__badge-kicker">Badges along the way</span>
            <span className="lx-hero__badge-name">
              {progress?.started ? `${doneCount} of ${card.badges.length} earned` : `${card.badges.length} verifiable badges`}
            </span>
          </figcaption>
        </figure>
      </section>

      {aside}

      <div className="lx-path__grid">
        <section className="lx-path__main" aria-labelledby="timeline-heading">
          <h2 id="timeline-heading" className="lx-section-title">
            Your route, course by course
          </h2>
          <ol className="lx-timeline lx-reveal" ref={timelineRef} style={{ ['--lx-fill' as string]: fill } as CSSProperties}>
            {path.courses.map(({ position, course }, i) => {
              const p = byCourse.get(course.slug);
              const isNext = progress?.nextCourseSlug === course.slug;
              const state = p?.passed ? 'done' : isNext && progress?.started ? 'next' : p?.enrolled ? 'active' : 'todo';
              return (
                <li key={course.slug} className={clsx('lx-step', `is-${state}`, categoryClass(course.category))} style={stagger(i)}>
                  <span className="lx-step__node" aria-hidden="true">
                    {p?.passed ? <Check /> : position}
                  </span>
                  <article className="lx-step__card">
                    <div className="lx-step__badge">
                      <BadgeImage src={course.badgeImageUrl} size={64} />
                      {p?.passed && <span className="lx-step__earned">Earned</span>}
                    </div>
                    <div className="lx-step__text">
                      <p className="lx-step__kicker">
                        Step {position} · <CategoryTag category={course.category} />
                        {isNext && <span className="lx-step__next">{progress?.started ? 'Up next' : 'Start here'}</span>}
                      </p>
                      <h3 className="lx-step__title">
                        <Link to={courseLink(course.slug)} className="lx-step__link">
                          {course.title}
                        </Link>
                      </h3>
                      <p className="lx-step__subtitle">{course.subtitle}</p>
                      <p className="lx-step__meta">
                        <LevelTag level={course.level} /> · {course.lessonCount} lessons · {formatHours(course.estimatedMinutes)} · badge:{' '}
                        {course.badgeName}
                      </p>
                      {p?.enrolled && !p.passed && (
                        <ProgressBar value={p.progressPercent} label="Your progress" className="lx-step__progress" />
                      )}
                      {p?.passed && <p className="lx-ok">Completed · certificate earned</p>}
                    </div>
                  </article>
                </li>
              );
            })}
          </ol>
          <p className="lx-muted lx-path__note">
            <Lock aria-hidden="true" className="lx-inline-icon" /> Nothing is locked: take the courses in order or jump to the
            ones you need. More courses are added to this path as they are published.
          </p>
        </section>

        <aside className="lx-path__aside" aria-label="About this path">
          <section className="lx-panel" aria-labelledby="path-about">
            <h2 id="path-about" className="lx-panel__title">
              <Compass aria-hidden="true" /> About this path
            </h2>
            <Markdown source={path.description} minLevel={3} />
          </section>
          {path.outcomes.length > 0 && (
            <section className="lx-panel" aria-labelledby="path-outcomes">
              <h2 id="path-outcomes" className="lx-panel__title">
                <Target aria-hidden="true" /> What you’ll be able to do
              </h2>
              <ul className="lx-outcomes">
                {path.outcomes.map((o) => (
                  <li key={o}>
                    <span className="lx-outcomes__check" aria-hidden="true">
                      <Check />
                    </span>
                    <span>{o}</span>
                  </li>
                ))}
              </ul>
            </section>
          )}
          {path.audience.length > 0 && (
            <section className="lx-panel" aria-labelledby="path-audience">
              <h2 id="path-audience" className="lx-panel__title">
                <Users aria-hidden="true" /> Who it’s for
              </h2>
              <ul className="lx-audience">
                {path.audience.map((a) => (
                  <li key={a}>{a}</li>
                ))}
              </ul>
            </section>
          )}
        </aside>
      </div>
    </div>
  );
}
