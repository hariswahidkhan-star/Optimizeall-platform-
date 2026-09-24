import { Compass } from 'lucide-react';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { useSiteCopy } from './site/copy';
import { MovedOrNotFound } from './site/redirects';
import { StatusPage } from './StatusPage';

/** The 404 page. A public address that has moved (Website → Redirects) navigates to its new address instead. */
export function NotFound() {
  return (
    <MovedOrNotFound>
      <NotFoundPage />
    </MovedOrNotFound>
  );
}

function NotFoundPage() {
  const copy = useSiteCopy();
  return (
    <StatusPage
      code="404"
      icon={<Compass />}
      title={copy.text('shared.page404.title')}
      description={copy.text('shared.page404.description')}
      actions={
        <>
          <ButtonLink to="/">Back to the home page</ButtonLink>
          <ButtonLink to="/faq" variant="secondary">
            Read the FAQ
          </ButtonLink>
        </>
      }
    />
  );
}
