import React, { useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import type { UserDetails } from './userDetailApi';
import { mutateUser, type UpdateUserCommand, type UserCommandResponse } from './userMutationApi';

export function UserEditDialog({ user, onCompleted }: { user: UserDetails; onCompleted?: (result: UserCommandResponse) => void }) {
  const api = useApi();
  const [displayName, setDisplayName] = useState(user.displayName ?? '');
  const [jobTitle, setJobTitle] = useState(user.jobTitle ?? '');
  const [error, setError] = useState<string | null>(null);
  const sourceLimitation = user.isReadOnly ? user.sourceOfAuthorityReason ?? messages.userSourceReadOnlyTitle : null;
  const submit = async () => {
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
      if (response.status !== 'succeeded') setError(response.error === 'consent_required' ? 'Microsoft Graph consent is required before this action can be completed.' : 'User details could not be updated. Review the user and try again.');
      onCompleted?.(response);
    } catch {
      setError('User details could not be updated. Review the user and try again.');
    }
  };

  return (
    <div>
      <label>{messages.usersNameColumn}<input value={displayName} onChange={(event) => setDisplayName(event.target.value)} /></label>
      <label>{messages.userJobTitle}<input value={jobTitle} onChange={(event) => setJobTitle(event.target.value)} /></label>
      {error && <p role="alert">{error}</p>}
      <ConfirmationDialog
      title={messages.userEditDialogTitle}
      target={user.displayName || user.userPrincipalName || user.id}
      proposedChange={messages.userEditProposedChange}
      requiredCapability="users.update"
      sourceLimitation={sourceLimitation}
      onConfirm={submit}
      />
    </div>
  );
}
