import React, { useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { mutateUser, type CreateUserCommand, type UserCommandResponse } from './userMutationApi';
import { UserWriteReasonField, normalizeUserWriteReason } from './UserWriteReasonField';

export function UserCreateDialog({ onCompleted }: { onCompleted?: (result: UserCommandResponse) => void }) {
  const api = useApi();
  const [displayName, setDisplayName] = useState('');
  const [userPrincipalName, setUserPrincipalName] = useState('');
  const [usageLocation, setUsageLocation] = useState('NO');
  const [result, setResult] = useState<UserCommandResponse | null>(null);
  const [pending, setPending] = useState(false);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<'reason_required' | 'reason_too_long' | null>(null);

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
    if (pending) return;
    const normalizedReason = normalizeUserWriteReason(reason);
    if (normalizedReason.error) {
      setReasonError(normalizedReason.error);
      return;
    }
    setReasonError(null);
    setPending(true);
    try {
      const response = await mutateUser(api, '/api/users', 'POST', { ...command, reason: normalizedReason.reason });
      setResult(response);
      onCompleted?.(response);
    } catch {
      setResult({ status: 'failed', requiredCapability: 'users.create', replayed: false, error: 'user_mutation_failed' });
    } finally {
      setPending(false);
    }
  };

  return (
    <ConfirmationDialog
      title={messages.userCreateDialogTitle}
      target={displayName || userPrincipalName || messages.usersUnnamedUser}
      proposedChange={messages.userCreateProposedChange}
      requiredCapability="users.create"
      confirmLabel={messages.confirmCreateUser}
      busy={pending}
      confirmBlocked={Boolean(reasonError)}
      onConfirm={submit}
    >
      <label>{messages.usersNameColumn}<input value={displayName} onChange={(event) => { setDisplayName(event.target.value); setResult(null); }} /></label>
      <label>{messages.usersUpnColumn}<input value={userPrincipalName} onChange={(event) => { setUserPrincipalName(event.target.value); setResult(null); }} /></label>
      <label>{messages.userUsageLocation}<input value={usageLocation} onChange={(event) => { setUsageLocation(event.target.value.toUpperCase()); setResult(null); }} /></label>
      <UserWriteReasonField value={reason} onChange={(value) => { setReason(value); setReasonError(null); }} error={reasonError} />
      {result?.auditWarning && <p role="alert" className="audit-warning">{result.auditWarning}</p>}
      {result?.temporaryCredentialNotice && (
        <section role="status" aria-label={messages.userTemporaryPasswordNotice}>
          <strong>{messages.userTemporaryPasswordNotice}</strong>
          <code>{result.temporaryCredentialNotice.temporaryPassword}</code>
          <p>{messages.userTemporaryPasswordForceChange}</p>
        </section>
      )}
      {result?.error === 'idempotency_key_reused' && <p role="alert">{messages.userMutationConflict}</p>}
      {result?.error === 'throttled' && <p role="alert">{messages.userMutationThrottled}</p>}
      {result?.error === 'user_mutation_failed' && <p role="alert">User creation could not be completed. Review the details and try again.</p>}
      {result && result.status !== 'succeeded' && result.error !== 'idempotency_key_reused' && result.error !== 'throttled' && result.error !== 'user_mutation_failed' && <p role="alert">User creation could not be completed. Review the details and try again.</p>}
    </ConfirmationDialog>
  );
}
