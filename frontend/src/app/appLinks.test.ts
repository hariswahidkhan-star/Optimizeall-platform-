import { matchRoutes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import fixture from './appLinks.fixture.json';
import { routes } from './router';

/**
 * The backend builds notification/email/onboarding links with `Common/Notifications/AppLinks.cs`; its unit test keeps
 * that class and appLinks.fixture.json identical. Here every fixture path must resolve to a real route of the app
 * router (not the catch-all NotFound).
 */
const samples: Record<string, string> = {
  ':id': '0f8fad5b-d9cb-469f-a165-70867728950e',
  ':slug': 'spring-launch',
  ':code': 'Ab12Cd34',
};

describe('backend app links', () => {
  it('lists every link once', () => {
    const names = fixture.links.map((l) => l.name);
    expect(new Set(names).size).toBe(names.length);
    expect(fixture.links.length).toBeGreaterThan(15);
  });

  it.each(fixture.links.map((l) => [l.name, l.path] as const))(
    '%s (%s) resolves to a real route',
    (_, pattern) => {
      const path = pattern.replace(/:(id|slug|code)\b/g, (param) => samples[param] ?? param);
      const matches = matchRoutes(routes, path);
      expect(matches, path).not.toBeNull();
      const leaf = matches![matches!.length - 1]!;
      expect(leaf.route.path, `${path} falls through to NotFound`).not.toBe('*');
      expect(leaf.pathname.replace(/\/$/, '')).toBe(path);
    },
  );
});
