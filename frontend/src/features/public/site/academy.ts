import { useMemo } from 'react';
import { useCategories, usePublicCatalog, type CategorySummary, type CourseCard } from '@/features/learning/api';

/** Public course page for a course slug (the engine's enrol flow starts there). */
export const coursePath = (slug: string) => `/learn/${encodeURIComponent(slug)}`;
export const categoryPath = (category: string) => `/learn?category=${encodeURIComponent(category)}`;

export interface AcademyOverview {
  /** Published courses (the catalog total). */
  courseCount: number;
  /** Lessons across all published courses (a lower bound when the catalog has more than 200 courses). */
  lessonCount: number;
  /** False when the catalog was larger than one page, so {@link lessonCount} is "at least". */
  lessonCountComplete: boolean;
  /** Subjects that have published courses, with counts. */
  categories: CategorySummary[];
  /** Featured courses first, then the rest of the catalog (as the catalog orders them). */
  courses: CourseCard[];
  featured: CourseCard[];
  /** Distinct skills taught across the catalog (for the skills marquee). */
  skills: string[];
}

/**
 * Live academy numbers for the marketing pages, straight from the public learning API — never hard-coded. One catalog
 * request (the whole catalog, at most 200 cards) gives the course and lesson totals, featured courses and skills; the
 * categories endpoint gives the subjects. `data` is null until both have loaded (sections render nothing, or their
 * skeletons, rather than invented figures).
 */
export function useAcademyOverview(): { data: AcademyOverview | null; isLoading: boolean } {
  const catalog = usePublicCatalog({ page: 1, pageSize: 200 });
  const categories = useCategories();
  const data = useMemo<AcademyOverview | null>(() => {
    if (!catalog.data || !categories.data) return null;
    const courses = catalog.data.items;
    // Subject courses lead; the platform's own onboarding course goes last.
    const featured = courses.filter((c) => c.isFeatured).sort((a, b) => Number(a.category === 'Platform') - Number(b.category === 'Platform'));
    const skills = Array.from(new Set(courses.flatMap((c) => c.skills))).slice(0, 36);
    return {
      courseCount: catalog.data.total,
      lessonCount: courses.reduce((sum, c) => sum + c.lessonCount, 0),
      lessonCountComplete: courses.length >= catalog.data.total,
      categories: categories.data.filter((c) => c.courseCount > 0),
      courses,
      featured,
      skills,
    };
  }, [catalog.data, categories.data]);
  return { data, isLoading: catalog.isLoading || categories.isLoading };
}
