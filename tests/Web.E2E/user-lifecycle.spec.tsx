import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { UserCreateDialog } from '../../src/Web/src/features/users/UserCreateDialog';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

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
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    expect(apiMock.mock.calls[0][0]).toBe('/api/users');
    expect(apiMock.mock.calls[0][0]).not.toContain('graph.microsoft.com');
    expect(screen.getByRole('alert').textContent).toContain('idempotency key');
  });
});
