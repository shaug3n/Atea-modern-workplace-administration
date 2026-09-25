import React from 'react';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import type { Capability } from '../capabilities/capabilityTypes';
import { messages } from '../app/messages';
import type { AppSession } from './TenantContextHeader';

type NavItem = {
  href: string;
  label: string;
  capability?: Capability;
  workspaceRole?: 'members' | 'settings';
};

const navItems: NavItem[] = [
  { href: '/overview', label: messages.navOverview },
  { href: '/onboarding', label: messages.navOnboarding },
  { href: '/users', label: messages.navUsers, capability: 'users.view' },
  { href: '/licenses', label: messages.navLicenses, capability: 'licenses.assign' },
  { href: '/audit', label: messages.navAudit, capability: 'audit.view' },
  { href: '/workspace-access', label: messages.navWorkspaceAccess, workspaceRole: 'members' },
  { href: '/workspace-settings', label: messages.navWorkspaceSettings, workspaceRole: 'settings' },
];

export function PrimaryNav({ capabilities, session, currentPath, onNavigate }: { capabilities: CapabilitySnapshot | null; session: AppSession; currentPath: string; onNavigate?: (path: string) => void }) {
  const visibleItems = navItems.filter((item) => {
    if (item.workspaceRole === 'members') return session.workspaceAccess?.canManageMembers === true;
    if (item.workspaceRole === 'settings') return session.workspaceAccess?.canManageSettings === true;
    return !item.capability || capabilities?.capabilities.find((decision) => decision.capability === item.capability)?.state === 'allowed';
  });

  return (
    <nav className="primary-nav" aria-label={messages.primaryNavigationLabel}>
      <ul>
        {visibleItems.map((item) => {
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
        })}
      </ul>
    </nav>
  );
}
