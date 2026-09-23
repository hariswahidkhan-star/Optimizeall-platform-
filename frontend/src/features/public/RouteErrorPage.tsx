import { TriangleAlert } from 'lucide-react';
import { isRouteErrorResponse, useRouteError } from 'react-router-dom';
import { Button } from '@/components/ui/Button';
import { buttonClasses } from '@/components/ui/buttonStyles';
import { isApiError } from '@/lib/api/errors';
import { StatusPage } from './StatusPage';

/** Router error boundary: unexpected render/loader errors land here instead of a blank screen. */
export function RouteErrorPage() {
  const error = useRouteError();
  const traceId = isApiError(error) ? error.traceId : undefined;
  const title = isRouteErrorResponse(error) && error.status === 404 ? 'We couldn’t find that page' : 'Something went wrong';

  return (
    <StatusPage
      standalone
      icon={<TriangleAlert />}
      title={title}
      description={
        <>
          Please reload the page. If it keeps happening, contact support
          {traceId ? (
            <>
              {' '}
              and mention reference <code>{traceId}</code>
            </>
          ) : null}
          .
        </>
      }
      actions={
        <>
          <Button onClick={() => window.location.reload()}>Reload</Button>
          <a className={buttonClasses('secondary')} href="/">
            Go to the home page
          </a>
        </>
      }
    />
  );
}
