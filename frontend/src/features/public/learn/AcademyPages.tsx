import {
  ArrowRight,
  Award,
  BadgeCheck,
  BookOpen,
  Clapperboard,
  Eye,
  GraduationCap,
  Info,
  LogIn,
  Route,
  Search,
  ShieldCheck,
  Sparkles,
  UserPlus,
} from 'lucide-react';
import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody } from '@/components/ui/Card';
import { ErrorState } from '@/components/ui/ErrorState';
import { Input } from '@/components/ui/Input';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/toastContext';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import {
  CATEGORY_LABELS,
  useCategories,
  useEnrol,
  useMyPath,
  useMyPaths,
  usePath,
  usePaths,
  usePublicCatalog,
  usePublicCourse,
  usePublicLesson,
  type CourseDetail,
  type JsonLd,
  type LearningSeo,
} from '@/features/learning/api';
import { PublicCatalog } from '@/features/learning/components/CatalogBrowser';
import { CategoryArt } from '@/features/learning/components/CategoryArt';
import { BadgeImage, categoryClass, CourseCard, LinkedInIcon } from '@/features/learning/components/CourseCard';
import { CourseView } from '@/features/learning/components/CourseView';
import { LessonView } from '@/features/learning/components/LessonView';
import { stagger, useCountUp, useReveal } from '@/features/learning/components/Motion';
import { PathDetailView, PathGrid } from '@/features/learning/components/PathViews';
import { authPathForEnrol, clearEnrolIntent, ENROL_PARAM, rememberEnrolIntent } from '@/features/learning/enrolIntent';
import '@/features/learning/learning.css';
import '@/features/learning/academy.css';
import { useDocumentHead } from '../site/head';
import { NotFound } from '../NotFound';

/**
 * The free public academy (/learn, /learn/paths, /learn/paths/:pathSlug, /learn/:slug, /learn/:slug/:lessonSlug): every
 * course, lesson and lecture transcript is readable without an account; knowledge checks work in the browser. Enrolling
 * ("Enrol for free — start learning") signs the visitor in or up and brings them back to the course, where the
 * enrolment completes and lesson 1 opens in "My learning" (see enrolIntent.ts).
 */
export const academyPaths = {
  home: '/learn',
  paths: '/learn/paths',
  path: (slug: string) => `/learn/paths/${encodeURIComponent(slug)}`,
  course: (slug: string) => `/learn/${encodeURIComponent(slug)}`,
  lesson: (slug: string, lesson: string) => `/learn/${encodeURIComponent(slug)}/${encodeURIComponent(lesson)}`,
  portalCourse: (slug: string) => `/app/learning/courses/${encodeURIComponent(slug)}`,
  portalLesson: (slug: string, lesson: string) =>
    `/app/learning/courses/${encodeURIComponent(slug)}/lessons/${encodeURIComponent(lesson)}`,
};

function headFrom(seo: LearningSeo | undefined, jsonLd: JsonLd[] | undefined, type: 'website' | 'article' = 'website') {
  return seo
    ? { title: seo.title, description: seo.description, canonical: seo.canonicalPath, noIndex: seo.noIndex, jsonLd, type }
    : {};
}

function useCanLearn() {
  const { status, user } = useAuth();
  const signedIn = status === 'authenticated';
  return {
    status,
    signedIn,
    // Enrolments belong to learner (participant) accounts; staff accounts can read everything but not enrol.
    canLearn: signedIn && !!user?.permissions.includes(Permissions.ParticipantPortal),
  };
}

// ---------------------------------------------------------------- direct enrol

/**
 * "Enrol for free — start learning". Signed in: enrols (idempotent) and opens the first unfinished lesson. Signed out:
 * goes to registration (or sign-in) with a validated return path to this course + ?enrol=1, and remembers the intent in
 * this browser for the email-verification round trip. Back here signed in with ?enrol=1, it completes automatically.
 */
