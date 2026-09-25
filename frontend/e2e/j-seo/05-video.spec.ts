import { expect, test } from '@playwright/test';
import { ApiSession } from '../journeys/support/api';
import { BASE, expectValidJsonLd, noJsPage, readHead } from './support/seo';

/**
 * Website videos (self-hosted files under /media/videos/): an editor adds a Video block to a CMS page; the page then has
 * an accessible, lazy player (poster, captions track, download fallback) with and without JavaScript, a VideoObject in
 * its JSON-LD and an entry in the video sitemap. The page is deleted at the end.
 */
test.describe.serial('video block', () => {
  const slug = `e2e-video-${Date.now().toString(36)}`;
  let session: ApiSession;
  let pageId = '';

  test.beforeAll(async () => {
    session = await ApiSession.login(
      process.env.E2E_ADMIN_EMAIL ?? 'admin@demo.optimizeall.app',
      process.env.E2E_ADMIN_PASSWORD ?? 'Demo#2026!pass',
    );
    const created = await session.post<{ id: string }>('/agency/website/pages', {
      slug,
      title: 'Our audit process on video',
      summary:
        'Watch a senior strategist audit a website step by step: tracking, search visibility, paid media and a 90-day plan.',
      kind: 'Standard',
      isPublished: true,
      blocks: [
        {
          type: 'video',
          data: {
            title: 'How we run a marketing audit',
            description: 'A two-minute walkthrough of our free marketing audit.',
            mp4Url: '/media/videos/marketing-audit.mp4',
            webmUrl: '/media/videos/marketing-audit.webm',
            posterUrl: '/media/videos/marketing-audit.jpg',
            captionsUrl: '/media/videos/marketing-audit.en.vtt',
            captionsLanguage: 'en',
            durationSeconds: 124,
            uploadDate: '2026-09-20',
            transcript: 'We start with your **tracking**, then search and paid media.',
          },
        },
      ],
    });
    pageId = created.id;
  });

  test.afterAll(async () => {
    if (pageId) await session.delete(`/agency/website/pages/${pageId}`).catch(() => undefined);
  });

  test('without JavaScript: a lazy player with poster and captions, and VideoObject JSON-LD', async ({
    browser,
  }) => {
    const page = await noJsPage(browser);
    expect((await page.goto(`/${slug}`))!.status()).toBe(200);
    const video = page.locator('#oa-ssr video');
    await expect(video).toHaveAttribute('preload', 'none');
    await expect(video).toHaveAttribute('poster', `${BASE}/media/videos/marketing-audit.jpg`);
    await expect(video.locator('track[kind="captions"][srclang="en"]')).toHaveCount(1);
    await expect(video.locator('source[type="video/mp4"]')).toHaveCount(1);
    const head = await readHead(page);
    const ld = head.jsonLd.find((n) => n['@type'] === 'VideoObject')!;
    expectValidJsonLd(ld, 'VideoObject');
    expect(ld.duration).toBe('PT2M4S');
    expect(ld.contentUrl).toBe(`${BASE}/media/videos/marketing-audit.mp4`);
    await page.context().close();
  });

  test('with JavaScript: the app renders the same accessible player', async ({ page }) => {
    await page.goto(`/${slug}`);
    const video = page.locator('video.site-video__player');
    await expect(video).toBeVisible();
    await expect(video).toHaveAttribute('preload', 'none');
    await expect(video).toHaveAttribute('aria-label', 'How we run a marketing audit');
    await expect(video.locator('track[kind="captions"]')).toHaveAttribute('label', 'English');
    // The 16:9 box is reserved before anything loads: no layout shift.
    const box = await video.boundingBox();
    expect(Math.abs(box!.width / box!.height - 16 / 9)).toBeLessThan(0.02);
    await page.getByText('Transcript').click();
    await expect(page.getByText('tracking', { exact: true })).toBeVisible();
  });

  test('the video sitemap lists it with thumbnail, duration and date', async ({ request }) => {
    const xml = await (await request.get(`${BASE}/sitemaps/videos.xml`)).text();
    expect(xml).toContain(`<loc>${BASE}/${slug}</loc>`);
    expect(xml).toContain(
      `<video:thumbnail_loc>${BASE}/media/videos/marketing-audit.jpg</video:thumbnail_loc>`,
    );
    expect(xml).toContain('<video:duration>124</video:duration>');
    expect(xml).toContain('<video:title>How we run a marketing audit</video:title>');
  });
});
