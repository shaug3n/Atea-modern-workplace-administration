import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminApp, isAdminPath } from '../../src/Web/src/features/admin/AdminApp';
import { App } from '../../src/Web/src/app/App';

const workspaceId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const customerTenantId = '11111111-1111-1111-1111-111111111111';
const customerObjectId = '22222222-2222-2222-2222-222222222222';

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

const detailAfterMembership = {
  ...initialDetail,
  memberships: [{
    id: 'membership-1',
    tenantObjectId: customerObjectId,
    email: 'customer.admin@example.test',
    platformRole: 'CustomerAdmin',
    isAteaOperator: false,
  }],
  invitations: [{
    id: 'invitation-1',
    email: 'customer.admin@example.test',
    displayName: 'Customer Admin',
    expiresAt: '2026-09-22T12:00:00Z',
    redeemedAt: null,
  }],
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

  it('logs in with the cookie API, creates a workspace, hands off membership and invitation, and keeps safe status metadata', async () => {
    let authenticated = false;
    let membershipAdded = false;
    const requests: Array<{ path: string; method: string; body?: string }> = [];

    vi.spyOn(window, 'fetch').mockImplementation(async (input, init) => {
      const path = new URL(String(input), window.location.origin).pathname;
      requests.push({ path, method: init?.method ?? 'GET', body: typeof init?.body === 'string' ? init.body : undefined });

      if (path === '/api/admin-auth/session') return authenticated ? jsonResponse({ displayName: 'Local Atea Admin' }) : jsonResponse({}, 401);
      if (path === '/api/admin-auth/login') {
        authenticated = true;
        return jsonResponse({ displayName: 'Local Atea Admin' });
      }
      if (path === '/api/platform/workspaces' && (!init?.method || init.method === 'GET')) return jsonResponse([]);
      if (path === '/api/platform/workspaces' && init?.method === 'POST') return jsonResponse(workspace);
      if (path === `/api/platform/workspaces/${workspaceId}`) return jsonResponse(membershipAdded ? detailAfterMembership : initialDetail);
      if (path === `/api/platform/workspaces/${workspaceId}/memberships`) {
        membershipAdded = true;
        return jsonResponse(detailAfterMembership.memberships[0]);
      }
      if (path === `/api/platform/workspaces/${workspaceId}/invitations`) {
        return jsonResponse({ invitationUrl: 'https://example.test/redeem/opaque-fixture', expiresAt: '2026-09-22T12:00:00Z' });
      }
      throw new Error(`Unexpected request: ${path}`);
    });

    render(<AdminApp />);
    expect(await screen.findByRole('heading', { name: 'Admin sign in' })).toBeTruthy();

    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'local-admin' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'local-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    fireEvent.click(await screen.findByRole('button', { name: 'Create workspace' }));
    fireEvent.change(screen.getByLabelText('Tenant ID'), { target: { value: customerTenantId } });
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: workspace.displayName } });
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace' }));

    expect(await screen.findByRole('heading', { name: workspace.displayName })).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Add membership' }));
    fireEvent.change(screen.getByLabelText('Entra object ID'), { target: { value: customerObjectId } });
    fireEvent.change(screen.getByLabelText('Membership email'), { target: { value: 'customer.admin@example.test' } });
    fireEvent.change(screen.getByLabelText('Platform role'), { target: { value: 'CustomerAdmin' } });
    fireEvent.click(screen.getAllByRole('button', { name: 'Add membership', exact: true })[1]);

    expect(await screen.findByText(/Membership added/)).toBeTruthy();
    expect(screen.getByText(/customer\.admin@example\.test — CustomerAdmin/)).toBeTruthy();
    expect(screen.getByText(/Invitation pending/)).toBeTruthy();
    expect(screen.queryByText(/nonce|hash|opaque-fixture/i)).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Create invitation' }));
    fireEvent.change(screen.getByLabelText('Invitation email'), { target: { value: 'customer.admin@example.test' } });
    fireEvent.change(screen.getByLabelText('Invitee display name'), { target: { value: 'Customer Admin' } });
    fireEvent.click(screen.getAllByRole('button', { name: 'Create invitation', exact: true })[1]);

    expect(await screen.findByText(/credential-like secret/i)).toBeTruthy();
    expect(requests.map(({ path }) => path)).toEqual([
      '/api/admin-auth/session',
      '/api/admin-auth/login',
      '/api/platform/workspaces',
      '/api/platform/workspaces',
      `/api/platform/workspaces/${workspaceId}`,
      `/api/platform/workspaces/${workspaceId}/memberships`,
      `/api/platform/workspaces/${workspaceId}`,
      `/api/platform/workspaces/${workspaceId}/invitations`,
    ]);
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
