import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { problem } from '@/test/fetchMock';
import { axeViolations } from '@/test/render';
import { json, mockAdminApi, renderAdmin } from '../test/helpers';
import { CopyEditor, type CopyCatalog } from './CopyEditor';
import { EmailTemplatesTab, type EmailTemplate, type EmailTemplateSummary } from './EmailTemplatesTab';

function catalog(overrides: Partial<CopyCatalog['groups'][number]['entries'][number]> = {}): CopyCatalog {
  return {
    groups: [
      {
        id: 'helpCentre',
        label: 'Help centre (FAQ page)',
        scope: 'Portal',
        entries: [
          {
            key: 'faq.hero.title',
            label: 'Headline',
            type: 'Text',
            default: 'Frequently asked questions',
            value: 'Frequently asked questions',
            isCustomized: false,
            placeholders: [],
            maxLength: 300,
            updatedAt: null,
            concurrencyStamp: null,
            ...overrides,
          },
          {
            key: 'creators.faq.items',
            label: 'Questions and answers',
            type: 'Pairs',
            default: 'Who can join? | Anyone.',
            value: 'Who can join? | Anyone.',
            isCustomized: false,
            placeholders: [],
            maxLength: 10000,
            updatedAt: null,
            concurrencyStamp: null,
          },
        ],
      },
    ],
  };
}

function renderEditor() {
  return renderAdmin(<CopyEditor endpoint="/admin/content/copy" title="Portal texts" description="Edit the texts." />);
}

describe('CopyEditor', () => {
  it('saves only changed texts with their stamps, previews pairs and has no axe violations', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi({
      'GET /admin/content/copy': () => json(200, catalog()),
      'PUT /admin/content/copy': () =>
        json(200, catalog({ value: 'Help & answers', isCustomized: true, concurrencyStamp: 's1', updatedAt: '2026-09-23T10:00:00Z' })),
    });
    const { container } = renderEditor();

    const headline = await screen.findByRole('textbox', { name: /Headline/ });
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled();
    await user.clear(headline);
    await user.type(headline, 'Help & answers');
    expect(screen.getByText('Unsaved')).toBeInTheDocument();

    const pairs = screen.getByRole('textbox', { name: /Questions and answers/ });
    await user.type(pairs, '{enter}When? | Every two weeks.');
    const preview = screen.getByLabelText('Questions and answers: preview');
    expect(within(preview).getByText('When?')).toBeInTheDocument();
    expect(within(preview).getByText('Every two weeks.')).toBeInTheDocument();

    expect(await axeViolations(container)).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Save 2 changes' }));
    const put = calls.find((c) => c.method === 'PUT');
    expect(put?.body).toEqual({
      changes: [
        { key: 'faq.hero.title', value: 'Help & answers', concurrencyStamp: null },
        { key: 'creators.faq.items', value: 'Who can join? | Anyone.\nWhen? | Every two weeks.', concurrencyStamp: null },
      ],
    });
    expect(await screen.findByText('Customized')).toBeInTheDocument();
  });

  it('resets a customized text to its default (sent as null) and explains conflicts', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi({
      'GET /admin/content/copy': () =>
        json(200, catalog({ value: 'Help & answers', isCustomized: true, concurrencyStamp: 's1' })),
      'PUT /admin/content/copy': () => problem(409, 'concurrency.conflict', 'This text was changed by someone else.'),
    });
    renderEditor();

    await user.click(await screen.findByRole('button', { name: 'Reset to default' }));
    expect(screen.getByRole('textbox', { name: /Headline/ })).toHaveValue('Frequently asked questions');
    await user.click(screen.getByRole('button', { name: 'Save 1 change' }));
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      changes: [{ key: 'faq.hero.title', value: null, concurrencyStamp: 's1' }],
    });
    expect(await screen.findByText('Someone else changed these texts')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reload latest texts' })).toBeInTheDocument();
  });

  it('searches every page and shows server validation next to the text', async () => {
    const user = userEvent.setup();
    mockAdminApi({
      'GET /admin/content/copy': () => json(200, catalog()),
      'PUT /admin/content/copy': () =>
        problem(400, 'content.copy_invalid', 'Some texts are invalid.', {
          errors: { 'faq.hero.title': ['Use a single line.'] },
        }),
    });
    renderEditor();
    await user.type(await screen.findByRole('searchbox', { name: 'Search all texts' }), 'questions and');
    expect(screen.queryByRole('textbox', { name: /Headline/ })).not.toBeInTheDocument();
    await user.clear(screen.getByRole('searchbox', { name: 'Search all texts' }));
    await user.type(screen.getByRole('textbox', { name: /Headline/ }), '!');
    await user.click(screen.getByRole('button', { name: 'Save 1 change' }));
    expect(await screen.findByText('Use a single line.')).toBeInTheDocument();
  });
});

const summary: EmailTemplateSummary = {
  key: 'website.newsletter_confirm',
  group: 'Website emails',
  name: 'Newsletter: confirm subscription',
  description: 'Double opt-in email.',
  subject: 'Confirm your Optimize All newsletter subscription',
  isCustomized: false,
  updatedAt: null,
};

const template: EmailTemplate = {
  ...summary,
  body: 'Confirm: {{confirmUrl}}\n\nLeave: {{unsubscribeUrl}}',
  actionLabel: null,
  defaultSubject: summary.subject,
  defaultBody: 'Confirm: {{confirmUrl}}\n\nLeave: {{unsubscribeUrl}}',
  defaultActionLabel: null,
  hasActionLabel: false,
  hasHtml: false,
  variables: [
    { name: 'confirmUrl', description: 'The confirmation link.', sample: 'https://x.test/c', required: true },
    { name: 'unsubscribeUrl', description: 'The unsubscribe link.', sample: 'https://x.test/u', required: true },
    { name: 'siteName', description: 'The site name.', sample: 'Optimize All', required: false },
  ],
  concurrencyStamp: null,
};

describe('EmailTemplatesTab', () => {
  it('opens a template, previews it, inserts variables and saves with the stamp', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi({
      'GET /admin/email-templates': () => json(200, [summary]),
      'GET /admin/email-templates/website.newsletter_confirm': () => json(200, template),
      'POST /admin/email-templates/website.newsletter_confirm/preview': (req) =>
        json(200, { subject: (req.body as { subject: string }).subject, text: 'Confirm: https://x.test/c', html: null }),
      'PUT /admin/email-templates/website.newsletter_confirm': () =>
        json(200, { ...template, subject: 'Welcome to {{siteName}}', isCustomized: true, concurrencyStamp: 's1' }),
    });
    const { container } = renderAdmin(<EmailTemplatesTab />);

    await user.click(await screen.findByRole('button', { name: 'Newsletter: confirm subscription' }));
    expect(await screen.findByText('Confirm: https://x.test/c')).toBeInTheDocument();
    await waitFor(async () => expect(await axeViolations(container)).toEqual([]));

    const subject = screen.getByRole('textbox', { name: /Subject/ });
    await user.clear(subject);
    await user.type(subject, 'Welcome to ');
    const body = screen.getByRole('textbox', { name: /Email text/ });
    await user.click(body);
    await user.click(screen.getByRole('button', { name: '{{siteName}}' }));
    expect(body).toHaveValue(`${template.body}{{siteName}}`);

    await user.click(screen.getByRole('button', { name: 'Save template' }));
    const put = calls.find((c) => c.method === 'PUT');
    expect(put?.body).toMatchObject({ subject: 'Welcome to ', body: `${template.body}{{siteName}}`, concurrencyStamp: null });
    expect(await screen.findByText('Customized')).toBeInTheDocument();
  });
});
