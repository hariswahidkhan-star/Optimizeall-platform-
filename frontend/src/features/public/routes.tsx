import type { RouteObject } from 'react-router-dom';
import { FaqPage } from './FaqPage';
import { LandingPage } from './LandingPage';

/**
 * Public agency website routes (children of PublicLayout; wired into app/router.tsx). Pages are lazy-loaded so the
 * portals don't pay for the marketing site and vice versa. `/creators` keeps the creator-program landing page and
 * `/faq` the creator FAQ; `/:slug` renders CMS pages (About, How we work, legal pages…) and 404s otherwise.
 */
export const publicRoutes: RouteObject[] = [
  { index: true, lazy: async () => ({ Component: (await import('./pages/HomePage')).HomePage }) },
  { path: 'creators', element: <LandingPage /> },
  { path: 'faq', element: <FaqPage /> },
  { path: 'services', lazy: async () => ({ Component: (await import('./pages/ServicePages')).ServicesPage }) },
  { path: 'services/:slug', lazy: async () => ({ Component: (await import('./pages/ServicePages')).ServiceDetailPage }) },
  { path: 'industries', lazy: async () => ({ Component: (await import('./pages/IndustryAndCasePages')).IndustriesPage }) },
  { path: 'industries/:slug', lazy: async () => ({ Component: (await import('./pages/IndustryAndCasePages')).IndustryDetailPage }) },
  { path: 'case-studies', lazy: async () => ({ Component: (await import('./pages/IndustryAndCasePages')).CaseStudiesPage }) },
  { path: 'case-studies/:slug', lazy: async () => ({ Component: (await import('./pages/IndustryAndCasePages')).CaseStudyDetailPage }) },
  { path: 'pricing', lazy: async () => ({ Component: (await import('./pages/PricingPage')).PricingPage }) },
  { path: 'team', lazy: async () => ({ Component: (await import('./pages/TeamAndCareersPages')).TeamPage }) },
  { path: 'careers', lazy: async () => ({ Component: (await import('./pages/TeamAndCareersPages')).CareersPage }) },
  { path: 'careers/:slug', lazy: async () => ({ Component: (await import('./pages/TeamAndCareersPages')).JobDetailPage }) },
  { path: 'blog', lazy: async () => ({ Component: (await import('./pages/BlogPages')).BlogPage }) },
  { path: 'blog/:slug', lazy: async () => ({ Component: (await import('./pages/BlogPages')).BlogPostPage }) },
  { path: 'contact', lazy: async () => ({ Component: (await import('./pages/ContactPages')).ContactPage }) },
  { path: 'free-audit', lazy: async () => ({ Component: (await import('./pages/ContactPages')).FreeAuditPage }) },
  { path: 'get-a-quote', lazy: async () => ({ Component: (await import('./pages/QuotePage')).QuotePage }) },
  { path: 'book-a-consultation', lazy: async () => ({ Component: (await import('./pages/BookConsultationPage')).BookConsultationPage }) },
  { path: 'newsletter/confirm', lazy: async () => ({ Component: (await import('./pages/MiscPages')).NewsletterConfirmPage }) },
  { path: 'newsletter/unsubscribe', lazy: async () => ({ Component: (await import('./pages/MiscPages')).NewsletterUnsubscribePage }) },
  { path: 'search', lazy: async () => ({ Component: (await import('./pages/MiscPages')).SearchPage }) },
  // The two pillars: the academy's marketing overview (the catalog is /learn) and About, both on CMS pages.
  { path: 'academy', lazy: async () => ({ Component: (await import('./pages/PillarPages')).AcademyOverviewPage }) },
  { path: 'about', lazy: async () => ({ Component: (await import('./pages/PillarPages')).AboutPage }) },
  { path: 'partners', lazy: async () => ({ Component: (await import('./partners/PartnerPages')).PartnersPage }) },
  { path: 'partners/:slug', lazy: async () => ({ Component: (await import('./partners/PartnerPages')).PartnerProfilePage }) },
  // Free academy (Learning module) and public certificate verification.
  { path: 'learn', lazy: async () => ({ Component: (await import('./learn/AcademyPages')).AcademyPage }) },
  { path: 'learn/paths', lazy: async () => ({ Component: (await import('./learn/AcademyPages')).AcademyPathsPage }) },
  { path: 'learn/paths/:pathSlug', lazy: async () => ({ Component: (await import('./learn/AcademyPages')).AcademyPathPage }) },
  { path: 'learn/:slug', lazy: async () => ({ Component: (await import('./learn/AcademyPages')).AcademyCoursePage }) },
  { path: 'learn/:slug/:lessonSlug', lazy: async () => ({ Component: (await import('./learn/AcademyPages')).AcademyLessonPage }) },
  { path: 'verify/certificates/:id', lazy: async () => ({ Component: (await import('./learn/VerifyCertificatePage')).VerifyCertificatePage }) },
  { path: ':slug', lazy: async () => ({ Component: (await import('./pages/CmsPage')).CmsPage }) },
];
