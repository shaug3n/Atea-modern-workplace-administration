import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CapabilityDecision } from '../../src/Web/src/capabilities/capabilityTypes';
import { UserCreateDialog } from '../../src/Web/src/features/users/UserCreateDialog';
import { UsersPage } from '../../src/Web/src/features/users/UsersPage';
import type { UserFiltersState, UsersDirectoryResponse } from '../../src/Web/src/features/users/usersApi';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

const capabilities: CapabilityDecision[] = [
  { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
  { capability: 'users.create', state: 'hidden', reasonCode: 'directory_role_required' },
  { capability: 'users.update', state: 'allowed', reasonCode: 'active_role' },
  { capability: 'users.disable', state: 'allowed', reasonCode: 'active_role' },
];

const usersResponse: UsersDirectoryResponse = {
  items: [{ id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: 'ada@example.com', accountEnabled: true, userType: 'Member' }],
  continuationToken: null,
  fetchedAt: '2026-09-21T08:00:00Z',
  freshness: 'fresh',
  partialData: false,
};

describe('user lifecycle browser boundary', () => {
  afterEach(() => {
    cleanup();
    apiMock.mockReset();
  });

  it('submits lifecycle mutations only to same-origin API and surfaces conflict guidance', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'idempotency_key_reused',
      requiredCapability: 'users.create',
      replayed: false,
      error: 'idempotency_key_reused',
    }), { status: 409, headers: { 'Content-Type': 'application/json' } }));

    render(<UserCreateDialog />);
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Ada Lovelace' } });
    fireEvent.change(screen.getByLabelText('User principal name'), { target: { value: 'ada@example.com' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Create user' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    expect(apiMock.mock.calls[0][0]).toBe('/api/users');
    expect(apiMock.mock.calls[0][0]).not.toContain('graph.microsoft.com');
    expect(screen.getByRole('alert').textContent).toContain('idempotency key');
  });

  it('submits user disable only after explicit browser confirmation to the same-origin API', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'succeeded',
      requiredCapability: 'users.disable',
      replayed: false,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    const loadUsers = vi.fn(async (_filters: UserFiltersState, _continuationToken: string | null) => usersResponse);

    render(<UsersPage capabilities={capabilities} loadUsers={loadUsers} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Actions for Ada Lovelace' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Disable user' }));
    const confirm = screen.getByRole('button', { name: 'Disable user' }) as HTMLButtonElement;
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });
    expect(confirm.disabled).toBe(true);
    expect(apiMock).not.toHaveBeenCalled();

    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    expect(confirm.disabled).toBe(false);
    fireEvent.click(confirm);

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    const [path, init] = apiMock.mock.calls[0];
    expect(new URL(path, window.location.origin).origin).toBe(window.location.origin);
    expect(path).toBe('/api/users/user-1/disable');
    expect(path).not.toMatch(/graph\.microsoft\.com/i);
    expect(init.method).toBe('POST');
    expect(init.headers['Idempotency-Key']).toBeTruthy();
  });
});
