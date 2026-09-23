import type { IsoDateTime } from '@/lib/api/types';

/** Mirrors `Modules/LandingPages` DTOs and `Domain/LandingPages` block/form schemas. */

export type BlockType =
  | 'hero'
  | 'text'
  | 'image'
  | 'video'
  | 'features'
  | 'testimonials'
  | 'pricing'
  | 'faq'
  | 'countdown'
  | 'form'
  | 'cta'
  | 'logos'
  | 'spacer';

export interface HeroProps {
  headline: string;
  subheadline?: string | null;
  ctaLabel?: string | null;
  ctaHref?: string | null;
  imageUrl?: string | null;
  imageAlt?: string | null;
  align: 'left' | 'center';
  theme: 'light' | 'dark' | 'brand';
}
export interface TextProps {
  heading?: string | null;
  body: string;
}
export interface ImageProps {
  url: string;
  alt?: string | null;
  decorative?: boolean;
  caption?: string | null;
  linkHref?: string | null;
}
export interface VideoProps {
  url?: string | null;
  provider?: 'youtube' | 'vimeo' | null;
  videoId?: string | null;
  title: string;
}
export interface FeaturesProps {
  heading?: string | null;
  intro?: string | null;
  items: { title: string; body?: string | null; icon?: string | null }[];
}
export interface TestimonialsProps {
  heading?: string | null;
  items: { quote: string; author: string; role?: string | null; avatarUrl?: string | null; rating?: number | null }[];
}
export interface PricingProps {
  heading?: string | null;
  footnote?: string | null;
  plans: {
    name: string;
    price: string;
    period?: string | null;
    description?: string | null;
    features: string[];
    ctaLabel?: string | null;
    ctaHref?: string | null;
    highlighted?: boolean;
  }[];
}
export interface FaqProps {
  heading?: string | null;
  items: { question: string; answer: string }[];
}
export interface CountdownProps {
  heading?: string | null;
  endsAt: string;
  expiredText?: string | null;
}
export interface FormBlockProps {
  formId: string;
  heading?: string | null;
  description?: string | null;
}
export interface CtaProps {
  heading: string;
  body?: string | null;
  buttonLabel: string;
  buttonHref: string;
  style: 'primary' | 'highlight' | 'secondary';
}
export interface LogosProps {
  heading?: string | null;
  items: { name: string; imageUrl: string; href?: string | null }[];
}
export interface SpacerProps {
  size: 'sm' | 'md' | 'lg' | 'xl';
}

export interface BlockPropsMap {
  hero: HeroProps;
  text: TextProps;
  image: ImageProps;
  video: VideoProps;
  features: FeaturesProps;
  testimonials: TestimonialsProps;
  pricing: PricingProps;
  faq: FaqProps;
  countdown: CountdownProps;
  form: FormBlockProps;
  cta: CtaProps;
  logos: LogosProps;
  spacer: SpacerProps;
}

export type Block = { [K in BlockType]: { id: string; type: K; props: BlockPropsMap[K] } }[BlockType];

export type VariantKey = 'A' | 'B' | 'C' | 'D';

export interface Variant {
  key: VariantKey;
  name: string;
  weight: number;
  blocks: Block[];
}

export type PageStatus = 'Draft' | 'Published' | 'Archived';

export interface PageListItem {
  id: string;
  clientAccountId: string;
  clientName: string;
  clientSlug: string;
  name: string;
  slug: string;
  status: PageStatus;
  hasUnpublishedChanges: boolean;
  experimentEnabled: boolean;
  variantCount: number;
  publishedAt: IsoDateTime | null;
  publicPath: string;
  updatedAt: IsoDateTime;
}

