import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TemporaryAccessPassDialog } from '../../../../src/Web/src/features/users/TemporaryAccessPassDialog';
import { RevokeSessionsDialog } from '../../../../src/Web/src/features/users/RevokeSessionsDialog';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

function confirmDialog() {
  fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));
}

describe('user security dialogs', () => {
  afterEach(() => { cleanup(); apiMock.mockReset(); });

  it('keeps a failed TAP alert inside the confirmation dialog', async () => {
    apiMock.mockRejectedValue(new Error('tap_failed'));
    render(<TemporaryAccessPassDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    confirmDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Grant Temporary Access Pass' });
    expect(within(dialog).getByRole('alert').textContent).toContain('temporary access pass could not be issued');
  });

  it('keeps a failed session-revoke alert inside the confirmation dialog', async () => {
    apiMock.mockRejectedValue(new Error('revoke_failed'));
    render(<RevokeSessionsDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    confirmDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Revoke user sessions' });
    expect(within(dialog).getByRole('alert').textContent).toContain('Sessions could not be revoked');
  });

  it('posts TAP without a body, does not use storage, and never reveals replayed code', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', replayed: true, temporaryAccessPass: 'REPLAYED-SECRET' }), { status: 200 }));
    render(<TemporaryAccessPassDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    confirmDialog();

    await screen.findByRole('alert');
    expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/authentication-methods/temporary-access-pass', expect.objectContaining({ method: 'POST' }));
    expect(apiMock.mock.calls[0][1]).not.toHaveProperty('body');
    expect(screen.queryByText('REPLAYED-SECRET')).toBeNull();
    expect(setItem).not.toHaveBeenCalled();
    setItem.mockRestore();
  });
});
