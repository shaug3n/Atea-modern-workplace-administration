import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../auth/AuthProvider';
import { messages } from '../../messages/en';
import {
  checkInvitationConnectionHealth,
  completeInvitationConsent,
  redeemInvitation as defaultRedeem,
  type InvitationCompletion,
  type InvitationRedemption,
} from './invitationApi';
import { clearPendingFlow, readPendingFlow, updatePendingFlow } from './pendingFlow';

type AutoResult = {
  status: string;
  health?: NonNullable<InvitationCompletion['health']>;
};
type RedemptionPromise = Promise<AutoResult>;
const onboardingRuns = new Map<string, RedemptionPromise>();

export function InvitationRedemptionPage({
  nonce,
  redeem,
  challenge,
  tenantId,
}: {
  nonce: string;
  redeem?: (nonce: string) => Promise<InvitationRedemption>;
  challenge?: string;
  tenantId?: string;
}) {
  const { account, getApiToken, signInForTenant } = useAuth();
  const redeemAction = useCallback(
    (value: string) => redeem ? redeem(value) : defaultRedeem(value, getApiToken, challenge, tenantId),
    [challenge, getApiToken, redeem, tenantId],
  );
  const [result, setResult] = useState<InvitationRedemption | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(Boolean(challenge));
  const [autoResult, setAutoResult] = useState<AutoResult | null>(null);
  const [autoFailed, setAutoFailed] = useState(false);
  const [retrying, setRetrying] = useState(false);

  const runAutomaticOnboarding = useCallback(async () => {
    if (!challenge || !tenantId || account?.tenantId?.toLowerCase() !== tenantId.toLowerCase()) {
      setAutoFailed(true);
      setLoading(false);
      return;
    }
    const flow = readPendingFlow();
    if (!flow || flow.kind !== 'invitation' || flow.nonce !== nonce ||
        flow.challenge !== challenge || flow.tenantId?.toLowerCase() !== tenantId.toLowerCase()) {
      setAutoFailed(true);
      setLoading(false);
      return;
    }
    const key = `${nonce}:${challenge}:${tenantId}`;
    let pending = onboardingRuns.get(key);
    if (!pending) {
      pending = (async () => {
        let current = readPendingFlow();
        if (!current) throw new Error('pending_flow_unavailable');
        if (current.step === 'result' && current.result) {
          return { status: current.result.status, health: current.result as NonNullable<InvitationCompletion['health']> };
        }
        if (current.step !== 'completion_submitted') {
          if (current.step !== 'completion_pending') {
            updatePendingFlow({ step: 'redeeming' });
            await redeemAction(nonce);
            updatePendingFlow({ step: 'completion_pending' });
          }
          updatePendingFlow({ step: 'completion_submitted' });
          const completion = await completeInvitationConsent(challenge, tenantId, getApiToken);
          if (!completion.valid || completion.status === 'invalid_callback') throw new Error('invitation_completion_invalid');
          if (completion.status === 'consent_denied') {
            return { status: 'consent_denied' };
          }
          if (completion.status !== 'consent_received') throw new Error('invitation_completion_unknown');
          const health = completion.health ?? await checkInvitationConnectionHealth(getApiToken, tenantId);
          const coverage = completion.permissionCoverage ?? health.permissionCoverage;
          const normalized = {
            ...health,
            permissionCoverage: coverage,
          };
          updatePendingFlow({
            step: 'result',
            result: {
              status: normalized.status,
              availableScopes: coverage?.availableScopes,
              missingScopes: coverage?.missingScopes,
              unknownScopes: coverage?.unknownScopes,
            },
          });
          return { status: normalized.status, health: normalized };
        }
        const health = await checkInvitationConnectionHealth(getApiToken, tenantId);
        updatePendingFlow({
          step: 'result',
          result: {
            status: health.status,
            availableScopes: health.permissionCoverage?.availableScopes,
            missingScopes: health.permissionCoverage?.missingScopes,
            unknownScopes: health.permissionCoverage?.unknownScopes,
          },
        });
        return { status: health.status, health };
      })();
      onboardingRuns.set(key, pending);
    }
    try {
      const value = await pending;
      if (value.status === 'consent_denied') {
        clearPendingFlow();
        setAutoResult(value);
      } else {
        setAutoResult(value);
      }
      setError(null);
      setAutoFailed(false);
    } catch {
      onboardingRuns.delete(key);
      const latest = readPendingFlow();
      if (latest?.step === 'completion_submitted') {
        setAutoResult({ status: 'temporarily_unavailable' });
      } else {
        setAutoFailed(true);
      }
    } finally {
      setLoading(false);
    }
  }, [account?.tenantId, challenge, getApiToken, nonce, redeemAction, tenantId]);

  useEffect(() => {
    if (challenge) void runAutomaticOnboarding();
  }, [challenge, runAutomaticOnboarding]);

  async function retryAutomaticHealth() {
    if (!tenantId) return;
    setRetrying(true);
    try {
      const health = await checkInvitationConnectionHealth(getApiToken, tenantId);
      updatePendingFlow({
        step: 'result',
        result: {
          status: health.status,
          availableScopes: health.permissionCoverage?.availableScopes,
          missingScopes: health.permissionCoverage?.missingScopes,
          unknownScopes: health.permissionCoverage?.unknownScopes,
        },
      });
      setAutoResult({ status: health.status, health });
      setAutoFailed(false);
    } catch {
      setAutoResult({ status: 'temporarily_unavailable' });
    } finally {
      setRetrying(false);
    }
  }

  async function submit() {
    setLoading(true);
    setError(null);
    try {
      setResult(await redeemAction(nonce));
    } catch (reason) {
      setError(reason instanceof Error && reason.message === 'invitation_invalid_or_expired' ? messages.invitationInvalid : messages.invitationFailed);
    } finally {
      setLoading(false);
    }
  }

  function continueToWorkspace() {
    window.history.pushState(null, '', '/onboarding');
    window.dispatchEvent(new PopStateEvent('popstate'));
  }

  if (challenge) {
    if (loading) return <main className="content-panel"><p role="status">Connecting your workspace to Microsoft 365…</p></main>;
    if (autoFailed) return <main className="content-panel"><h1>{messages.invitationTitle}</h1><p role="alert">{messages.invitationFailed}</p><button type="button" onClick={() => void runAutomaticOnboarding()}>{messages.retry}</button><button type="button" onClick={() => void signInForTenant(tenantId!, `/invitations/${nonce}`).catch(() => setAutoFailed(true))}>{messages.authSwitchAccount}</button></main>;
    if (autoResult?.status === 'consent_denied') return <main className="content-panel"><h1>{messages.invitationTitle}</h1><p role="alert">{messages.consentCallbackDenied}</p><a href="/settings#connection">Review permissions and consent</a></main>;
    const coverage = autoResult?.health?.permissionCoverage ?? readPendingFlow()?.result;
    if (autoResult?.status === 'permission_incomplete' || (coverage?.missingScopes?.length ?? 0) > 0) {
      return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="status">Connected to Microsoft, but some delegated permissions are unavailable.</p><ul>{(coverage?.missingScopes ?? []).map(scope => <li key={scope}>{scope}</li>)}</ul><a href="/settings#connection">Review permissions and consent</a></main>;
    }
    if (autoResult?.status === 'connected' && !(coverage?.unknownScopes?.length)) return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="status">Connected to Microsoft 365</p><a href="/overview">{messages.consentCallbackBack}</a></main>;
    return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="status">Connection coverage could not be verified. Retry the health check before continuing.</p>{(coverage?.unknownScopes ?? []).map(scope => <p key={scope}>{scope}</p>)}<button type="button" disabled={retrying} onClick={() => void retryAutomaticHealth()}>{retrying ? messages.connectionLoading : messages.consentCallbackRetry}</button></main>;
  }

  return <main className="content-panel" aria-labelledby="invitation-title"><p className="eyebrow">{messages.invitationEyebrow}</p><h1 id="invitation-title">{messages.invitationTitle}</h1><p>{messages.invitationSignedInAs} <strong>{account?.username ?? account?.name ?? messages.unknownUser}</strong></p>{!result && <button type="button" onClick={() => void submit()} disabled={loading}>{loading ? messages.invitationRedeeming : messages.invitationRedeem}</button>}{error && <p role="alert">{error}</p>}{result && <section role="status"><h2>{messages.invitationRedeemed}</h2><p>{result.workspaceName}</p><button type="button" onClick={continueToWorkspace}>{messages.invitationContinue}</button></section>}</main>;
}
