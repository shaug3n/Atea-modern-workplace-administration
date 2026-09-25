import React from 'react';
import type { ReactNode } from 'react';
import { type Capability, type CapabilityDecision } from '../capabilities/capabilityTypes';
import { OverviewPage, type ConnectionHealthLoader, type OverviewLoader } from '../features/overview/OverviewPage';
import { AuditActivityPage } from '../features/audit/AuditActivityPage';
import { LicensesPage } from '../features/licenses/LicensesPage';
import { WorkspaceSettingsPage } from '../features/workspace-settings/WorkspaceSettingsPage';
import { ConsentCallbackPage } from '../features/workspace-settings/ConsentCallbackPage';
import { OnboardingPage } from '../features/workspace-settings/OnboardingPage';
import { WorkspaceAccessPage } from '../features/workspace-access/WorkspaceAccessPage';
import { UserDetailPage } from '../features/users/UserDetailPage';
import { UsersPage } from '../features/users/UsersPage';
import { DevicesPage } from '../features/devices/DevicesPage';
import { DeviceDetailPage } from '../features/devices/DeviceDetailPage';
import { ExchangeOverviewPage } from '../features/exchange/ExchangeOverviewPage';
import { WorkspaceModulesPage } from '../features/workspace-settings/WorkspaceModulesPage';
import { WorkspaceSettingsHub } from '../features/workspace-settings/WorkspaceSettingsHub';
import { messages } from './messages';
import type { AppSession } from '../components/TenantContextHeader';

export type AppRoute = {
  path: string;
  label: string;
  pageTitle?: string;
  capability?: Capability;
  module?: 'users' | 'devices' | 'licenses' | 'exchange';
  workspaceAccess?: 'members' | 'settings' | 'modules' | 'any';
  render: (options?: { loadConnectionHealth?: ConnectionHealthLoader; loadOverview?: OverviewLoader; capabilities?: CapabilityDecision[]; navigate?: (path: string) => void; session?: AppSession }) => ReactNode;
};

export function isInvitationPath(pathname: string) {
  return /^\/invitations\/[^/]+$/.test(pathname);
}

function WorkInProgressPage({ title, description }: { title: string; description: string }) {
  return (
    <section className="content-panel" aria-labelledby="page-title">
      <p className="eyebrow">{messages.shellPreviewLabel}</p>
      <h1 id="page-title">{title}</h1>
      <p>{description}</p>
    </section>
  );
}

