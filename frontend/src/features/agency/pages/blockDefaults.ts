import type { Block, BlockType } from './api';

export const blockLabels: Record<BlockType, string> = {
  hero: 'Hero',
  text: 'Text',
  image: 'Image',
  video: 'Video (YouTube/Vimeo)',
  features: 'Features',
  testimonials: 'Testimonials',
  pricing: 'Pricing',
  faq: 'FAQ',
  countdown: 'Countdown',
  form: 'Form',
  cta: 'Call to action',
  logos: 'Logos',
  spacer: 'Spacer',
};

export const blockTypes = Object.keys(blockLabels) as BlockType[];

function newId(type: BlockType, existing: Block[]): string {
  let n = 1;
  while (existing.some((b) => b.id === `${type}-${n}`)) n++;
  return `${type}-${n}`;
}

/** A new block with sensible, valid default content. */
export function createBlock(type: BlockType, existing: Block[], formId?: string): Block {
  const id = newId(type, existing);
  switch (type) {
    case 'hero':
      return { id, type, props: { headline: 'A clear, benefit-led headline', subheadline: 'One sentence on what visitors get and why now.', align: 'center', theme: 'brand' } };
    case 'text':
      return { id, type, props: { heading: 'Section heading', body: 'Write a short paragraph here.' } };
    case 'image':
      return { id, type, props: { url: '', alt: '', decorative: false } };
    case 'video':
      return { id, type, props: { url: '', title: 'Product walkthrough video' } };
    case 'features':
      return {
        id,
        type,
        props: {
          heading: 'Why choose us',
          items: [
            { title: 'Benefit one', body: 'Explain the outcome, not the feature.' },
            { title: 'Benefit two', body: 'Keep each point short and specific.' },
            { title: 'Benefit three', body: 'Back it with a number where you can.' },
          ],
        },
      };
    case 'testimonials':
      return { id, type, props: { heading: 'What customers say', items: [{ quote: 'A short, specific quote about the result.', author: 'Customer name', role: 'Role, Company', rating: 5 }] } };
    case 'pricing':
      return {
        id,
        type,
        props: {
          heading: 'Simple pricing',
          plans: [
            { name: 'Starter', price: '$19', period: 'per month', features: ['Core features', 'Email support'], ctaLabel: 'Get started', ctaHref: '#form', highlighted: false },
            { name: 'Pro', price: '$49', period: 'per month', features: ['Everything in Starter', 'Priority support'], ctaLabel: 'Get started', ctaHref: '#form', highlighted: true },
          ],
        },
      };
    case 'faq':
      return { id, type, props: { heading: 'Frequently asked questions', items: [{ question: 'A common question?', answer: 'A clear, honest answer.' }] } };
    case 'countdown':
      return { id, type, props: { heading: 'Offer ends in', endsAt: new Date(Date.now() + 7 * 86_400_000).toISOString(), expiredText: 'This offer has ended.' } };
    case 'form':
      return { id, type, props: { formId: formId ?? '', heading: 'Get in touch', description: null } };
    case 'cta':
      return { id, type, props: { heading: 'Ready to get started?', body: null, buttonLabel: 'Talk to us', buttonHref: '#form', style: 'primary' } };
    case 'logos':
      return { id, type, props: { heading: 'Trusted by', items: [] } };
    case 'spacer':
      return { id, type, props: { size: 'md' } };
  }
}

/** Short human summary of a block for the block list. */
export function blockSummary(block: Block): string {
  switch (block.type) {
    case 'hero':
      return block.props.headline;
    case 'text':
      return block.props.heading || block.props.body.slice(0, 40);
    case 'cta':
      return block.props.heading;
    case 'features':
    case 'testimonials':
    case 'faq':
    case 'pricing':
    case 'logos':
      return block.props.heading ?? '';
    case 'form':
      return block.props.heading ?? 'Form';
    case 'video':
      return block.props.title;
    case 'image':
      return block.props.alt ?? '';
    case 'countdown':
      return block.props.heading ?? '';
    default:
      return '';
  }
}

/** Moves an item within a list (returns a new list). */
export function move<T>(list: T[], from: number, to: number): T[] {
  if (to < 0 || to >= list.length || from === to) return list;
  const next = [...list];
  const [item] = next.splice(from, 1);
  next.splice(to, 0, item);
  return next;
}
