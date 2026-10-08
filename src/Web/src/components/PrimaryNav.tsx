import React, { useState } from 'react';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import { messages } from '../app/messages';
import { appRoutes, type NavigationGroup } from '../app/routes';
import type { AppSession } from './TenantContextHeader';
import { Icon } from './icons';

type NavGroup = { key: NavigationGroup; label: string; disclosure?: boolean };

const isRouteActive = (path: string, href: string) => path === href || path.startsWith(`${href}/`);

export function PrimaryNav({ capabilities, session = { user: {}, workspace: { id: '', name: '' } }, currentPath, onNavigate }: { capabilities: CapabilitySnapshot | null; session?: AppSession; currentPath: string; onNavigate?: (path: string) => void }) {
  const [expanded, setExpanded] = useState<Record<string, boolean>>({ 'identity-access': true, services: true });
  const moduleAccess = session.workspace.moduleAccess ?? session.workspace.enabledModules ?? ['users', 'devices', 'licenses', 'exchange'];
  const enabledModules = session.workspace.enabledModules ? moduleAccess.filter(key => session.workspace.enabledModules?.includes(key)) : moduleAccess;
  const enabled = (key: string) => enabledModules.includes(key);
  const settings = session.workspaceAccess?.canManageSettings === true || session.workspaceAccess?.canManageMembers === true || session.workspaceAccess?.canManageModules === true;
  const groupDefinitions: NavGroup[] = [
    { key: 'overview', label: 'Overview' },
    { key: 'identity-access', label: 'Identity & Access', disclosure: true },
    { key: 'devices', label: 'Devices' },
    { key: 'licenses', label: 'Licenses' },
    { key: 'services', label: 'Services', disclosure: true },
    { key: 'operations', label: 'Operations' },
    { key: 'platform', label: 'Platform' },
  ];
  const items = appRoutes
    .filter(route => route.navigation)
    .filter(route => {
      const navigation = route.navigation!;
      switch (navigation.visibility) {
        case 'always':
          return true;
        case 'module':
          return route.module ? enabled(route.module) : false;
        case 'device-module-or-settings-manager':
          return enabled(route.module ?? 'devices') || session.workspaceAccess?.canManageSettings === true;
        case 'audit-not-hidden':
          return capabilities?.capabilities.find(item => item.capability === 'audit.view')?.state !== 'hidden';
        case 'workspace-manager':
          return settings;
      }
    })
    .sort((a, b) => groupDefinitions.findIndex(group => group.key === a.navigation!.group) - groupDefinitions.findIndex(group => group.key === b.navigation!.group) || a.navigation!.order - b.navigation!.order);
  const groups = groupDefinitions
    .map(group => ({ ...group, items: items.filter(item => item.navigation!.group === group.key) }))
    .filter(group => group.items.length > 0);

  return (
    <nav className="primary-nav" aria-label={messages.primaryNavigationLabel}>
      {groups.map(group => {
        const activeItem = group.items.filter(item => isRouteActive(currentPath, item.path)).sort((a, b) => b.path.length - a.path.length)[0];
        const isExpanded = !group.disclosure || Boolean(activeItem) || expanded[group.key] !== false;
        return <div className={`primary-nav__group${group.disclosure ? ' primary-nav__group--disclosure' : ''}`} key={group.key}>
          <h2 className="primary-nav__group-label">{group.disclosure
            ? <button type="button" aria-expanded={isExpanded} aria-controls={`primary-nav-${group.key}`} onClick={() => setExpanded(previous => ({ ...previous, [group.key]: !isExpanded }))}>{group.label}<span className="primary-nav__chevron" aria-hidden="true" /></button>
            : group.label}</h2>
          <ul id={group.disclosure ? `primary-nav-${group.key}` : undefined} hidden={!isExpanded}>{group.items.map(item => (
            <li key={item.path}>
              <a href={item.path} aria-current={activeItem?.path === item.path ? 'page' : undefined} onClick={event => {
                if (!onNavigate) return;
                event.preventDefault();
                onNavigate(item.path);
              }}><Icon name={item.navigation!.icon} size={18} /><span>{item.label}</span></a>
            </li>
          ))}</ul>
        </div>;
      })}
    </nav>
  );
}
