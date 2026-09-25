import { Outlet } from 'react-router-dom';
import { SiteChrome } from '@/features/public/site/SiteChrome';
import { PageVideos } from '@/features/public/site/SiteVideo';
import { ImpersonationBanner } from './ImpersonationBanner';
import './PublicLayout.css';

/**
 * Public pages (agency website, creator landing pages, 404): the website frame — announcement bar, header with the
 * services mega-menu, CMS footer and cookie consent — lives in `features/public/site/SiteChrome`.
 */
export function PublicLayout() {
  return (
    <>
      {/* Renders only while a staff member is viewing as a signed-in user. */}
      <ImpersonationBanner />
      <SiteChrome>
        <Outlet />
        {/* Videos placed on built-in pages in code (siteVideos.json); CMS pages use the Video block. */}
        <PageVideos />
      </SiteChrome>
    </>
  );
}
