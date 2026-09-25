import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
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

  it('prevents a read-only action link from navigating while leaving its context visible', () => {
    const navigate = vi.fn();
    render(<PermissionState decision={{ capability: 'users.create', state: 'read_only', reasonCode: 'role_read_only' }}><a href="/users/new" onClick={navigate}>Create user</a></PermissionState>);
    fireEvent.click(screen.getByRole('link', { name: 'Create user' }));
    expect(navigate).not.toHaveBeenCalled();
    expect(screen.getByRole('link', { name: 'Create user' }).getAttribute('aria-disabled')).toBe('true');
  });

  it('shows missing delegated scopes alongside the blocked action', () => {
    render(
      <PermissionState decision={{ capability: 'users.update', state: 'consent_required', reasonCode: 'delegated_scope_required', missingScopes: ['User.ReadWrite.All'] }}>
        <button>Save changes</button>
      </PermissionState>
    );

    expect(screen.getByRole('status').textContent).toContain('User.ReadWrite.All');
    expect(screen.getByRole('status').textContent).toContain('Unavailable through the API’s delegated Graph token');
  });

  it('does not label a transient scope probe as missing consent', () => {
    render(
      <PermissionState decision={{ capability: 'devices.view', state: 'temporarily_unavailable', reasonCode: 'scope_probe_unavailable', missingScopes: ['DeviceManagementManagedDevices.Read.All'] }}>
        <button>Open devices</button>
      </PermissionState>
    );

    expect(screen.getByRole('status').textContent).toContain('could not be verified');
    expect(screen.getByRole('status').textContent).not.toContain('Unavailable through the API’s delegated Graph token');
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

  it('starts API consent with POST before exposing the Entra authorization link', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ authorizationUrl: 'https://login.example/consent' }), { status: 200 }));
    render(
      <PermissionState decision={{ capability: 'users.create', state: 'consent_required', reasonCode: 'consent_required', nextStep: { label: 'Grant delegated consent', href: '/api/workspaces/current/consent/start' } }}>
        <button>Create user</button>
      </PermissionState>
    );

    fireEvent.click(screen.getByRole('button', { name: 'Grant delegated consent' }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/workspaces/current/consent/start', { method: 'POST' }));
    expect((await screen.findByRole('link', { name: 'Continue consent' })).getAttribute('href')).toBe('https://login.example/consent');
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
