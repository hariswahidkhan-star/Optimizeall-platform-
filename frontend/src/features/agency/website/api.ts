import type { BillingPeriod, FaqEntry, HomeStat, MenuItem, MetricMeasurement, PageBlock, SiteLink, TrustLogo } from '@/features/public/site/api';

/** Staff CMS types (`/api/v1/agency/website/...`). See docs/api/website.md. */

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Seo {
  title: string | null;
  description: string | null;
  ogImageUrl: string | null;
  canonicalUrl: string | null;
  noIndex: boolean;
}

export interface ServiceCategory {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  icon: string | null;
  sortOrder: number;
  isPublished: boolean;
  serviceCount: number;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface ServiceSummary {
  id: string;
  categoryId: string;
  categoryName: string;
  slug: string;
  name: string;
  tagline: string;
  icon: string | null;
  isPublished: boolean;
  isFeatured: boolean;
  sortOrder: number;
  packageCount: number;
  updatedAt: string;
}

export interface Package {
  id: string;
  serviceId: string;
  name: string;
  description: string | null;
  price: number | null;
  currency: string;
  billingPeriod: BillingPeriod;
  setupFee: number | null;
  features: string[];
  isMostPopular: boolean;
  isCustomQuote: boolean;
  isActive: boolean;
  sortOrder: number;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface Service {
  id: string;
  categoryId: string;
  slug: string;
  name: string;
  tagline: string;
  heroTitle: string | null;
  heroBody: string | null;
  overviewMarkdown: string | null;
  problemsSolved: string[];
  deliverables: string[];
  processSteps: { title: string; description: string }[];
  tools: string[];
  kpis: string[];
  faqs: FaqEntry[];
  relatedServiceIds: string[];
  icon: string | null;
  heroImageUrl: string | null;
  ctaLabel: string | null;
  ctaUrl: string | null;
  seo: Seo;
  isPublished: boolean;
  isFeatured: boolean;
  sortOrder: number;
  packages: Package[];
  createdAt: string;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface Industry {
  id: string;
  slug: string;
  name: string;
  summary: string;
  bodyMarkdown: string | null;
  challenges: string[];
  serviceIds: string[];
  icon: string | null;
  heroImageUrl: string | null;
  seo: Seo;
  isPublished: boolean;
  sortOrder: number;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface ResultMetric {
  label: string;
  value: string;
  measurement: MetricMeasurement;
  context: string | null;
}

export interface CaseStudy {
  id: string;
  slug: string;
  title: string;
  clientName: string;
  clientAnonymized: boolean;
  summary: string;
  industryId: string | null;
  serviceIds: string[];
  challengeMarkdown: string | null;
  strategyMarkdown: string | null;
  executionMarkdown: string | null;
  metrics: ResultMetric[];
  testimonialQuote: string | null;
  testimonialAuthor: string | null;
  testimonialRole: string | null;
  coverImageUrl: string | null;
  galleryImageUrls: string[];
  seo: Seo;
  isPublished: boolean;
  isFeatured: boolean;
  publishedAt: string | null;
  sortOrder: number;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface Testimonial {
  id: string;
  quote: string;
  authorName: string;
  authorRole: string | null;
  company: string | null;
  rating: number | null;
  avatarUrl: string | null;
  serviceId: string | null;
  isPublished: boolean;
  isFeatured: boolean;
  sortOrder: number;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface TeamMember {
  id: string;
  slug: string;
  name: string;
  role: string;
  bio: string | null;
  photoUrl: string | null;
  expertise: string[];
  socialLinks: SiteLink[];
  isPublished: boolean;
  sortOrder: number;
  updatedAt: string;
  concurrencyStamp: string;
}

/** A 301 redirect of an old public address (`/agency/website/redirects`). */
export interface SiteRedirect {
  id: string;
  fromPath: string;
  toPath: string;
  source: 'Automatic' | 'Manual';
  /** What moved, for automatic redirects: page, post, service, service-line, case-study, industry, landing-page. */
  contentType: string | null;
  contentId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface SitePageSummary {
  id: string;
  slug: string;
  title: string;
  kind: 'Standard' | 'Legal';
  isPublished: boolean;
  blockCount: number;
  updatedAt: string;
  /** Scheduled go-live of a published page (UTC). */
  publishAt: string | null;
  version: number;
}

export interface SitePage {
  id: string;
  slug: string;
  title: string;
  summary: string | null;
  kind: 'Standard' | 'Legal';
  blocks: PageBlock[];
  seo: Seo;
  isPublished: boolean;
  sortOrder: number;
  updatedAt: string;
  concurrencyStamp: string;
  publishAt: string | null;
  version: number;
}

export interface SitePageRevisionSummary {
  version: number;
  action: 'initial' | 'created' | 'updated' | 'restored' | string;
  note: string | null;
  title: string;
  isPublished: boolean;
  publishAt: string | null;
  authorUserId: string | null;
  authorName: string | null;
  createdAt: string;
  isCurrent: boolean;
}

export interface SitePageRevision extends SitePageRevisionSummary {
  slug: string;
  summary: string | null;
  kind: 'Standard' | 'Legal';
  blocks: PageBlock[];
  seo: Seo;
}


export type BlogStatus = 'Draft' | 'InReview' | 'Scheduled' | 'Published' | 'Archived';

export interface BlogPostSummary {
  id: string;
  slug: string;
  title: string;
  status: BlogStatus;
  authorName: string | null;
  categories: string[];
  tags: string[];
  readingMinutes: number;
  publishAt: string | null;
  publishedAt: string | null;
  updatedAt: string;
}

export interface BlogPost {
  id: string;
  slug: string;
  title: string;
  excerpt: string;
  bodyMarkdown: string;
  coverImageUrl: string | null;
  coverImageAlt: string | null;
  authorId: string | null;
  categoryIds: string[];
  tags: string[];
  readingMinutes: number;
  status: BlogStatus;
  publishAt: string | null;
  publishedAt: string | null;
  relatedPostIds: string[];
  seo: Seo;
  createdAt: string;
  updatedAt: string;
  concurrencyStamp: string;
  can: { edit: boolean; submit: boolean; publish: boolean; schedule: boolean; unpublish: boolean; returnToDraft: boolean; delete: boolean };
}

export interface BlogCategory {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  sortOrder: number;
  postCount: number;
  updatedAt: string;
  concurrencyStamp: string;
}

export type InquiryType = 'Contact' | 'Audit' | 'Quote' | 'Consultation';
export type InquiryStatus = 'New' | 'InProgress' | 'Qualified' | 'Converted' | 'Closed' | 'Spam';

export interface InquirySummary {
  id: string;
  reference: string;
  type: InquiryType;
  status: InquiryStatus;
  name: string;
  email: string;
  company: string | null;
  serviceSlugs: string[];
  budgetRange: string | null;
  utmSource: string | null;
  utmCampaign: string | null;
  assignedToUserId: string | null;
  createdAt: string;
}

export interface Inquiry extends InquirySummary {
  phone: string | null;
  website: string | null;
  message: string | null;
  packageIds: string[];
  timeline: string | null;
  details: Record<string, string>;
  utmMedium: string | null;
  utmTerm: string | null;
  utmContent: string | null;
  referrer: string | null;
  landingPath: string | null;
  consentVersion: string;
  consentAt: string;
  staffNotes: string | null;
  bookingId: string | null;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface CountBy {
  key: string;
  count: number;
}

export interface Overview {
  inquiriesLast30Days: number;
  inquiriesPrevious30Days: number;
  newInquiries: number;
  upcomingConsultations: number;
  confirmedSubscribers: number;
  pendingSubscribers: number;
  newApplications: number;
  publishedPosts: number;
  postsInReview: number;
  inquiriesByType: CountBy[];
  inquiriesBySource: CountBy[];
  inquiriesByDay: CountBy[];
}

export type WorkplaceType = 'OnSite' | 'Hybrid' | 'Remote';
export type EmploymentType = 'FullTime' | 'PartTime' | 'Contract' | 'Internship' | 'Temporary';
export type ApplicationStage = 'New' | 'Screening' | 'Interview' | 'Offer' | 'Hired' | 'Rejected';

export interface Job {
  id: string;
  slug: string;
  title: string;
  department: string;
  location: string;
  countryCode: string | null;
  workplace: WorkplaceType;
  employmentType: EmploymentType;
  summary: string;
  descriptionMarkdown: string;
  requirements: string[];
  benefits: string[];
  salaryMin: number | null;
  salaryMax: number | null;
  salaryCurrency: string | null;
  salaryPeriod: 'Hour' | 'Month' | 'Year' | null;
  status: 'Draft' | 'Open' | 'Closed';
  postedAt: string | null;
  closesAt: string | null;
  applicationCount: number;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface ApplicationSummary {
  id: string;
  jobOpeningId: string;
  jobTitle: string;
  name: string;
  email: string;
  stage: ApplicationStage;
  createdAt: string;
  updatedAt: string;
}

export interface Application extends ApplicationSummary {
  phone: string | null;
  portfolioUrl: string | null;
  coverLetter: string | null;
  cvFileName: string;
  cvSizeBytes: number;
  consentAt: string;
  consentVersion: string;
  notes: { id: string; authorUserId: string | null; body: string; stageFrom: ApplicationStage | null; stageTo: ApplicationStage | null; createdAt: string }[];
  concurrencyStamp: string;
}

export interface AvailabilityWindow {
  day: string;
  start: string;
  end: string;
}

export interface BookingSettings {
  timeZone: string;
  slotMinutes: number;
  minNoticeHours: number;
  maxDaysAhead: number;
  weeklyAvailability: AvailabilityWindow[];
  isEnabled: boolean;
  blackouts: { id: string; date: string; reason: string | null }[];
  concurrencyStamp: string;
}

export type BookingStatus = 'Confirmed' | 'Cancelled' | 'Completed' | 'NoShow';

export interface Booking {
  id: string;
  reference: string;
  slotStart: string;
  slotEnd: string;
  status: BookingStatus;
  name: string;
  email: string;
  phone: string | null;
  company: string | null;
  website: string | null;
  notes: string | null;
  serviceSlugs: string[];
  visitorTimeZone: string;
  inquiryId: string | null;
  cancellationReason: string | null;
  cancelledAt: string | null;
  createdAt: string;
  concurrencyStamp: string;
}

export interface Subscriber {
  id: string;
  email: string;
  status: 'Pending' | 'Confirmed' | 'Unsubscribed';
  source: string | null;
  consentVersion: string;
  consentAt: string;
  confirmedAt: string | null;
  unsubscribedAt: string | null;
  utmSource: string | null;
  createdAt: string;
}

export interface SiteSettings {
  siteName: string;
  tagline: string;
  header: { menu: MenuItem[]; cta: SiteLink | null };
  footer: { blurb: string | null; columns: { title: string; links: SiteLink[] }[]; legalLinks: SiteLink[] };
  contact: { email: string | null; phone: string | null; whatsApp: string | null; address: string | null; hours: string | null };
  social: { platform: string; url: string }[];
  trustLogos: TrustLogo[];
  announcement: { enabled: boolean; text: string | null; linkLabel: string | null; linkUrl: string | null };
  seo: {
    siteUrl: string | null;
    titleTemplate: string;
    defaultTitle: string;
    defaultDescription: string | null;
    defaultOgImageUrl: string | null;
    twitterHandle: string | null;
  };
  organization: {
    legalName: string | null;
    logoUrl: string | null;
    foundingYear: number | null;
    streetAddress: string | null;
    locality: string | null;
    region: string | null;
    postalCode: string | null;
    countryCode: string | null;
    areaServed: string[];
  };
  analytics: { ga4MeasurementId: string | null; gtmContainerId: string | null; metaPixelId: string | null };
  homeStats: HomeStat[];
}

export interface SiteSettingsEnvelope {
  settings: SiteSettings;
  updatedAt: string;
  concurrencyStamp: string;
}

export const W = '/agency/website';
