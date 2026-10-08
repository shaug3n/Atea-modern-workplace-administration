import { describe, expect, it } from 'vitest';
import { capabilityDecisionFor, type AppRoute } from '../../../src/Web/src/app/routes';
import type { Capability } from '../../../src/Web/src/capabilities/capabilityTypes';
import { reservedCapabilities } from '../../../src/Web/src/capabilities/capabilityTypes';

const typedReservedCapabilities: Capability[] = [
  'authentication.campaigns.view',
  'authentication.campaigns.manage',
  'licenses.hygiene.view',
  'platform.about.view',
  'feedback.submit',
];

const typedReservedModules: NonNullable<AppRoute['module']>[] = [
  'authentication-campaigns',
  'license-hygiene',
  'about',
  'feedback',
];

describe('reserved contracts', () => {
  it('keeps reserved keys typed but absent decisions fail closed', () => {
    expect(reservedCapabilities).toEqual(typedReservedCapabilities);
    expect(typedReservedModules).toEqual([
      'authentication-campaigns',
      'license-hygiene',
      'about',
      'feedback',
    ]);

    const futureRoute: AppRoute = {
      path: '/future',
      label: 'Future',
      capability: 'authentication.campaigns.view',
      module: 'authentication-campaigns',
      render: () => null,
    };
    expect(capabilityDecisionFor(futureRoute, [])).toEqual({
      capability: 'authentication.campaigns.view',
      state: 'hidden',
      reasonCode: 'capability_not_returned',
    });
  });
});
