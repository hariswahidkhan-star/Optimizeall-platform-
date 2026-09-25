import { ArrowRight, Award, BadgeCheck, GraduationCap, UserPlus } from 'lucide-react';
import { Link, useParams } from 'react-router-dom';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody } from '@/components/ui/Card';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { useAuth } from '@/lib/auth/useAuth';
import { usePublicCourse, usePublicLesson, type LearningSeo, type JsonLd } from '@/features/learning/api';
import { PublicCatalog } from '@/features/learning/components/CatalogBrowser';
import { LinkedInIcon } from '@/features/learning/components/CourseCard';
import { CourseView } from '@/features/learning/components/CourseView';
import { LessonView } from '@/features/learning/components/LessonView';
import '@/features/learning/learning.css';
import { useDocumentHead } from '../site/head';
import { NotFound } from '../NotFound';

/**
 * The free public academy (/learn, /learn/:slug, /learn/:slug/:lessonSlug): every course and lesson is readable without an
 * account; knowledge checks work in the browser. Tracking progress, the final assessment and the certificate need a free
 * account, so every page carries that call to action (signed-in participants are sent to the same page in the portal).
 */
export const academyPaths = {
  home: '/learn',
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

function SignUpCta({ next }: { next: string }) {
  const { status } = useAuth();
  const signedIn = status === 'authenticated';
  return (
    <Card className="lx-cta">
      <CardBody>
        <div className="lx-cta__row">
          <Award aria-hidden="true" className="lx-cta__icon" />
          <div>
            <p className="lx-cta__title">
              {signedIn ? 'Track this course in your dashboard' : 'Create a free account to track progress, take the exam and earn your certificate'}
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
              <ButtonLink to={`/register?next=${encodeURIComponent(next)}`} leadingIcon={<UserPlus aria-hidden="true" />}>
                Create a free account
              </ButtonLink>
              <ButtonLink to={`/login?next=${encodeURIComponent(next)}`} variant="secondary">
                Sign in
              </ButtonLink>
            </>
          )}
        </div>
      </CardBody>
    </Card>
  );
}

export function AcademyPage() {
  useDocumentHead({
    title: 'Free courses with certificates — sales, marketing, SEO and AI',
    description:
      'Optimize All Academy: free, practical courses in sales, marketing, SEO, AI and more. Learn at your own pace, pass the assessment and add a verified certificate to LinkedIn.',
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
  return (
    <>
      <header className="site-hero lx-academy-hero">
        <div className="container">
          <p className="site-hero__eyebrow">Optimize All Academy</p>
          <h1 className="site-hero__title">Free courses. Real skills. Verified certificates.</h1>
          <p className="site-hero__lead">
            Practical training in sales, marketing, SEO and AI — built for creators, freelancers and growing teams. Read every lesson for free;
            create a free account to track progress, take the final assessment and earn a certificate you can add to LinkedIn.
          </p>
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
      <section className="site-section" aria-label="Course catalog">
        <div className="container">
          <PublicCatalog linkFor={academyPaths.course} />
        </div>
      </section>
    </>
  );
}

export function AcademyCoursePage() {
  const { slug = '' } = useParams();
  const q = usePublicCourse(slug);
  const { status } = useAuth();
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
  const first = course.modules[0]?.lessons[0]?.slug;
  const signedIn = status === 'authenticated';
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
      <CourseView
        course={course}
        lessonLink={(l) => academyPaths.lesson(course.card.slug, l)}
        courseLink={academyPaths.course}
        actions={
          <>
            {first && (
              <ButtonLink to={academyPaths.lesson(course.card.slug, first)} size="lg">
                Start the first lesson
              </ButtonLink>
            )}
            <ButtonLink
              to={signedIn ? academyPaths.portalCourse(course.card.slug) : `/register?next=${encodeURIComponent(academyPaths.portalCourse(course.card.slug))}`}
              size="lg"
              variant="secondary"
            >
              {signedIn ? 'Enrol in my learning' : 'Create a free account'}
            </ButtonLink>
          </>
        }
        aside={<SignUpCta next={academyPaths.portalCourse(course.card.slug)} />}
      />
    </div>
  );
}

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
        footer={<SignUpCta next={academyPaths.portalLesson(slug, lessonSlug)} />}
      />
    </div>
  );
}
