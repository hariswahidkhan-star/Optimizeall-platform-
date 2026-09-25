import { useMemo } from 'react';
import { useLearningSummary, type CategorySummary, type CourseCard } from '@/features/learning/api';

/** Public course page for a course slug (the engine's enrol flow starts there). */
export const coursePath = (slug: string) => `/learn/${encodeURIComponent(slug)}`;
export const categoryPath = (category: string) => `/learn?category=${encodeURIComponent(category)}`;

export interface AcademyOverview {
  /** Published courses (the catalog total). */
  courseCount: number;
  /** Lessons across all published courses. */
  lessonCount: number;
  /** Always true now (the summary endpoint counts every course); kept for the "+" suffix logic. */
  lessonCountComplete: boolean;
  /** Subjects that have published courses, with counts. */
  categories: CategorySummary[];
  /** Up to eight highlight cards: featured subject courses first, then the curated order. */
  courses: CourseCard[];
  featured: CourseCard[];
  /** Distinct skills taught across the catalog (for the skills marquee). */
  skills: string[];
}

/**
 * Live academy numbers for the marketing pages, straight from the public learning API — never hard-coded. One small,
 * cacheable request (GET /public/learning/summary, shared with the header's Academy menu) gives the course, lesson and
 * subject counts, the highlight cards and the skills. `data` is null until it has loaded (sections render nothing, or
 * their skeletons, rather than invented figures).
 */
export function useAcademyOverview(): { data: AcademyOverview | null; isLoading: boolean } {
  const summary = useLearningSummary();
  const data = useMemo<AcademyOverview | null>(() => {
    if (!summary.data) return null;
    const s = summary.data;
    return {
      courseCount: s.courseCount,
      lessonCount: s.lessonCount,
      lessonCountComplete: true,
      categories: s.categories.filter((c) => c.courseCount > 0),
      courses: s.highlights,
      featured: s.highlights.filter((c) => c.isFeatured),
      skills: s.skills,
    };
  }, [summary.data]);
  return { data, isLoading: summary.isLoading };
}
