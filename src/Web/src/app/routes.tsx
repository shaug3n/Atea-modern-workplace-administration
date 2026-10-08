import React from 'react';
import type { ReactNode } from 'react';
import { type Capability, type CapabilityDecision } from '../capabilities/capabilityTypes';
import { PimGuidancePage } from '../features/identity/PimGuidancePage';
import { NotFoundPage } from '../components/NotFoundPage';
import { OverviewPage, type ConnectionHealthLoader, type OverviewLoader } from '../features/overview/OverviewPage';
import { AuditActivityPage } from '../features/audit/AuditActivityPage';
import { LicensesPage } from '../features/licenses/LicensesPage';
import { LicenseHygienePage } from '../features/licenses/hygiene/LicenseHygienePage';
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
import { AuthenticationCampaignsPage, type AuthenticationCampaignsLoader } from '../features/authentication-campaigns/AuthenticationCampaignsPage';
import { messages } from './messages';
import type { AppSession } from '../components/TenantContextHeader';
import type { IconName } from '../components/icons';

export type NavigationGroup = 'overview' | 'identity-access' | 'devices' | 'licenses' | 'services' | 'operations' | 'platform';
export type NavigationVisibility = 'always' | 'module' | 'device-module-or-settings-manager' | 'audit-not-hidden' | 'workspace-manager';

export type AppRoute = {
  path: string;
  label: string;
  pageTitle?: string;
  capability?: Capability;
  module?: 'users' | 'devices' | 'licenses' | 'exchange' | 'authentication-campaigns' | 'license-hygiene' | 'about' | 'feedback';
  workspaceAccess?: 'members' | 'settings' | 'modules' | 'any';
  navigation?: { group: NavigationGroup; order: number; icon: IconName; visibility: NavigationVisibility };
  render: (options?: { loadConnectionHealth?: ConnectionHealthLoader; loadOverview?: OverviewLoader; loadAuthenticationCampaigns?: AuthenticationCampaignsLoader; capabilities?: CapabilityDecision[]; navigate?: (path: string) => void; session?: AppSession; authorizationUnavailable?: boolean; onAuthorizationRetry?: () => Promise<void>; onRefreshAccess?: () => Promise<void> }) => ReactNode;
};

export function isInvitationPath(pathname: string) {
  return /^\/invitations\/[^/]+$/.test(pathname);
}