export const appRoutes: AppRoute[] = [
  { path: '/consent-callback', label: messages.connectionTitle, render: () => <ConsentCallbackPage /> },
  { path: '/onboarding/consent/callback', label: messages.connectionTitle, render: () => <ConsentCallbackPage /> },
  { path: '/onboarding', label: messages.navOnboarding, render: (options) => <OnboardingPage onNavigate={options?.navigate} /> },
  { path: '/settings', label: 'Workspace Settings', workspaceAccess: 'any', render: (options) => options?.session ? <WorkspaceSettingsHub session={options.session} /> : null },
  { path: '/settings/setup', label: 'Setup', workspaceAccess: 'settings', render: (options) => <OnboardingPage onNavigate={options?.navigate} /> },
  { path: '/settings/general', label: 'General', workspaceAccess: 'settings', render: () => <WorkspaceSettingsPage /> },
  { path: '/settings/modules', label: 'Modules', workspaceAccess: 'modules', render: () => <WorkspaceModulesPage /> },
  { path: '/settings/access', label: 'Access', workspaceAccess: 'members', render: (options) => <WorkspaceAccessPage isOwner={options?.session?.workspaceAccess?.isOwner === true} canManageModules={options?.session?.workspaceAccess?.canManageMemberModules === true} availableModules={options?.session?.workspace.moduleAccess ?? []} /> },
  {
    path: '/overview',
    label: messages.navOverview,
    render: (options) => <OverviewPage loadConnectionHealth={options?.loadConnectionHealth} loadOverview={options?.loadOverview} session={options?.session} />,
  },
  {
    path: '/users',
    label: messages.navUsers,
    module: 'users',
    capability: 'users.view',
    render: (options) => <UsersPage capabilities={options?.capabilities ?? []} onNavigate={options?.navigate} />,
  },
  {
    path: '/users/:userId',
    label: messages.navUsers,
    pageTitle: messages.userDetailTitle,
    module: 'users',
    capability: 'users.view',
    render: (options) => <UserDetailPage capabilities={options?.capabilities} modules={effectiveAssignedModules(options?.session)} />,
  },
  {
    path: '/licenses',
    label: messages.navLicenses,
    module: 'licenses',
    capability: 'licenses.view',
    render: () => <LicensesPage />,
  },
  {
    path: '/audit',
    label: 'Activity',
    capability: 'audit.view',
    render: () => <AuditActivityPage />,
  },
  {
    path: '/activity',
    label: 'Activity',
    capability: 'audit.view',
    render: () => <AuditActivityPage />,
  },
  {
    path: '/devices/:id',
    label: messages.navDevices,
    pageTitle: 'Device details',
    module: 'devices',
    capability: 'devices.view',
    render: (options) => <DeviceDetailPage capabilities={options?.capabilities} onNavigate={options?.navigate} />,
  },
  {
    path: '/devices',
    label: messages.navDevices,
    module: 'devices',
    capability: 'devices.view',
    render: (options) => <DevicesPage capabilities={options?.capabilities} moduleAssigned={options?.session?.workspace.moduleAccess?.includes('devices') ?? true} moduleEnabled={options?.session?.workspace.enabledModules?.includes('devices') ?? true} onNavigate={options?.navigate} />,
  },
  {
    path: '/services/exchange',
    label: 'Exchange',
    module: 'exchange',
    render: () => <ExchangeOverviewPage />,
  },
  {
    path: '/workspace-access',
    label: messages.navWorkspaceAccess,
    workspaceAccess: 'members',
    render: (options) => <WorkspaceAccessPage isOwner={options?.session?.workspaceAccess?.isOwner === true} canManageModules={options?.session?.workspaceAccess?.canManageMemberModules === true} availableModules={options?.session?.workspace.moduleAccess ?? []} />,
  },
  {
    path: '/workspace-settings',
    label: messages.navWorkspaceSettings,
    workspaceAccess: 'settings',
    render: () => <WorkspaceSettingsPage />,
  },
];

export function matchRoute(pathname: string): AppRoute {
  const normalizedPath = normalizePath(pathname);
  if (normalizedPath === '/') {
    return appRoutes[0];
  }

  return appRoutes.find((route) => route.path === normalizedPath || matchesParameterizedRoute(route.path, normalizedPath)) ?? {
    path: normalizedPath,
    label: messages.notFoundTitle,
    render: () => <WorkInProgressPage title={messages.notFoundTitle} description={messages.notFoundDescription} />,
  };
}

export function capabilityDecisionFor(route: AppRoute, capabilities: CapabilityDecision[]): CapabilityDecision | null {
  if (!route.capability) {
    return null;
  }

  return capabilities.find((decision) => decision.capability === route.capability) ?? {
    capability: route.capability,
    state: 'hidden',
    reasonCode: 'capability_not_returned',
  };
}

function matchesParameterizedRoute(routePath: string, pathname: string) {
  if (!routePath.includes(':')) {
    return false;
  }

  const routeParts = routePath.split('/').filter(Boolean);
  const pathParts = pathname.split('/').filter(Boolean);
  return routeParts.length === pathParts.length && routeParts.every((part, index) => part.startsWith(':') || part === pathParts[index]);
}

function normalizePath(pathname: string) {
  if (!pathname || pathname === '/') {
    return '/';
  }

  return pathname.endsWith('/') ? pathname.slice(0, -1) : pathname;
}

function effectiveAssignedModules(session?: AppSession) {
  const assignedModules = session?.workspace.moduleAccess;
  const enabledModules = session?.workspace.enabledModules;
  return assignedModules && enabledModules
    ? assignedModules.filter((module) => enabledModules.includes(module))
    : assignedModules;
}