export interface PageDetail {
  id: string;
  clientAccountId: string;
  clientName: string;
  clientSlug: string;
  name: string;
  slug: string;
  status: PageStatus;
  metaTitle: string | null;
  metaDescription: string | null;
  ogImageUrl: string | null;
  noIndex: boolean;
  templateKey: string | null;
  variants: Variant[];
  experimentEnabled: boolean;
  experimentId: string;
  experimentStartedAt: IsoDateTime | null;
  publishedVersionId: string | null;
  publishedVersion: number | null;
  publishedAt: IsoDateTime | null;
  hasUnpublishedChanges: boolean;
  publicPath: string;
  concurrencyStamp: string;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface PageTemplate {
  key: string;
  name: string;
  category: string;
  description: string;
  metaTitle: string;
  metaDescription: string;
  formTemplateKey: string | null;
  blocks: Block[];
}

export interface VersionInfo {
  id: string;
  version: number;
  publishedAt: IsoDateTime;
  publishedByUserId: string | null;
  contentHash: string;
  isCurrent: boolean;
}

export interface VariantStats {
  key: VariantKey;
  name: string;
  weight: number;
  views: number;
  uniqueVisitors: number;
  assigned: number;
  submissions: number;
  conversionRate: number | null;
  absoluteLift: number | null;
  relativeLift: number | null;
  pValue: number | null;
  significant: boolean;
  note: string;
}

export interface PageAnalytics {
  pageId: string;
  from: IsoDateTime;
  to: IsoDateTime;
  views: number;
  uniqueVisitors: number;
  submissions: number;
  conversionRate: number | null;
  experimentEnabled: boolean;
  experimentId: string;
  experimentStartedAt: IsoDateTime | null;
  variants: VariantStats[];
  daily: { date: string; views: number; submissions: number }[];
  method: string;
}

// ------------------------------------------------------------------------------------------------ Forms

export type FieldType =
  | 'text'
  | 'email'
  | 'phone'
  | 'number'
  | 'select'
  | 'multiselect'
  | 'checkbox'
  | 'radio'
  | 'date'
  | 'textarea'
  | 'file'
  | 'hidden'
  | 'consent';

export type ConditionOperator = 'equals' | 'notEquals' | 'contains' | 'in' | 'isEmpty' | 'isNotEmpty' | 'greaterThan' | 'lessThan';

export interface FieldCondition {
  field: string;
  operator: ConditionOperator;
  value?: string | null;
  values?: string[] | null;
}

export interface FormFieldDef {
  key: string;
  type: FieldType;
  label: string;
  required?: boolean;
  placeholder?: string | null;
  helpText?: string | null;
  options?: { value: string; label: string }[] | null;
  validation?: {
    minLength?: number | null;
    maxLength?: number | null;
    min?: number | null;
    max?: number | null;
    pattern?: string | null;
    patternMessage?: string | null;
    accept?: ('pdf' | 'image')[] | null;
    maxSizeMb?: number | null;
    minChoices?: number | null;
    maxChoices?: number | null;
  } | null;
  showIf?: FieldCondition | null;
  urlParam?: string | null;
  defaultValue?: string | null;
  width?: 'full' | 'half' | null;
}

export interface FormStep {
  id: string;
  title?: string | null;
  description?: string | null;
  fields: FormFieldDef[];
}

export interface FormSchema {
  steps: FormStep[];
}

export type CaptchaProvider = 'None' | 'HCaptcha' | 'Turnstile';
export type FormStatus = 'Draft' | 'Active' | 'Archived';

export interface FormListItem {
  id: string;
  clientAccountId: string;
  clientName: string;
  name: string;
  status: FormStatus;
  fieldCount: number;
  stepCount: number;
  submissions: number;
  lastSubmissionAt: IsoDateTime | null;
  updatedAt: IsoDateTime;
}

export interface FormDetail {
  id: string;
  clientAccountId: string;
  clientName: string;
  name: string;
  status: FormStatus;
  schema: FormSchema;
  submitLabel: string;
  successMessage: string;
  redirectUrl: string | null;
  notifyUserIds: string[];
  autoresponderEnabled: boolean;
  autoresponderSubject: string | null;
  autoresponderBody: string | null;
  allowedOrigins: string[];
  consentText: string | null;
  consentVersion: number;
  captcha: CaptchaProvider;
  minFillSeconds: number;
  templateKey: string | null;
  concurrencyStamp: string;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface FormTemplateInfo {
  key: string;
  name: string;
  description: string;
  schema: FormSchema;
  submitLabel: string;
}

export interface Submission {
  id: string;
  submittedAt: IsoDateTime;
  name: string | null;
  email: string | null;
  phone: string | null;
  values: Record<string, string>;
  utmSource: string | null;
  utmMedium: string | null;
  utmCampaign: string | null;
  utmTerm: string | null;
  utmContent: string | null;
  referrer: string | null;
  embedOrigin: string | null;
  landingPageId: string | null;
  landingPageName: string | null;
  variantKey: string | null;
  consentGiven: boolean;
  consentVersion: number | null;
  consentText: string | null;
  eventPublished: boolean;
  files: { id: string; fieldKey: string; fileName: string; contentType: string; sizeBytes: number }[];
}

export interface EmbedInfo {
  formUrl: string;
  iframeSnippet: string;
  allowedOrigins: string[];
  frameAncestors: string;
  guidance: string;
}

/** Form definition served to visitors (`GET /public/forms/{id}` or inside a landing page). */
export interface PublicForm {
  id: string;
  name: string;
  schema: FormSchema;
  submitLabel: string;
  successMessage: string;
  redirectUrl: string | null;
  consentText: string | null;
  consentVersion: number;
  captcha: { provider: 'hcaptcha' | 'turnstile'; siteKey: string } | null;
  token: string;
}

export interface PublicLandingPage {
  pageId: string;
  clientName: string;
  clientSlug: string;
  slug: string;
  title: string;
  metaDescription: string | null;
  ogImageUrl: string | null;
  noIndex: boolean;
  versionId: string;
  version: number;
  variantKey: VariantKey;
  experimentId: string | null;
  blocks: Block[];
  forms: PublicForm[];
}

export const pageKeys = {
  all: ['agency', 'pages'] as const,
  list: (params: object) => ['agency', 'pages', 'list', params] as const,
  page: (id: string) => ['agency', 'pages', 'page', id] as const,
  analytics: (id: string) => ['agency', 'pages', 'page', id, 'analytics'] as const,
  versions: (id: string) => ['agency', 'pages', 'page', id, 'versions'] as const,
  templates: ['agency', 'pages', 'templates'] as const,
  formTemplates: ['agency', 'pages', 'form-templates'] as const,
  forms: (params: object) => ['agency', 'pages', 'forms', params] as const,
  form: (id: string) => ['agency', 'pages', 'form', id] as const,
  submissions: (id: string, params: object) => ['agency', 'pages', 'form', id, 'submissions', params] as const,
};
