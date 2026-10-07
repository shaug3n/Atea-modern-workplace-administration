import React, { useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import type { UserDetails } from './userDetailApi';
import { mutateUser, type UpdateUserCommand, type UserCommandResponse } from './userMutationApi';

export function UserEditDialog({ user, onCompleted, onCancel }: { user: UserDetails; onCompleted?: (result: UserCommandResponse) => void; onCancel?: () => void }) {
  const api = useApi();
  const [displayName, setDisplayName] = useState(user.displayName ?? '');
  const [jobTitle, setJobTitle] = useState(user.jobTitle ?? '');
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const sourceLimitation = user.isReadOnly ? user.sourceOfAuthorityReason ?? messages.userSourceReadOnlyTitle : null;
  const submit = async () => {
    if (pending) return;
    setPending(true);
    const command: UpdateUserCommand = {
      displayName,
      givenName: user.givenName ?? null,
      surname: user.surname ?? null,
      jobTitle: jobTitle || null,
      department: user.department ?? null,
      officeLocation: user.officeLocation ?? null,
      mobilePhone: user.mobilePhone ?? null,
      usageLocation: user.usageLocation ?? null,
      accountEnabled: user.accountEnabled,
    };
    try {
      const response = await mutateUser(api, `/api/users/${encodeURIComponent(user.id)}`, 'PATCH', command);
      if (response.status !== 'succeeded') setError(formatEditError(response));
      onCompleted?.(response);
    } catch {
      setError('User details could not be updated. Review the user and try again.');
    } finally {
      setPending(false);
    }
  };

  return (
    <ConfirmationDialog
      title={messages.userEditDialogTitle}
      target={user.displayName || user.userPrincipalName || user.id}
      proposedChange={messages.userEditProposedChange}
      requiredCapability="users.update"
      confirmLabel={messages.confirmEditUser}
      sourceLimitation={sourceLimitation}
      busy={pending}
      onConfirm={submit}
      onCancel={onCancel}
    >
      <label>{messages.usersNameColumn}<input value={displayName} onChange={(event) => setDisplayName(event.target.value)} /></label>
      <label>{messages.userJobTitle}<input value={jobTitle} onChange={(event) => setJobTitle(event.target.value)} /></label>
      {error && <p role="alert">{error}</p>}
    </ConfirmationDialog>
  );
}

function formatEditError(response: UserCommandResponse) {
  if (response.error === 'idempotency_key_reused' || response.error === 'conflict') return messages.userMutationConflict;
  if (response.error === 'throttled' || response.status === 'temporarily_unavailable') return messages.userMutationThrottled;
  if (response.error === 'consent_required') return 'Microsoft Graph consent is required before this action can be completed.';
  if (response.error === 'source_of_authority_read_only' || response.status === 'source_of_authority_read_only') return messages.userDisableSourceReadOnly;
  if (response.error === 'capability_required' || response.status === 'denied') return messages.userDisablePermissionDenied;
  return 'User details could not be updated. Review the user and try again.';
}
