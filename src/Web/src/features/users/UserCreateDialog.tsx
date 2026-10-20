import React, { useId, useRef, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { mutateUser, type CreateUserCommand, type UserCommandResponse } from './userMutationApi';
import { UserWriteReasonField, normalizeUserWriteReason } from './UserWriteReasonField';

export function UserCreateDialog({ onCompleted, onCancel }: { onCompleted?: (result: UserCommandResponse) => void; onCancel?: () => void }) {
  const api = useApi();
  const [displayName, setDisplayName] = useState('');
  const [userPrincipalName, setUserPrincipalName] = useState('');
  const [usageLocation, setUsageLocation] = useState('NO');
  const [result, setResult] = useState<UserCommandResponse | null>(null);
  const [pending, setPending] = useState(false);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<'reason_required' | 'reason_too_long' | null>(null);
  const [fieldErrors, setFieldErrors] = useState<{ displayName?: string; userPrincipalName?: string }>({});
  const displayNameId = useId();
  const displayNameErrorId = useId();
  const userPrincipalNameId = useId();
  const userPrincipalNameErrorId = useId();
  const displayNameInputRef = useRef<HTMLInputElement>(null);
  const userPrincipalNameInputRef = useRef<HTMLInputElement>(null);

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
    const nextFieldErrors: { displayName?: string; userPrincipalName?: string } = {};
    if (!displayName.trim()) nextFieldErrors.displayName = messages.userCreateDisplayNameRequired;
    if (!userPrincipalName.trim()) {
      nextFieldErrors.userPrincipalName = messages.userCreateUpnRequired;
    } else if (!userPrincipalNameInputRef.current?.validity.valid) {
      nextFieldErrors.userPrincipalName = messages.userCreateUpnInvalid;
    }
    setFieldErrors(nextFieldErrors);
    if (Object.keys(nextFieldErrors).length > 0) {
      if (nextFieldErrors.displayName) displayNameInputRef.current?.focus();
      else userPrincipalNameInputRef.current?.focus();
      return;
    }
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
      onCancel={pending ? undefined : onCancel}
    >
      <div className="mutation-phrase">
        <label htmlFor={displayNameId}>{messages.usersNameColumn}</label>
        <input
          ref={displayNameInputRef}
          id={displayNameId}
          value={displayName}
          required
          aria-invalid={Boolean(fieldErrors.displayName)}
          aria-describedby={fieldErrors.displayName ? displayNameErrorId : undefined}
          onChange={(event) => {
            setDisplayName(event.target.value);
            setFieldErrors((current) => ({ ...current, displayName: undefined }));
            setResult(null);
          }}
        />
        {fieldErrors.displayName && <span id={displayNameErrorId} className="mutation-reason-required" role="alert">{fieldErrors.displayName}</span>}
      </div>
      <div className="mutation-phrase">
        <label htmlFor={userPrincipalNameId}>{messages.usersUpnColumn}</label>
        <input
          ref={userPrincipalNameInputRef}
          id={userPrincipalNameId}
          type="email"
          value={userPrincipalName}
          required
          aria-invalid={Boolean(fieldErrors.userPrincipalName)}
          aria-describedby={fieldErrors.userPrincipalName ? userPrincipalNameErrorId : undefined}
          onChange={(event) => {
            setUserPrincipalName(event.target.value);
            setFieldErrors((current) => ({ ...current, userPrincipalName: undefined }));
            setResult(null);
          }}
        />
        {fieldErrors.userPrincipalName && <span id={userPrincipalNameErrorId} className="mutation-reason-required" role="alert">{fieldErrors.userPrincipalName}</span>}
      </div>
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