export const appRoutes: AppRoute[] = [
  { path: '/consent-callback', label: messages.connectionTitle, render: () => <ConsentCallbackPage /> },
  { path: '/onboarding/consent/callback', label: messages.connectionTitle, render: () => <ConsentCallbackPage /> },
  { path: '/onboarding', label: messages.navOnboarding, render: (options) => <OnboardingPage onNavigate={options?.navigate} /> },
  { path: '/identity', label: 'PIM guidance', render: (options) => <PimGuidancePage onRefreshAccess={options?.onRefreshAccess} /> },
  { path: '/settings', label: 'Workspace Settings', workspaceAccess: 'any', navigation: { group: 'platform', order: 0, icon: 'settings', visibility: 'workspace-manager' }, render: (options) => options?.session ? <WorkspaceSettingsHub session={options.session} /> : null },
  { path: '/settings/setup', label: 'Setup', workspaceAccess: 'settings', render: (options) => <OnboardingPage onNavigate={options?.navigate} /> },
  { path: '/settings/general', label: 'General', workspaceAccess: 'settings', render: () => <WorkspaceSettingsPage /> },
  { path: '/settings/modules', label: 'Modules', workspaceAccess: 'modules', render: () => <WorkspaceModulesPage /> },
  { path: '/settings/access', label: 'Access', workspaceAccess: 'members', render: (options) => <WorkspaceAccessPage isOwner={options?.session?.workspaceAccess?.isOwner === true} canManageModules={options?.session?.workspaceAccess?.canManageMemberModules === true} availableModules={options?.session?.workspace.moduleAccess ?? []} /> },
  {
    path: '/overview',
    label: messages.navOverview,
    navigation: { group: 'overview', order: 0, icon: 'overview', visibility: 'always' },
    render: (options) => <OverviewPage loadOverview={options?.loadOverview} session={options?.session} onNavigate={options?.navigate} />,
  },
  {
    path: '/users',
    label: messages.navUsers,
    module: 'users',
    capability: 'users.view',
    navigation: { group: 'identity-access', order: 0, icon: 'users', visibility: 'module' },
    render: (options) => <UsersPage capabilities={options?.capabilities ?? []} onNavigate={options?.navigate} authorizationUnavailable={options?.authorizationUnavailable} onAuthorizationRetry={options?.onAuthorizationRetry} />,
  },
  {
    path: '/authentication-campaigns',
    label: messages.navAuthenticationCampaigns,
    module: 'authentication-campaigns',
    capability: 'authentication.campaigns.view',
    navigation: { group: 'identity-access', order: 1, icon: 'lock', visibility: 'module' },
    render: (options) => <AuthenticationCampaignsPage
      loadRegistrations={options?.loadAuthenticationCampaigns ?? (() => Promise.reject(new Error('authentication_campaigns_loader_unavailable')))}
      capabilities={options?.capabilities}
      session={options?.session}
      onNavigate={options?.navigate}
      authorizationUnavailable={options?.authorizationUnavailable}
      onAuthorizationRetry={options?.onAuthorizationRetry}
    />,
  },
  {
    path: '/users/:userId',
    label: messages.navUsers,
    pageTitle: messages.userDetailTitle,
    module: 'users',
    capability: 'users.view',
    render: (options) => <UserDetailPage onNavigate={options?.navigate} capabilities={options?.capabilities} modules={effectiveAssignedModules(options?.session)} />,
  },
  {
    path: '/licenses',
    label: messages.navLicenses,
    module: 'licenses',
    capability: 'licenses.view',
    navigation: { group: 'licenses', order: 0, icon: 'licenses', visibility: 'module' },
    render: (options) => <LicensesPage
      capabilities={options?.capabilities}
      moduleEnabled={options?.session?.workspace.enabledModules}
      moduleAssigned={options?.session?.workspace.moduleAccess}
    />,
  },
  {
    path: '/licenses/hygiene',
    label: messages.navLicenseHygiene,
    module: 'license-hygiene',
    capability: 'licenses.hygiene.view',
    render: (options) => <LicenseHygienePage
      workspaceId={options?.session?.workspace.id ?? ''}
      capabilities={options?.capabilities}
      enabledModules={options?.session?.workspace.enabledModules}
      assignedModules={options?.session?.workspace.moduleAccess}
    />,
  },
  {
    path: '/audit',
    label: 'Activity',
    capability: 'audit.view',
    render: (options) => <AuditActivityPage authorizationUnavailable={options?.authorizationUnavailable} onAuthorizationRetry={options?.onAuthorizationRetry} />,
  },
  {
    path: '/activity',
    label: 'Activity',
    capability: 'audit.view',
    navigation: { group: 'operations', order: 0, icon: 'activity', visibility: 'audit-not-hidden' },
    render: (options) => <AuditActivityPage authorizationUnavailable={options?.authorizationUnavailable} onAuthorizationRetry={options?.onAuthorizationRetry} />,
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
    navigation: { group: 'devices', order: 0, icon: 'devices', visibility: 'device-module-or-settings-manager' },
    render: (options) => <DevicesPage capabilities={options?.capabilities} moduleAssigned={options?.session?.workspace.moduleAccess?.includes('devices') ?? true} moduleEnabled={options?.session?.workspace.enabledModules?.includes('devices') ?? true} onNavigate={options?.navigate} authorizationUnavailable={options?.authorizationUnavailable} onAuthorizationRetry={options?.onAuthorizationRetry} />,
  },
  {
    path: '/services/exchange',
    label: 'Exchange',
    module: 'exchange',
    navigation: { group: 'services', order: 0, icon: 'mail', visibility: 'module' },
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
    render: (options) => <NotFoundPage path={normalizedPath} onNavigate={options?.navigate} />,
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
