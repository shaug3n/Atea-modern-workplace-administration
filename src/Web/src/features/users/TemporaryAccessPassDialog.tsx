import React, { useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import type { ApiFetch } from './userDetailApi';
import { grantTemporaryAccessPass } from './authenticationMethodsApi';

export function TemporaryAccessPassDialog({ userId, target, onClose }: { userId: string; target: string; onClose: () => void }) {
  const api = useApi();
  const [pending, setPending] = useState(false);
  const [code, setCode] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const close = () => { setCode(null); setError(null); onClose(); };
  const submit = async () => {
    if (pending) return;
    setPending(true); setError(null);
    try {
      const result = await grantTemporaryAccessPass(api as ApiFetch, userId);
      if (result.status === 'succeeded' && result.temporaryAccessPass && !result.replayed) setCode(result.temporaryAccessPass);
      else setError(result.replayed ? 'This temporary access pass request was already completed. A pass cannot be shown again.' : 'The tenant did not return a temporary access pass. No secret was exposed.');
    } catch { setError('The temporary access pass could not be issued. Review permissions and try again.'); }
    finally { setPending(false); }
  };
  if (code) return <div className="modal-backdrop"><section className="mutation-dialog" role="dialog" aria-modal="true" aria-labelledby="tap-result-title"><h2 id="tap-result-title">Temporary access pass issued</h2><p>Copy this one-time code now. It will not be shown again after closing.</p><code className="temporary-access-pass__code">{code}</code><button type="button" className="button button--quiet" onClick={() => void navigator.clipboard?.writeText(code)}>Copy code</button><button type="button" className="button button--secondary" onClick={close}>Close</button></section></div>;
  return <ConfirmationDialog title="Grant Temporary Access Pass" target={target} proposedChange="Issue a single-use temporary access pass valid for 60 minutes." requiredCapability="authentication.methods.manage" busy={pending} onConfirm={() => void submit()} onCancel={close}>{error && <p role="alert">{error}</p>}</ConfirmationDialog>;
}
