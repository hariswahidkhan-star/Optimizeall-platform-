import { ChevronDown } from 'lucide-react';
import { Badge } from '@/components/ui/Badge';
import { Switch } from '@/components/ui/Switch';
import type { Faq } from '../api/types';
import type { ContentConfig, FormProps } from './ContentEditor';
import { ContentTab } from './ContentTab';
import { parseSortOrder, requireLength, TextAreaField, TextField } from './fields';

export interface FaqDraft {
  question: string;
  answer: string;
  category: string;
  sortOrder: string;
  isPublished: boolean;
}

const FIELDS = ['question', 'answer', 'category', 'sortOrder', 'isPublished'] as const;
const KNOWN_CATEGORIES = [
  'Getting started',
  'Eligibility',
  'Submissions',
  'Payments',
  'Disclosure & rules',
  'Account',
];

function FaqForm({ draft, setDraft, errors }: FormProps<FaqDraft>) {
  const set = <K extends keyof FaqDraft>(key: K, value: FaqDraft[K]) => setDraft({ ...draft, [key]: value });
  return (
    <>
      <TextField
        label="Question"
        required
        maxLength={300}
        value={draft.question}
        onChange={(v) => set('question', v)}
        error={errors.question}
      />
      <TextAreaField
        label="Answer"
        required
        maxLength={10000}
        rows={6}
        value={draft.answer}
        onChange={(v) => set('answer', v)}
        error={errors.answer}
        hint="Plain text. Line breaks are kept."
      />
      <TextField
        label="Category"
        required
        maxLength={60}
        value={draft.category}
        onChange={(v) => set('category', v)}
        error={errors.category}
        hint={`Questions are grouped by category on the public FAQ page. Existing: ${KNOWN_CATEGORIES.join(', ')}.`}
      />
      <TextField
        label="Sort order"
        type="number"
        value={draft.sortOrder}
        onChange={(v) => set('sortOrder', v)}
        error={errors.sortOrder}
        hint="Lower numbers show first. Use “Reorder” to arrange questions visually."
      />
      <Switch
        checked={draft.isPublished}
        onCheckedChange={(v) => set('isPublished', v)}
        label="Published"
        description="Unpublished questions are hidden from the public FAQ page."
      />
    </>
  );
}

/** Same disclosure pattern as the public FAQ page. */
function FaqPreview({ draft }: { draft: FaqDraft }) {
  return (
    <div className="stack admin-tight-stack">
      <p className="eyebrow">{draft.category || 'Category'}</p>
      <details className="admin-faq-preview" open>
        <summary>
          <span>{draft.question || 'Your question'}</span>
          <ChevronDown aria-hidden="true" />
        </summary>
        <p className="admin-prewrap">{draft.answer || 'The answer appears here.'}</p>
      </details>
    </div>
  );
}

export const faqConfig: ContentConfig<Faq, FaqDraft> = {
  kind: 'faqs',
  singular: 'question',
  fields: FIELDS,
  label: (f) => f.question,
  empty: () => ({ question: '', answer: '', category: 'Getting started', sortOrder: '0', isPublished: true }),
  fromItem: (f) => ({
    question: f.question,
    answer: f.answer,
    category: f.category,
    sortOrder: String(f.sortOrder),
    isPublished: f.isPublished,
  }),
  toRequest: (d) => {
    const errors: Record<string, string> = {};
    requireLength(errors, 'question', d.question, 5, 300, 'The question');
    requireLength(errors, 'answer', d.answer, 1, 10000, 'an answer');
    requireLength(errors, 'category', d.category, 1, 60, 'a category');
    const sortOrder = parseSortOrder(errors, d.sortOrder);
    return {
      errors,
      body: {
        question: d.question.trim(),
        answer: d.answer.trim(),
        category: d.category.trim(),
        sortOrder,
        isPublished: d.isPublished,
      },
    };
  },
  Form: FaqForm,
  Preview: FaqPreview,
};

export function FaqsTab() {
  return (
    <ContentTab
      config={faqConfig}
      title="FAQs"
      description="Questions on the public FAQ page, grouped by category in sort order."
      reorderable
      filters={[
        { id: 'category', label: 'Category', options: KNOWN_CATEGORIES.map((c) => ({ value: c, label: c })) },
        {
          id: 'isPublished',
          label: 'Published',
          options: [
            { value: 'true', label: 'Published' },
            { value: 'false', label: 'Unpublished' },
          ],
        },
      ]}
      columns={[
        { id: 'question', header: 'Question', primary: true, cell: (f) => <strong>{f.question}</strong> },
        { id: 'category', header: 'Category', cell: (f) => f.category },
        { id: 'order', header: 'Order', align: 'right', cell: (f) => f.sortOrder },
        {
          id: 'published',
          header: 'Status',
          cell: (f) =>
            f.isPublished ? (
              <Badge tone="success">Published</Badge>
            ) : (
              <Badge tone="neutral">Unpublished</Badge>
            ),
        },
      ]}
      renderReorderItem={(f) => (
        <div className="admin-cell-stack">
          <strong>{f.question}</strong>
          <span className="text-small text-muted">{f.category}</span>
        </div>
      )}
    />
  );
}
