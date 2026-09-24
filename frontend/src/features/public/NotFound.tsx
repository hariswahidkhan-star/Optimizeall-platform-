import { Compass } from 'lucide-react';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { useSiteCopy } from './site/copy';
import { StatusPage } from './StatusPage';

export function NotFound() {
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
