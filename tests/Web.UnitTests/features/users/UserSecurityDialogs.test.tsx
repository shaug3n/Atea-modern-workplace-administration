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

  it('starts with a single-use 60-minute pass and exposes the three lifetime presets', () => {
    render(<TemporaryAccessPassDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);

    const dialog = screen.getByRole('dialog', { name: 'Grant Temporary Access Pass' });
    const lifetime = within(dialog).getByLabelText('Duration in minutes') as HTMLInputElement;
    const usableOnce = within(dialog).getByLabelText('One-time use') as HTMLInputElement;
    expect(lifetime.value).toBe('60');
    expect(lifetime.min).toBe('10');
    expect(lifetime.max).toBe('1440');
    expect(lifetime.step).toBe('1');
    expect(usableOnce.checked).toBe(true);
    expect(within(dialog).getByRole('button', { name: '1 hour' })).toBeTruthy();
    expect(within(dialog).getByRole('button', { name: '8 hours' })).toBeTruthy();
    expect(within(dialog).getByRole('button', { name: '24 hours' })).toBeTruthy();
    fireEvent.change(lifetime, { target: { value: '10' } });
    expect((within(dialog).getByRole('button', { name: 'Decrease duration by 10 minutes' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(lifetime, { target: { value: '1440' } });
    expect((within(dialog).getByRole('button', { name: 'Increase duration by 10 minutes' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('uses presets and explicit ten-minute controls without restricting direct whole-minute values', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', temporaryAccessPass: 'fixture-tap-value' }), { status: 200 }));
    render(<TemporaryAccessPassDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    const lifetime = screen.getByLabelText('Duration in minutes') as HTMLInputElement;

    fireEvent.click(screen.getByRole('button', { name: '8 hours' }));
    expect(lifetime.value).toBe('480');
    fireEvent.click(screen.getByRole('button', { name: '24 hours' }));
    expect(lifetime.value).toBe('1440');
    fireEvent.click(screen.getByRole('button', { name: '1 hour' }));
    expect(lifetime.value).toBe('60');
    fireEvent.click(screen.getByRole('button', { name: 'Increase duration by 10 minutes' }));
    expect(lifetime.value).toBe('70');
    fireEvent.click(screen.getByRole('button', { name: 'Decrease duration by 10 minutes' }));
    expect(lifetime.value).toBe('60');
    fireEvent.change(lifetime, { target: { value: '61' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Recovery access' } });
    confirmDialog();

    expect(await screen.findByText('fixture-tap-value')).toBeTruthy();
    expect(JSON.parse(apiMock.mock.calls[0][1].body)).toEqual({
      reason: 'Recovery access',
      lifetimeInMinutes: 61,
      isUsableOnce: true,
    });
  });

  it('retains TAP options and reason after an explicit tenant-policy rejection', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'policy_rejected',
      error: 'tenant_policy_rejected',
      graphCorrelationId: 'corr-policy',
      graphRequestId: 'req-policy',
    }), { status: 422 }));
    render(<TemporaryAccessPassDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    const lifetime = screen.getByLabelText('Duration in minutes') as HTMLInputElement;
    const usableOnce = screen.getByLabelText('One-time use') as HTMLInputElement;
    fireEvent.change(lifetime, { target: { value: '61' } });
    fireEvent.click(usableOnce);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Recovery access  ' } });
    confirmDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Grant Temporary Access Pass' });
    expect(within(dialog).getByRole('alert').textContent).toContain('tenant policy');
    expect(within(dialog).getByText('corr-policy')).toBeTruthy();
    expect(within(dialog).getByText('req-policy')).toBeTruthy();
    expect((within(dialog).getByLabelText('Duration in minutes') as HTMLInputElement).value).toBe('61');
    expect((within(dialog).getByLabelText('One-time use') as HTMLInputElement).checked).toBe(false);
    expect((within(dialog).getByLabelText('Reason') as HTMLTextAreaElement).value).toBe('  Recovery access  ');
    expect(JSON.parse(apiMock.mock.calls[0][1].body)).toEqual({
      reason: 'Recovery access',
      lifetimeInMinutes: 61,
      isUsableOnce: false,
    });
  });

  it('derives the visible expiry from the returned start date and lifetime', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'succeeded',
      temporaryAccessPass: 'fixture-tap-value',
      startDateTime: '2026-09-23T10:00:00Z',
      lifetimeInMinutes: 61,
      isUsableOnce: false,
    }), { status: 200 }));
    render(<TemporaryAccessPassDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Recovery access' } });
    confirmDialog();

    const result = await screen.findByRole('dialog', { name: 'Temporary access pass issued' });
    expect(within(result).getByText('Valid for 61 minutes. Can be used multiple times.')).toBeTruthy();
    expect(within(result).getByText(/Expires:/)).toBeTruthy();
  });

  it.each(['9', '1441', '61.5'])('rejects an invalid duration of %s without submitting', async (value) => {
    render(<TemporaryAccessPassDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Duration in minutes'), { target: { value } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Recovery access' } });
    confirmDialog();

    expect((await screen.findByRole('alert')).textContent).toContain('Enter a whole number from 10 through 1,440 minutes.');
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('keeps a failed session-revoke alert visible with the confirmation dialog', async () => {
    apiMock.mockRejectedValue(new Error('revoke_failed'));
    render(<RevokeSessionsDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Session compromise  ' } });
    confirmDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Revoke user sessions' });
    expect(within(dialog).getByRole('alert').textContent).toContain('Sessions could not be revoked');
    expect((within(dialog).getByLabelText('Reason') as HTMLTextAreaElement).value).toBe('  Session compromise  ');
  });

  it('posts a trimmed TAP reason without storage and never reveals replayed code', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', replayed: true, temporaryAccessPass: 'fixture-replayed-value' }), { status: 200 }));
    render(<TemporaryAccessPassDialog userId="user-1" target="Ada Lovelace" onClose={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  User recovery  ' } });
    confirmDialog();

    await screen.findByRole('alert');
    expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/authentication-methods/temporary-access-pass', expect.objectContaining({ method: 'POST', body: '{"reason":"User recovery","lifetimeInMinutes":60,"isUsableOnce":true}' }));
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
    expect(within(result).queryByText(/Expires:/)).toBeNull();
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
