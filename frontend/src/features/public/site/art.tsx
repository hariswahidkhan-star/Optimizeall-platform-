import clsx from 'clsx';
import { BadgeCheck, Check, PlayCircle } from 'lucide-react';
import { useId, type CSSProperties, type ReactNode } from 'react';
import type { CourseCategory } from '@/features/learning/api';
import { useInViewClass } from './motion';
import './marketing.css';

/**
 * Decorative illustrations for the marketing pages, drawn with HTML, CSS and inline SVG only (no images, CSP-safe).
 * Everything here is aria-hidden: the words that matter are in the page copy. Motion lives in marketing.css, runs only
 * while the art is on screen (`is-inview`) and is switched off entirely for reduced motion.
 */

const catClass = (category: CourseCategory | null | undefined) => `lx-cat--${(category ?? 'Ai').toLowerCase()}`;

/** The brand hexagon badge used on certificates (same silhouette as the academy badges). */
function HexBadge({ className }: { className?: string }) {
  const id = useId().replace(/:/g, '');
  return (
    <svg className={className} viewBox="0 0 64 72" aria-hidden="true" focusable="false">
      <defs>
        <linearGradient id={`${id}f`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#343f87" />
          <stop offset="1" stopColor="#1f2659" />
        </linearGradient>
        <linearGradient id={`${id}r`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#fdcc52" />
          <stop offset="1" stopColor="#e89a06" />
        </linearGradient>
      </defs>
      <path d="M32 2 60 18v36L32 70 4 54V18Z" fill={`url(#${id}f)`} stroke={`url(#${id}r)`} strokeWidth="3" />
      <path d="M32 12 51 23v26L32 60 13 49V23Z" fill="none" stroke="rgb(255 255 255 / 0.18)" strokeWidth="1.5" />
      <path d="m22 36 7 7 14-15" fill="none" stroke="#fcb31e" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

/** The marketing dashboard window of the hero visuals (chart lines draw in, bars grow). */
function Dashboard() {
  return (
    <div className="oa-hv__dash">
      <div className="oa-hv__bar">
        <i />
        <i />
        <i />
        <span>Growth overview</span>
        <em>Live</em>
      </div>
      <div className="oa-hv__kpis">
        {['Organic traffic', 'Qualified leads', 'Revenue'].map((label, i) => (
          <div key={label} className="oa-hv__kpi" style={{ '--i': i } as CSSProperties}>
            <span>{label}</span>
            <b />
            <svg viewBox="0 0 60 18" preserveAspectRatio="none">
              <path d={['M0 15 12 12 24 13 36 7 48 8 60 2', 'M0 14 12 13 24 9 36 10 48 5 60 4', 'M0 16 12 11 24 12 36 8 48 6 60 3'][i]} />
            </svg>
          </div>
        ))}
      </div>
      <div className="oa-hv__chart">
        <svg viewBox="0 0 300 110" preserveAspectRatio="none">
          <defs>
            <linearGradient id="oa-hv-area" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stopColor="var(--oa-hv-line)" stopOpacity="0.28" />
              <stop offset="1" stopColor="var(--oa-hv-line)" stopOpacity="0" />
            </linearGradient>
          </defs>
          <g className="oa-hv__grid">
            <line x1="0" y1="27" x2="300" y2="27" />
            <line x1="0" y1="55" x2="300" y2="55" />
            <line x1="0" y1="83" x2="300" y2="83" />
          </g>
          <path className="oa-hv__area" d="M0 92 C30 88 45 80 70 78 S120 70 140 58 S190 52 210 40 S260 26 300 12 V110 H0Z" fill="url(#oa-hv-area)" />
          <path className="oa-hv__line" pathLength={1} d="M0 92 C30 88 45 80 70 78 S120 70 140 58 S190 52 210 40 S260 26 300 12" />
          <path className="oa-hv__line oa-hv__line--2" pathLength={1} d="M0 100 C40 97 60 94 90 90 S150 84 180 76 S240 66 300 52" />
          <circle className="oa-hv__dot" cx="300" cy="12" r="4" />
        </svg>
        <div className="oa-hv__bars">
          {[38, 52, 46, 64, 58, 76, 70, 88].map((h, i) => (
            <span key={i} style={{ '--h': `${h}%`, '--i': i } as CSSProperties} />
          ))}
        </div>
      </div>
    </div>
  );
}

/**
 * Home hero: a layered product visual — a marketing dashboard (chart lines drawing in, bars growing) behind a course
 * player card (progress ring filling, lessons ticking off) and a certificate that shimmers — on a slow orbit and a
 * drifting gradient mesh. `course` is a real featured course when the catalog has loaded.
 */
export function HeroVisual({ course }: { course?: { title: string; category: CourseCategory; lessonCount: number } | null }) {
  const ref = useInViewClass<HTMLDivElement>();
  return (
    <div ref={ref} className="oa-hv" aria-hidden="true">
      <div className="oa-hv__mesh" />
      <div className="oa-hv__orbit">
        <span />
        <span />
      </div>

      <Dashboard />

      <div className={clsx('oa-hv__player', catClass(course?.category))}>
        <div className="oa-hv__player-art">
          <span className="oa-hv__tag">{course ? labelFor(course.category) : 'Academy'}</span>
          <PlayCircle className="oa-hv__play" />
          <svg className="oa-hv__ring" viewBox="0 0 44 44">
            <circle cx="22" cy="22" r="18" className="oa-hv__ring-track" />
            <circle cx="22" cy="22" r="18" className="oa-hv__ring-fill" pathLength={100} />
          </svg>
          <span className="oa-hv__pct" />
        </div>
        <div className="oa-hv__player-body">
          <p className="oa-hv__course">{course?.title ?? 'Your first free course'}</p>
          <ul>
            {['Watch the lesson', 'Knowledge check', 'Final assessment'].map((t, i) => (
              <li key={t} style={{ '--i': i } as CSSProperties}>
                <span className="oa-hv__check">
                  <Check />
                </span>
                {t}
              </li>
            ))}
          </ul>
        </div>
      </div>

      <div className="oa-hv__cert">
        <HexBadge className="oa-hv__hex" />
        <div>
          <span>Certificate earned</span>
          <small>
            <BadgeCheck /> Verified · Add to LinkedIn
          </small>
        </div>
      </div>
    </div>
  );
}

/**
 * Agency pages (Services): the same dashboard with a delivery plan card (audit to reporting, ticking off) and an
 * "approved in your client portal" chip in place of the course player and certificate.
 */
export function AgencyVisual() {
  const ref = useInViewClass<HTMLDivElement>();
  return (
    <div ref={ref} className="oa-hv oa-hv--agency" aria-hidden="true">
      <div className="oa-hv__mesh" />
      <div className="oa-hv__orbit">
        <span />
        <span />
      </div>
      <Dashboard />
      <div className="oa-hv__player oa-hv__report">
        <div className="oa-hv__player-body">
          <p className="oa-hv__course">Your 90-day growth plan</p>
          <ul>
            {['Audit', 'Strategy', 'Execution', 'Monthly reporting'].map((t, i) => (
              <li key={t} style={{ '--i': i } as CSSProperties}>
                <span className="oa-hv__check">
                  <Check />
                </span>
                {t}
              </li>
            ))}
          </ul>
        </div>
      </div>
      <div className="oa-hv__cert">
        <span className="oa-hv__approve">
          <BadgeCheck />
        </span>
        <div>
          <span>Creative approved</span>
          <small>In your client portal</small>
        </div>
      </div>
    </div>
  );
}

function labelFor(category: CourseCategory): string {
  return category === 'Seo' ? 'SEO' : category === 'Ai' ? 'AI' : category === 'Data' ? 'Data' : category === 'Platform' ? 'Platform' : category;
}

/** Small abstract illustration for a course subject (AI, Marketing, SEO, Sales, Business, Design, Data, Platform). */
export function CategoryArt({ category, className }: { category: CourseCategory; className?: string }) {
  const art: Record<CourseCategory, ReactNode> = {
    Ai: (
      <>
        <g className="oa-art__lines">
          <path d="M20 44 48 20 76 44 48 58Z M20 44 76 44 M48 20 48 58" />
        </g>
        {[
          [20, 44],
          [48, 20],
          [76, 44],
          [48, 58],
        ].map(([cx, cy], i) => (
          <circle key={i} className="oa-art__node" cx={cx} cy={cy} r="5" style={{ '--i': i } as CSSProperties} />
        ))}
        <path className="oa-art__spark" d="M48 33 50.5 39.5 57 42 50.5 44.5 48 51 45.5 44.5 39 42 45.5 39.5Z" />
      </>
    ),
    Marketing: (
      <>
        <path className="oa-art__fill" d="M22 30h10l22-12v36L32 42H22Z" />
        <path className="oa-art__stroke" d="M60 26c4 3 4 13 0 16M66 20c8 6 8 22 0 28" />
        <path className="oa-art__stroke oa-art__soft" d="M30 42l4 14h7l-3-13" />
      </>
    ),
    Seo: (
      <>
        {[46, 36, 28, 20].map((y, i) => (
          <rect key={i} className="oa-art__bar" x={14 + i * 12} y={y} width="8" height={60 - y} rx="2" style={{ '--i': i } as CSSProperties} />
        ))}
        <circle className="oa-art__stroke" cx="66" cy="28" r="12" />
        <path className="oa-art__stroke" d="m75 37 9 9" />
      </>
    ),
    Sales: (
      <>
        <path className="oa-art__fill oa-art__soft" d="M14 16h68l-24 22v18l-20 6V38Z" />
        <path className="oa-art__stroke" d="M14 16h68l-24 22v18l-20 6V38Z" />
        <path className="oa-art__stroke" d="m62 58 10-10 6 6 8-10" />
      </>
    ),
    Business: (
      <>
        <rect className="oa-art__fill oa-art__soft" x="18" y="24" width="26" height="36" rx="3" />
        <rect className="oa-art__fill" x="50" y="12" width="28" height="48" rx="3" />
        <path className="oa-art__win" d="M56 20h6M66 20h6M56 30h6M66 30h6M56 40h6M66 40h6M24 32h6M34 32h4M24 42h6M34 42h4" />
      </>
    ),
    Design: (
      <>
        <circle className="oa-art__fill oa-art__soft" cx="34" cy="34" r="18" />
        <rect className="oa-art__fill" x="44" y="26" width="28" height="28" rx="4" transform="rotate(12 58 40)" />
        <path className="oa-art__stroke" d="M18 60c14-4 26-22 50-40" />
      </>
    ),
    Data: (
      <>
        <circle className="oa-art__fill oa-art__soft" cx="30" cy="36" r="18" />
        <path className="oa-art__fill" d="M30 36V18a18 18 0 0 1 17 12Z" />
        {[30, 20, 38].map((h, i) => (
          <rect key={i} className="oa-art__bar" x={56 + i * 10} y={60 - h} width="7" height={h} rx="2" style={{ '--i': i } as CSSProperties} />
        ))}
      </>
    ),
    Platform: (
      <>
        <circle className="oa-art__stroke" cx="38" cy="36" r="16" />
        <circle className="oa-art__stroke oa-art__accent" cx="56" cy="36" r="16" />
        <circle className="oa-art__node" cx="47" cy="36" r="4" />
      </>
    ),
  };
  return (
    <svg className={clsx('oa-art', className)} viewBox="0 0 96 72" aria-hidden="true" focusable="false">
      {art[category]}
    </svg>
  );
}

/** A certificate card with a shimmering seal (certificate strip on the home and academy pages). */
export function CertificateVisual() {
  const ref = useInViewClass<HTMLDivElement>();
  return (
    <div ref={ref} className="oa-certv" aria-hidden="true">
      <div className="oa-certv__paper">
        <div className="oa-certv__head">
          <span className="oa-certv__brand">Optimize All Academy</span>
          <span className="oa-certv__verified">
            <BadgeCheck /> Verified
          </span>
        </div>
        <p className="oa-certv__kicker">Certificate of completion</p>
        <p className="oa-certv__name">Your name here</p>
        <i className="oa-certv__line" />
        <i className="oa-certv__line oa-certv__line--short" />
        <div className="oa-certv__foot">
          <span>OA-····-····</span>
          <HexBadge className="oa-certv__seal" />
        </div>
      </div>
      <div className="oa-certv__li">
        <span className="oa-certv__in">in</span> Add to profile
      </div>
    </div>
  );
}

/** Two interlocking rings (the brand mark's idea) for the two pillars: Academy and Agency. */
export function PillarsVisual() {
  const ref = useInViewClass<HTMLDivElement>();
  return (
    <div ref={ref} className="oa-pillars" aria-hidden="true">
      <div className="oa-hv__mesh" />
      <svg viewBox="0 0 400 300">
        <defs>
          <linearGradient id="oa-pl-a" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#6a76bd" />
            <stop offset="1" stopColor="#1f2659" />
          </linearGradient>
          <linearGradient id="oa-pl-b" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#fdcc52" />
            <stop offset="1" stopColor="#e89a06" />
          </linearGradient>
        </defs>
        <circle className="oa-pillars__ring oa-pillars__ring--a" cx="160" cy="150" r="96" stroke="url(#oa-pl-a)" />
        <circle className="oa-pillars__ring oa-pillars__ring--b" cx="240" cy="150" r="96" stroke="url(#oa-pl-b)" />
        <circle className="oa-pillars__orbit" cx="200" cy="150" r="136" />
        <circle className="oa-pillars__sat" cx="200" cy="14" r="6" />
      </svg>
      <span className="oa-pillars__label oa-pillars__label--a">Academy</span>
      <span className="oa-pillars__label oa-pillars__label--b">Agency</span>
    </div>
  );
}
