import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { FormTemplateAdmin, PageTemplateAdmin } from './api';
import {
  FormTemplateDialog,
  PageTemplateDialog,
  SaveAsTemplateDialog,
  TemplateLibraryPage,
} from './TemplateLibraryPage';

const pageTemplate: PageTemplateAdmin = {
  key: 'lead-generation',
  name: 'Lead generation',
  category: 'Lead gen',
  description: 'Book a call.',
  metaTitle: 'Get a plan',
  metaDescription: 'Free plan.',
  formTemplateKey: 'contact',
  blockCount: 6,
  sortOrder: 10,
  isActive: true,
  isCustom: false,
  isCustomized: false,
  pagesUsing: 3,
  concurrencyStamp: 'pt',
};

const formTemplate: FormTemplateAdmin = {
  key: 'contact',
  name: 'Contact us',
  description: 'Everyday enquiry form.',
  schema: {
    steps: [
      {
        id: 's1',
        title: null,
        description: null,
        fields: [{ key: 'email', type: 'email', label: 'Email', required: true }],
      },
    ],
  } as FormTemplateAdmin['schema'],
  submitLabel: 'Send',
  successMessage: 'Thanks!',
  consentText: null,
  autoresponderSubject: null,
  autoresponderBody: null,
  sortOrder: 10,
  isActive: true,
  isCustom: false,
  isCustomized: false,
  formsUsing: 2,
  concurrencyStamp: 'ft',
};

describe('template library', () => {
  it('is read-only without settings.manage and explains why', async () => {
    const user = userEvent.setup();
    mockFetch({
      'POST /auth/refresh': () =>
        json(200, session(makeUser({ roles: ['Designer'], permissions: ['forms.manage'] }))),
      'GET /agency/pages/admin/templates': () => json(200, [pageTemplate]),
      'GET /agency/pages/admin/form-templates': () => json(200, [formTemplate]),
    });
    const { container } = renderWithApp(<TemplateLibraryPage />);
    expect(await screen.findByText(/needs the settings.manage permission/)).toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'Actions for Lead generation' }));
    expect(screen.getByRole('menuitem', { name: /Edit details/ })).toHaveAttribute('aria-disabled', 'true');
    await user.keyboard('{Escape}');
    expect(await axeViolations(container)).toEqual([]);
  });

  it('edits a page template with its stamp and shows a conflict', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'PUT /agency/pages/admin/templates/lead-generation': () =>
        problem(
          409,
          'concurrency.conflict',
          'This template was changed by someone else. Reload and try again.',
        ),
    });
    const { container } = renderWithApp(
      <PageTemplateDialog
        template={pageTemplate}
        formTemplates={[formTemplate]}
        onClose={() => {}}
        onSaved={() => {}}
      />,
      {
        withAuth: false,
      },
    );
    const dialog = await screen.findByRole('dialog');
    const name = within(dialog).getByLabelText(/^Name/);
    await user.clear(name);
    await user.type(name, 'Lead gen (agency)');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    expect(
      await within(dialog).findByText('This template was changed by someone else. Reload and try again.'),
    ).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({
      name: 'Lead gen (agency)',
      formTemplateKey: 'contact',
      concurrencyStamp: 'pt',
    });
    expect(await axeViolations(container)).toEqual([]);
  });

  it('edits form template texts and keeps the schema', async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    const { calls } = mockFetch({
      'PUT /agency/pages/admin/form-templates/contact': () => json(200, formTemplate),
    });
    const { container } = renderWithApp(
      <FormTemplateDialog template={formTemplate} onClose={() => {}} onSaved={onSaved} />,
      { withAuth: false },
    );
    const dialog = await screen.findByRole('dialog');
    const label = within(dialog).getByLabelText(/Button label/);
    await user.clear(label);
    await user.type(label, 'Send it');
    expect(await axeViolations(container)).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({
      submitLabel: 'Send it',
      schema: formTemplate.schema,
      consentText: null,
    });
  });

  it('saves a page as a template', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /agency/pages/landing-pages/p1/save-as-template': () =>
        json(200, { ...pageTemplate, key: 'custom-1', isCustom: true }),
    });
    const { container } = renderWithApp(
      <SaveAsTemplateDialog kind="page" id="p1" defaultName="Spring offer" onClose={() => {}} />,
      { withAuth: false },
    );
    const dialog = await screen.findByRole('dialog', { name: 'Save page as template' });
    expect(await axeViolations(container)).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Save template' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
        name: 'Spring offer',
        category: 'Custom',
      }),
    );
  });
});
