import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../src/Web/src/app/App';
import { UserDetailPage } from '../../src/Web/src/features/users/UserDetailPage';

const apiFetch = vi.hoisted(() => vi.fn());
const getApiToken = vi.hoisted(() => vi.fn().mockResolvedValue('api-token'));

vi.mock('../../src/Web/src/auth/useApi', () => ({ useApi: () => apiFetch }));
vi.mock('../../src/Web/src/auth/AuthProvider', () => ({ useAuth: () => ({ account: { username: 'customer@example.com' }, getApiToken }) }));

afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); apiFetch.mockReset(); window.history.replaceState({}, '', '/'); });

describe('customer overview route', () => {
  it('renders the customer overview connection state from route loaders', async () => {
    window.history.replaceState({}, '', '/overview');
    render(<App
      loadSession={async () => ({ user: { displayName: 'Customer admin' }, workspace: { id: 'workspace-1', name: 'Local customer' } })}
      loadCapabilities={async () => ({ evaluatedAt: '2026-09-21T12:00:00Z', sourceState: 'connected', capabilities: [
        { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
        { capability: 'pim.activate', state: 'pim_activation_required', reasonCode: 'pim_activation_required', pim: { state: 'eligible_inactive', activationUrl: 'https://entra.microsoft.com/pim' }, nextStep: { label: 'Activate the required Entra role', href: 'https://entra.microsoft.com/pim' } }
      ] })}
      loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: '2026-09-21T12:00:00Z' })}
    />);

    expect((await screen.findByTestId('connection-state')).textContent).toMatch(/stale|connected/i);
  });

  it('renders the workspace after continuing from a redeemed invitation', async () => {
    window.history.replaceState({}, '', '/invitations/test-nonce');
    apiFetch.mockImplementation(async (path: string) => {
      if (path === '/api/capabilities') return Response.json({ evaluatedAt: '2026-09-21T12:00:00Z', sourceState: 'unknown', capabilities: [] });
      if (path === '/api/session') return Response.json({ user: { displayName: 'Customer admin' }, workspace: { id: 'workspace-1', name: 'Local customer' } });
      if (path === '/api/overview') return Response.json({ freshness: 'live', fetchedAt: '2026-09-21T12:00:00Z', totalUsers: 0, licenseCoverage: { assigned: 0, available: 0, percentage: 0 }, permissionHealth: { state: 'healthy', allowedCount: 0, totalCount: 0 }, pimAttention: { requiresAttention: false, count: 0 }, partialData: false, access: { state: 'allowed' } });
      if (path === '/api/workspaces/current/connection-health') return Response.json({ status: 'consent_required', lastVerifiedAt: null });
      return new Response(null, { status: 404 });
    });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json({ status: 'consent_required', workspaceId: 'workspace-1', workspaceName: 'Local customer', nextStep: '/overview' })));

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Redeem invitation' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Continue to workspace' }));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Overview' })).toBeTruthy());
    expect(window.location.pathname).toBe('/overview');
  });

  it('uses the fixture API boundary for user actions and associated-device navigation', async () => {
    window.history.replaceState({}, '', '/users/user-1');
    const requests: string[] = [];
    apiFetch.mockImplementation(async (path: string) => {
      requests.push(path);
      if (path === '/api/users/user-1') return Response.json({
        access: { authorization: { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' }, fetchedAt: '2026-09-21T12:00:00Z', freshness: 'fresh', partialData: false },
        user: { id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: 'ada@example.com', accountEnabled: true, userType: 'Member', isReadOnly: false, sourceOfAuthority: 'cloud' },
        licenses: { access: { authorization: { capability: 'licenses.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T12:00:00Z', freshness: 'fresh', partialData: false }, items: [] },
        groups: { access: { authorization: { capability: 'groups.manage_members', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T12:00:00Z', freshness: 'fresh', partialData: false }, items: [] },
        roles: { access: { authorization: { capability: 'roles.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T12:00:00Z', freshness: 'fresh', partialData: false }, items: [] },
        pim: { access: { authorization: { capability: 'pim.activate', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T12:00:00Z', freshness: 'fresh', partialData: false }, items: [] },
      });
      if (path === '/api/users/user-1/devices') return Response.json({
        items: [{ id: 'device-1', deviceName: 'WIN-TEST-01', operatingSystem: 'Windows' }],
        fetchedAt: '2026-09-21T12:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
      });
      throw new Error(`unexpected fixture route: ${path}`);
    });

    render(<UserDetailPage userId="user-1" capabilities={[
      { capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' },
      { capability: 'users.reset_password', state: 'allowed', reasonCode: 'active_role' },
      { capability: 'users.sessions.revoke', state: 'allowed', reasonCode: 'active_role' },
    ]} />);

    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Reset password' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Actions' })).toBeTruthy();
    expect(await screen.findByRole('link', { name: 'Open device WIN-TEST-01' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Open device WIN-TEST-01' }).getAttribute('href')).toBe('/devices?device=device-1');
    expect(requests.every((path) => path.startsWith('/api/'))).toBe(true);
    expect(requests.some((path) => /actions\/(retire|wipe)/i.test(path))).toBe(false);
  });
});
