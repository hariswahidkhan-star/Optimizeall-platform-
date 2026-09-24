import { type Page, expect, test } from '@playwright/test';
import {
  API_URL,
  accounts,
  actor,
  anonGet,
  api,
  imageForm,
  jsonLd,
  landing,
  makePng,
  modal,
  openPublic,
  recall,
  refused,
  remember,
  runId,
  sitemapPaths,
  state,
  toast,
  watchErrors,
} from './support/content';

/**
 * Service catalog and the rest of the dynamic site content, as the website editor:
 *   a new service line and service (draft → hidden) with packages (price validation, one "most popular", inactive
 *   packages hidden) → publish → /services, /services/:slug (JSON-LD Service + FAQ), /pricing, search and the sitemap →
 *   reorder → unpublishing the service line hides everything in it → delete.
 *   Case studies (each metric labelled measured or estimated), testimonials, team members (photo upload with type,
 *   size and dimension limits) and industries: publish → visible, unpublish → gone, stale edits → 409.
 */

interface Service {
  id: string;
  slug: string;
  name: string;
  concurrencyStamp: string;
  packages: { id: string; name: string; isMostPopular: boolean; concurrencyStamp: string }[];
  [key: string]: unknown;
}

const drawer = (page: Page, name: string) => page.getByRole('dialog', { name });

