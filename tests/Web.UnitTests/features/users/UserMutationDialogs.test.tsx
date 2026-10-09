import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmationDialog } from '../../../../src/Web/src/components/ConfirmationDialog';
import { DataFreshness } from '../../../../src/Web/src/components/DataFreshness';
import { PasswordResetDialog } from '../../../../src/Web/src/features/users/PasswordResetDialog';
import { UserCreateDialog } from '../../../../src/Web/src/features/users/UserCreateDialog';
import { UserEditDialog } from '../../../../src/Web/src/features/users/UserEditDialog';
import { GroupMembershipDialog } from '../../../../src/Web/src/features/users/GroupMembershipDialog';
import { LicenseAssignmentDialog } from '../../../../src/Web/src/features/users/LicenseAssignmentDialog';
import type { UserDetails } from '../../../../src/Web/src/features/users/userDetailApi';
import { mutateUser } from '../../../../src/Web/src/features/users/userMutationApi';

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
        confirmLabel="Disable user"
        onConfirm={onConfirm}
      />,
    );

    const confirm = screen.getByRole('button', { name: 'Disable user' }) as HTMLButtonElement;
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
    const { unmount } = render(<ConfirmationDialog title="Confirm" target="Ada" proposedChange="Change" requiredCapability="users.edit" confirmLabel="Save" onConfirm={vi.fn()} onCancel={vi.fn()} />);

    const dialog = screen.getByRole('dialog');
    const cancel = screen.getByRole('button', { name: 'Cancel' });
    const confirm = screen.getByRole('button', { name: 'Save' });
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
    expect((screen.getByRole('button', { name: 'Save changes' }) as HTMLButtonElement).disabled).toBe(true);
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
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  New employee onboarding  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Create user' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    const [path, init] = apiMock.mock.calls[0];
    expect(path).toBe('/api/users');
    expect(path).not.toMatch(/graph\.microsoft\.com/i);
    expect(init.headers['Idempotency-Key']).toBeTruthy();
    expect(JSON.parse(init.body)).toEqual({
      displayName: 'Ada Lovelace',
      givenName: 'Ada',
      surname: 'Lovelace',
      userPrincipalName: 'ada@example.com',
      mailNickname: 'ada',
      jobTitle: null,
      department: null,
      officeLocation: null,
      mobilePhone: null,
      usageLocation: 'NO',
      accountEnabled: true,
      reason: 'New employee onboarding',
    });
    expect(await screen.findByText('Temp-Password-12345!')).toBeTruthy();
    expect(screen.getByText('The user must change this password at next sign-in.')).toBeTruthy();
  });

  it('keeps the entire create form inside the keyboard-contained confirmation dialog', () => {
    render(<UserCreateDialog />);

    const dialog = screen.getByRole('dialog', { name: 'Create user' });
    const name = within(dialog).getByLabelText('Name');
    const upn = within(dialog).getByLabelText('User principal name');
    const location = within(dialog).getByLabelText('Usage location');
    const reason = within(dialog).getByLabelText('Reason');
    expect(name).toBeTruthy();
    expect(upn).toBeTruthy();
    expect(location).toBeTruthy();
    expect(reason).toBeTruthy();

    const reviewed = within(dialog).getByLabelText('I reviewed the target, change and required capability.');
    reviewed.focus();
    fireEvent.keyDown(dialog, { key: 'Tab' });
    expect(document.activeElement).toBe(name);
  });

  it('allows keyboard-reachable reason entry and returns focus after Cancel or Escape', () => {
    function Harness() {
      const [open, setOpen] = React.useState(false);
      return <><button type="button" onClick={() => setOpen(true)}>Open create user</button>{open && <UserCreateDialog onCancel={() => setOpen(false)} />}</>;
    }
    render(<Harness />);
    const trigger = screen.getByRole('button', { name: 'Open create user' });
    trigger.focus();
    fireEvent.click(trigger);

    let dialog = screen.getByRole('dialog', { name: 'Create user' });
    expect(within(dialog).getByLabelText('Reason')).toBeTruthy();
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeTruthy();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(trigger);

    trigger.focus();
    fireEvent.click(trigger);
    dialog = screen.getByRole('dialog', { name: 'Create user' });
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('blocks create-dialog cancellation while the create request is pending', async () => {
    let finishCreate: ((response: Response) => void) | undefined;
    apiMock.mockImplementation(() => new Promise<Response>(resolve => { finishCreate = resolve; }));
    render(<UserCreateDialog onCancel={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Ada Lovelace' } });
    fireEvent.change(screen.getByLabelText('User principal name'), { target: { value: 'ada@example.com' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'New employee onboarding' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Create user' }));

    const dialog = screen.getByRole('dialog', { name: 'Create user' });
    expect(await within(dialog).findByRole('button', { name: 'Create user…' })).toBeTruthy();
    expect(within(dialog).queryByRole('button', { name: 'Cancel' })).toBeNull();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByRole('dialog', { name: 'Create user' })).toBeTruthy();
    expect(apiMock).toHaveBeenCalledTimes(1);
    finishCreate?.(new Response(JSON.stringify({ status: 'failed', error: 'user_mutation_failed' }), { status: 500 }));
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
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Account recovery  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Reset password' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    const [path, init] = apiMock.mock.calls[0];
    expect(path).toBe('/api/users/user-1/reset-password');
    expect(init.headers['Idempotency-Key']).toBeTruthy();
    expect(JSON.parse(init.body)).toEqual({ reason: 'Account recovery' });
    expect(await screen.findByText('Temp-Reset-12345!')).toBeTruthy();
    expect(screen.getByText('The user must change this password at next sign-in.')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Reset password' })).toBeNull();
  });

  it('shows a recoverable error when password reset fails', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'forbidden',
      requiredCapability: 'users.reset_password',
      replayed: false,
      error: 'consent_required',
    }), { status: 403, headers: { 'Content-Type': 'application/json' } }));

    render(<PasswordResetDialog user={user} onClose={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Account recovery' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Reset password' }));

    const dialog = screen.getByRole('dialog', { name: 'Reset password' });
    expect((await within(dialog).findByRole('alert')).textContent).toContain('Microsoft Graph consent is required before this action can be completed.');
    expect((within(dialog).getByLabelText('Reason') as HTMLTextAreaElement).value).toBe('Account recovery');
  });

  it('preserves edited profile fields and adds a trimmed reason to the update body', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded' }), { status: 200 }));
    render(<UserEditDialog user={user} />);
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Ada Byron' } });
    fireEvent.change(screen.getByLabelText('Job title'), { target: { value: 'Mathematician' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Correcting profile data  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    const [, init] = apiMock.mock.calls[0];
    expect(init.method).toBe('PATCH');
    expect(JSON.parse(init.body)).toEqual({
      displayName: 'Ada Byron',
      givenName: null,
      surname: null,
      jobTitle: 'Mathematician',
      department: null,
      officeLocation: null,
      mobilePhone: null,
      usageLocation: null,
      accountEnabled: true,
      reason: 'Correcting profile data',
    });
  });

  it('serializes provided DELETE bodies while retaining bodyless DELETE compatibility', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded' }), { status: 200 }));
    await mutateUser(apiMock, '/api/users/user-1/groups/group-1', 'DELETE', { groupObjectId: 'group-1', reason: 'Removal requested' });
    await mutateUser(apiMock, '/api/users/user-1/groups/group-1', 'DELETE', undefined);

    expect(apiMock.mock.calls[0][1].body).toBe('{"groupObjectId":"group-1","reason":"Removal requested"}');
    expect(apiMock.mock.calls[1][1].body).toBeUndefined();
  });

  it('rejects blank and overlength create reasons but accepts exactly 1,000 UTF-16 units', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded' }), { status: 201 }));
    render(<UserCreateDialog />);
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Ada Lovelace' } });
    fireEvent.change(screen.getByLabelText('User principal name'), { target: { value: 'ada@example.com' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '   ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Create user' }));
    expect(screen.getByText('Enter a reason before continuing.')).toBeTruthy();
    const dialog = screen.getByRole('dialog', { name: 'Create user' });
    expect(within(dialog).getByRole('alert').textContent).toContain('Enter a reason');
    expect(document.activeElement).toBe(within(dialog).getByLabelText('Reason'));
    expect(apiMock).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '😀'.repeat(501) } });
    fireEvent.click(screen.getByRole('button', { name: 'Create user' }));
    expect(within(dialog).getByRole('alert').textContent).toContain('Keep the reason to 1,000 characters or fewer.');
    expect(document.activeElement).toBe(within(dialog).getByLabelText('Reason'));
    expect(apiMock).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '😀'.repeat(500) } });
    fireEvent.click(screen.getByRole('button', { name: 'Create user' }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    expect(JSON.parse(apiMock.mock.calls[0][1].body).reason).toBe('😀'.repeat(500));
  });

  it('keeps create form values and its failure alert inside the active dialog', async () => {
    apiMock.mockRejectedValue(new Error('offline'));
    render(<UserCreateDialog />);
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Ada Lovelace' } });
    fireEvent.change(screen.getByLabelText('User principal name'), { target: { value: 'ada@example.com' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'New employee onboarding' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Create user' }));

    const dialog = screen.getByRole('dialog', { name: 'Create user' });
    expect((await within(dialog).findByRole('alert')).textContent).toContain('User creation could not be completed');
    expect((within(dialog).getByLabelText('Name') as HTMLInputElement).value).toBe('Ada Lovelace');
    expect((within(dialog).getByLabelText('User principal name') as HTMLInputElement).value).toBe('ada@example.com');
    expect((within(dialog).getByLabelText('Reason') as HTMLTextAreaElement).value).toBe('New employee onboarding');
  });

  it('sends reason with the existing group-add request body', async () => {
    apiMock.mockImplementation(async (path: string) => path === '/api/groups?pageSize=100'
      ? new Response(JSON.stringify({ items: [{ id: 'group-1', displayName: 'Operators' }] }), { status: 200 })
      : new Response(JSON.stringify({ status: 'succeeded' }), { status: 200 }));
    render(<GroupMembershipDialog userId="user-1" groupId={null} mode="add" />);
    fireEvent.change(await screen.findByLabelText('Group'), { target: { value: 'group-1' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Team membership  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Add to group' }));

    await waitFor(() => expect(apiMock.mock.calls.some(([path]) => path === '/api/users/user-1/groups/group-1')).toBe(true));
    const [, init] = apiMock.mock.calls.find(([path]) => path === '/api/users/user-1/groups/group-1')!;
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual({ groupObjectId: 'group-1', reason: 'Team membership' });
  });

  it('sends a JSON reason body with group DELETE requests', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded' }), { status: 200 }));
    render(<GroupMembershipDialog userId="user-1" groupId="group-1" target="Operators" mode="remove" />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  No longer required  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Remove from group' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    expect(apiMock.mock.calls[0][1].method).toBe('DELETE');
    expect(JSON.parse(apiMock.mock.calls[0][1].body)).toEqual({ groupObjectId: 'group-1', reason: 'No longer required' });
  });

  it('preserves license options and sends reason bodies for assignment and removal', async () => {
    apiMock.mockImplementation(async (path: string) => path === '/api/licenses?pageSize=100'
      ? new Response(JSON.stringify({ items: [{ skuId: 'sku-1', partNumber: 'E3', displayName: 'Microsoft 365 E3' }] }), { status: 200 })
      : new Response(JSON.stringify({ status: 'succeeded' }), { status: 200 }));
    const { rerender } = render(<LicenseAssignmentDialog userId="user-1" skuId={null} mode="assign" />);
    fireEvent.change(await screen.findByLabelText('License'), { target: { value: 'sku-1' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Required for the role  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Assign license' }));
    await waitFor(() => expect(apiMock.mock.calls.some(([path]) => path === '/api/users/user-1/licenses/sku-1')).toBe(true));
    const [, assignInit] = apiMock.mock.calls.find(([path]) => path === '/api/users/user-1/licenses/sku-1')!;
    expect(assignInit.method).toBe('POST');
    expect(JSON.parse(assignInit.body)).toEqual({ skuId: 'sku-1', disabledPlans: [], reason: 'Required for the role' });

    apiMock.mockClear();
    rerender(<LicenseAssignmentDialog userId="user-1" skuId="sku-1" target="Microsoft 365 E3" mode="remove" disabledPlans={['plan-1']} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  License no longer needed  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Remove license' }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    expect(apiMock.mock.calls[0][1].method).toBe('DELETE');
    expect(JSON.parse(apiMock.mock.calls[0][1].body)).toEqual({ skuId: 'sku-1', disabledPlans: ['plan-1'], reason: 'License no longer needed' });
  });
});
