import { Outlet } from 'react-router-dom';
import { SiteChrome } from '@/features/public/site/SiteChrome';
import './PublicLayout.css';

/**
 * Public pages (agency website, creator landing pages, 404): the website frame — announcement bar, header with the
 * services mega-menu, CMS footer and cookie consent — lives in `features/public/site/SiteChrome`.
 */
export function PublicLayout() {
  return (
    <SiteChrome>
      <Outlet />
    </SiteChrome>
  );
}
