import React, { useState } from 'react';
import { messages } from '../../app/messages';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { useApi } from '../../auth/useApi';
import { revokeUserSessions, type ApiFetch } from './userDetailApi';
import { normalizeUserWriteReason, UserWriteReasonField } from './UserWriteReasonField';

export function RevokeSessionsDialog({ userId, target, onClose, onCompleted, onAuditWarning }: { userId: string; target: string; onClose: () => void; onCompleted?: () => void; onAuditWarning?: (warning: string | null) => void }) {
  const api = useApi();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<'reason_required' | 'reason_too_long' | null>(null);
  const submit = async (value: string) => {
    const normalizedReason = normalizeUserWriteReason(value);
    if (normalizedReason.error) { setReasonError(normalizedReason.error); return; }
    setReasonError(null);
    setPending(true);
    setError(null);
    try {
      const result = await revokeUserSessions(api as ApiFetch, userId, normalizedReason.reason);
      if (result.status === 'succeeded') {
        onAuditWarning?.(result.auditWarning?.trim() || null);
        onCompleted?.();
        onClose();
      } else setError('Sessions could not be revoked. Review permissions and try again.');
    } catch { setError('Sessions could not be revoked. Review permissions and try again.'); }
    finally { setPending(false); }
  };
  return (
    <ConfirmationDialog
      title="Revoke user sessions"
      target={target}
      proposedChange="Revoke active refresh tokens and require the user to sign in again."
      requiredCapability="users.sessions.revoke"
      confirmLabel={messages.confirmRevokeSessions}
      consequence={messages.confirmRevokeSessionsConsequence}
      tone="danger"
      busy={pending}
      confirmBlocked={Boolean(reasonError) || !reason.trim()}
      onConfirm={() => void submit(reason)}
      onCancel={() => { if (!pending) onClose(); }}
    >
      <UserWriteReasonField value={reason} onChange={(value) => { setReason(value); setReasonError(null); }} error={reasonError} />
      {error && <p role="alert">{error}</p>}
    </ConfirmationDialog>
  );
}
