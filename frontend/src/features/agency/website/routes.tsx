import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';

/** Agency portal area: Website & CMS (public site content, blog, careers). Paths are relative to /agency. */
export const nav: PortalNavItem[] = [];

export const routes: RouteObject[] = [];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [];
