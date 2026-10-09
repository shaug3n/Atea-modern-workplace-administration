import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
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
    expect(document.body.textContent).toContain('unavailable until the directory has a safe role-assignment index');
  });

  it('keeps compatible URL filters, resets paging for shortcuts, and restores filters on browser history', async () => {
    window.history.pushState(null, '', '/users?search=Ada&license=E3&accountStatus=disabled&userType=Guest&tenantRole=Global');
    const loadUsers = vi.fn(async (_filters: UserFiltersState, continuationToken: string | null) => ({
      items: [{ id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: 'ada@example.com', accountEnabled: true, userType: 'Member' }],
      continuationToken: continuationToken ? null : 'next-page',
      fetchedAt: new Date().toISOString(),
      freshness: 'fresh' as const,
      partialData: false,
    }));

    render(<UsersPage capabilities={capabilities} loadUsers={loadUsers} />);
    await waitFor(() => expect(screen.getByText('Page 1')).toBeTruthy());
    expect(new URLSearchParams(window.location.search).get('tenantRole')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: /Enabled/ }));
    await waitFor(() => expect(loadUsers).toHaveBeenLastCalledWith({ search: 'Ada', accountStatus: 'enabled', userType: '', license: 'E3', tenantRole: '' }, null));
    expect(new URLSearchParams(window.location.search).get('search')).toBe('Ada');
    expect(new URLSearchParams(window.location.search).get('license')).toBe('E3');
    expect(new URLSearchParams(window.location.search).get('accountStatus')).toBe('enabled');
    expect(new URLSearchParams(window.location.search).get('userType')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
    await waitFor(() => expect(screen.getByText('Page 2')).toBeTruthy());
    expect(loadUsers).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'Ada', accountStatus: 'enabled', license: 'E3' }), 'next-page');

    window.history.back();
    await waitFor(() => expect(loadUsers).toHaveBeenLastCalledWith({ search: 'Ada', accountStatus: 'disabled', userType: 'Guest', license: 'E3', tenantRole: '' }, null));
    expect(screen.getByText('Page 1')).toBeTruthy();
    expect(new URLSearchParams(window.location.search).get('accountStatus')).toBe('disabled');
    expect(new URLSearchParams(window.location.search).get('userType')).toBe('Guest');

    window.history.forward();
    await waitFor(() => expect(loadUsers).toHaveBeenLastCalledWith({ search: 'Ada', accountStatus: 'enabled', userType: '', license: 'E3', tenantRole: '' }, null));
    expect(screen.getByText('Page 1')).toBeTruthy();
    window.history.replaceState(null, '', '/');
  });
});
