import React from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../src/Web/src/app/App';
import { UserDetailPage } from '../../src/Web/src/features/users/UserDetailPage';

const apiAccessToken = 'api-access-token-sentinel';
const graphAccessToken = 'graph-access-token-sentinel';
const apiMock = vi.hoisted(() => vi.fn((path: string, init?: RequestInit) => {
  const headers = new Headers(init?.headers);
  headers.set('Authorization', `Bearer ${apiAccessToken}`);
  return window.fetch(path, { ...init, headers });
}));

vi.mock('../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

const session = {
  user: { displayName: 'Atea Operator', userPrincipalName: 'operator@atea.example' },
  workspace: { id: 'workspace-1', name: 'Contoso Workplace' },
};

function requestText(input: RequestInfo | URL, init?: RequestInit) {
  const headers = new Headers(init?.headers);
  return [
    String(input),
    ...Array.from(headers.entries()).flat(),
    typeof init?.body === 'string' ? init.body : '',
  ].join('\n');
}

function assertNoGraphExposure(value: unknown) {
  const text = typeof value === 'string' ? value : JSON.stringify(value);
  expect(text).not.toMatch(/graph\.microsoft\.com/i);
  expect(text).not.toContain(graphAccessToken);
}

function capability(state: 'allowed' | 'read_only' | 'hidden' | 'consent_required', capabilityName: 'users.view' | 'workspace.settings.manage') {
  return {
    workspaceId: 'workspace-1',
    evaluatedAt: '2026-09-21T10:00:00Z',
    capabilities: [{ capability: capabilityName, state, reasonCode: state === 'hidden' ? 'role_missing' : state }],
    sourceState: 'graph_authoritative',
  } as const;
}

describe('browser security boundaries', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    window.history.replaceState({}, '', '/');
  });

  it('keeps Graph hostnames and Graph access tokens out of browser transport and rendered state', async () => {
    const observedRequests: Array<{ input: RequestInfo | URL; init?: RequestInit }> = [];
    vi.spyOn(window, 'fetch').mockImplementation(async (input, init) => {
      observedRequests.push({ input, init });
      const url = new URL(String(input), window.location.origin);
      expect(url.origin).toBe(window.location.origin);
      return new Response(JSON.stringify({
        access: { authorization: { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' }, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'fresh', partialData: false },
        user: { id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: 'ada@example.com', accountEnabled: true, userType: 'Member', isReadOnly: false, sourceOfAuthority: 'cloud' },
        licenses: { access: { authorization: { capability: 'licenses.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'fresh', partialData: false }, items: [] },
        groups: { access: { authorization: { capability: 'groups.manage_members', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'fresh', partialData: false }, items: [] },
        roles: { access: { authorization: { capability: 'roles.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'fresh', partialData: false }, items: [] },
        pim: { access: { authorization: { capability: 'pim.activate', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'fresh', partialData: false }, items: [] },
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    render(React.createElement(UserDetailPage, { userId: 'user-1' }));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy());
    expect(observedRequests).toHaveLength(1);
    expect(String(observedRequests[0].input)).toBe('/api/users/user-1');
    expect(requestText(observedRequests[0].input, observedRequests[0].init)).toContain(`Bearer ${apiAccessToken}`);
    observedRequests.forEach((request) => assertNoGraphExposure(requestText(request.input, request.init)));
    assertNoGraphExposure(document.documentElement.outerHTML);
  });

  it('renders denied capability UX from injected state without contacting a tenant or Graph', async () => {
    window.history.replaceState({}, '', '/users');

    render(React.createElement(App, {
      loadCapabilities: async () => capability('consent_required', 'users.view'),
      loadSession: async () => session,
    }));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Permission required' })).toBeTruthy());
    expect(screen.getByRole('status').textContent).toContain('Delegated Microsoft Graph consent is required before this action can run.');
    expect(screen.getByText('This workspace section is not available for your current role or tenant state.')).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalled();
    assertNoGraphExposure(document.documentElement.outerHTML);
  });

  it('renders read-only UX for an existing role without presenting a mutation path', async () => {
    window.history.replaceState({}, '', '/workspace-settings');

    render(React.createElement(App, {
      loadCapabilities: async () => capability('read_only', 'workspace.settings.manage'),
      loadSession: async () => session,
    }));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Permission required' })).toBeTruthy());
    expect(screen.getByRole('status').textContent).toContain('This action is read-only for your current Entra role.');
    expect(screen.queryByRole('button', { name: 'Save settings' })).toBeNull();
    expect(apiMock).not.toHaveBeenCalled();
    assertNoGraphExposure(document.documentElement.outerHTML);
  });
});
