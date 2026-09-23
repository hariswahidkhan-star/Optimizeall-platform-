import { Compass } from 'lucide-react';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { StatusPage } from './StatusPage';

export function NotFound() {
  return (
    <StatusPage
      code="404"
      icon={<Compass />}
      title="We couldn’t find that page"
      description="The link may be out of date, or the page may have moved."
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
