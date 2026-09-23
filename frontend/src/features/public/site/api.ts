import { useQuery } from '@tanstack/react-query';
import { api, type QueryParams } from '@/lib/api/client';

/** Types for the public website API (`/api/v1/public/...`). See docs/api/website.md. */

export type MetricMeasurement = 'Measured' | 'Estimated';
export type BillingPeriod = 'OneTime' | 'Monthly' | 'Quarterly' | 'Yearly';

export interface SiteLink {
  label: string;
  url: string;
}

export interface MenuItem {
  label: string;
  url: string | null;
  description: string | null;
  children: MenuItem[] | null;
}

export interface PublicSeo {
  title: string;
  description: string | null;
  ogImageUrl: string | null;
  canonicalUrl: string | null;
  noIndex: boolean;
}

export interface ConsentTexts {
  formVersion: string;
  formText: string;
  newsletterVersion: string;
  newsletterText: string;
  careersVersion: string;
  careersText: string;
}

export interface MenuCategory {
  slug: string;
  name: string;
  description: string | null;
  icon: string | null;
  services: { slug: string; name: string; tagline: string; icon: string | null }[];
}

export interface TrustLogo {
  name: string;
  imageUrl: string;
  url: string | null;
}

export interface HomeStat {
  label: string;
  value: string;
  measurement: MetricMeasurement;
  context: string | null;
}

export interface PublicSite {
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
  analytics: { ga4MeasurementId: string | null; gtmContainerId: string | null; metaPixelId: string | null };
  serviceMenu: MenuCategory[];
  consent: ConsentTexts;
  bookingEnabled: boolean;
}

export interface Price {
  amount: number;
  currency: string;
  billingPeriod: BillingPeriod;
}

export interface ServiceCard {
  id: string;
  slug: string;
  name: string;
  tagline: string;
  icon: string | null;
  categorySlug: string;
  categoryName: string;
  startingPrice: Price | null;
}

export interface ServiceCategoryGroup {
  slug: string;
  name: string;
  description: string | null;
  icon: string | null;
  services: ServiceCard[];
}

export interface PublicPackage {
  id: string;
  name: string;
  description: string | null;
  price: number | null;
  currency: string;
  billingPeriod: BillingPeriod;
  setupFee: number | null;
  features: string[];
  isMostPopular: boolean;
  isCustomQuote: boolean;
}

export interface Metric {
  label: string;
  value: string;
  measurement: MetricMeasurement;
  context: string | null;
}

export interface CaseStudyCard {
  slug: string;
  title: string;
  clientName: string;
  summary: string;
  industrySlug: string | null;
  industryName: string | null;
  serviceSlugs: string[];
  serviceNames: string[];
  coverImageUrl: string | null;
  highlights: Metric[];
  isFeatured: boolean;
}

export interface Testimonial {
  id: string;
  quote: string;
  authorName: string;
  authorRole: string | null;
  company: string | null;
  rating: number | null;
  avatarUrl: string | null;
  serviceSlug: string | null;
}

export interface IndustryCard {
  slug: string;
  name: string;
  summary: string;
  icon: string | null;
}

export type JsonLd = Record<string, unknown>;

export interface FaqEntry {
  question: string;
  answer: string;
}

export interface PublicService {
  id: string;
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
  icon: string | null;
  heroImageUrl: string | null;
  ctaLabel: string | null;
  ctaUrl: string | null;
  categorySlug: string;
  categoryName: string;
  packages: PublicPackage[];
  relatedServices: ServiceCard[];
  caseStudies: CaseStudyCard[];
  testimonials: Testimonial[];
  seo: PublicSeo;
  jsonLd: JsonLd[];
}

export interface PublicIndustry {
  slug: string;
  name: string;
  summary: string;
  bodyMarkdown: string | null;
  challenges: string[];
  icon: string | null;
  heroImageUrl: string | null;
  services: ServiceCard[];
  caseStudies: CaseStudyCard[];
  seo: PublicSeo;
  jsonLd: JsonLd[];
}

