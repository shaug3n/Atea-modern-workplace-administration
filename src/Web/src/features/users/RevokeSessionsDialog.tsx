import React, { useState } from 'react';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { useApi } from '../../auth/useApi';
import { revokeUserSessions, type ApiFetch } from './userDetailApi';

export function RevokeSessionsDialog({ userId, target, onClose, onCompleted }: { userId: string; target: string; onClose: () => void; onCompleted?: () => void }) {
  const api = useApi();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const submit = async () => { setPending(true); setError(null); try { const result = await revokeUserSessions(api as ApiFetch, userId); if (result.status === 'succeeded') { onCompleted?.(); onClose(); } else setError('Sessions could not be revoked. Review permissions and try again.'); } catch { setError('Sessions could not be revoked. Review permissions and try again.'); } finally { setPending(false); } };
  return <><ConfirmationDialog title="Revoke user sessions" target={target} proposedChange="Revoke active refresh tokens and require the user to sign in again." requiredCapability="users.sessions.revoke" busy={pending} onConfirm={() => void submit()} onCancel={() => { if (!pending) onClose(); }} />{error && <p role="alert">{error}</p>}</>;
}
