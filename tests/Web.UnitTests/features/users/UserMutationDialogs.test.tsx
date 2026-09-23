import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmationDialog } from '../../../../src/Web/src/components/ConfirmationDialog';
import { DataFreshness } from '../../../../src/Web/src/components/DataFreshness';
import { PasswordResetDialog } from '../../../../src/Web/src/features/users/PasswordResetDialog';
import { UserCreateDialog } from '../../../../src/Web/src/features/users/UserCreateDialog';
import { UserEditDialog } from '../../../../src/Web/src/features/users/UserEditDialog';
import type { UserDetails } from '../../../../src/Web/src/features/users/userDetailApi';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

describe('UserMutationDialogs', () => {
  const user: UserDetails = {
    id: 'user-1',
    displayName: 'Ada Lovelace',
    userPrincipalName: 'ada@example.com',
    accountEnabled: true,
    isReadOnly: false,
  };

  afterEach(() => {
    cleanup();
    apiMock.mockReset();
  });

  it('requires reviewed confirmation and destructive phrase before disable can proceed', () => {
    const onConfirm = vi.fn();
    render(
      <ConfirmationDialog
        title="Disable user"
        target="Ada Lovelace"
        proposedChange="Disable sign-in for this user."
        requiredCapability="users.disable"
        destructivePhrase="DISABLE"
        onConfirm={onConfirm}
      />,
    );

    const confirm = screen.getByRole('button', { name: 'Confirm action' }) as HTMLButtonElement;
    expect(confirm.disabled).toBe(true);
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    expect(confirm.disabled).toBe(true);
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });
    expect(confirm.disabled).toBe(false);
    fireEvent.click(confirm);
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('focuses the confirmation dialog and contains Tab navigation', () => {
    const trigger = document.createElement('button');
    trigger.textContent = 'Open';
    document.body.appendChild(trigger);
    trigger.focus();
    const { unmount } = render(<ConfirmationDialog title="Confirm" target="Ada" proposedChange="Change" requiredCapability="users.edit" onConfirm={vi.fn()} onCancel={vi.fn()} />);

    const dialog = screen.getByRole('dialog');
    const cancel = screen.getByRole('button', { name: 'Cancel' });
    const confirm = screen.getByRole('button', { name: 'Confirm action' });
    const reviewed = screen.getByLabelText('I reviewed the target, change and required capability.');
    reviewed.focus();
    fireEvent.keyDown(dialog, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(cancel);
    fireEvent.keyDown(dialog, { key: 'Tab' });
    expect(document.activeElement).toBe(reviewed);

    unmount();
    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });

  it('explains source-of-authority read-only limitations and blocks confirmation', () => {
    const readOnlyUser: UserDetails = {
      id: 'user-1',
      displayName: 'Ada Lovelace',
      userPrincipalName: 'ada@example.com',
      mail: 'ada@example.com',
      accountEnabled: true,
      userType: 'Member',
      isReadOnly: true,
      sourceOfAuthority: 'on_premises_sync',
      sourceOfAuthorityReason: 'This user is synchronized from an on-premises directory.',
    };

    render(<UserEditDialog user={readOnlyUser} />);

    expect(screen.getByRole('alert').textContent).toContain('synchronized from an on-premises directory');
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    expect((screen.getByRole('button', { name: 'Confirm action' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('uses feature-specific labels for data freshness', () => {
    render(<DataFreshness freshness="fresh" partialData={false} labels={{ fresh: 'Device data is fresh' }} />);

    expect(screen.getByText('Device data is fresh')).toBeTruthy();
  });

  it('exposes a cancel action for editable user details', () => {
    const onCancel = vi.fn();
    const user: UserDetails = {
      id: 'user-1',
      displayName: 'Ada Lovelace',
      userPrincipalName: 'ada@example.com',
      accountEnabled: true,
      isReadOnly: false,
    };

    render(<UserEditDialog user={user} onCancel={onCancel} />);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('posts create mutations to the BFF with an idempotency key and displays the temporary password once', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'succeeded',
      requiredCapability: 'users.create',
      replayed: false,
      temporaryCredentialNotice: { temporaryPassword: 'Temp-Password-12345!', forceChangePasswordNextSignIn: true },
    }), { status: 201, headers: { 'Content-Type': 'application/json' } }));

    render(<UserCreateDialog />);
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Ada Lovelace' } });
    fireEvent.change(screen.getByLabelText('User principal name'), { target: { value: 'ada@example.com' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    const [path, init] = apiMock.mock.calls[0];
    expect(path).toBe('/api/users');
    expect(path).not.toMatch(/graph\.microsoft\.com/i);
    expect(init.headers['Idempotency-Key']).toBeTruthy();
    expect(await screen.findByText('Temp-Password-12345!')).toBeTruthy();
    expect(screen.getByText('The user must change this password at next sign-in.')).toBeTruthy();
  });

  it('opens the password reset confirmation and cancels without calling the API', () => {
    const onClose = vi.fn();

    render(<PasswordResetDialog user={user} onClose={onClose} />);

    expect(screen.getByRole('dialog')).toBeTruthy();
    expect(screen.getByText('Generate a temporary password and require the user to change it at next sign-in.')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('submits a password reset and shows the temporary password only in the result view', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'succeeded',
      requiredCapability: 'users.reset_password',
      replayed: false,
      temporaryCredentialNotice: { temporaryPassword: 'Temp-Reset-12345!', forceChangePasswordNextSignIn: true },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    render(<PasswordResetDialog user={user} onClose={vi.fn()} />);
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    const [path, init] = apiMock.mock.calls[0];
    expect(path).toBe('/api/users/user-1/reset-password');
    expect(init.headers['Idempotency-Key']).toBeTruthy();
    expect(await screen.findByText('Temp-Reset-12345!')).toBeTruthy();
    expect(screen.getByText('The user must change this password at next sign-in.')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Confirm action' })).toBeNull();
  });

  it('shows a recoverable error when password reset fails', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'forbidden',
      requiredCapability: 'users.reset_password',
      replayed: false,
      error: 'consent_required',
    }), { status: 403, headers: { 'Content-Type': 'application/json' } }));

    render(<PasswordResetDialog user={user} onClose={vi.fn()} />);
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    expect((await screen.findByRole('alert')).textContent).toContain('Microsoft Graph consent is required before this action can be completed.');
    expect(screen.getByRole('dialog')).toBeTruthy();
  });
});
