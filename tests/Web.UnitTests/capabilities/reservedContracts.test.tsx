import { describe, expect, it } from 'vitest';
import { appRoutes, capabilityDecisionFor, type AppRoute } from '../../../src/Web/src/app/routes';
import type { Capability } from '../../../src/Web/src/capabilities/capabilityTypes';
import { reservedCapabilities } from '../../../src/Web/src/capabilities/capabilityTypes';
import { accessSummaryCapabilityGroups } from '../../../src/Web/src/features/my-access/accessSummary';

const typedReservedCapabilities: Capability[] = [
  'authentication.campaigns.manage',
];

const typedActiveCapabilities: Capability[] = [
  'licenses.hygiene.view',
  'platform.about.view',
  'feedback.submit',
];

const typedReservedModules: NonNullable<AppRoute['module']>[] = [
];

describe('reserved contracts', () => {
  it('keeps only inactive capability keys reserved while shipped modules are active', () => {
    expect(reservedCapabilities).toEqual(typedReservedCapabilities);
    expect(typedActiveCapabilities).toEqual(['licenses.hygiene.view', 'platform.about.view', 'feedback.submit']);
    expect(typedReservedModules).toEqual([]);
    expect(reservedCapabilities).not.toContain('platform.about.view');
    expect(reservedCapabilities).not.toContain('feedback.submit');
    expect(reservedCapabilities).not.toContain('authentication.campaigns.view');
    expect(typedReservedModules).not.toContain('about');
    expect(typedReservedModules).not.toContain('feedback');

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

  it('keeps reserved capabilities out of access-summary mappings while including workspace member management', () => {
    const mapped = accessSummaryCapabilityGroups.flatMap(group => [...group.read, ...group.write]);
    expect(mapped).toContain('workspace.members.manage');
    expect(mapped).toContain('authentication.campaigns.view');
    expect(mapped).not.toContain('authentication.campaigns.manage');
    expect(mapped.some(capability => reservedCapabilities.includes(capability as typeof reservedCapabilities[number]))).toBe(false);
  });
});
