import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminApp, isAdminPath } from '../../src/Web/src/features/admin/AdminApp';
import { App } from '../../src/Web/src/app/App';

const workspaceId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const customerTenantId = '11111111-1111-1111-1111-111111111111';

const workspace = {
  id: workspaceId,
  tenantId: customerTenantId,
  displayName: 'Demo customer workspace',
  connectionStatus: 'awaiting_invitation',
};

const initialDetail = {
  ...workspace,
  lastVerifiedAt: null,
  connectionFailureCategory: null,
  memberships: [],
  invitations: [],
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('local Atea admin onboarding flow', () => {
  beforeEach(() => {
    window.history.replaceState({}, '', '/admin');
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    window.history.replaceState({}, '', '/');
  });

  it('onboards a workspace and its first customer admin in one guided flow', async () => {
    let authenticated = false;
    const requests: Array<{ path: string; method: string; body?: Record<string, unknown> }> = [];

    vi.spyOn(window, 'fetch').mockImplementation(async (input, init) => {
      const path = new URL(String(input), window.location.origin).pathname;
      requests.push({ path, method: init?.method ?? 'GET', body: typeof init?.body === 'string' ? JSON.parse(init.body) as Record<string, unknown> : undefined });

      if (path === '/api/admin-auth/session') return authenticated ? jsonResponse({ displayName: 'Local Atea Admin' }) : jsonResponse({}, 401);
      if (path === '/api/admin-auth/login') {
        authenticated = true;
        return jsonResponse({ displayName: 'Local Atea Admin' });
      }
      if (path === '/api/platform/workspaces' && (!init?.method || init.method === 'GET')) return jsonResponse([]);
      if (path === '/api/platform/workspaces/onboard' && init?.method === 'POST') return jsonResponse({ workspace, invitationUrl: 'https://example.test/invitations/one-time-first-admin', expiresAt: '2026-09-27T12:00:00Z' }, 201);
      if (path === `/api/platform/workspaces/${workspaceId}`) return jsonResponse({ ...initialDetail, invitations: [{ id: 'invitation-1', email: 'customer.admin@example.test', displayName: 'Customer Admin', role: 'customer_admin', expiresAt: '2026-09-27T12:00:00Z', redeemedAt: null, revokedAt: null }] });
      if (path === `/api/platform/workspaces/${workspaceId}/invitations/invitation-1/reissue` && init?.method === 'POST') {
        return jsonResponse({ invitationUrl: 'https://example.test/invitations/reissued-admin', expiresAt: '2026-10-01T12:00:00Z' });
      }
      if (path === `/api/platform/workspaces/${workspaceId}/invitations/invitation-1` && init?.method === 'DELETE') return new Response(null, { status: 204 });
      throw new Error(`Unexpected request: ${path}`);
    });

    render(<AdminApp />);
    expect(await screen.findByRole('heading', { name: 'Admin sign in' })).toBeTruthy();

    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'local-admin' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'local-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    fireEvent.click(await screen.findByRole('button', { name: 'Create workspace' }));
    fireEvent.change(screen.getByLabelText('Tenant ID'), { target: { value: customerTenantId } });
    fireEvent.change(screen.getByLabelText('Workspace name'), { target: { value: workspace.displayName } });
    fireEvent.change(screen.getByLabelText('First admin sign-in address'), { target: { value: 'customer.admin@example.test' } });
    fireEvent.change(screen.getByLabelText('First admin display name'), { target: { value: 'Customer Admin' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace and invite admin' }));

    expect(await screen.findByRole('heading', { name: workspace.displayName })).toBeTruthy();
    expect(await screen.findByText('https://example.test/invitations/one-time-first-admin')).toBeTruthy();
    expect(screen.getByText(/waiting for the first customer administrator/i)).toBeTruthy();
    expect(screen.queryByRole('button', { name: /add membership/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /create invitation/i })).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Reissue invitation for customer.admin@example.test' }));
    expect(await screen.findByText('https://example.test/invitations/reissued-admin')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Revoke invitation for customer.admin@example.test' }));
    expect(screen.getByText('This invalidates the pending link.')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm revoke invitation for customer.admin@example.test' }));
    await waitFor(() => expect(requests.some(({ path, method }) => path.endsWith('/invitations/invitation-1') && method === 'DELETE')).toBe(true));
    await waitFor(() => expect(requests.filter(({ path }) => path === `/api/platform/workspaces/${workspaceId}`).length).toBe(3));

    expect(requests.map(({ path }) => path)).toEqual([
      '/api/admin-auth/session',
      '/api/admin-auth/login',
      '/api/platform/workspaces',
      '/api/platform/workspaces/onboard',
      `/api/platform/workspaces/${workspaceId}`,
      `/api/platform/workspaces/${workspaceId}/invitations/invitation-1/reissue`,
      `/api/platform/workspaces/${workspaceId}`,
      `/api/platform/workspaces/${workspaceId}/invitations/invitation-1`,
      `/api/platform/workspaces/${workspaceId}`,
    ]);
    expect(requests.find(request => request.path === '/api/platform/workspaces/onboard')?.body).toEqual({ tenantId: customerTenantId, displayName: workspace.displayName, adminUpn: 'customer.admin@example.test', adminDisplayName: 'Customer Admin' });
  });

  it('keeps the local admin route separate from the customer Entra sign-in boundary', async () => {
    expect(isAdminPath('/admin')).toBe(true);
    expect(isAdminPath('/admin/workspaces/demo')).toBe(true);
    expect(isAdminPath('/overview')).toBe(false);

    window.history.replaceState({}, '', '/overview');
    render(<App loadCapabilities={async () => ({ capabilities: [], evaluatedAt: '2026-09-21T12:00:00Z', sourceState: 'unknown' })} loadSession={async () => { throw new Error('customer session required'); }} />);

    await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/workspace/i));
    expect(screen.queryByRole('heading', { name: 'Admin sign in' })).toBeNull();
  });
});