export interface PublicCaseStudy {
  slug: string;
  title: string;
  clientName: string;
  summary: string;
  industrySlug: string | null;
  industryName: string | null;
  services: ServiceCard[];
  challengeMarkdown: string | null;
  strategyMarkdown: string | null;
  executionMarkdown: string | null;
  metrics: Metric[];
  testimonialQuote: string | null;
  testimonialAuthor: string | null;
  testimonialRole: string | null;
  coverImageUrl: string | null;
  galleryImageUrls: string[];
  publishedAt: string | null;
  related: CaseStudyCard[];
  seo: PublicSeo;
  jsonLd: JsonLd[];
}

export interface TeamMember {
  slug: string;
  name: string;
  role: string;
  bio: string | null;
  photoUrl: string | null;
  expertise: string[];
  socialLinks: SiteLink[];
}

export interface PageBlock {
  id: string;
  type: string;
  data: Record<string, unknown>;
}

export interface PublicPage {
  slug: string;
  title: string;
  summary: string | null;
  kind: 'Standard' | 'Legal';
  blocks: PageBlock[];
  testimonials: Testimonial[];
  caseStudies: CaseStudyCard[];
  serviceCategories: ServiceCategoryGroup[];
  trustLogos: TrustLogo[];
  updatedAt: string;
  seo: PublicSeo;
  jsonLd: JsonLd[];
}

export interface PostCard {
  slug: string;
  title: string;
  excerpt: string;
  coverImageUrl: string | null;
  coverImageAlt: string | null;
  authorName: string | null;
  categories: { slug: string; name: string }[];
  tags: string[];
  readingMinutes: number;
  publishedAt: string | null;
}

export interface PublicPost extends Omit<PostCard, 'authorName'> {
  bodyMarkdown: string;
  author: {
    slug: string;
    name: string;
    role: string;
    bio: string | null;
    photoUrl: string | null;
    socialLinks: SiteLink[];
  } | null;
  updatedAt: string;
  related: PostCard[];
  seo: PublicSeo;
  jsonLd: JsonLd[];
}

export interface BlogIndex {
  items: PostCard[];
  total: number;
  page: number;
  pageSize: number;
  categories: { slug: string; name: string; description: string | null; postCount: number }[];
  tags: { tag: string; postCount: number }[];
}

export interface PricingTeaser {
  serviceSlug: string;
  serviceName: string;
  package: PublicPackage;
}

export interface Home {
  serviceCategories: ServiceCategoryGroup[];
  featuredCaseStudies: CaseStudyCard[];
  testimonials: Testimonial[];
  industries: IndustryCard[];
  latestPosts: PostCard[];
  pricingTeaser: PricingTeaser[];
  stats: HomeStat[];
  trustLogos: TrustLogo[];
  seo: PublicSeo;
  jsonLd: JsonLd[];
}

export interface Pricing {
  services: { service: ServiceCard; packages: PublicPackage[] }[];
}

export type WorkplaceType = 'OnSite' | 'Hybrid' | 'Remote';
export type EmploymentType = 'FullTime' | 'PartTime' | 'Contract' | 'Internship' | 'Temporary';

export interface JobCard {
  slug: string;
  title: string;
  department: string;
  location: string;
  workplace: WorkplaceType;
  employmentType: EmploymentType;
  summary: string;
  postedAt: string | null;
}

export interface PublicJob extends JobCard {
  descriptionMarkdown: string;
  requirements: string[];
  benefits: string[];
  salary: { min: number | null; max: number | null; currency: string; period: 'Hour' | 'Month' | 'Year' } | null;
  closesAt: string | null;
  seo: PublicSeo;
  jsonLd: JsonLd[];
}

export interface SearchHit {
  kind: string;
  slug: string;
  title: string;
  summary: string;
  url: string;
}

export interface SearchResult {
  query: string;
  services: SearchHit[];
  posts: SearchHit[];
  caseStudies: SearchHit[];
}

export interface FormOption {
  value: string;
  label: string;
}

export interface FormToken {
  token: string;
  minFillSeconds: number;
  budgetRanges: FormOption[];
  timelines: FormOption[];
}

export interface Slots {
  timeZone: string;
  slotMinutes: number;
  enabled: boolean;
  slots: string[];
}

// ---------------------------------------------------------------- query hooks

