import { describe, expect, it } from 'vitest';
import { appRoutes, capabilityDecisionFor, type AppRoute } from '../../../src/Web/src/app/routes';
import type { Capability } from '../../../src/Web/src/capabilities/capabilityTypes';
import { reservedCapabilities } from '../../../src/Web/src/capabilities/capabilityTypes';

const typedReservedCapabilities: Capability[] = [
  'authentication.campaigns.manage',
  'licenses.hygiene.view',
  'platform.about.view',
  'feedback.submit',
];

const typedReservedModules: NonNullable<AppRoute['module']>[] = [
  'license-hygiene',
  'about',
  'feedback',
];

describe('reserved contracts', () => {
  it('keeps reserved keys typed but absent decisions fail closed', () => {
    expect(reservedCapabilities).toEqual(typedReservedCapabilities);
    expect(typedReservedModules).toEqual([
      'license-hygiene',
      'about',
      'feedback',
    ]);

    const futureRoute: AppRoute = {
      path: '/future',
      label: 'Future',
      capability: 'authentication.campaigns.manage',
      module: 'authentication-campaigns',
      render: () => null,
    };
    expect(capabilityDecisionFor(futureRoute, [])).toEqual({
      capability: 'authentication.campaigns.manage',
      state: 'hidden',
      reasonCode: 'capability_not_returned',
    });
    expect(appRoutes.find(route => route.path === '/authentication-campaigns')).toMatchObject({
      module: 'authentication-campaigns',
      capability: 'authentication.campaigns.view',
    });
  });
});
