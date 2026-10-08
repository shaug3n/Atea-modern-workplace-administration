import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import type { CapabilityDecision } from '../../../src/Web/src/capabilities/capabilityTypes';
import { DomainAccessChip } from '../../../src/Web/src/components/DomainAccessChip';

describe('DomainAccessChip', () => {
  afterEach(cleanup);

  it('hidden view access stays hidden even when manage is allowed', () => {
    const viewDecision: CapabilityDecision = {
      capability: 'devices.view',
      state: 'hidden',
      reasonCode: 'not_available',
    };
    const writeDecision: CapabilityDecision = {
      capability: 'devices.manage',
      state: 'allowed',
      reasonCode: 'active_role',
    };

    const { container } = render(
      <DomainAccessChip
        label="Devices"
        viewDecision={viewDecision}
        viewReason="Device viewing is unavailable."
        writeDecision={writeDecision}
        writeReason="Device changes are allowed."
      />,
    );

    expect(container.textContent).toBe('');
  });

  it('view access without an allowed manage decision is read-only', () => {
    const viewDecision: CapabilityDecision = {
      capability: 'devices.view',
      state: 'allowed',
      reasonCode: 'active_role',
    };
    const writeDecision: CapabilityDecision = {
      capability: 'devices.manage',
      state: 'read_only',
      reasonCode: 'role_read_only',
    };

    render(
      <DomainAccessChip
        label="Devices"
        viewDecision={viewDecision}
        viewReason="Device viewing is available."
        writeDecision={writeDecision}
        writeReason="Device changes are read-only."
      />,
    );

    expect(screen.getByText('Devices')).toBeTruthy();
    expect(screen.getByText('Read-only')).toBeTruthy();
    expect(screen.queryByText('Read / write')).toBeNull();
  });
});
