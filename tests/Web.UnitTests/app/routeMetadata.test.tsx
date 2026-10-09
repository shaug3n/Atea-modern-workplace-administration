import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { appRoutes, matchRoute, type NavigationGroup, type NavigationVisibility } from '../../../src/Web/src/app/routes';
import { App, getAuthorizedAppRoutes } from '../../../src/Web/src/app/App';
import type { CapabilitySnapshot } from '../../../src/Web/src/capabilities/capabilityTypes';

describe('route navigation metadata', () => {
  afterEach(() => {
    cleanup();
    window.history.pushState(null, '', '/');
  });

  it('marks only current navigation destinations and keeps details out of navigation', () => {
    const navigationRoutes = appRoutes.filter((route) => route.navigation);
    const destinations = navigationRoutes.map(({ path, navigation }) => ({
      path,
      group: navigation?.group,
      visibility: navigation?.visibility,
      icon: navigation?.icon,
    }));

    expect(destinations).toEqual([
      { path: '/settings', group: 'platform', visibility: 'workspace-manager', icon: 'settings' },
      { path: '/about', group: 'platform', visibility: 'module', icon: 'overview' },
      { path: '/feedback', group: 'platform', visibility: 'module', icon: 'activity' },
      { path: '/overview', group: 'overview', visibility: 'always', icon: 'overview' },
      { path: '/users', group: 'identity-access', visibility: 'module', icon: 'users' },
      { path: '/authentication-campaigns', group: 'identity-access', visibility: 'module', icon: 'lock' },
      { path: '/licenses', group: 'licenses', visibility: 'module', icon: 'licenses' },
      { path: '/activity', group: 'operations', visibility: 'audit-not-hidden', icon: 'activity' },
      { path: '/devices', group: 'devices', visibility: 'device-module-or-settings-manager', icon: 'devices' },
      { path: '/services/exchange', group: 'services', visibility: 'module', icon: 'mail' },
    ]);

    const groupOrder: NavigationGroup[] = ['overview', 'identity-access', 'devices', 'licenses', 'services', 'operations', 'platform'];
    const visibilityKinds: NavigationVisibility[] = ['always', 'module', 'device-module-or-settings-manager', 'audit-not-hidden', 'workspace-manager'];
    expect([...new Set(navigationRoutes.map((route) => route.navigation!.group))].sort((a, b) => groupOrder.indexOf(a) - groupOrder.indexOf(b))).toEqual(groupOrder);
    expect(navigationRoutes.map((route) => route.navigation!.order)).toEqual([0, 1, 2, 0, 0, 1, 0, 0, 0, 0]);
    expect(navigationRoutes.every((route) => visibilityKinds.includes(route.navigation!.visibility))).toBe(true);

    const feedback = appRoutes.find((route) => route.path === '/feedback')!;
    expect(feedback).toMatchObject({
      module: 'feedback',
      capability: 'feedback.submit',
      navigation: { group: 'platform', order: 2 },
    });
    const workspace = { user: {}, workspace: { id: 'workspace-1', name: 'Workspace', enabledModules: ['feedback'], moduleAccess: ['feedback'] } };
    expect(getAuthorizedAppRoutes(workspace, [{ capability: 'feedback.submit', state: 'allowed', reasonCode: 'workspace_member' }]))
      .toContainEqual({ path: '/feedback', label: 'Feedback' });
    expect(getAuthorizedAppRoutes(workspace, [{ capability: 'feedback.submit', state: 'hidden', reasonCode: 'role_required' }]).map(route => route.path))
      .not.toContain('/feedback');
    expect(getAuthorizedAppRoutes({ ...workspace, workspace: { ...workspace.workspace, enabledModules: [] } }, [{ capability: 'feedback.submit', state: 'allowed', reasonCode: 'workspace_member' }]).map(route => route.path))
      .not.toContain('/feedback');

    const nonNavigablePaths = [
      '/consent-callback',
      '/onboarding/consent/callback',
      '/onboarding',
      '/identity',
      '/users/:userId',
      '/licenses/hygiene',
      '/devices/:id',
      '/settings/setup',
      '/settings/general',
      '/settings/modules',
      '/settings/access',
      '/workspace-access',
      '/my-access',
      '/workspace-settings',
      '/audit',
      '/about/system-versions',
    ];
    for (const path of nonNavigablePaths) {
      expect(appRoutes.find((route) => route.path === path)?.navigation).toBeUndefined();
    }
  });

  it('registers hygiene as an authorized non-navigation route', () => {
    const route = appRoutes.find(({ path }) => path === '/licenses/hygiene');

    expect(route).toMatchObject({ path: '/licenses/hygiene', module: 'license-hygiene', capability: 'licenses.hygiene.view' });
    expect(route?.navigation).toBeUndefined();
  });

  it.each([
    ['denied capability', '55555555-5555-5555-5555-555555555555', 'hidden', ['licenses', 'license-hygiene'], ['licenses', 'license-hygiene']],
    ['stale capability snapshot', 'stale-workspace', 'allowed', ['licenses', 'license-hygiene'], ['licenses', 'license-hygiene']],
    ['missing capability decision', '55555555-5555-5555-5555-555555555555', null, ['licenses', 'license-hygiene'], ['licenses', 'license-hygiene']],
    ['missing assigned module', '55555555-5555-5555-5555-555555555555', 'allowed', ['licenses', 'license-hygiene'], ['licenses']],
    ['unknown assigned module state', '55555555-5555-5555-5555-555555555555', 'allowed', ['licenses', 'license-hygiene'], undefined],
    ['missing enabled module', '55555555-5555-5555-5555-555555555555', 'allowed', ['licenses'], ['licenses', 'license-hygiene']],
  ])('denies the direct hygiene route with %s', async (_case, capabilityWorkspaceId, state, enabledModules, moduleAccess) => {
    window.history.pushState(null, '', '/licenses/hygiene');
    const session = {
      user: { displayName: 'Alex Morgan', userPrincipalName: 'alex@example.com' },
      workspace: { id: '55555555-5555-5555-5555-555555555555', name: 'Contoso', enabledModules, moduleAccess },
    };
    const snapshot: CapabilitySnapshot = {
      workspaceId: capabilityWorkspaceId,
      evaluatedAt: '2026-10-08T10:00:00Z',
      sourceState: 'graph_authoritative',
      capabilities: state === null ? [] : [{ capability: 'licenses.hygiene.view', state: state as 'allowed' | 'hidden', reasonCode: 'role_required' }],
    };

    render(<App loadCapabilities={async () => snapshot} loadSession={async () => session} />);

    expect(await screen.findByText(/Data cannot be shown right now|turned off for this workspace|don't have access/i)).toBeTruthy();
    expect(screen.queryByText('What this review covers')).toBeNull();
  });

  it('keeps My access available to ordinary signed-in members without capability or manager gates', () => {
    const route = appRoutes.find(({ path }) => path === '/my-access');

    expect(route).toBeDefined();
    expect(route?.navigation).toBeUndefined();
    expect(route?.module).toBeUndefined();
    expect(route?.capability).toBeUndefined();
    expect(route?.workspaceAccess).toBeUndefined();
    expect(matchRoute('/my-access')).toBe(route);
  });
});