test.describe.serial('service catalog and site content', () => {
  test('service line → service with packages → publish → services, detail, pricing, search and sitemap', async ({
    browser,
  }) => {
    const id = runId();
    const lineName = `E2E Growth Lab ${id}`;
    const lineSlug = `e2e-growth-lab-${id}`;
    const serviceName = `Conversion sprints ${id}`;
    const serviceSlug = `conversion-sprints-${id}`;
    const editor = await actor(browser, state().editor, landing.agency);
    const errors = watchErrors(editor);
    errors.ignore(/HTTP 400 POST \S+\/agency\/website\/services\/[0-9a-f-]+\/packages$/); // the invalid currency, on purpose
    await editor
      .getByRole('navigation', { name: 'Agency navigation' })
      .getByRole('link', { name: 'Services & packages' })
      .click();

    // ---------------------------------------------------------------- a new, published service line
    await editor.getByRole('tab', { name: 'Service lines' }).click();
    await editor.getByRole('button', { name: 'New service line' }).click();
    const line = drawer(editor, 'New service line');
    await line.getByLabel('Name', { exact: true }).fill(lineName);
    await line.getByLabel('Slug', { exact: true }).fill(lineSlug);
    await line.getByLabel('Description (optional)').fill('Experiments that turn visits into revenue.');
    await expect(line.getByRole('switch', { name: 'Published' })).toBeChecked(); // new lines default to published
    await line.getByRole('button', { name: 'Save' }).click();
    await expect(toast(editor, 'Service line created')).toBeVisible();

    // ---------------------------------------------------------------- a draft service in it
    await editor.getByRole('tab', { name: 'Services', exact: true }).click();
    await editor.getByRole('button', { name: 'New service' }).click();
    const svc = drawer(editor, 'New service');
    await svc.getByLabel('Name', { exact: true }).fill(serviceName);
    await svc.getByLabel('Slug', { exact: true }).fill(serviceSlug);
    await svc.getByLabel('Service line', { exact: true }).selectOption({ label: lineName });
    await svc.getByLabel('Tagline', { exact: true }).fill('Two-week CRO sprints with measured lifts.');
    await svc
      .getByLabel('Overview (optional)')
      .fill('## How a sprint works\n\nResearch, hypotheses, tests, and a readout.');
    await svc
      .getByLabel('Deliverables (optional)')
      .fill('Research readout\nThree A/B tests\nWinning variant shipped');
    await svc.getByRole('button', { name: 'Save' }).click();
    await expect(toast(editor, 'Service created')).toBeVisible();

    const editorApi = await api(state().editor);
    const list = await editorApi.get<{ items: { id: string; slug: string }[] }>(
      `/agency/website/services?search=${encodeURIComponent(serviceSlug)}`,
    );
    const serviceId = list.items.find((s) => s.slug === serviceSlug)!.id;
    remember('service', { id: serviceId, slug: serviceSlug, name: serviceName, lineSlug });

    // Draft: 404 publicly, not in the sitemap, not in search.
    expect((await anonGet(`/api/v1/public/services/${serviceSlug}`)).status).toBe(404);
    expect(await sitemapPaths()).not.toContain(`/services/${serviceSlug}`);

    // ---------------------------------------------------------------- packages (in the service's editor)
    const row = editor.getByRole('row').filter({ hasText: serviceName });
    await row.getByRole('button', { name: `Edit ${serviceName}` }).click();
    const edit = drawer(editor, 'Edit service');
    await edit.getByRole('button', { name: 'Add package' }).click();
    const pkg = modal(editor, 'New package');
    await pkg.getByLabel('Name', { exact: true }).fill('Starter sprint');
    await pkg.getByLabel('Price (optional)').fill('1500');
    await pkg.getByLabel('Included features (optional)').fill('1 test\nReadout call');
    await pkg.getByLabel('Currency', { exact: true }).fill('US');
    await pkg.getByRole('button', { name: 'Save package' }).click();
    await expect(pkg.getByText('Use a three-letter currency code such as USD.')).toBeVisible();
    await pkg.getByLabel('Currency', { exact: true }).fill('USD');
    await pkg.getByRole('button', { name: 'Save package' }).click();
    await expect(toast(editor, 'Package saved')).toBeVisible();
    await expect(edit.getByRole('button', { name: 'Edit Starter sprint' })).toBeVisible();

    // Two more packages through the API: "Growth" most popular, then "Scale" most popular (only one may be), and an
    // inactive "Legacy" package that must never be shown.
    const add = (body: Record<string, unknown>) =>
      editorApi.post(`/agency/website/services/${serviceId}/packages`, {
        currency: 'USD',
        billingPeriod: 'Monthly',
        features: ['Everything in Starter'],
        isActive: true,
        sortOrder: 20,
        ...body,
      });
    // A custom quote with a fixed price is contradictory (the editor never sends one; the API refuses it too).
    const contradictory = await refused(add({ name: 'Bespoke', price: 100, isCustomQuote: true }));
    expect(contradictory.status).toBe(400);
    expect(JSON.stringify(contradictory.body)).toContain('Custom-quote packages have no fixed price.');
    await add({ name: 'Growth sprint', price: 3500, isMostPopular: true });
    await add({ name: 'Scale programme', price: 7000, isMostPopular: true, sortOrder: 30 });
    await add({ name: 'Legacy retainer', price: 900, isActive: false, sortOrder: 40 });
    const withPackages = await editorApi.get<Service>(`/agency/website/services/${serviceId}`);
    expect(withPackages.packages.filter((p) => p.isMostPopular).map((p) => p.name)).toEqual([
      'Scale programme',
    ]);

    // ---------------------------------------------------------------- publish the service
    await edit.getByRole('switch', { name: 'Published' }).click();
    await edit.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(toast(editor, 'Service saved')).toBeVisible();
    errors.expectClean('the services admin');

    const detail = await openPublic(browser, `/services/${serviceSlug}`);
    const detailErrors = watchErrors(detail);
    await expect(detail.getByRole('heading', { level: 1 })).toContainText(serviceName);
    await expect(
      detail.getByRole('main').getByRole('heading', { level: 2, name: 'How a sprint works' }),
    ).toBeVisible();
    await expect(detail.getByRole('main').getByText('Three A/B tests')).toBeVisible();
    for (const name of ['Starter sprint', 'Growth sprint', 'Scale programme'])
      await expect(detail.getByRole('article', { name: new RegExp(name) })).toBeVisible();
    await expect(detail.getByRole('article', { name: /Legacy retainer/ })).toHaveCount(0);
    await expect(
      detail.getByRole('article', { name: /Scale programme/ }).getByText('Most popular'),
    ).toBeVisible();
    await expect(
      detail.getByRole('article', { name: /Growth sprint/ }).getByText('Most popular'),
    ).toHaveCount(0);
    const ld = await jsonLd(detail);
    const serviceLd = ld.find((x) => x['@type'] === 'Service') as
      { name: string; offers?: unknown } | undefined;
    expect(serviceLd?.name).toBe(serviceName);
    expect(ld.some((x) => x['@type'] === 'BreadcrumbList')).toBe(true);
    await expect(detail).toHaveTitle(new RegExp(`^${serviceName}`));
    detailErrors.expectClean('the service page');

    const services = await openPublic(browser, '/services');
    await expect(services.getByRole('main').getByText(lineName).first()).toBeVisible();
    await expect(
      services
        .getByRole('main')
        .getByRole('link', { name: new RegExp(serviceName) })
        .first(),
    ).toBeVisible();
    const pricing = await openPublic(browser, '/pricing');
    const pricingSection = pricing
      .locator('section')
      .filter({ has: pricing.getByRole('heading', { name: serviceName }) });
    await expect(pricingSection.getByRole('article', { name: /Scale programme/ })).toBeVisible();
    await expect(pricingSection.getByRole('article', { name: /Legacy retainer/ })).toHaveCount(0);
    const search = (await anonGet(`/api/v1/public/search?q=${encodeURIComponent(serviceName)}`)).json() as {
      services: { slug: string }[];
    };
    expect(search.services.map((s) => s.slug)).toContain(serviceSlug);
    expect(await sitemapPaths()).toContain(`/services/${serviceSlug}`);
  });

  test('ordering, unpublishing a service line and deleting a service reach the public site', async ({
    browser,
  }) => {
    const id = runId();
    const editorApi = await api(state().editor);
    const categories = await editorApi.get<{ id: string; slug: string }[]>(
      '/agency/website/service-categories',
    );
    const line = categories.find((c) => c.slug === `e2e-growth-lab-${id}`)!;
    const svc = await editorApi.get<{ items: { id: string; slug: string; name: string }[] }>(
      `/agency/website/services?categoryId=${line.id}`,
    );
    const first = svc.items[0]!;
    // A second service in the same line, published, then put first.
    const second = await editorApi.post<Service>('/agency/website/services', {
      categoryId: line.id,
      slug: `landing-audits-${id}`,
      name: `Landing audits ${id}`,
      tagline: 'A teardown of your top landing pages.',
      deliverables: ['Audit deck'],
      seo: {},
      isPublished: true,
      sortOrder: 50,
    });
    const order = async () => {
      const groups = (await anonGet('/api/v1/public/services')).json() as {
        slug: string;
        services: { slug: string }[];
      }[];
      return groups.find((g) => g.slug === line.slug)?.services.map((s) => s.slug) ?? [];
    };
    expect(await order()).toEqual([first.slug, second.slug]);
    await editorApi.post('/agency/website/services/reorder', { ids: [second.id, first.id] });
    expect(await order()).toEqual([second.slug, first.slug]);
    // Reorder with a duplicate id is refused.
    expect(
      (await refused(editorApi.post('/agency/website/services/reorder', { ids: [second.id, second.id] })))
        .status,
    ).toBe(400);

    // Unpublishing the service line hides its services everywhere (menu, list, detail, sitemap).
    const full = (
      await editorApi.get<
        {
          id: string;
          slug: string;
          name: string;
          description: string | null;
          icon: string | null;
          sortOrder: number;
          concurrencyStamp: string;
        }[]
      >('/agency/website/service-categories')
    ).find((c) => c.id === line.id)!;
    await editorApi.put(`/agency/website/service-categories/${line.id}`, { ...full, isPublished: false });
    expect(await order()).toEqual([]);
    expect((await anonGet(`/api/v1/public/services/${first.slug}`)).status).toBe(404);
    expect(await sitemapPaths()).not.toContain(`/services/${first.slug}`);
    const site = (await anonGet('/api/v1/public/site')).json() as {
      servicesMenu?: { slug: string }[];
      menu?: { slug: string }[];
    };
    expect(JSON.stringify(site)).not.toContain(line.slug);
    const hidden = await openPublic(browser, `/services/${first.slug}`);
    await expect(hidden.getByRole('heading', { name: "We couldn't find that service" })).toBeVisible();

    // A service line that still has services cannot be deleted.
    const inUse = await refused(editorApi.delete(`/agency/website/service-categories/${line.id}`));
    expect(inUse.status).toBe(409);
    expect(inUse.code).toBe('website.category_in_use');

    // Deleting a service removes it (and its packages).
    await editorApi.delete(`/agency/website/services/${second.id}`);
    expect((await refused(editorApi.get(`/agency/website/services/${second.id}`))).status).toBe(404);
    // Republish the line for later specs.
    const again = (await editorApi.get<(typeof full)[]>('/agency/website/service-categories')).find(
      (c) => c.id === line.id,
    )!;
    await editorApi.put(`/agency/website/service-categories/${line.id}`, { ...again, isPublished: true });
    expect(await order()).toEqual([first.slug]);
  });

  test('case study, testimonial, team member and industry: publish → visible, unpublish → gone', async ({
    browser,
  }) => {
    const id = runId();
    const editorApi = await api(state().editor);
    const service = recall<{ id: string; slug: string }>('service');

    // ---------------------------------------------------------------- case study: every metric must say measured/estimated
    const caseBody = {
      slug: `e2e-sprint-results-${id}`,
      title: `Sprint results ${id}`,
      clientName: 'A DTC coffee brand',
      clientAnonymized: true,
      summary: 'Three sprints, one checkout rebuild.',
      serviceIds: [service.id],
      challengeMarkdown: 'Checkout abandonment at 81%.',
      strategyMarkdown: 'Test the payment step first.',
      executionMarkdown: 'Three sprints over six weeks.',
      metrics: [
        { label: 'Checkout conversion', value: '+38%', measurement: 'Measured', context: 'GA4, 6 weeks' },
        { label: 'Annual revenue impact', value: '$1.2M', measurement: null },
      ],
      seo: {},
      isPublished: true,
      isFeatured: false,
      sortOrder: 0,
    };
    const missing = await refused(editorApi.post('/agency/website/case-studies', caseBody));
    expect(missing.status).toBe(400);
    expect(JSON.stringify(missing.body)).toContain('Say whether the figure is measured or estimated.');
    caseBody.metrics[1]!.measurement = 'Estimated';
    const cs = await editorApi.post<{ id: string; concurrencyStamp: string }>(
      '/agency/website/case-studies',
      caseBody,
    );
    const casePage = await openPublic(browser, `/case-studies/${caseBody.slug}`);
    await expect(casePage.getByRole('heading', { level: 1, name: caseBody.title })).toBeVisible();
    const main = casePage.getByRole('main');
    await expect(main.getByText('+38%')).toBeVisible();
    await expect(main.getByText('Measured', { exact: true }).first()).toBeVisible();
    await expect(main.getByText('Estimated', { exact: true }).first()).toBeVisible();
    expect((await jsonLd(casePage)).some((x) => x['@type'] === 'Article')).toBe(true);
    // Linked from the service it used.
    const svcDto = (await anonGet(`/api/v1/public/services/${service.slug}`)).json() as {
      caseStudies: { slug: string }[];
    };
    expect(svcDto.caseStudies.map((c) => c.slug)).toContain(caseBody.slug);
    // Stale edit → 409; current stamp → saved; unpublish → 404.
    const stale = await refused(
      editorApi.put(`/agency/website/case-studies/${cs.id}`, {
        ...caseBody,
        concurrencyStamp: '00000000-0000-0000-0000-00000000abcd',
      }),
    );
    expect(stale.status).toBe(409);
    await editorApi.put(`/agency/website/case-studies/${cs.id}`, {
      ...caseBody,
      isPublished: false,
      concurrencyStamp: cs.concurrencyStamp,
    });
    expect((await anonGet(`/api/v1/public/case-studies/${caseBody.slug}`)).status).toBe(404);
    expect(await sitemapPaths()).not.toContain(`/case-studies/${caseBody.slug}`);

    // ---------------------------------------------------------------- testimonial (published, featured → first on the home page)
    const quote = `They doubled our demo bookings in a quarter (${id}).`;
    const t = await editorApi.post<{ id: string; concurrencyStamp: string }>('/agency/website/testimonials', {
      quote,
      authorName: 'Robin Reyes',
      authorRole: 'Head of Growth',
      company: 'Brewline',
      rating: 5,
      serviceId: service.id,
      isPublished: true,
      isFeatured: true,
      sortOrder: -100,
    });
    const home = await openPublic(browser, '/');
    await expect(home.getByRole('main').getByText(quote)).toBeVisible();
    const pub = (await anonGet('/api/v1/public/testimonials')).json() as { quote: string }[];
    expect(pub.map((x) => x.quote)).toContain(quote);
    await editorApi.put(`/agency/website/testimonials/${t.id}`, {
      quote,
      authorName: 'Robin Reyes',
      isPublished: false,
      isFeatured: true,
      sortOrder: -100,
      concurrencyStamp: t.concurrencyStamp,
    });
    const after = (await anonGet('/api/v1/public/testimonials')).json() as { quote: string }[];
    expect(after.map((x) => x.quote)).not.toContain(quote);

    // ---------------------------------------------------------------- industry
    const industry = await editorApi.post<{ id: string }>('/agency/website/industries', {
      slug: `e2e-coffee-${id}`,
      name: `Coffee roasters ${id}`,
      summary: 'Growth for specialty coffee brands.',
      bodyMarkdown: 'Subscriptions, wholesale and cafés.',
      challenges: ['Seasonal demand'],
      serviceIds: [service.id],
      seo: {},
      isPublished: true,
      sortOrder: 0,
    });
    const ind = await openPublic(browser, `/industries/e2e-coffee-${id}`);
    await expect(ind.getByRole('heading', { level: 1 })).toContainText(`Coffee roasters ${id}`);
    expect(await sitemapPaths()).toContain(`/industries/e2e-coffee-${id}`);
    await editorApi.delete(`/agency/website/industries/${industry.id}`);
    expect((await anonGet(`/api/v1/public/industries/e2e-coffee-${id}`)).status).toBe(404);
  });

  test('team member with an uploaded photo; image uploads are checked by content, size and dimensions', async ({
    browser,
  }) => {
    const id = runId();
    const name = `Tam Tester ${id}`;
    const editor = await actor(browser, state().editor, landing.agency);
    const errors = watchErrors(editor);
    errors.ignore(/HTTP 400 POST \S+\/agency\/website\/images$/);
    await editor.goto('/agency/website/team');
    await editor.getByRole('button', { name: 'New team member' }).click();
    const form = drawer(editor, 'New team member');
    await form.getByLabel('Name', { exact: true }).fill(name);
    await form.getByLabel('Slug', { exact: true }).fill(`tam-tester-${id}`);
    await form.getByLabel('Role', { exact: true }).fill('CRO lead');

    // A text file named .png is refused by content (magic bytes), a tiny image by its dimensions.
    const photoDrop = form.locator('input[type="file"]').first();
    await photoDrop.setInputFiles({
      name: 'notes.png',
      mimeType: 'image/png',
      buffer: Buffer.from('definitely not an image'),
    });
    await form.getByRole('button', { name: 'Upload image' }).click();
    await expect(form.getByText('Upload a PNG, JPEG or WebP image.')).toBeVisible();
    await photoDrop.setInputFiles({ name: 'tiny.png', mimeType: 'image/png', buffer: makePng(3, 64, 64) });
    await form.getByRole('button', { name: 'Upload image' }).click();
    await expect(form.getByText('Images must be at least 200×200 pixels.')).toBeVisible();
    await photoDrop.setInputFiles({
      name: 'tam.png',
      mimeType: 'image/png',
      buffer: makePng(Number.parseInt(id.slice(-4), 36) % 997, 400, 400),
    });
    await form.getByRole('button', { name: 'Upload image' }).click();
    await expect(toast(editor, 'Image uploaded')).toBeVisible();
    await expect(form.getByLabel('Photo (optional)')).toHaveValue(/^\/api\/v1\/files\/[0-9a-f-]{36}$/);
    const photoUrl = await form.getByLabel('Photo (optional)').inputValue();
    await expect(form.getByRole('switch', { name: 'Show on the team page' })).toBeChecked(); // new members are shown
    await form.getByRole('button', { name: 'Save' }).click();
    await expect(toast(editor, 'Team member created')).toBeVisible();
    errors.expectClean('the team admin');

    // Public: the team page shows the member with the uploaded photo (served anonymously, no metadata, nosniff).
    const team = await openPublic(browser, '/team');
    const card = team.getByRole('listitem').filter({ has: team.getByRole('heading', { name }) });
    await expect(card).toBeVisible();
    await expect(card.locator('img')).toHaveAttribute('src', photoUrl);
    const img = await fetch(`${API_URL}${photoUrl}`);
    expect(img.status).toBe(200);
    expect(img.headers.get('content-type')).toBe('image/png');
    expect(img.headers.get('x-content-type-options')).toBe('nosniff');

    // Over 10 MB is refused before it is even inspected; an arbitrary external URL is refused as an image.
    const editorApi = await api(state().editor);
    const big = new FormData();
    big.append(
      'file',
      new Blob([new Uint8Array(10 * 1024 * 1024 + 1024)], { type: 'image/png' }),
      'huge.png',
    );
    const tooBig = await refused(editorApi.upload('/agency/website/images', big));
    expect(tooBig.status).toBe(400);
    expect(tooBig.code).toBe('file.too_large');
    const members =
      await editorApi.get<
        { id: string; slug: string; concurrencyStamp: string; name: string; role: string }[]
      >('/agency/website/team');
    const me = members.find((m) => m.slug === `tam-tester-${id}`)!;
    const external = await refused(
      editorApi.put(`/agency/website/team/${me.id}`, {
        slug: me.slug,
        name: me.name,
        role: me.role,
        photoUrl: 'https://evil.example.com/tracker.png',
        isPublished: true,
        concurrencyStamp: me.concurrencyStamp,
      }),
    );
    expect(external.status).toBe(400);
    expect(JSON.stringify(external.body)).toContain('Use an uploaded image');
    // Unauthorised uploads: a user without any website permission.
    const am = await api(accounts.am);
    expect((await refused(am.upload('/agency/website/images', imageForm(5)))).status).toBe(403);

    // Hide from the team page.
    await editorApi.put(`/agency/website/team/${me.id}`, {
      slug: me.slug,
      name: me.name,
      role: me.role,
      photoUrl,
      isPublished: false,
      concurrencyStamp: me.concurrencyStamp,
    });
    const team2 = (await anonGet('/api/v1/public/team')).json() as { slug: string }[];
    expect(team2.map((m) => m.slug)).not.toContain(me.slug);
  });
});
