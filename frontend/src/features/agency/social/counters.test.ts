import { describe, expect, it } from 'vitest';
import { composeText, countFor, normalizeHashtag, textLength } from './counters';
import { presets } from './test/fixtures';

const x = presets.find((p) => p.network === 'X')!;
const facebook = presets.find((p) => p.network === 'Facebook')!;

describe('composer counters (mirror of the server rules)', () => {
  it('counts every URL on X as 23 characters', () => {
    const text = `${'a'.repeat(256)} https://example.com/a/very/long/path/that/is/much/longer/than/twenty-three`;
    expect(textLength(text, 23)).toBe(280);
    expect(countFor(x, text, [], null).over).toBe(false);
    expect(countFor(x, 'b'.repeat(281), [], null).over).toBe(true);
  });

  it('weights CJK and emoji as two on X and counts graphemes elsewhere', () => {
    expect(textLength('日本', 23)).toBe(4);
    expect(textLength('👍🏽', 23)).toBe(2);
    expect(textLength('é!', 23)).toBe(2);
    expect(textLength('👍🏽', null)).toBe(1);
  });

  it('appends hashtags once and the link only where the network puts links in the text', () => {
    expect(composeText('Morning run #Fitness', ['fitness', '#Run', 'Morning Run'], null, 'Attachment')).toBe(
      'Morning run #Fitness\n\n#Run #MorningRun',
    );
    expect(countFor(x, 'Read this', [], 'https://example.com/post').length).toBe(9 + 1 + 23);
    expect(countFor(facebook, 'Read this', [], 'https://example.com/post').length).toBe(9);
    expect(normalizeHashtag('  ##summer sale ')).toBe('#summersale');
  });
});
