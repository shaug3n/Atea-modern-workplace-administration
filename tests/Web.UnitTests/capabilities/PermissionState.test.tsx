import { cleanup, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CapabilitySnapshot } from '../../../src/Web/src/capabilities/capabilityTypes';
import { PermissionState } from '../../../src/Web/src/components/PermissionState';
import { useCapabilities } from '../../../src/Web/src/capabilities/useCapabilities';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock
}));

describe('PermissionState', () => {
  afterEach(cleanup);

  it('renders nothing when the capability is hidden', () => {
    const { container } = render(
      <PermissionState decision={{ capability: 'users.view', state: 'hidden', reasonCode: 'directory_read_required' }}>
        <button>Manage users</button>
      </PermissionState>
    );

    expect(container.textContent).toBe('');
  });

  it('keeps read only content visible with an accessible explanation', () => {
    render(
      <PermissionState decision={{ capability: 'users.create', state: 'read_only', reasonCode: 'role_read_only' }}>
        <button>Create user</button>
      </PermissionState>
    );

    expect((screen.getByRole('button', { name: 'Create user' }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByRole('status').textContent).toContain('read-only');
  });

  it.each([
    ['consent_required', 'Grant delegated consent', 'https://entra.example/consent'],
    ['pim_activation_required', 'Activate the required Entra role', 'https://entra.example/activate'],
    ['pim_approval_required', 'Wait for PIM approval', 'https://entra.example/approval'],
    ['pim_mfa_required', 'Complete MFA for PIM activation', 'https://entra.example/mfa'],
  ] as const)('renders %s state with a next-step link', (state, linkText, href) => {
    render(
      <PermissionState decision={{ capability: 'users.create', state, reasonCode: state, nextStep: { label: linkText, href } }}>
        <button>Create user</button>
      </PermissionState>
    );

    expect((screen.getByRole('button', { name: 'Create user' }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByRole('link', { name: linkText }).getAttribute('href')).toBe(href);
  });
});

describe('useCapabilities', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('loads the workspace capability snapshot from the API boundary', async () => {
    const snapshot: CapabilitySnapshot = {
      workspaceId: '55555555-5555-5555-5555-555555555555',
      evaluatedAt: '2026-09-20T10:00:00.000Z',
      capabilities: [
        { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
        { capability: 'users.create', state: 'read_only', reasonCode: 'role_read_only' },
      ],
    };

    const loadCapabilities = vi.fn().mockResolvedValue(snapshot);

    function Harness() {
      const { capabilities, loading } = useCapabilities(loadCapabilities);
      if (loading) return <p>Loading</p>;
      return <p>{capabilities?.capabilities.find((entry) => entry.capability === 'users.create')?.state}</p>;
    }

    render(<Harness />);

    await waitFor(() => expect(screen.getByText('read_only')).toBeTruthy());
    expect(loadCapabilities).toHaveBeenCalledOnce();
  });
});
