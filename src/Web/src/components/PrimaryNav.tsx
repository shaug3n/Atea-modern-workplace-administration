import React from 'react';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import { messages } from '../app/messages';
import type { AppSession } from './TenantContextHeader';

export function PrimaryNav({ capabilities, session = { user: {}, workspace: { id: '', name: '' } }, currentPath, onNavigate }: { capabilities: CapabilitySnapshot | null; session?: AppSession; currentPath: string; onNavigate?: (path: string) => void }) {
  const moduleAccess = session.workspace.moduleAccess ?? session.workspace.enabledModules ?? ['users', 'devices', 'licenses', 'exchange'];
  const enabledModules = session.workspace.enabledModules ? moduleAccess.filter(key => session.workspace.enabledModules?.includes(key)) : moduleAccess;
  const enabled = (key: string) => enabledModules.includes(key);
  const capabilityAllowed = (capability: string) => {
    if (!capabilities) return true;
    const decision = capabilities.capabilities.find((item) => item.capability === capability);
    return !decision || decision.state === 'allowed' || decision.state === 'read_only';
  };
  const settings = session.workspaceAccess?.canManageSettings === true;
  const access = session.workspaceAccess?.canManageMembers === true;
  const modules = session.workspaceAccess?.canManageModules === true;
  const groups = [
    { label: null, items: [{ href: '/overview', label: messages.navOverview }] },
    { label: 'People', items: [
      ...(enabled('users') && capabilityAllowed('users.view') ? [{ href: '/users', label: messages.navUsers }] : []),
      ...(enabled('licenses') && capabilityAllowed('licenses.view') ? [{ href: '/licenses', label: messages.navLicenses }] : []),
    ] },
    { label: null, items: enabled('devices') || settings ? [{ href: '/devices', label: messages.navDevices }] : [] },
    { label: 'Services', items: enabled('exchange') ? [{ href: '/services/exchange', label: 'Exchange' }] : [] },
    { label: null, items: capabilityAllowed('audit.view') ? [{ href: '/activity', label: 'Activity' }] : [] },
    { label: 'Settings', items: settings ? [
      { href: '/settings', label: 'Settings' },
      { href: '/settings/setup', label: 'Setup' },
      { href: '/settings/general', label: 'General' },
      ...(modules ? [{ href: '/settings/modules', label: 'Modules' }] : []),
      ...(access ? [{ href: '/settings/access', label: 'Access' }] : []),
    ] : [] },
  ].filter(group => group.items.length > 0);

  return (
    <nav className="primary-nav" aria-label={messages.primaryNavigationLabel}>
      {groups.map((group) => <div className="primary-nav__group" key={group.label ?? group.items[0].href}>
        {group.label && <h2 className="primary-nav__group-label">{group.label}</h2>}
        <ul>{group.items.map((item) => {
          const active = currentPath === item.href || currentPath.startsWith(`${item.href}/`);
          return (
            <li key={item.href}>
              <a href={item.href} aria-current={active ? 'page' : undefined} onClick={(event) => {
                if (!onNavigate) {
                  return;
                }
                event.preventDefault();
                onNavigate(item.href);
              }}>
                {item.label}
              </a>
            </li>
          );
        })}</ul>
      </div>)}
    </nav>
  );
}
