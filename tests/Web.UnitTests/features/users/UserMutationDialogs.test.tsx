import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmationDialog } from '../../../../src/Web/src/components/ConfirmationDialog';
import { UserCreateDialog } from '../../../../src/Web/src/features/users/UserCreateDialog';
import { UserEditDialog } from '../../../../src/Web/src/features/users/UserEditDialog';
import type { UserDetails } from '../../../../src/Web/src/features/users/userDetailApi';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

describe('UserMutationDialogs', () => {
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

    const confirm = screen.getByRole('button', { name: 'Confirm action' });
    expect(confirm).toBeDisabled();
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    expect(confirm).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });
    expect(confirm).not.toBeDisabled();
    fireEvent.click(confirm);
    expect(onConfirm).toHaveBeenCalledTimes(1);
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
    expect(screen.getByRole('button', { name: 'Confirm action' })).toBeDisabled();
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
});
