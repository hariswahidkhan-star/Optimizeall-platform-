import type { CourseCategory } from '../api';

export type LearnSlotName = 'learn.course' | 'learn.lesson' | 'learn.exam' | 'learn.certificate' | 'learn.dashboard';

export interface LearnSlotProps {
  slot: LearnSlotName;
  /** Topic keywords of the page (course skills + category label), for matching partner content. */
  keywords: readonly string[];
  categories: readonly CourseCategory[];
}

/**
 * Insertion point for partner content on learning pages (course, lesson, exam result, certificate/verification, the
 * participant Learning panel). Renders nothing today; the partners feature replaces this body with
 * `<PartnerSlot slot={slot} keywords={keywords} categories={categories} />` — every learning page already passes the
 * right slot name, keywords and categories, so no page needs to change.
 */
export function LearnSlot(props: LearnSlotProps) {
  void props;
  return null;
}
