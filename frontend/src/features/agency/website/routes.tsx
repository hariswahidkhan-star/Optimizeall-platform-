import {
  BookOpen,
  Briefcase,
  CalendarClock,
  Factory,
  FileText,
  Inbox,
  LayoutDashboard,
  Mail,
  MessageSquareQuote,
  Package,
  Signpost,
  Settings2,
  Trophy,
  Type,
  UsersRound,
} from 'lucide-react';
import type { ComponentType } from 'react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem, PortalRouteHandle } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';

/**
 * Agency portal area: Website & CMS (public site content, blog, careers, website leads). Paths are relative to /agency.
 * There is deliberately no route at exactly `website` (the overview lives at `website/overview`), so each section
 * keeps its own permission for detail pages. Pages are lazy-loaded.
 */
const requires = {
  overview: { anyOf: [Permissions.SiteManage, Permissions.CrmView, Permissions.BlogWrite, Permissions.BlogPublish, Permissions.CareersManage] },
  inquiries: { anyOf: [Permissions.SiteManage, Permissions.CrmView] },
  blog: { anyOf: [Permissions.BlogWrite, Permissions.BlogPublish] },
  careers: { anyOf: [Permissions.CareersManage] },
  site: { anyOf: [Permissions.SiteManage] },
} satisfies Record<string, PermissionRequirement>;

type Loader = () => Promise<Record<string, unknown>>;

function page(path: string, load: Loader, name: string, req: PermissionRequirement): RouteObject {
  const handle: PortalRouteHandle = { requires: req };
  return {
    path,
    handle,
    lazy: async () => ({ Component: (await load())[name] as ComponentType }),
  };
}

const leads = () => import('./pages/LeadsAdmin');
const content = () => import('./pages/ContentPages');
const pagesAdmin = () => import('./pages/PagesAdmin');
const blog = () => import('./pages/BlogAdmin');

export const nav: PortalNavItem[] = [
  { to: 'website/overview', label: 'Website', icon: LayoutDashboard, description: 'Website leads, bookings and content at a glance.', requires: requires.overview },
  { to: 'website/inquiries', label: 'Website inquiries', shortLabel: 'Inquiries', icon: Inbox, description: 'Contact, audit, quote and consultation requests.', requires: requires.inquiries },
  { to: 'website/services', label: 'Services & packages', shortLabel: 'Services', icon: Package, description: 'Service catalog, categories and pricing packages.', requires: requires.site },
  { to: 'website/industries', label: 'Industries', icon: Factory, description: 'Industry landing pages.', requires: requires.site },
  { to: 'website/case-studies', label: 'Case studies', icon: Trophy, description: 'Client results with measured or estimated metrics.', requires: requires.site },
  { to: 'website/testimonials', label: 'Testimonials', icon: MessageSquareQuote, description: 'Client quotes shown across the site.', requires: requires.site },
  { to: 'website/team', label: 'Team', icon: UsersRound, description: 'Team members on the About page.', requires: requires.site },
  { to: 'website/pages', label: 'Pages', icon: FileText, description: 'Block-based pages and legal pages.', requires: requires.site },
  { to: 'website/blog', label: 'Blog', icon: BookOpen, description: 'Write, review, schedule and publish posts.', requires: requires.blog },
  { to: 'website/careers', label: 'Careers', icon: Briefcase, description: 'Job openings and applications.', requires: requires.careers },
  { to: 'website/bookings', label: 'Consultations', icon: CalendarClock, description: 'Booked consultations and availability.', requires: requires.site },
  { to: 'website/newsletter', label: 'Newsletter', icon: Mail, description: 'Double opt-in subscribers.', requires: requires.site },
  { to: 'website/redirects', label: 'Redirects', icon: Signpost, description: 'Old addresses that send visitors to where content lives now.', requires: requires.site },
  { to: 'website/copy', label: 'Page texts', icon: Type, description: 'Headlines, introductions and buttons of the built-in pages.', requires: requires.site },
  { to: 'website/settings', label: 'Site settings', icon: Settings2, description: 'Navigation, footer, SEO defaults and analytics.', requires: requires.site },
];

export const routes: RouteObject[] = [
  page('website/overview', leads, 'WebsiteOverviewPage', requires.overview),
  page('website/inquiries', leads, 'InquiriesPage', requires.inquiries),
  page('website/inquiries/:inquiryId', leads, 'InquiryDetailPage', requires.inquiries),
  page('website/services', () => import('./pages/ServicesAdminPage'), 'ServicesAdminPage', requires.site),
  page('website/industries', content, 'IndustriesAdminPage', requires.site),
  page('website/case-studies', content, 'CaseStudiesAdminPage', requires.site),
  page('website/testimonials', content, 'TestimonialsAdminPage', requires.site),
  page('website/team', content, 'TeamAdminPage', requires.site),
  page('website/pages', pagesAdmin, 'PagesAdminPage', requires.site),
  page('website/pages/:pageId', pagesAdmin, 'PageEditorPage', requires.site),
  page('website/blog', blog, 'BlogAdminPage', requires.blog),
  page('website/blog/:postId', blog, 'PostEditorPage', requires.blog),
  page('website/careers', () => import('./pages/CareersAdmin'), 'CareersAdminPage', requires.careers),
  page('website/bookings', leads, 'BookingsPage', requires.site),
  page('website/newsletter', leads, 'SubscribersPage', requires.site),
  page('website/redirects', () => import('./pages/RedirectsAdmin'), 'RedirectsAdminPage', requires.site),
  page('website/copy', () => import('./pages/CopyAdmin'), 'SiteCopyPage', requires.site),
  page('website/settings', () => import('./pages/SettingsAdmin'), 'SiteSettingsPage', requires.site),
];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [Permissions.SiteManage, Permissions.BlogWrite, Permissions.BlogPublish, Permissions.CareersManage, Permissions.CrmView];