function useEnrolAndStart(course: CourseDetail | undefined) {
  const slug = course?.card.slug ?? '';
  const { status, signedIn, canLearn } = useCanLearn();
  const navigate = useNavigate();
  const toast = useToast();
  const enrol = useEnrol(slug);
  const [params, setParams] = useSearchParams();
  const auto = params.get(ENROL_PARAM) === '1';
  const ran = useRef(false);
  const first = course?.modules[0]?.lessons[0]?.slug;

  const start = () => {
    if (!course) return;
    enrol.mutate(undefined, {
      onSuccess: (data) => {
        clearEnrolIntent();
        const lesson = data.progress?.resumeLessonSlug ?? first;
        navigate(lesson ? academyPaths.portalLesson(slug, lesson) : academyPaths.portalCourse(slug), { replace: auto });
      },
      onError: (e) => toast.error('Enrolment failed', errorMessage(e)),
    });
  };

  useEffect(() => {
    if (!auto || !course || status === 'loading' || ran.current) return;
    if (canLearn) {
      ran.current = true;
      start();
    } else if (signedIn) {
      // A staff account: nothing to enrol; drop the intent and keep the page.
      ran.current = true;
      clearEnrolIntent();
      const next = new URLSearchParams(params);
      next.delete(ENROL_PARAM);
      setParams(next, { replace: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [auto, course, status, canLearn, signedIn]);

  const goSignUp = (mode: 'login' | 'register') => {
    rememberEnrolIntent(slug);
    navigate(authPathForEnrol(slug, mode));
  };

  return { start, goSignUp, pending: enrol.isPending || (auto && canLearn && !enrol.isError), signedIn, canLearn, auto };
}

function EnrolActions({ course, flow, compact }: { course: CourseDetail; flow: ReturnType<typeof useEnrolAndStart>; compact?: boolean }) {
  const first = course.modules[0]?.lessons[0]?.slug;
  return (
    <>
      {flow.canLearn ? (
        <Button size="lg" leadingIcon={<GraduationCap aria-hidden="true" />} loading={flow.pending} onClick={flow.start} className="lx-cta-glow">
          Enrol for free — start learning
        </Button>
      ) : flow.signedIn ? (
        first && (
          <ButtonLink to={academyPaths.lesson(course.card.slug, first)} size="lg" leadingIcon={<BookOpen aria-hidden="true" />}>
            Start the first lesson
          </ButtonLink>
        )
      ) : (
        <Button size="lg" leadingIcon={<GraduationCap aria-hidden="true" />} onClick={() => flow.goSignUp('register')} className="lx-cta-glow">
          Enrol for free — start learning
        </Button>
      )}
      {!compact && !flow.canLearn && !flow.signedIn && (
        <Button size="lg" variant="secondary" leadingIcon={<LogIn aria-hidden="true" />} onClick={() => flow.goSignUp('login')}>
          I have an account
        </Button>
      )}
      {!compact && first && (flow.canLearn || !flow.signedIn) && (
        <ButtonLink to={academyPaths.lesson(course.card.slug, first)} size="lg" variant="ghost" leadingIcon={<Eye aria-hidden="true" />}>
          Preview lesson 1
        </ButtonLink>
      )}
    </>
  );
}

function SignUpCta({ next, slug }: { next: string; slug: string }) {
  const { signedIn } = useCanLearn();
  const navigate = useNavigate();
  const go = (mode: 'login' | 'register') => {
    rememberEnrolIntent(slug);
    navigate(authPathForEnrol(slug, mode));
  };
  return (
    <Card className="lx-cta">
      <CardBody>
        <div className="lx-cta__row">
          <Award aria-hidden="true" className="lx-cta__icon" />
          <div>
            <p className="lx-cta__title">
              {signedIn ? 'Track this course in your dashboard' : 'Enrol for free to track progress, take the exam and earn your certificate'}
            </p>
            <p className="lx-muted">Every course is free. Certificates are verifiable online and can be added to your LinkedIn profile.</p>
          </div>
        </div>
        <div className="lx-actions">
          {signedIn ? (
            <ButtonLink to={next} trailingIcon={<ArrowRight aria-hidden="true" />}>
              Open in my learning
            </ButtonLink>
          ) : (
            <>
              <Button leadingIcon={<UserPlus aria-hidden="true" />} onClick={() => go('register')}>
                Create a free account
              </Button>
              <Button variant="secondary" onClick={() => go('login')}>
                Sign in
              </Button>
            </>
          )}
        </div>
      </CardBody>
    </Card>
  );
}

// ---------------------------------------------------------------- /learn (the academy hub)

export const ACADEMY_TITLE = 'Free courses with certificates — sales, marketing, SEO, AI';
export const ACADEMY_DESCRIPTION =
  'Optimize All Academy: free, practical courses in sales, marketing, SEO and AI. Learn at your own pace and add a verified certificate to LinkedIn.';

function HeroStat({ value, label }: { value: number; label: string }) {
  const shown = useCountUp(value);
  return (
    <div className="lx-hub-stat">
      <span className="lx-hub-stat__value">{value > 0 ? shown.toLocaleString('en') : '—'}</span>
      <span className="lx-hub-stat__label">{label}</span>
    </div>
  );
}

function HubSearch({ onSearch }: { onSearch: (q: string) => void }) {
  const id = useId();
  const [q, setQ] = useState('');
  const submit = (e: FormEvent) => {
    e.preventDefault();
    onSearch(q.trim());
  };
  return (
    <form className="lx-hub-search" role="search" aria-label="Search the academy" onSubmit={submit}>
      <label htmlFor={id} className="visually-hidden">
        Search free courses
      </label>
      <Input
        id={id}
        type="search"
        value={q}
        onChange={(e) => setQ(e.target.value)}
        placeholder="What do you want to learn? Try “Claude”, “GA4” or “negotiation”"
        leading={<Search aria-hidden="true" />}
      />
      <Button type="submit">Search</Button>
    </form>
  );
}

export function AcademyPage() {
  useDocumentHead({
    // Same title and description as the server-rendered page (SeoPageResolver.Learning.cs), within 60 / 155 characters.
    title: ACADEMY_TITLE,
    description: ACADEMY_DESCRIPTION,
    canonical: academyPaths.home,
    jsonLd: [
      {
        '@context': 'https://schema.org',
        '@type': 'BreadcrumbList',
        itemListElement: [
          { '@type': 'ListItem', position: 1, name: 'Home', item: '/' },
          { '@type': 'ListItem', position: 2, name: 'Academy', item: academyPaths.home },
        ],
      },
    ],
  });
  const [params, setParams] = useSearchParams();
  const categories = useCategories();
  const paths = usePaths();
  const fresh = usePublicCatalog({ sort: 'newest', pageSize: 4 });
  const { canLearn } = useCanLearn();
  const myPaths = useMyPaths(canLearn);
  const catalogRef = useRef<HTMLElement>(null);
  const tilesRef = useReveal<HTMLUListElement>(categories.data?.length);
  const freshRef = useReveal<HTMLUListElement>(fresh.data?.items.length);
  const [catalogKey, setCatalogKey] = useState(0);
  const totalCourses = categories.data?.reduce((s, c) => s + c.courseCount, 0) ?? 0;
  const progressBySlug = new Map(myPaths.data?.map((p) => [p.card.slug, p.progress]) ?? []);

  const toCatalog = (patch: Record<string, string>) => {
    const next = new URLSearchParams(params);
    next.delete('page');
    for (const [k, v] of Object.entries(patch)) {
      if (v) next.set(k, v);
      else next.delete(k);
    }
    setParams(next, { replace: true });
    setCatalogKey((k) => k + 1); // the catalog's search box is uncontrolled: remount it with the new query
    catalogRef.current?.scrollIntoView({ block: 'start' });
  };

  return (
    <>
      <header className="site-hero lx-academy-hero lx-hub-hero">
        <div className="lx-hub-hero__aurora" aria-hidden="true" />
        <div className="container">
          <p className="site-hero__eyebrow">Optimize All Academy</p>
          <h1 className="site-hero__title">Free courses. Real skills. Verified certificates.</h1>
          <p className="site-hero__lead">
            Practical, up-to-date training in AI, marketing, SEO and sales — with video lectures, hands-on lessons and learning
            paths. Read every lesson for free; enrol to track progress, take the final assessment and earn a certificate you
            can add to LinkedIn.
          </p>
          <HubSearch onSearch={(q) => toCatalog({ q, category: '' })} />
          <div className="lx-hub-stats" aria-label="The academy in numbers">
            <HeroStat value={totalCourses} label="free courses" />
            <HeroStat value={paths.data?.paths.length ?? 0} label="learning paths" />
            <HeroStat value={categories.data?.length ?? 0} label="subjects" />
            <div className="lx-hub-stat">
              <span className="lx-hub-stat__value">$0</span>
              <span className="lx-hub-stat__label">forever</span>
            </div>
          </div>
          <ul className="lx-hero-points">
            <li>
              <GraduationCap aria-hidden="true" /> 100% free, self-paced
            </li>
            <li>
              <BadgeCheck aria-hidden="true" /> Verifiable certificates and Open Badges
            </li>
            <li>
              <LinkedInIcon /> One click to your LinkedIn profile
            </li>
          </ul>
        </div>
      </header>

      {categories.data && categories.data.length > 0 && (
        <section className="site-section lx-hub-section" aria-labelledby="hub-subjects">
          <div className="container">
            <div className="lx-hub-head">
              <h2 id="hub-subjects" className="lx-section-title">
                Explore by subject
              </h2>
            </div>
            <ul className="lx-cat-tiles lx-reveal" ref={tilesRef}>
              {categories.data.map((c, i) => (
                <li key={c.category} style={stagger(i)}>
                  <button type="button" className={`lx-cat-tile ${categoryClass(c.category)}`} onClick={() => toCatalog({ category: c.category, q: '' })}>
                    <CategoryArt category={c.category} />
                    <span className="lx-cat-tile__name">{CATEGORY_LABELS[c.category]}</span>
                    <span className="lx-cat-tile__count">
                      {c.courseCount} {c.courseCount === 1 ? 'course' : 'courses'}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        </section>
      )}

      {paths.data && paths.data.paths.length > 0 && (
        <section className="site-section lx-hub-section lx-hub-section--tint" aria-labelledby="hub-paths">
          <div className="container">
            <div className="lx-hub-head">
              <div>
                <p className="lx-hub-kicker">
                  <Route aria-hidden="true" className="lx-inline-icon" /> Learning paths
                </p>
                <h2 id="hub-paths" className="lx-section-title">
                  Follow a path from beginner to job-ready
                </h2>
              </div>
              <Link to={academyPaths.paths} className="lx-hub-more">
                All learning paths <ArrowRight aria-hidden="true" className="lx-inline-icon" />
              </Link>
            </div>
            <PathGrid paths={paths.data.paths.slice(0, 4)} linkFor={academyPaths.path} progressFor={(s) => progressBySlug.get(s)} />
          </div>
        </section>
      )}

      {fresh.data && fresh.data.items.length > 0 && (
        <section className="site-section lx-hub-section" aria-labelledby="hub-new">
          <div className="container">
            <div className="lx-hub-head">
              <div>
                <p className="lx-hub-kicker">
                  <Sparkles aria-hidden="true" className="lx-inline-icon" /> New &amp; trending
                </p>
                <h2 id="hub-new" className="lx-section-title">
                  Just published
                </h2>
              </div>
            </div>
            <ul className="lx-grid lx-grid--rail lx-reveal" ref={freshRef} aria-label="New courses">
              {fresh.data.items.map((course, i) => (
                <li key={course.id} style={stagger(i)}>
                  <CourseCard course={course} to={academyPaths.course(course.slug)} headingLevel={3} />
                </li>
              ))}
            </ul>
          </div>
        </section>
      )}

      <section className="site-section lx-hub-section" aria-labelledby="hub-cert">
        <div className="container">
          <div className="lx-cert-band">
            <div className="lx-cert-band__text">
              <p className="lx-hub-kicker">
                <ShieldCheck aria-hidden="true" className="lx-inline-icon" /> Free certificates
              </p>
              <h2 id="hub-cert" className="lx-section-title">
                Earn a verified certificate — and show it on LinkedIn
              </h2>
              <p>
                Pass a course’s final assessment and you get a certificate with a public verification page, a PDF and an
                Open Badge. Add it to your LinkedIn profile in one click — no fees, ever.
              </p>
              <ul className="lx-cert-band__points">
                <li>
                  <BadgeCheck aria-hidden="true" /> Verifiable by anyone, anytime
                </li>
                <li>
                  <LinkedInIcon /> “Add to profile” with the credential ID filled in
                </li>
                <li>
                  <Clapperboard aria-hidden="true" /> Video lectures, transcripts and hands-on lessons
                </li>
              </ul>
            </div>
            <div className="lx-cert-band__art" aria-hidden="true">
              <div className="lx-cert-mock">
                <span className="lx-cert-mock__rail" />
                <span className="lx-cert-mock__kicker">Certificate of achievement</span>
                <span className="lx-cert-mock__name">Your name</span>
                <span className="lx-cert-mock__line" />
                <span className="lx-cert-mock__line lx-cert-mock__line--short" />
                <span className="lx-cert-mock__seal lx-shimmer">
                  <Award />
                </span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="site-section" aria-labelledby="hub-catalog" id="catalog" ref={catalogRef}>
        <div className="container">
          <h2 id="hub-catalog" className="lx-section-title lx-hub-catalog-title">
            All courses
          </h2>
          <PublicCatalog key={catalogKey} linkFor={academyPaths.course} headingLevel={3} />
        </div>
      </section>
    </>
  );
}

// ---------------------------------------------------------------- /learn/:slug

export function AcademyCoursePage() {
  const { slug = '' } = useParams();
  const q = usePublicCourse(slug);
  const flow = useEnrolAndStart(q.data);
  useDocumentHead(headFrom(q.data?.seo, q.data?.jsonLd));
  if (q.isPending)
    return (
      <div className="container lx-public">
        <Skeleton height={420} radius="var(--radius-xl)" />
      </div>
    );
  if (q.isError) {
    if ((q.error as { status?: number }).status === 404) return <NotFound />;
    return (
      <div className="container lx-public">
        <ErrorState error={q.error} title="This course isn’t available right now" onRetry={() => void q.refetch()} />
      </div>
    );
  }
  const course = q.data;
  return (
    <div className="container lx-public">
      <nav aria-label="Breadcrumb" className="ui-breadcrumbs lx-crumbs">
        <ol>
          <li>
            <Link to="/">Home</Link>
          </li>
          <li aria-hidden="true">/</li>
          <li>
            <Link to={academyPaths.home}>Academy</Link>
          </li>
        </ol>
      </nav>
      {flow.auto && flow.canLearn && (
        <p className="lx-enrolling" role="status">
          <GraduationCap aria-hidden="true" /> Enrolling you in {course.card.title} and opening the first lesson…
        </p>
      )}
      {flow.signedIn && !flow.canLearn && (
        <p className="lx-enrolling lx-enrolling--info" role="note">
          <Info aria-hidden="true" /> You’re signed in with a staff account. Enrolment is for learner accounts — you can read
          every lesson here.
        </p>
      )}
      <CourseView
        course={course}
        lessonLink={(l) => academyPaths.lesson(course.card.slug, l)}
        courseLink={academyPaths.course}
        actions={<EnrolActions course={course} flow={flow} />}
        stickyActions={<EnrolActions course={course} flow={flow} compact />}
        aside={<SignUpCta next={academyPaths.portalCourse(course.card.slug)} slug={course.card.slug} />}
      />
    </div>
  );
}

// ---------------------------------------------------------------- /learn/:slug/:lessonSlug

export function AcademyLessonPage() {
  const { slug = '', lessonSlug = '' } = useParams();
  const q = usePublicLesson(slug, lessonSlug);
  useDocumentHead(headFrom(q.data?.seo, q.data?.jsonLd, 'article'));
  if (q.isPending)
    return (
      <div className="container lx-public">
        <Skeleton height={520} radius="var(--radius-xl)" />
      </div>
    );
  if (q.isError) {
    if ((q.error as { status?: number }).status === 404) return <NotFound />;
    return (
      <div className="container lx-public">
        <ErrorState error={q.error} title="This lesson isn’t available right now" onRetry={() => void q.refetch()} />
      </div>
    );
  }
  return (
    <div className="container lx-public lx-public--narrow">
      <LessonView
        lesson={q.data}
        courseLink={academyPaths.course(slug)}
        lessonLink={(l) => academyPaths.lesson(slug, l)}
        footer={<SignUpCta next={academyPaths.portalLesson(slug, lessonSlug)} slug={slug} />}
      />
    </div>
  );
}

// ---------------------------------------------------------------- /learn/paths

export function AcademyPathsPage() {
  const q = usePaths();
  const { canLearn } = useCanLearn();
  const mine = useMyPaths(canLearn);
  useDocumentHead(headFrom(q.data?.seo, q.data?.jsonLd));
  const progress = new Map(mine.data?.map((p) => [p.card.slug, p.progress]) ?? []);
  return (
    <>
      <header className="site-hero lx-academy-hero lx-hub-hero lx-hub-hero--compact">
        <div className="lx-hub-hero__aurora" aria-hidden="true" />
        <div className="container">
          <nav aria-label="Breadcrumb" className="ui-breadcrumbs lx-crumbs lx-crumbs--hero">
            <ol>
              <li>
                <Link to={academyPaths.home}>Academy</Link>
              </li>
              <li aria-hidden="true">/</li>
            </ol>
          </nav>
          <p className="site-hero__eyebrow">Learning paths</p>
          <h1 className="site-hero__title">Step-by-step routes to real, job-ready skills</h1>
          <p className="site-hero__lead">
            Each path orders free courses from first principles to advanced practice, with a verified certificate and a LinkedIn
            badge for every course you pass.
          </p>
        </div>
      </header>
      <section className="site-section" aria-label="All learning paths">
        <div className="container">
          {q.isPending && (
            <div className="lx-path-grid" aria-busy="true">
              {[1, 2, 3, 4].map((n) => (
                <Skeleton key={n} height={300} radius="var(--radius-xl)" />
              ))}
            </div>
          )}
          {q.isError && <ErrorState error={q.error} title="Learning paths aren’t available right now" onRetry={() => void q.refetch()} />}
          {q.data && <PathGrid paths={q.data.paths} linkFor={academyPaths.path} progressFor={(s) => progress.get(s)} headingLevel={2} />}
        </div>
      </section>
    </>
  );
}

// ---------------------------------------------------------------- /learn/paths/:pathSlug

export function AcademyPathPage() {
  const { pathSlug = '' } = useParams();
  const q = usePath(pathSlug);
  const { canLearn, signedIn } = useCanLearn();
  const mine = useMyPath(pathSlug, canLearn);
  const navigate = useNavigate();
  useDocumentHead(headFrom(q.data?.seo, q.data?.jsonLd));
  if (q.isPending)
    return (
      <div className="container lx-public">
        <Skeleton height={420} radius="var(--radius-xl)" />
      </div>
    );
  if (q.isError) {
    if ((q.error as { status?: number }).status === 404) return <NotFound />;
    return (
      <div className="container lx-public">
        <ErrorState error={q.error} title="This learning path isn’t available right now" onRetry={() => void q.refetch()} />
      </div>
    );
  }
  const path = q.data;
  const progress = mine.data?.progress ?? null;
  const nextSlug = progress?.nextCourseSlug ?? path.courses[0]?.course.slug;
  const next = path.courses.find((c) => c.course.slug === nextSlug)?.course;
  return (
    <div className="container lx-public">
      <nav aria-label="Breadcrumb" className="ui-breadcrumbs lx-crumbs">
        <ol>
          <li>
            <Link to={academyPaths.home}>Academy</Link>
          </li>
          <li aria-hidden="true">/</li>
          <li>
            <Link to={academyPaths.paths}>Learning paths</Link>
          </li>
        </ol>
      </nav>
      <PathDetailView
        path={path}
        progress={progress}
        courseLink={academyPaths.course}
        actions={
          next && (
            <>
              <ButtonLink to={academyPaths.course(next.slug)} size="lg" trailingIcon={<ArrowRight aria-hidden="true" />} className="lx-cta-glow">
                {progress?.started ? `Continue: ${next.title}` : `Start with ${next.title}`}
              </ButtonLink>
              {!signedIn && (
                <Button
                  size="lg"
                  variant="secondary"
                  leadingIcon={<UserPlus aria-hidden="true" />}
                  onClick={() => {
                    rememberEnrolIntent(next.slug);
                    navigate(authPathForEnrol(next.slug, 'register'));
                  }}
                >
                  Enrol for free
                </Button>
              )}
            </>
          )
        }
      />
      {path.courses[0] && (
        <div className="lx-path__cert">
          <BadgeImage src={path.card.badges[0]?.imageUrl ?? ''} size={56} />
          <p>
            <strong>Every course in this path ends with a verifiable certificate.</strong> Collect all {path.card.badges.length}{' '}
            badges and show the whole path on your LinkedIn profile.
          </p>
        </div>
      )}
    </div>
  );
}
