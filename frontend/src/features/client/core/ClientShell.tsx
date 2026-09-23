import type { ReactNode } from 'react';
import { EmptyState, ErrorState, FormField, PageHeader, Select, Skeleton, type Breadcrumb } from '@/components/ui';
import type { MyOrganization } from '@/features/agency/shared/deliveryTypes';
import { useClientOrg } from './useClientOrg';
import './client-core.css';

type OrgContext = ReturnType<typeof useClientOrg> & { org: MyOrganization };

/** Organization picker, shown only when the user belongs to several client organizations. */
export function OrgSwitcher() {
  const { orgs, org, setOrg } = useClientOrg();
  if (orgs.length < 2 || !org) return null;
  return (
    <FormField label="Organization" className="cc-org-switcher">
      <Select value={org.clientId} onChange={(e) => setOrg(e.target.value)} options={orgs.map((o) => ({ value: o.clientId, label: `${o.name} (${o.role})` }))} />
    </FormField>
  );
}

/**
 * Page frame for client-portal pages: resolves the selected organization, shows the org switcher next to the page
 * title and renders `children(ctx)` once an organization is known.
 */
export function ClientShell({
  title,
  description,
  breadcrumbs,
  actions,
  children,
}: {
  title: ReactNode;
  description?: ReactNode;
  breadcrumbs?: Breadcrumb[];
  actions?: ReactNode;
  children: (ctx: OrgContext) => ReactNode;
}) {
  const ctx = useClientOrg();
  if (ctx.isPending) return <Skeleton height={200} />;
  if (ctx.error) return <ErrorState error={ctx.error} />;
  if (!ctx.org)
    return (
      <EmptyState
        title="No organization yet"
        description="Your account isn't linked to a client organization. Ask your account manager to invite you."
      />
    );
  return (
    <div className="dl-page">
      <PageHeader
        eyebrow={ctx.org.name}
        title={title}
        description={description}
        breadcrumbs={breadcrumbs}
        actions={
          <span className="dl-row">
            {actions}
            <OrgSwitcher />
          </span>
        }
      />
      {children(ctx as OrgContext)}
    </div>
  );
}
