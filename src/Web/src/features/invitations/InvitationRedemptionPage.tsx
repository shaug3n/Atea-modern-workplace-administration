import { useState } from 'react';
import { useAuth } from '../../auth/AuthProvider';
import { messages } from '../../messages/en';
import { redeemInvitation as defaultRedeem, type InvitationRedemption } from './invitationApi';

export function InvitationRedemptionPage({ nonce, redeem }: { nonce: string; redeem?: (nonce: string) => Promise<InvitationRedemption> }) {
  const { account, getApiToken } = useAuth();
  const redeemAction = redeem ?? ((value: string) => defaultRedeem(value, getApiToken));
  const [result, setResult] = useState<InvitationRedemption | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  async function submit() {
    setLoading(true); setError(null);
    try { setResult(await redeemAction(nonce)); } catch (reason) { setError(reason instanceof Error && reason.message === 'invitation_invalid_or_expired' ? messages.invitationInvalid : messages.invitationFailed); } finally { setLoading(false); }
  }
  function continueToWorkspace() { window.history.pushState(null, '', '/overview'); window.dispatchEvent(new PopStateEvent('popstate')); }
  return <main className="content-panel" aria-labelledby="invitation-title"><p className="eyebrow">{messages.invitationEyebrow}</p><h1 id="invitation-title">{messages.invitationTitle}</h1><p>{messages.invitationSignedInAs} <strong>{account?.username ?? account?.name ?? messages.unknownUser}</strong></p>{!result && <button type="button" onClick={() => void submit()} disabled={loading}>{loading ? messages.invitationRedeeming : messages.invitationRedeem}</button>}{error && <p role="alert">{error}</p>}{result && <section role="status"><h2>{messages.invitationRedeemed}</h2><p>{result.workspaceName}</p><button type="button" onClick={continueToWorkspace}>{messages.invitationContinue}</button></section>}</main>;
}
