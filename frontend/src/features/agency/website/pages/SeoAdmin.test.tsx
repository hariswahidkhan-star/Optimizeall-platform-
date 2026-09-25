import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { SeoAdminPage, type SeoOverview, type SeoOverviewRow, type SeoSettings } from './SeoAdmin';

const staff = makeUser({
  id: 'staff-1',
  roles: ['Admin'],
  permissions: ['site.manage'],
  displayName: 'Web Editor',
});

const row = (over: Partial<SeoOverviewRow>): SeoOverviewRow => ({
  path: '/services',
  url: 'https://www.optimizeall.com/services',
  status: 200,
  title: 'Marketing Services: SEO, Ads, Social & Web | Optimize All',
  titleLength: 57,
  description: 'SEO, Google and Meta ads, social media and more from one accountable team.',
  descriptionLength: 75,
  canonical: 'https://www.optimizeall.com/services',
  indexable: true,
  robots: 'index, follow',
  inSitemap: true,
  jsonLdTypes: ['BreadcrumbList', 'ItemList'],
  h1Count: 1,
  imageCount: 0,
  videoCount: 0,
  lastModified: null,
  source: 'Page texts',
  editPath: '/agency/website/copy',
  copyKeys: { title: 'services.seo.title', description: 'services.seo.description' },
  warnings: [],
  ...over,
});

const overview: SeoOverview = {
  siteUrl: 'https://www.optimizeall.com',
  total: 2,
  indexable: 2,
  withErrors: 0,
  withWarnings: 1,
  generatedAt: '2026-09-24T10:00:00Z',
  rows: [
    row({}),
    row({
      path: '/blog/old-post',
      title: 'A very long blog post title that goes on well past the sixty character limit | Optimize All',
      titleLength: 92,
      source: 'Blog post',
      editPath: '/agency/website/blog',
      copyKeys: null,
      warnings: [
        {
          code: 'title.long',
          severity: 'warning',
          message: 'The title is 92 characters; search results show about 60.',
        },
      ],
    }),
  ],
};

const settings: SeoSettings = {
  crawlerGroups: [
    {
      key: 'search',
      label: 'Search engines',
      description: 'Google, Bing…',
      allowedByDefault: true,
      allowed: true,
      userAgents: ['Googlebot'],
    },
    {
      key: 'aiTraining',
      label: 'AI model training',
      description: 'GPTBot…',
      allowedByDefault: true,
      allowed: true,
      userAgents: ['GPTBot'],
    },
  ],
  indexNowEnabled: false,
  indexNowKey: null,
  llmsTxtEnabled: true,
  securityContactEmail: null,
  updatedAt: '2026-09-24T10:00:00Z',
  concurrencyStamp: 'stamp-1',
};

describe('Website → SEO', () => {
  it('lists pages with issues first, links content to its editor and edits built-in snippets in place', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/website/seo/overview': () => json(200, overview),
      'GET /agency/website/copy': () =>
        json(200, {
          groups: [
            {
              id: 'services',
              label: 'Services pages',
              scope: 'Website',
              entries: [
                {
                  key: 'services.seo.title',
                  label: 'Search title',
                  type: 'Text',
                  default: 'x',
                  value: 'Marketing Services: SEO, Ads, Social & Web',
                  isCustomized: false,
                  placeholders: [],
                  maxLength: 300,
                  updatedAt: null,
                  concurrencyStamp: null,
                },
                {
                  key: 'services.seo.description',
                  label: 'Search description',
                  type: 'Textarea',
                  default: 'y',
                  value: 'Old description',
                  isCustomized: false,
                  placeholders: [],
                  maxLength: 5000,
                  updatedAt: null,
                  concurrencyStamp: null,
                },
              ],
            },
          ],
        }),
      'PUT /agency/website/copy': () => json(200, { groups: [] }),
    });
    const { container } = renderWithApp(<SeoAdminPage />, { route: '/agency/website/seo' });

    // The default filter shows pages with errors or warnings only.
    const table = await screen.findByRole('table', { name: 'Public URLs and their search metadata' });
    expect(await within(table).findByText('/blog/old-post')).toBeInTheDocument();
    expect(within(table).queryByText('/services')).not.toBeInTheDocument();
    expect(within(table).getByRole('link', { name: 'Edit in Blog post' })).toHaveAttribute(
      'href',
      '/agency/website/blog',
    );
    expect(await axeViolations(container)).toEqual([]);

    await user.selectOptions(screen.getByRole('combobox', { name: 'Show' }), 'all');
    await user.click(await within(table).findByRole('button', { name: 'Edit snippet' }));
    const dialog = await screen.findByRole('dialog', { name: 'Search snippet of /services' });
    const description = await within(dialog).findByLabelText(/Search description/);
    await user.clear(description);
    await user.type(description, 'A new description.');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    const put = calls.find((c) => c.method === 'PUT' && c.path === '/agency/website/copy');
    expect(put?.body).toEqual({
      changes: [
        {
          key: 'services.seo.title',
          value: 'Marketing Services: SEO, Ads, Social & Web',
          concurrencyStamp: null,
        },
        { key: 'services.seo.description', value: 'A new description.', concurrencyStamp: null },
      ],
    });
  });

  it('saves the crawler policy with the concurrency stamp', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/website/seo/overview': () => json(200, overview),
      'GET /agency/website/seo/settings': () => json(200, settings),
      'PUT /agency/website/seo/settings': () => json(200, { ...settings, concurrencyStamp: 'stamp-2' }),
    });
    renderWithApp(<SeoAdminPage />, { route: '/agency/website/seo' });
    await user.click(await screen.findByRole('tab', { name: 'Crawlers & AI' }));
    const training = await screen.findByRole('group', { name: 'AI model training' });
    await user.click(within(training).getByRole('switch'));
    await user.click(screen.getByRole('button', { name: 'Save SEO settings' }));
    await screen.findByText('SEO settings saved');
    const put = calls.find((c) => c.method === 'PUT' && c.path === '/agency/website/seo/settings');
    expect(put?.body).toMatchObject({
      crawlerGroups: { search: true, aiTraining: false },
      concurrencyStamp: 'stamp-1',
      llmsTxtEnabled: true,
    });
  });
});
