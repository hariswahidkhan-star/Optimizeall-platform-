import { ArrowRight } from 'lucide-react';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { usePublicCatalog } from '@/features/learning/api';
import { CourseCard } from '@/features/learning/components/CourseCard';
import '@/features/learning/learning.css';

const academyPaths = { home: '/learn', course: (slug: string) => `/learn/${encodeURIComponent(slug)}` };

/** "Free courses" section of the public home page: featured courses. */
export function FeaturedCourses() {
  const q = usePublicCatalog({ page: 1, pageSize: 3 });
  if (!q.data || q.data.items.length === 0) return null;
  return (
    <section className="site-section site-section--muted" aria-labelledby="free-courses-heading">
      <div className="container">
        <div className="site-section__head">
          <div>
            <p className="eyebrow">Optimize All Academy</p>
            <h2 id="free-courses-heading" className="site-section__title">
              Free courses with certificates
            </h2>
            <p className="site-section__intro">Sales, marketing, SEO and AI training — free for everyone, with a certificate you can add to LinkedIn.</p>
          </div>
          <ButtonLink to={academyPaths.home} variant="secondary" trailingIcon={<ArrowRight aria-hidden="true" />}>
            Browse all courses
          </ButtonLink>
        </div>
        <ul className="lx-grid">
          {q.data.items.map((c) => (
            <li key={c.id}>
              <CourseCard course={c} to={academyPaths.course(c.slug)} />
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}
