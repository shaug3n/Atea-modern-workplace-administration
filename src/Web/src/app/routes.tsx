import React from 'react';
import type { ReactNode } from 'react';
import { workspaceSettingsCapability, type Capability, type CapabilityDecision } from '../capabilities/capabilityTypes';
import { OverviewPage, type ConnectionHealthLoader } from '../features/overview/OverviewPage';
import { AuditActivityPage } from '../features/audit/AuditActivityPage';
import { UserDetailPage } from '../features/users/UserDetailPage';
import { UsersPage } from '../features/users/UsersPage';
import { messages } from './messages';

export type AppRoute = {
  path: string;
  label: string;
  capability?: Capability;
  render: (options?: { loadConnectionHealth?: ConnectionHealthLoader; capabilities?: CapabilityDecision[]; navigate?: (path: string) => void }) => ReactNode;
};

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
  {
    path: '/overview',
    label: messages.navOverview,
    render: (options) => <OverviewPage loadConnectionHealth={options?.loadConnectionHealth} />,
  },
  {
    path: '/users',
    label: messages.navUsers,
    capability: 'users.view',
    render: (options) => <UsersPage capabilities={options?.capabilities ?? []} onNavigate={options?.navigate} />,
  },
  {
    path: '/users/:userId',
    label: messages.navUsers,
    capability: 'users.view',
    render: () => <UserDetailPage />,
  },
  {
    path: '/licenses',
    label: messages.navLicenses,
    capability: 'licenses.assign',
    render: () => <WorkInProgressPage title={messages.licensesTitle} description={messages.licensesDescription} />,
  },
  {
    path: '/audit',
    label: messages.navAudit,
    capability: 'audit.view',
    render: () => <AuditActivityPage />,
  },
  {
    path: '/workspace-settings',
    label: messages.navWorkspaceSettings,
    capability: workspaceSettingsCapability,
    render: () => <WorkInProgressPage title={messages.workspaceSettingsTitle} description={messages.workspaceSettingsDescription} />,
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
