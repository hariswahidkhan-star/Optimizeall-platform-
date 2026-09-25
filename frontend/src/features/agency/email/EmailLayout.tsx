import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { FormField } from '@/components/ui/FormField';
import { Select } from '@/components/ui/Select';
import { useScrollEdges } from '@/components/ui/useScrollEdges';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useWorkspaces } from './api/queries';
import { EmailWorkspaceProvider, useEmailWorkspace } from './shared/workspace';
import './email.css';

interface Section {
  to: string;
  label: string;
  end?: boolean;
  requires?: string;
}

const EMAIL_SECTIONS: Section[] = [
  { to: '/agency/email', label: 'Overview', end: true },
  { to: '/agency/email/campaigns', label: 'Campaigns' },
  { to: '/agency/email/lists', label: 'Audience' },
  { to: '/agency/email/segments', label: 'Segments' },
  { to: '/agency/email/templates', label: 'Templates' },
  { to: '/agency/email/automations', label: 'Journeys' },
  { to: '/agency/sms', label: 'SMS & WhatsApp', requires: Permissions.SmsManage },
  { to: '/agency/email/settings', label: 'Settings' },
];

/** Workspace switcher: the agency's own marketing or one client's. */
export function WorkspacePicker() {
  const workspaces = useWorkspaces();
  const { key, setClientId } = useEmailWorkspace();
  const options = (workspaces.data ?? [{ key: 'agency', clientAccountId: null, name: 'Optimize All (agency)' }]).map((w) => ({
    value: w.key,
    label: w.name,
  }));
  return (
    <FormField label="Workspace" hint="Lists, templates and campaigns belong to one client (or the agency).">
      <Select
        value={key}
        options={options}
        onChange={(event) => setClientId(event.target.value === 'agency' ? null : event.target.value)}
        size="sm"
      />
    </FormField>
  );
}

function SectionNav() {
  const { hasPermission } = useAuth();
  const { pathname } = useLocation();
  const listRef = useScrollEdges<HTMLUListElement>('.is-active', pathname);
  return (
    <nav className="email-subnav" aria-label="Email marketing sections">
      <ul ref={listRef} className="ui-scroll-fade">
        {EMAIL_SECTIONS.filter((s) => !s.requires || hasPermission(s.requires)).map((s) => (
          <li key={s.to}>
            <NavLink to={s.to} end={s.end} className={({ isActive }) => (isActive ? 'email-subnav__link is-active' : 'email-subnav__link')}>
              {s.label}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
}

/** Shell of every email/SMS page: workspace picker + section navigation. */
export function EmailLayout() {
  return (
    <EmailWorkspaceProvider>
      <div className="email-area">
        <div className="email-area__bar">
          <SectionNav />
          <div className="email-area__workspace">
            <WorkspacePicker />
          </div>
        </div>
        <Outlet />
      </div>
    </EmailWorkspaceProvider>
  );
}
