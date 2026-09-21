import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CapabilityDecision } from '../../../../src/Web/src/capabilities/capabilityTypes';
import { UsersPage } from '../../../../src/Web/src/features/users/UsersPage';
import type { UserFiltersState, UsersDirectoryResponse } from '../../../../src/Web/src/features/users/usersApi';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

const usersResponse: UsersDirectoryResponse = {
  items: [
    { id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: 'ada@example.com', accountEnabled: true, userType: 'Member' },
    { id: 'user-2', displayName: 'Grace Hopper', userPrincipalName: 'grace@example.com', mail: 'grace@example.com', accountEnabled: true, userType: 'Member' },
  ],
  continuationToken: null,
  fetchedAt: '2026-09-21T08:00:00Z',
  freshness: 'fresh',
  partialData: false,
};

function decision(capability: CapabilityDecision['capability'], state: CapabilityDecision['state']): CapabilityDecision {
  return { capability, state, reasonCode: state === 'allowed' ? 'active_role' : 'role_read_only' };
}

const allowedCapabilities: CapabilityDecision[] = [
  decision('users.view', 'allowed'),
  decision('users.create', 'hidden'),
  decision('users.update', 'allowed'),
  decision('users.disable', 'allowed'),
];

describe('UsersPage', () => {
  afterEach(() => {
    cleanup();
    apiMock.mockReset();
  });

  it('opens the create form when the create capability is allowed', async () => {
    render(<UsersPage capabilities={[decision('users.view', 'allowed'), decision('users.create', 'allowed'), decision('users.disable', 'hidden')]} loadUsers={async () => usersResponse} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Create user' }));
    expect(screen.getByRole('dialog', { name: 'Create user' })).toBeTruthy();
    expect(screen.getByLabelText('Name')).toBeTruthy();
  });

  it('opens disable confirmation and mutates only after review and destructive phrase', async () => {
    const loadUsers = vi.fn(async (_filters: UserFiltersState, _continuationToken: string | null) => usersResponse);
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'succeeded',
      requiredCapability: 'users.disable',
      replayed: false,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    render(<UsersPage capabilities={allowedCapabilities} loadUsers={loadUsers} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Disable Ada Lovelace' }));
    expect(screen.getByRole('dialog', { name: 'Disable user' })).toBeTruthy();
    expect(screen.getByText('Disable sign-in for this user.')).toBeTruthy();

    const confirm = screen.getByRole('button', { name: 'Confirm action' }) as HTMLButtonElement;
    expect(confirm.disabled).toBe(true);
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    expect(confirm.disabled).toBe(true);
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });
    expect(confirm.disabled).toBe(false);
    expect(apiMock).not.toHaveBeenCalled();

    fireEvent.click(confirm);

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    const [path, init] = apiMock.mock.calls[0];
    expect(path).toBe('/api/users/user-1/disable');
    expect(path).not.toMatch(/graph\.microsoft\.com/i);
    expect(init.method).toBe('POST');
    expect(init.headers['Idempotency-Key']).toBeTruthy();
    expect(await screen.findByText('Ada Lovelace sign-in was disabled.')).toBeTruthy();
    await waitFor(() => expect(loadUsers).toHaveBeenCalledTimes(2));
  });

  it('keeps disable inert for read-only capability decisions', async () => {
    render(
      <UsersPage
        capabilities={[
          decision('users.view', 'allowed'),
          decision('users.create', 'hidden'),
          decision('users.update', 'read_only'),
          decision('users.disable', 'read_only'),
        ]}
        loadUsers={async () => usersResponse}
      />,
    );

    const disableButton = await screen.findByRole('button', { name: 'Disable Ada Lovelace' }) as HTMLButtonElement;
    expect(disableButton.disabled).toBe(true);
    fireEvent.click(disableButton);

    expect(screen.queryByRole('dialog', { name: 'Disable user' })).toBeNull();
    expect(apiMock).not.toHaveBeenCalled();
    expect(screen.getAllByText('This action is read-only for your current Entra role.')).toHaveLength(2);
  });

  it('resets disable confirmation when the target user changes', async () => {
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={async () => usersResponse} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Disable Ada Lovelace' }));
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });
    expect((screen.getByRole('button', { name: 'Confirm action' }) as HTMLButtonElement).disabled).toBe(false);

    fireEvent.click(screen.getByRole('button', { name: 'Disable Grace Hopper' }));

    expect(within(screen.getByRole('dialog', { name: 'Disable user' })).getByText('Grace Hopper')).toBeTruthy();
    expect((screen.getByLabelText('I reviewed the target, change and required capability.') as HTMLInputElement).checked).toBe(false);
    expect((screen.getByLabelText('Type DISABLE to confirm') as HTMLInputElement).value).toBe('');
    expect((screen.getByRole('button', { name: 'Confirm action' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('closes disable confirmation with permission guidance when disable capability is revoked before submit', async () => {
    const loadUsers = vi.fn(async (_filters: UserFiltersState, _continuationToken: string | null) => usersResponse);
    const { rerender } = render(<UsersPage capabilities={allowedCapabilities} loadUsers={loadUsers} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Disable Ada Lovelace' }));
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });

    rerender(<UsersPage capabilities={[
      decision('users.view', 'allowed'),
      decision('users.create', 'hidden'),
      decision('users.update', 'allowed'),
      decision('users.disable', 'read_only'),
    ]} loadUsers={loadUsers} />);
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    expect(screen.queryByRole('dialog', { name: 'Disable user' })).toBeNull();
    expect(screen.getByRole('alert').textContent).toContain('cannot disable this user');
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('keeps the disable dialog open with friendly guidance when mutation fails', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'idempotency_key_reused',
      requiredCapability: 'users.disable',
      replayed: false,
      error: 'idempotency_key_reused',
    }), { status: 409, headers: { 'Content-Type': 'application/json' } }));

    render(<UsersPage capabilities={allowedCapabilities} loadUsers={async () => usersResponse} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Disable Ada Lovelace' }));
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    expect((await screen.findByRole('alert')).textContent).toContain('idempotency key');
    expect(screen.getByRole('dialog', { name: 'Disable user' })).toBeTruthy();
  });
});
