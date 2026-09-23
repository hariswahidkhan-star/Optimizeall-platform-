import { type ComponentType, lazy, Suspense } from 'react';
import { Spinner } from '@/components/ui/Spinner';

/**
 * Code-splits a routed page: its module is downloaded the first time the page renders, so the public website and each
 * portal only load the code they use. Keeps `element: <Page />` route definitions (and the permission guards that wrap
 * them) unchanged.
 */
export function lazyPage<M, K extends keyof M & string>(load: () => Promise<M>, name: K): ComponentType {
  const LazyComponent = lazy(async () => ({ default: (await load())[name] as ComponentType }));
  function LazyPage() {
    return (
      <Suspense
        fallback={
          <div className="lazy-page-loading">
            <Spinner label="Loading page" />
          </div>
        }
      >
        <LazyComponent />
      </Suspense>
    );
  }
  LazyPage.displayName = `LazyPage(${name})`;
  return LazyPage;
}
