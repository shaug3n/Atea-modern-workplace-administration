import React from 'react';
import { cleanup, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CapabilityDecision } from '../../src/Web/src/capabilities/capabilityTypes';
import { UsersPage } from '../../src/Web/src/features/users/UsersPage';
import { fetchUsers, type UserFiltersState } from '../../src/Web/src/features/users/usersApi';

vi.mock('../../src/Web/src/auth/useApi', () => ({
  useApi: () => async (path: string, init?: RequestInit) => window.fetch(path, init),
}));

const capabilities: CapabilityDecision[] = [
  { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
  { capability: 'users.create', state: 'hidden', reasonCode: 'directory_role_required' },
  { capability: 'users.update', state: 'hidden', reasonCode: 'directory_role_required' },
  { capability: 'users.disable', state: 'hidden', reasonCode: 'directory_role_required' },
];

describe('users directory browser boundary', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('renders client results and sends only same-origin API requests', async () => {
    const requests: string[] = [];
    vi.spyOn(window, 'fetch').mockImplementation(async (input) => {
      const path = String(input);
      requests.push(path);
      expect(new URL(path, window.location.origin).origin).toBe(window.location.origin);
      expect(path).toMatch(/^\/api\/users\?/);
      expect(path).not.toMatch(/graph\.microsoft\.com/i);
      return new Response(JSON.stringify({
        items: [{ id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: 'ada@example.com', accountEnabled: true, userType: 'Member' }],
        continuationToken: null,
        fetchedAt: new Date().toISOString(),
        freshness: 'fresh',
        partialData: false,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    const loadUsers = (filters: UserFiltersState, continuationToken: string | null) =>
      fetchUsers((path, init) => window.fetch(path, init), filters, continuationToken);

    render(<UsersPage capabilities={capabilities} loadUsers={loadUsers} />);

    await waitFor(() => expect(document.body.textContent).toContain('Ada Lovelace'));
    expect(requests).toHaveLength(1);
    expect(requests[0]).toContain('/api/users?');
    expect(document.body.textContent).toContain('not available yet');
  });
});
