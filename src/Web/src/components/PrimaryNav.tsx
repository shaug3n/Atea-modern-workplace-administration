import React from 'react';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import { workspaceSettingsCapability, type Capability } from '../capabilities/capabilityTypes';
import { messages } from '../app/messages';

type NavItem = {
  href: string;
  label: string;
  capability?: Capability;
};

const navItems: NavItem[] = [
  { href: '/overview', label: messages.navOverview },
  { href: '/users', label: messages.navUsers },
  { href: '/licenses', label: messages.navLicenses },
  { href: '/audit', label: messages.navAudit },
  { href: '/workspace-settings', label: messages.navWorkspaceSettings, capability: workspaceSettingsCapability },
];

export function PrimaryNav({ capabilities, currentPath, onNavigate }: { capabilities: CapabilitySnapshot; currentPath: string; onNavigate?: (path: string) => void }) {
  const workspaceSettings = capabilities.capabilities.find((decision) => decision.capability === workspaceSettingsCapability);
  const visibleItems = navItems.filter((item) => item.capability !== workspaceSettingsCapability || workspaceSettings?.state === 'allowed');

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
