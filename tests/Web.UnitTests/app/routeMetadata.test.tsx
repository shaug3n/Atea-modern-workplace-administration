import { describe, expect, it } from 'vitest';
import { appRoutes, type NavigationGroup, type NavigationVisibility } from '../../../src/Web/src/app/routes';

describe('route navigation metadata', () => {
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
    expect(navigationRoutes.map((route) => route.navigation!.order)).toEqual([0, 0, 0, 1, 0, 0, 0, 0]);
    expect(navigationRoutes.every((route) => visibilityKinds.includes(route.navigation!.visibility))).toBe(true);

    const nonNavigablePaths = [
      '/consent-callback',
      '/onboarding/consent/callback',
      '/onboarding',
      '/identity',
      '/users/:userId',
      '/devices/:id',
      '/settings/setup',
      '/settings/general',
      '/settings/modules',
      '/settings/access',
      '/workspace-access',
      '/workspace-settings',
      '/audit',
    ];
    for (const path of nonNavigablePaths) {
      expect(appRoutes.find((route) => route.path === path)?.navigation).toBeUndefined();
    }
  });
});
