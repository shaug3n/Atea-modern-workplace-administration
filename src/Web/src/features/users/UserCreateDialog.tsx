import React, { useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { mutateUser, type CreateUserCommand, type UserCommandResponse } from './userMutationApi';

export function UserCreateDialog({ onCompleted }: { onCompleted?: (result: UserCommandResponse) => void }) {
  const api = useApi();
  const [displayName, setDisplayName] = useState('');
  const [userPrincipalName, setUserPrincipalName] = useState('');
  const [usageLocation, setUsageLocation] = useState('NO');
  const [result, setResult] = useState<UserCommandResponse | null>(null);

  const command: CreateUserCommand = {
    displayName,
    givenName: displayName.split(' ')[0] ?? '',
    surname: displayName.split(' ').slice(1).join(' '),
    userPrincipalName,
    mailNickname: userPrincipalName.split('@')[0] ?? '',
    jobTitle: null,
    department: null,
    officeLocation: null,
    mobilePhone: null,
    usageLocation,
    accountEnabled: true,
  };

  const submit = async () => {
    const response = await mutateUser(api, '/api/users', 'POST', command);
    setResult(response);
    onCompleted?.(response);
  };

  return (
    <div role="dialog" aria-modal="true" aria-labelledby="user-create-dialog-title">
      <label>{messages.usersNameColumn}<input value={displayName} onChange={(event) => setDisplayName(event.target.value)} /></label>
      <label>{messages.usersUpnColumn}<input value={userPrincipalName} onChange={(event) => setUserPrincipalName(event.target.value)} /></label>
      <label>{messages.userUsageLocation}<input value={usageLocation} onChange={(event) => setUsageLocation(event.target.value.toUpperCase())} /></label>
      <ConfirmationDialog
        title={messages.userCreateDialogTitle}
        target={displayName || userPrincipalName || messages.usersUnnamedUser}
        proposedChange={messages.userCreateProposedChange}
        requiredCapability="users.create"
        onConfirm={submit}
      />
      {result?.temporaryCredentialNotice && (
        <section role="status" aria-label={messages.userTemporaryPasswordNotice}>
          <strong>{messages.userTemporaryPasswordNotice}</strong>
          <code>{result.temporaryCredentialNotice.temporaryPassword}</code>
          <p>{messages.userTemporaryPasswordForceChange}</p>
        </section>
      )}
      {result?.error === 'idempotency_key_reused' && <p role="alert">{messages.userMutationConflict}</p>}
      {result?.error === 'throttled' && <p role="alert">{messages.userMutationThrottled}</p>}
    </div>
  );
}
