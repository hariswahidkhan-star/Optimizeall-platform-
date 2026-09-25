import { PartnerSlot } from '@/features/public/partners/PartnerSlot';
import type { CourseCategory } from '../api';

export type LearnSlotName =
  'learn.course' | 'learn.lesson' | 'learn.exam' | 'learn.certificate' | 'learn.dashboard';

export interface LearnSlotProps {
  slot: LearnSlotName;
  /** Topic keywords of the page (course skills + category label), for matching partner content. */
  keywords: readonly string[];
  categories: readonly CourseCategory[];
}

/**
 * Partner content on learning pages (course, lesson, exam result, certificate/verification, the participant Learning
 * panel): the partners feature's ad unit for the page's `learn.*` slot, chosen by the API from the course's skills and
 * category (docs/SEO_CRO.md § partners; labelled "Sponsored", rel="sponsored" links).
 */
export function LearnSlot({ slot, keywords, categories }: LearnSlotProps) {
  return (
    <PartnerSlot slot={slot} keywords={[...keywords]} categories={categories.map((c) => c.toLowerCase())} />
  );
}
