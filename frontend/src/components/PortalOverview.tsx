import { ArrowRight, ShieldCheck } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useCurrentPortal } from '@/app/portalContext';
import { meetsRequirement } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { greetingFor } from '@/lib/format/dates';
import { firstName, humanize } from '@/lib/format/text';
import { Badge } from './ui/Badge';
import { Card, CardBody } from './ui/Card';
import { PageHeader } from './ui/PageHeader';
import './PortalOverview.css';

/**
 * Landing page of a staff portal: a greeting, the user's roles and a card per section of the portal the user can
 * open. Section cards come from the portal's nav definition, so they stay in sync as sections are added.
 */
export function PortalOverview() {
  const portal = useCurrentPortal();
  const { user, permissions } = useAuth();
  const sections = portal.nav.filter(
    (item) => item.to !== '' && (!item.requires || meetsRequirement(permissions, item.requires)),
  );
  const name = user ? firstName(user.displayName) : '';

  return (
    <>
      <PageHeader
        eyebrow={portal.label}
        title={`${greetingFor(new Date(), user?.timeZone)}${name ? `, ${name}` : ''}`}
        description={portal.description}
        meta={
          user && (
            <>
              {user.roles.map((role) => (
                <Badge key={role} tone="brand" icon={<ShieldCheck />}>
                  {humanize(role)}
                </Badge>
              ))}
              {(user.customRoles ?? []).map((name) => (
                <Badge key={`custom:${name}`} tone="neutral" icon={<ShieldCheck />}>
                  {name}
                </Badge>
              ))}
            </>
          )
        }
      />
      <section aria-labelledby="portal-sections-heading">
        <h2 id="portal-sections-heading" className="visually-hidden">
          Sections
        </h2>
        <ul className="portal-overview__grid">
          {sections.map((item) => {
            const Icon = item.icon;
            return (
              <Card as="li" key={item.to} interactive>
                <CardBody className="portal-overview__card">
                  <span className="portal-overview__icon" aria-hidden="true">
                    <Icon />
                  </span>
                  <div className="portal-overview__text">
                    <h3 className="portal-overview__title">
                      <Link className="ui-card__link" to={`${portal.basePath}/${item.to}`}>
                        {item.label}
                      </Link>
                    </h3>
                    {item.description && <p className="portal-overview__description">{item.description}</p>}
                  </div>
                  <ArrowRight className="portal-overview__arrow" aria-hidden="true" />
                </CardBody>
              </Card>
            );
          })}
        </ul>
      </section>
    </>
  );
}
