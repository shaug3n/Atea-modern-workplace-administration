import React, { useState } from 'react';
import { messages } from '../../app/messages';
import { ReasonDialog } from '../../components/ReasonDialog';
import { useApi } from '../../auth/useApi';
import { revokeUserSessions, type ApiFetch } from './userDetailApi';
import { getUserWriteReasonErrorMessage, getUserWriteReasonHint, normalizeUserWriteReason } from './UserWriteReasonField';

export function RevokeSessionsDialog({ userId, target, onClose, onCompleted, onAuditWarning }: { userId: string; target: string; onClose: () => void; onCompleted?: () => void; onAuditWarning?: (warning: string | null) => void }) {
  const api = useApi();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reasonError, setReasonError] = useState<string | null>(null);
  const submit = async (value: string) => {
    const normalizedReason = normalizeUserWriteReason(value);
    if (normalizedReason.error) { setReasonError(getUserWriteReasonErrorMessage(normalizedReason.error)); return; }
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
  return <>
    <ReasonDialog title="Revoke user sessions" target={target} proposedChange="Revoke active refresh tokens and require the user to sign in again." requiredCapability="users.sessions.revoke" confirmLabel={messages.confirmRevokeSessions} consequence={messages.confirmRevokeSessionsConsequence} tone="danger" busy={pending} reasonHint={getUserWriteReasonHint(reasonError)} onConfirm={(reason) => void submit(reason)} onCancel={() => { if (!pending) onClose(); }} />
    {error && <p role="alert">{error}</p>}
    {reasonError && <p role="alert">{reasonError}</p>}
  </>;
}
