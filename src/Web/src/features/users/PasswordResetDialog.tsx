import React, { useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import type { UserDetails } from './userDetailApi';
import { mutateUser, type UserCommandResponse } from './userMutationApi';

export function PasswordResetDialog({ user, onClose }: { user: UserDetails; onClose: () => void }) {
  const api = useApi();
  const [pending, setPending] = useState(false);
  const [result, setResult] = useState<UserCommandResponse | null>(null);

  const submit = async () => {
    if (pending) return;
    setPending(true);
    try {
      setResult(await mutateUser(api, `/api/users/${encodeURIComponent(user.id)}/reset-password`, 'POST', {}));
    } catch {
      setResult({ status: 'temporarily_unavailable', requiredCapability: 'users.reset_password', replayed: false, error: 'user_mutation_failed' });
    } finally {
      setPending(false);
    }
  };

  if (result?.status === 'succeeded' && result.temporaryCredentialNotice) {
    return (
      <section className="mutation-dialog" role="dialog" aria-modal="true" aria-labelledby="password-reset-result-title">
        <h2 id="password-reset-result-title">{messages.userResetPasswordSucceeded}</h2>
        <p>{messages.userResetPasswordCopyWarning}</p>
        <section className="temporary-credential" role="status" aria-label={messages.userTemporaryPasswordNotice}>
          <strong>{messages.userTemporaryPasswordNotice}</strong>
          <code>{result.temporaryCredentialNotice.temporaryPassword}</code>
          <p>{messages.userTemporaryPasswordForceChange}</p>
        </section>
        <div className="users-page__actions">
          <button type="button" onClick={onClose}>{messages.userMutationCancel}</button>
        </div>
      </section>
    );
  }

  return (
    <div>
      <ConfirmationDialog
        title={messages.userResetPasswordDialogTitle}
        target={user.displayName || user.userPrincipalName || user.id}
        proposedChange={messages.userResetPasswordProposedChange}
        requiredCapability="users.reset_password"
        confirmLabel={messages.confirmResetPassword}
        busy={pending}
        onConfirm={submit}
        onCancel={onClose}
      />
      {result && <p role="alert">{formatResetError(result)}</p>}
    </div>
  );
}

function formatResetError(response: UserCommandResponse) {
  if (response.error === 'consent_required') return 'Microsoft Graph consent is required before this action can be completed.';
  if (response.error === 'source_of_authority_read_only' || response.status === 'source_of_authority_read_only') return messages.userDisableSourceReadOnly;
  if (response.error === 'idempotency_key_reused' || response.error === 'conflict') return messages.userMutationConflict;
  if (response.error === 'throttled' || response.status === 'temporarily_unavailable') return messages.userMutationThrottled;
  return messages.userResetPasswordFailed;
}