export const publicKeys = {
  site: ['public', 'site'] as const,
  home: ['public', 'home'] as const,
  services: ['public', 'services'] as const,
  service: (slug: string) => ['public', 'service', slug] as const,
  pricing: ['public', 'pricing'] as const,
  industries: ['public', 'industries'] as const,
  industry: (slug: string) => ['public', 'industry', slug] as const,
  caseStudies: (filters: QueryParams) => ['public', 'case-studies', filters] as const,
  caseStudy: (slug: string) => ['public', 'case-study', slug] as const,
  team: ['public', 'team'] as const,
  page: (slug: string) => ['public', 'page', slug] as const,
  blog: (params: QueryParams) => ['public', 'blog', params] as const,
  post: (slug: string) => ['public', 'post', slug] as const,
  careers: ['public', 'careers'] as const,
  job: (slug: string) => ['public', 'job', slug] as const,
  search: (q: string) => ['public', 'search', q] as const,
  slots: (from: string) => ['public', 'slots', from] as const,
};

const fiveMinutes = 5 * 60_000;

export const useSite = () =>
  useQuery({ queryKey: publicKeys.site, queryFn: () => api.get<PublicSite>('/public/site'), staleTime: fiveMinutes });

export const useHome = () => useQuery({ queryKey: publicKeys.home, queryFn: () => api.get<Home>('/public/home') });

export const useServices = () =>
  useQuery({
    queryKey: publicKeys.services,
    queryFn: () => api.get<ServiceCategoryGroup[]>('/public/services'),
    staleTime: fiveMinutes,
  });

export const useService = (slug: string) =>
  useQuery({
    queryKey: publicKeys.service(slug),
    queryFn: () => api.get<PublicService>(`/public/services/${encodeURIComponent(slug)}`),
  });

export const usePricing = () =>
  useQuery({ queryKey: publicKeys.pricing, queryFn: () => api.get<Pricing>('/public/pricing') });

export const useIndustries = () =>
  useQuery({ queryKey: publicKeys.industries, queryFn: () => api.get<IndustryCard[]>('/public/industries') });

export const useIndustry = (slug: string) =>
  useQuery({
    queryKey: publicKeys.industry(slug),
    queryFn: () => api.get<PublicIndustry>(`/public/industries/${encodeURIComponent(slug)}`),
  });

export const useCaseStudies = (filters: { service?: string; industry?: string }) =>
  useQuery({
    queryKey: publicKeys.caseStudies(filters),
    queryFn: () => api.get<CaseStudyCard[]>('/public/case-studies', { query: filters }),
  });

export const useCaseStudy = (slug: string) =>
  useQuery({
    queryKey: publicKeys.caseStudy(slug),
    queryFn: () => api.get<PublicCaseStudy>(`/public/case-studies/${encodeURIComponent(slug)}`),
  });

export const useTeam = () => useQuery({ queryKey: publicKeys.team, queryFn: () => api.get<TeamMember[]>('/public/team') });

export const usePage = (slug: string, enabled = true) =>
  useQuery({
    queryKey: publicKeys.page(slug),
    queryFn: () => api.get<PublicPage>(`/public/pages/${encodeURIComponent(slug)}`),
    enabled,
  });

export const useBlog = (params: { page: number; category?: string; tag?: string; search?: string }) =>
  useQuery({
    queryKey: publicKeys.blog(params),
    queryFn: () => api.get<BlogIndex>('/public/blog', { query: { ...params, pageSize: 9 } }),
    placeholderData: (previous) => previous,
  });

export const usePost = (slug: string) =>
  useQuery({
    queryKey: publicKeys.post(slug),
    queryFn: () => api.get<PublicPost>(`/public/blog/${encodeURIComponent(slug)}`),
  });

export const useJobs = () => useQuery({ queryKey: publicKeys.careers, queryFn: () => api.get<JobCard[]>('/public/careers') });

export const useJob = (slug: string) =>
  useQuery({
    queryKey: publicKeys.job(slug),
    queryFn: () => api.get<PublicJob>(`/public/careers/${encodeURIComponent(slug)}`),
  });

export const useSearch = (q: string) =>
  useQuery({
    queryKey: publicKeys.search(q),
    queryFn: () => api.get<SearchResult>('/public/search', { query: { q } }),
    enabled: q.trim().length >= 2,
  });
