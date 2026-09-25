import React, { useState } from 'react';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import { messages } from '../app/messages';
import type { AppSession } from './TenantContextHeader';

type NavItem = { href: string; label: string };
type NavGroup = { key: string; label?: string; items: NavItem[]; disclosure?: boolean };

const isRouteActive = (path: string, href: string) => path === href || path.startsWith(`${href}/`);

export function PrimaryNav({ capabilities, session = { user: {}, workspace: { id: '', name: '' } }, currentPath, onNavigate }: { capabilities: CapabilitySnapshot | null; session?: AppSession; currentPath: string; onNavigate?: (path: string) => void }) {
  const [expanded, setExpanded] = useState<Record<string, boolean>>({ people: true, services: true });
  const moduleAccess = session.workspace.moduleAccess ?? session.workspace.enabledModules ?? ['users', 'devices', 'licenses', 'exchange'];
  const enabledModules = session.workspace.enabledModules ? moduleAccess.filter(key => session.workspace.enabledModules?.includes(key)) : moduleAccess;
  const enabled = (key: string) => enabledModules.includes(key);
  const settings = session.workspaceAccess?.canManageSettings === true || session.workspaceAccess?.canManageMembers === true || session.workspaceAccess?.canManageModules === true;
  const groups: NavGroup[] = [
    { key: 'overview', items: [{ href: '/overview', label: messages.navOverview }] },
    { key: 'people', label: 'People', disclosure: true, items: [
      ...(enabled('users') ? [{ href: '/users', label: messages.navUsers }] : []),
      ...(enabled('licenses') ? [{ href: '/licenses', label: messages.navLicenses }] : []),
    ] },
    { key: 'devices', items: enabled('devices') || session.workspaceAccess?.canManageSettings ? [{ href: '/devices', label: messages.navDevices }] : [] },
    { key: 'services', label: 'Services', disclosure: true, items: enabled('exchange') ? [{ href: '/services/exchange', label: 'Exchange' }] : [] },
    { key: 'activity', items: capabilities?.capabilities.find(item => item.capability === 'audit.view')?.state === 'hidden' ? [] : [{ href: '/activity', label: 'Activity' }] },
    { key: 'settings', items: settings ? [{ href: '/settings', label: 'Workspace Settings' }] : [] },
  ].filter(group => group.items.length > 0);

  return (
    <nav className="primary-nav" aria-label={messages.primaryNavigationLabel}>
      {groups.map(group => {
        const activeItem = group.items.filter(item => isRouteActive(currentPath, item.href)).sort((a, b) => b.href.length - a.href.length)[0];
        const isExpanded = !group.disclosure || Boolean(activeItem) || expanded[group.key] !== false;
        return <div className={`primary-nav__group${group.disclosure ? ' primary-nav__group--disclosure' : ''}`} key={group.key}>
          {group.disclosure && <h2 className="primary-nav__group-label"><button type="button" aria-expanded={isExpanded} aria-controls={`primary-nav-${group.key}`} onClick={() => setExpanded(previous => ({ ...previous, [group.key]: !isExpanded }))}>{group.label}<span className="primary-nav__chevron" aria-hidden="true" /></button></h2>}
          <ul id={group.disclosure ? `primary-nav-${group.key}` : undefined} hidden={!isExpanded}>{group.items.map(item => (
            <li key={item.href}>
              <a href={item.href} aria-current={activeItem?.href === item.href ? 'page' : undefined} onClick={event => {
                if (!onNavigate) return;
                event.preventDefault();
                onNavigate(item.href);
              }}>{item.label}</a>
            </li>
          ))}</ul>
        </div>;
      })}
    </nav>
  );
}
