import clsx from 'clsx';
import { BookOpen, Clock, Layers, Sparkles } from 'lucide-react';
import { Link } from 'react-router-dom';
import { ProgressBar } from '@/components/ui/Progress';
import { CATEGORY_LABELS, formatMinutes, type CourseCard as CourseCardData, type CourseCategory, type CourseLevel } from '../api';

export function categoryClass(category: CourseCategory): string {
  return `lx-cat--${category.toLowerCase()}`;
}

export function CategoryTag({ category }: { category: CourseCategory }) {
  return <span className={clsx('lx-tag', categoryClass(category))}>{CATEGORY_LABELS[category]}</span>;
}

export function LevelTag({ level }: { level: CourseLevel }) {
  const bars = level === 'Beginner' ? 1 : level === 'Intermediate' ? 2 : 3;
  return (
    <span className="lx-level">
      <span className="lx-level__bars" aria-hidden="true">
        {[1, 2, 3].map((n) => (
          <span key={n} className={clsx('lx-level__bar', n <= bars && 'is-on')} />
        ))}
      </span>
      {level}
    </span>
  );
}

/** The course badge image (decorative next to its name unless `alt` is given). */
export function BadgeImage({ src, alt = '', size = 64, className }: { src: string; alt?: string; size?: number; className?: string }) {
  return (
    <img
      className={clsx('lx-badge-img', className)}
      src={src}
      alt={alt}
      width={size}
      height={size}
      loading="lazy"
      decoding="async"
    />
  );
}

export interface CourseCardProps {
  course: CourseCardData;
  /** Where the card links (public /learn/:slug or the portal course page). */
  to: string;
  headingLevel?: 2 | 3;
  progress?: { percent: number; passed: boolean } | null;
}

/** A premium course card: category colour band, badge preview, level, duration and lesson count. */
export function CourseCard({ course, to, headingLevel = 3, progress }: CourseCardProps) {
  const Heading = `h${headingLevel}` as const;
  return (
    <article className={clsx('lx-card', categoryClass(course.category))}>
      <div className="lx-card__band" aria-hidden="true" />
      <div className="lx-card__top">
        <CategoryTag category={course.category} />
        <div className="lx-card__flags">
          {course.isFeatured && (
            <span className="lx-flag">
              <Sparkles aria-hidden="true" /> Featured
            </span>
          )}
          {course.isNew && <span className="lx-flag lx-flag--new">New</span>}
        </div>
      </div>
      <div className="lx-card__main">
        <div className="lx-card__text">
          <Heading className="lx-card__title">
            <Link to={to} className="lx-card__link">
              {course.title}
            </Link>
          </Heading>
          <p className="lx-card__subtitle">{course.subtitle}</p>
        </div>
        <BadgeImage src={course.badgeImageUrl} size={64} className="lx-card__badge" />
      </div>
      <ul className="lx-card__meta" aria-label="Course details">
        <li>
          <LevelTag level={course.level} />
        </li>
        <li>
          <Clock aria-hidden="true" /> {formatMinutes(course.estimatedMinutes)}
        </li>
        <li>
          <BookOpen aria-hidden="true" /> {course.lessonCount} lessons
        </li>
        <li>
          <Layers aria-hidden="true" /> {course.moduleCount} modules
        </li>
      </ul>
      {progress ? (
        progress.passed ? (
          <p className="lx-card__status lx-card__status--done">Completed · certificate earned</p>
        ) : (
          <ProgressBar value={progress.percent} label="Your progress" className="lx-card__progress" />
        )
      ) : (
        <p className="lx-card__status">Free · certificate: {course.badgeName}</p>
      )}
    </article>
  );
}

/** LinkedIn mark (lucide has no brand icons). Decorative: pair it with visible text. */
export function LinkedInIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" width="1em" height="1em" aria-hidden="true" fill="currentColor">
      <path d="M20.45 20.45h-3.56v-5.57c0-1.33-.02-3.04-1.85-3.04-1.85 0-2.14 1.45-2.14 2.94v5.67H9.35V9h3.41v1.56h.05c.48-.9 1.64-1.85 3.37-1.85 3.6 0 4.27 2.37 4.27 5.46v6.28zM5.34 7.43a2.06 2.06 0 1 1 0-4.13 2.06 2.06 0 0 1 0 4.13zM7.12 20.45H3.56V9h3.56v11.45zM22.22 0H1.77C.79 0 0 .77 0 1.73v20.54C0 23.23.79 24 1.77 24h20.45c.98 0 1.78-.77 1.78-1.73V1.73C24 .77 23.2 0 22.22 0z" />
    </svg>
  );
}
