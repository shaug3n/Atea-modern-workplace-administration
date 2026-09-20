import React from 'react';
import { cleanup, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { UserDetailPage } from '../../src/Web/src/features/users/UserDetailPage';

const apiMock = vi.hoisted(() => vi.fn((path: string, init?: RequestInit) => window.fetch(path, init)));

vi.mock('../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

describe('user detail browser boundary', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('loads detail from same-origin API only and never calls Graph from the browser', async () => {
    const requests: string[] = [];
    vi.spyOn(window, 'fetch').mockImplementation(async (input) => {
      const path = String(input);
      requests.push(path);
      expect(new URL(path, window.location.origin).origin).toBe(window.location.origin);
      expect(path).toBe('/api/users/user-1');
      expect(path).not.toMatch(/graph\.microsoft\.com/i);
      return new Response(JSON.stringify({
        access: { authorization: { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false },
        user: { id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: 'ada@example.com', accountEnabled: true, userType: 'Member', isReadOnly: false, sourceOfAuthority: 'cloud' },
        licenses: { access: { authorization: { capability: 'licenses.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false }, items: [] },
        groups: { access: { authorization: { capability: 'groups.manage_members', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false }, items: [] },
        roles: { access: { authorization: { capability: 'roles.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false }, items: [] },
        pim: { access: { authorization: { capability: 'pim.activate', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false }, items: [] },
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    render(<UserDetailPage userId="user-1" />);

    await waitFor(() => expect(document.body.textContent).toContain('Ada Lovelace'));
    expect(requests).toEqual(['/api/users/user-1']);
    expect(document.body.textContent).toContain('Roles and PIM');
  });
});
