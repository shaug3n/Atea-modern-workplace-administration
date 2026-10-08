import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TemporaryAccessPassDialog } from '../../../../src/Web/src/features/users/TemporaryAccessPassDialog';
import { RevokeSessionsDialog } from '../../../../src/Web/src/features/users/RevokeSessionsDialog';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

function confirmDialog() {
  fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
  fireEvent.click(within(screen.getByRole('dialog')).getAllByRole('button').at(-1)!);
}

describe('user security dialogs', () => {
  afterEach(() => { cleanup(); apiMock.mockReset(); });

  it('keeps a failed TAP alert inside the confirmation dialog', async () => {
    apiMock.mockRejectedValue(new Error('tap_failed'));
    render(<TemporaryAccessPassDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  User recovery  ' } });
    confirmDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Grant Temporary Access Pass' });
    expect(within(dialog).getByRole('alert').textContent).toContain('temporary access pass could not be issued');
  });

  it('keeps a failed session-revoke alert visible with the confirmation dialog', async () => {
    apiMock.mockRejectedValue(new Error('revoke_failed'));
    render(<RevokeSessionsDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Session compromise  ' } });
    confirmDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Revoke user sessions' });
    expect(dialog).toBeTruthy();
    expect((await screen.findByRole('alert')).textContent).toContain('Sessions could not be revoked');
  });

  it('posts a trimmed TAP reason without storage and never reveals replayed code', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', replayed: true, temporaryAccessPass: 'fixture-replayed-value' }), { status: 200 }));
    render(<TemporaryAccessPassDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  User recovery  ' } });
    confirmDialog();

    await screen.findByRole('alert');
    expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/authentication-methods/temporary-access-pass', expect.objectContaining({ method: 'POST', body: '{"reason":"User recovery"}' }));
    expect(apiMock.mock.calls[0][1].headers['Idempotency-Key']).toBeTruthy();
    expect(screen.queryByText('fixture-replayed-value')).toBeNull();
    expect(setItem).not.toHaveBeenCalled();
    setItem.mockRestore();
  });

  it('focuses and contains the one-time TAP result, then restores the prior trigger', async () => {
    const trigger = document.createElement('button');
    trigger.textContent = 'Grant Temporary Access Pass';
    document.body.appendChild(trigger);
    trigger.focus();
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', temporaryAccessPass: 'fixture-tap-value' }), { status: 200 }));
    const onClose = vi.fn();
    const { unmount } = render(<TemporaryAccessPassDialog userId="user-1" target="Ada Lovelace" onClose={onClose} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  First sign-in  ' } });
    confirmDialog();

    const result = await screen.findByRole('dialog', { name: 'Temporary access pass issued' });
    const close = within(result).getByRole('button', { name: 'Close' });
    expect(document.activeElement).toBe(within(result).getByRole('button', { name: 'Copy code' }));
    fireEvent.keyDown(result, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(close);
    fireEvent.keyDown(result, { key: 'Tab' });
    expect(document.activeElement).toBe(within(result).getByRole('button', { name: 'Copy code' }));
    fireEvent.click(close);
    expect(onClose).toHaveBeenCalledTimes(1);
    unmount();
    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });

  it('posts a trimmed reason in a session-revocation request while retaining its idempotency key', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded' }), { status: 200 }));
    render(<RevokeSessionsDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Access review  ' } });
    confirmDialog();

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    expect(apiMock.mock.calls[0][0]).toBe('/api/users/user-1/revoke-sessions');
    expect(apiMock.mock.calls[0][1].headers['Idempotency-Key']).toBeTruthy();
    expect(JSON.parse(apiMock.mock.calls[0][1].body)).toEqual({ reason: 'Access review' });
  });
});
