import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../src/Web/src/app/App';
import { UserDetailPage } from '../../src/Web/src/features/users/UserDetailPage';

const apiFetch = vi.hoisted(() => vi.fn());
const getApiToken = vi.hoisted(() => vi.fn().mockResolvedValue('api-token'));
const signIn = vi.hoisted(() => vi.fn());

vi.mock('../../src/Web/src/auth/useApi', () => ({ useApi: () => apiFetch }));
vi.mock('../../src/Web/src/auth/AuthProvider', () => ({ useAuth: () => ({ account: { username: 'customer@example.com' }, getApiToken, signIn }) }));

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
      if (path === '/api/session') return Response.json({ user: { displayName: 'Customer admin' }, workspace: { id: 'workspace-1', name: 'Local customer' }, workspaceAccess: { role: 'customer_admin', canManageMembers: true, canManageSettings: true } });
      if (path === '/api/overview') return Response.json({ freshness: 'live', fetchedAt: '2026-09-21T12:00:00Z', totalUsers: 0, licenseCoverage: { assigned: 0, available: 0, percentage: 0 }, permissionHealth: { state: 'healthy', allowedCount: 0, totalCount: 0 }, pimAttention: { requiresAttention: false, count: 0 }, partialData: false, access: { state: 'allowed' } });
      if (path === '/api/workspaces/current/connection-health') return Response.json({ status: 'consent_required', lastVerifiedAt: null });
      if (path === '/api/workspaces/current/consent/start') return Response.json({ authorizationUrl: 'https://login.microsoftonline.com/tenant/adminconsent?client_id=demo' });
      return new Response(null, { status: 404 });
    });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json({ status: 'consent_required', workspaceId: 'workspace-1', workspaceName: 'Local customer', nextStep: '/overview' })));

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Redeem invitation' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Continue to workspace' }));

    expect(await screen.findByRole('heading', { name: 'Workspace Settings' })).toBeTruthy();
    expect(window.location.pathname + window.location.hash).toBe('/settings#connection');
    expect(screen.getByRole('heading', { name: 'Connection', level: 2 })).toBeTruthy();
    expect((await screen.findByTestId('connection-state')).textContent).toBe('Consent required');
    fireEvent.click(screen.getByRole('button', { name: 'Start consent' }));
    expect((await screen.findByRole('link', { name: 'Continue consent' })).getAttribute('href')).toMatch(/adminconsent/);
  });

  it('keeps the session shell and connection settings available when capabilities cannot load', async () => {
    window.history.replaceState({}, '', '/onboarding');
    render(<App
      loadSession={async () => ({ user: { displayName: 'Customer admin' }, workspace: { id: 'workspace-1', name: 'Local customer' }, workspaceAccess: { role: 'customer_admin', canManageMembers: true, canManageSettings: true } })}
      loadCapabilities={async () => { throw new Error('Graph unavailable'); }}
    />);

    expect(await screen.findByRole('heading', { name: 'Workspace Settings' })).toBeTruthy();
    expect(window.location.pathname + window.location.hash).toBe('/settings#connection');
    expect(screen.getByRole('heading', { name: 'Connection', level: 2 })).toBeTruthy();
    const connection = screen.getByRole('heading', { name: 'Connection', level: 2 }).closest('section');
    expect(connection).not.toBeNull();
    expect(await within(connection!).findByRole('button', { name: 'Retry' })).toBeTruthy();
    expect(screen.getByRole('navigation', { name: 'Primary navigation' })).toBeTruthy();
    expect(screen.getByRole('switch', { name: /dark mode/i })).toBeTruthy();
  });

  it('shows an actionable customer connection state without requiring overview metrics', async () => {
    window.history.replaceState({}, '', '/onboarding');
    apiFetch.mockImplementation(async (path: string) => {
      if (path === '/api/session') return Response.json({ user: { displayName: 'Customer admin' }, workspace: { id: 'workspace-1', name: 'Local customer' }, workspaceAccess: { role: 'customer_admin', canManageMembers: true, canManageSettings: true } });
      if (path === '/api/capabilities') throw new Error('Graph unavailable');
      if (path === '/api/workspaces/current/connection-health') return Response.json({ status: 'permission_incomplete', lastVerifiedAt: null, correlationId: 'health-correlation-1' });
      if (path === '/api/workspaces/current/connection-health/check') return Response.json({ status: 'connected', lastVerifiedAt: '2026-09-23T12:00:00Z' });
      return new Response(null, { status: 404 });
    });
    render(<App />);

    expect(await screen.findByText(/permissions incomplete/i)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Check connection' }));
    expect(await screen.findByText(/connected/i)).toBeTruthy();
    expect(apiFetch.mock.calls.some(([path]) => path === '/api/overview')).toBe(false);
  });

  it('offers tenant consent from setup when delegated consent is required', async () => {
    window.history.replaceState({}, '', '/onboarding');
    apiFetch.mockImplementation(async (path: string, init?: RequestInit) => {
      if (path === '/api/session') return Response.json({ user: { displayName: 'Customer admin' }, workspace: { id: 'workspace-1', name: 'Local customer' }, workspaceAccess: { role: 'customer_admin', canManageMembers: true, canManageSettings: true } });
      if (path === '/api/capabilities') throw new Error('Graph unavailable');
      if (path === '/api/workspaces/current/connection-health') return Response.json({ status: 'consent_required', lastVerifiedAt: null });
      if (path === '/api/workspaces/current/consent/start' && init?.method === 'POST') return Response.json({ authorizationUrl: 'https://login.microsoftonline.com/tenant/adminconsent?client_id=demo' });
      return new Response(null, { status: 404 });
    });
    render(<App />);

    fireEvent.click(await screen.findByRole('button', { name: 'Start consent' }));
    const continueConsent = await screen.findByRole('link', { name: 'Continue consent' });
    expect(continueConsent.getAttribute('href')).toMatch(/adminconsent/);
  });

  it('distinguishes an expired sign-in session from missing workspace membership', async () => {
    window.history.replaceState({}, '', '/onboarding');
    apiFetch.mockImplementation(async (path: string) => {
      if (path === '/api/session') return new Response(JSON.stringify({ code: 'authorization_denied', correlationId: 'session-correlation-2' }), { status: 403, headers: { 'Content-Type': 'application/problem+json' } });
      if (path === '/api/capabilities') return Response.json({ evaluatedAt: '2026-09-21T12:00:00Z', sourceState: 'unknown', capabilities: [] });
      return new Response(null, { status: 404 });
    });
    render(<App />);

    expect(await screen.findByRole('heading', { name: /workspace access is not set up/i })).toBeTruthy();
    expect(screen.getByText(/session-correlation-2/)).toBeTruthy();
  });

  it('gives an expired session a sign-in action instead of a membership error', async () => {
    window.history.replaceState({}, '', '/onboarding');
    apiFetch.mockImplementation(async (path: string) => {
      if (path === '/api/session') return new Response(JSON.stringify({ title: 'Authentication required' }), { status: 401, headers: { 'Content-Type': 'application/problem+json' } });
      if (path === '/api/capabilities') return Response.json({ evaluatedAt: '2026-09-21T12:00:00Z', sourceState: 'unknown', capabilities: [] });
      return new Response(null, { status: 404 });
    });
    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Your sign-in has expired' })).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Return to sign in' }));
    expect(signIn).toHaveBeenCalledOnce();
  });

  it('shows a correlation ID and retries a temporary session service failure', async () => {
    window.history.replaceState({}, '', '/onboarding');
    let sessionAttempts = 0;
    apiFetch.mockImplementation(async (path: string) => {
      if (path === '/api/session') {
        sessionAttempts += 1;
        if (sessionAttempts === 1) return new Response(JSON.stringify({ title: 'Temporary failure', correlationId: 'temporary-correlation-3' }), { status: 503, headers: { 'Content-Type': 'application/problem+json' } });
        return Response.json({ user: { displayName: 'Customer admin' }, workspace: { id: 'workspace-1', name: 'Local customer' }, workspaceAccess: { role: 'customer_admin', canManageMembers: true, canManageSettings: true } });
      }
      if (path === '/api/capabilities') return Response.json({ evaluatedAt: '2026-09-21T12:00:00Z', sourceState: 'unknown', capabilities: [] });
      if (path === '/api/workspaces/current/connection-health') return Response.json({ status: 'consent_required', lastVerifiedAt: null });
      return new Response(null, { status: 404 });
    });
    render(<App />);

    expect(await screen.findByText(/temporary-correlation-3/)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('heading', { name: 'Workspace Settings' })).toBeTruthy();
    expect(window.location.pathname + window.location.hash).toBe('/settings#connection');
    expect((await screen.findByTestId('connection-state')).textContent).toBe('Consent required');
    expect(screen.getByRole('button', { name: 'Start consent' })).toBeTruthy();
    expect(sessionAttempts).toBe(2);
  });

  it('provides the customer access route to workspace admins, but denies members', async () => {
    window.history.replaceState({}, '', '/workspace-access');
    const loadSession = async () => ({ user: { displayName: 'Customer member' }, workspace: { id: 'workspace-1', name: 'Local customer' }, workspaceAccess: { role: 'member', canManageMembers: false, canManageSettings: false } });
    render(<App loadSession={loadSession} loadCapabilities={async () => ({ evaluatedAt: '2026-09-21T12:00:00Z', sourceState: 'unknown', capabilities: [] })} />);

    expect(await screen.findByRole('heading', { name: /workspace access is managed by an administrator/i })).toBeTruthy();
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
