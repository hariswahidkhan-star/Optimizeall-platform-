import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { isExternalHref, isInternalHref, isSafeHref } from './safeHref';

describe('isSafeHref', () => {
  it.each([
    'https://www.instagram.com/p/Cabc123/',
    'http://example.com/path?x=1#y',
    'HTTPS://EXAMPLE.COM',
    '/app/earnings',
    '/api/v1/files/0f8fad5b-d9cb-469f-a165-70867728950e',
    '/',
  ])('accepts %s', (href) => expect(isSafeHref(href)).toBe(true));

  it.each([
    'javascript:alert(1)',
    'JaVaScRiPt:alert(1)',
    ' javascript:alert(1)',
    'data:text/html,<script>alert(1)</script>',
    'vbscript:msgbox(1)',
    'mailto:someone@example.com',
    '//evil.example/path',
    '/\\evil.example',
    '\\\\evil.example',
    'https:\\\\evil.example',
    'https://user:pass@evil.example/',
    'https://good.example@evil.example/',
    'https://',
    'https:evil.example',
    'app/earnings',
    '',
    ' /app',
    '/app\n',
    'java\tscript:alert(1)',
  ])('rejects %j', (href) => expect(isSafeHref(href)).toBe(false));

  it('rejects non-strings', () => {
    expect(isSafeHref(null)).toBe(false);
    expect(isSafeHref(undefined)).toBe(false);
    expect(isSafeHref(42)).toBe(false);
  });

  it('separates internal paths from external URLs', () => {
    expect(isInternalHref('/app/support/1')).toBe(true);
    expect(isInternalHref('https://example.com')).toBe(false);
    expect(isExternalHref('https://example.com')).toBe(true);
    expect(isExternalHref('/app')).toBe(false);
  });
});

describe('SafeExternalLink', () => {
  it('renders a new-tab link without opener or referrer for safe URLs', () => {
    render(
      <SafeExternalLink href="https://www.tiktok.com/@ada/video/1" nofollow>
        Open post
      </SafeExternalLink>,
    );
    const link = screen.getByRole('link', { name: 'Open post' });
    expect(link).toHaveAttribute('href', 'https://www.tiktok.com/@ada/video/1');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer nofollow');
  });

  it('renders plain text (or the fallback) for unsafe URLs', () => {
    render(
      <>
        <SafeExternalLink href="javascript:alert(1)">Evil</SafeExternalLink>
        <SafeExternalLink href="//evil.example" fallback={<em>no link</em>}>
          Also evil
        </SafeExternalLink>
        <SafeExternalLink href={null}>Missing</SafeExternalLink>
      </>,
    );
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(screen.getByText('Evil')).toBeInTheDocument();
    expect(screen.getByText('no link')).toBeInTheDocument();
    expect(screen.getByText('Missing')).toBeInTheDocument();
  });
});
