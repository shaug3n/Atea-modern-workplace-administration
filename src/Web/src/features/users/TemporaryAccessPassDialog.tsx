import React, { useRef, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import type { ApiFetch } from './userDetailApi';
import { grantTemporaryAccessPass } from './authenticationMethodsApi';
import { useFocusContainment } from '../../components/useFocusContainment';

export function TemporaryAccessPassDialog({ userId, target, onClose }: { userId: string; target: string; onClose: () => void }) {
  const api = useApi();
  const [pending, setPending] = useState(false);
  const [code, setCode] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const close = () => {
    const trigger = Array.from(document.querySelectorAll<HTMLButtonElement>('button')).find((button) => button.textContent?.trim() === 'Grant Temporary Access Pass' && !button.closest('[role="dialog"]'));
    const restoreTarget = trigger ?? returnFocusRef.current;
    returnFocusRef.current = restoreTarget;
    setCode(null);
    setError(null);
    onClose();
    restoreTarget?.focus();
  };
  const returnFocusRef = useRef<HTMLElement | null>(document.activeElement instanceof HTMLElement ? document.activeElement : null);
  const resultRef = useFocusContainment<HTMLElement>(Boolean(code), close, returnFocusRef);
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
  if (code) return <div className="modal-backdrop"><section ref={resultRef} className="mutation-dialog" role="dialog" aria-modal="true" aria-labelledby="tap-result-title"><h2 id="tap-result-title">Temporary access pass issued</h2><p>Copy this one-time code now. It will not be shown again after closing.</p><code className="temporary-access-pass__code">{code}</code><button type="button" className="button button--quiet" onClick={() => void navigator.clipboard?.writeText(code)}>Copy code</button><button type="button" className="button button--secondary" onClick={close}>Close</button></section></div>;
  return <ConfirmationDialog title="Grant Temporary Access Pass" target={target} proposedChange="Issue a single-use temporary access pass valid for 60 minutes." requiredCapability="authentication.methods.manage" confirmLabel={messages.confirmIssueTap} busy={pending} onConfirm={() => void submit()} onCancel={close}>{error && <p role="alert">{error}</p>}</ConfirmationDialog>;
}
