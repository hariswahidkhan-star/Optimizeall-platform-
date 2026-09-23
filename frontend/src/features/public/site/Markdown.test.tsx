import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { Markdown, parseMarkdown } from './Markdown';

function renderMarkdown(source: string) {
  return render(
    <MemoryRouter>
      <Markdown source={source} />
    </MemoryRouter>,
  );
}

describe('Markdown (CMS content)', () => {
  it('renders raw HTML as text and drops unsafe link destinations', () => {
    const { container } = renderMarkdown('<img src=x onerror="alert(1)">\n\n[click](javascript:alert(1)) and [ok](https://example.com)');
    expect(container.querySelector('img')).toBeNull();
    expect(container.textContent).toContain('<img src=x onerror="alert(1)">');
    const links = Array.from(container.querySelectorAll('a'));
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['https://example.com']);
  });

  it('closes a fenced block only on a bare fence, like the API sanitizer', () => {
    // The API keeps everything up to a bare ``` verbatim as code. If the renderer closed the block at "```js", the rest
    // would be rendered as prose although the server never sanitized it as prose.
    const blocks = parseMarkdown('```\nconst a = 1;\n```js\n[x](https://example.com)\n```\nafter');
    expect(blocks).toEqual([
      { kind: 'code', lang: '', text: 'const a = 1;\n```js\n[x](https://example.com)' },
      { kind: 'paragraph', text: 'after' },
    ]);
  });

  it('needs a closing fence at least as long as the opening one and of the same character', () => {
    const blocks = parseMarkdown('````\n```\n~~~~\n````\ntext');
    expect(blocks).toEqual([
      { kind: 'code', lang: '', text: '```\n~~~~' },
      { kind: 'paragraph', text: 'text' },
    ]);
  });
});
