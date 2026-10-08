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

  it.each(['consent_required', 'temporarily_unavailable', 'read_only'] as const)(
    'does not claim read/write when view access is %s, even if manage is allowed',
    (state) => {
      render(
        <DomainAccessChip
          label="Devices"
          viewDecision={{
            capability: 'devices.view',
            state,
            reasonCode: 'view_not_allowed',
          }}
          viewReason="Device viewing is unavailable."
          writeDecision={{
            capability: 'devices.manage',
            state: 'allowed',
            reasonCode: 'active_role',
          }}
        />,
      );

      expect(screen.getByText('Devices')).toBeTruthy();
      expect(screen.getByText('Unavailable')).toBeTruthy();
      expect(screen.queryByText('Read / write')).toBeNull();
      expect(document.querySelector('.status-badge')?.getAttribute('data-tone')).toBe('neutral');
    },
  );

  it('shows read/write with success tone when both view and manage are allowed', () => {
    render(
      <DomainAccessChip
        label="Devices"
        viewDecision={{
          capability: 'devices.view',
          state: 'allowed',
          reasonCode: 'active_role',
        }}
        viewReason="Device viewing is available."
        writeDecision={{
          capability: 'devices.manage',
          state: 'allowed',
          reasonCode: 'active_role',
        }}
      />,
    );

    expect(screen.getByText('Read / write')).toBeTruthy();
    expect(document.querySelector('.status-badge')?.getAttribute('data-tone')).toBe('success');
  });

  it('shows read-only when no manage decision is supplied', () => {
    render(
      <DomainAccessChip
        label="Devices"
        viewDecision={{
          capability: 'devices.view',
          state: 'allowed',
          reasonCode: 'active_role',
        }}
        viewReason="Device viewing is available."
      />,
    );

    expect(screen.getByText('Read-only')).toBeTruthy();
    expect(screen.queryByText('Read / write')).toBeNull();
  });
});
