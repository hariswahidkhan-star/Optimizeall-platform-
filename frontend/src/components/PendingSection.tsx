import { PlugZap } from 'lucide-react';
import type { ReactNode } from 'react';
import { Card, CardBody } from './ui/Card';
import { EmptyState } from './ui/EmptyState';
import { PageHeader } from './ui/PageHeader';

export interface PendingSectionProps {
  title: string;
  description?: ReactNode;
  /** Header actions that already work (e.g. a link back to the overview). */
  actions?: ReactNode;
  /** Render without the page header (e.g. inside a tab of another page). */
  embedded?: boolean;
}

/**
 * Placeholder for a nav destination whose API is not wired up yet. Used only from `features/<portal>/routes.tsx`;
 * the portal's owner replaces the route element with the real page.
 */
export function PendingSection({ title, description, actions, embedded }: PendingSectionProps) {
  const body = (
    <Card flat>
      <CardBody>
        <EmptyState
          icon={<PlugZap />}
          headingLevel={2}
          title="This section is being connected to the API"
          description="The screens are ready in the design system; live data for this area will appear here shortly."
        />
      </CardBody>
    </Card>
  );
  if (embedded) return body;
  return (
    <>
      <PageHeader title={title} description={description} actions={actions} />
      {body}
    </>
  );
}
