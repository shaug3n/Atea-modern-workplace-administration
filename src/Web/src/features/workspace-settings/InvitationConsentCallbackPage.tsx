import { useEffect, useState } from 'react';
import { useAuth } from '../../auth/AuthProvider';
import { tenantAuthority } from '../../auth/msalConfig';
import { messages } from '../../messages/en';
import {
  clearPendingFlow,
  readPendingFlow,
  updatePendingFlow,
} from '../invitations/pendingFlow';
import {
  resumeInvitationConsent,
  type InvitationConsentResume,
} from '../invitations/invitationApi';

type ResumeAction = (
  nonce: string,
  state: string,
  tenant?: string,
  errorCode?: string,
) => Promise<InvitationConsentResume>;

type Outcome = 'loading' | 'missing' | 'denied' | 'invalid' | 'ready' | 'failed';

const callbackRuns = new Map<string, Promise<Outcome>>();
const safeConsentErrors = new Set(['access_denied', 'interaction_required', 'temporarily_unavailable']);

function runOnce(key: string, action: () => Promise<Outcome>): Promise<Outcome> {
  const existing = callbackRuns.get(key);
  if (existing) return existing;
  const pending = action().finally(() => {
    if (callbackRuns.get(key) === pending) callbackRuns.delete(key);
  });
  callbackRuns.set(key, pending);
  return pending;
}

function validTenant(value: string | null): value is string {
  return Boolean(value && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) &&
    value !== '00000000-0000-0000-0000-000000000000');
}

export function InvitationConsentCallbackPage({ resume = resumeInvitationConsent }: { resume?: ResumeAction }) {
  const { signInForTenant } = useAuth();
  const [outcome, setOutcome] = useState<Outcome>('loading');
  const [retrying, setRetrying] = useState(false);
  const callback = new URLSearchParams(window.location.search);

  useEffect(() => {
    const flow = readPendingFlow();
    const callbackState = callback.get('state') ?? '';
    const callbackTenant = callback.get('tenant');
    const rawError = callback.get('error');
    const errorCode = rawError ? (safeConsentErrors.has(rawError) ? rawError : 'consent_failed') : undefined;
    const key = flow ? `${flow.challenge}:${callbackState}:${callbackTenant ?? ''}` : `missing:${window.location.pathname}`;

    window.history.replaceState(null, '', window.location.pathname);
    if (!flow || flow.kind !== 'invitation' || !['consent_callback', 'consent_resume'].includes(flow.step) ||
        !flow.nonce || !callbackState || callbackState !== flow.challenge) {
      if (flow?.kind === 'invitation' && flow.step === 'tenant_sign_in' && flow.nonce && flow.tenantId) {
        void startTenantSignIn(flow.nonce, flow.tenantId, setOutcome, signInForTenant);
        return;
      }
      setOutcome('missing');
      return;
    }
    if (callbackTenant && !validTenant(callbackTenant)) {
      clearPendingFlow();
      setOutcome('invalid');
      return;
    }

    updatePendingFlow({ step: 'consent_resume' });
    void runOnce(key, async () => {
      try {
        const result = await resume(flow.nonce!, callbackState, validTenant(callbackTenant) ? callbackTenant : undefined, errorCode);
        if (!result.valid || result.status === 'invalid_callback') {
          clearPendingFlow();
          return 'invalid';
        }
        if (result.status === 'consent_denied') {
          clearPendingFlow();
          return 'denied';
        }
        if (result.status !== 'ready_to_sign_in' || !result.tenantId || !validTenant(result.tenantId) ||
            (validTenant(callbackTenant) && callbackTenant.toLowerCase() !== result.tenantId.toLowerCase())) {
          clearPendingFlow();
          return 'invalid';
        }
        updatePendingFlow({ tenantId: result.tenantId, step: 'tenant_sign_in' });
        await startTenantSignIn(flow.nonce!, result.tenantId, setOutcome, signInForTenant);
        return 'ready';
      } catch {
        updatePendingFlow({ step: 'consent_callback' });
        return 'failed';
      }
    }).then(setOutcome);
  }, []);

  async function retrySignIn() {
    const flow = readPendingFlow();
    if (!flow || flow.kind !== 'invitation' || !flow.nonce) return;
    if (!flow.tenantId) {
      window.location.assign(`/invitations/${flow.nonce}`);
      return;
    }
    setRetrying(true);
    await startTenantSignIn(flow.nonce, flow.tenantId, setOutcome, signInForTenant);
    setRetrying(false);
  }

  if (outcome === 'loading') return <main className="content-panel"><p role="status">{messages.consentCallbackLoading}</p></main>;
  if (outcome === 'ready') return <main className="content-panel"><p role="status">Consent verified. Continuing with your invited account…</p></main>;
  if (outcome === 'denied') return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="alert">{messages.consentCallbackDenied}</p></main>;
  if (outcome === 'invalid' || outcome === 'missing') return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="alert">Open your original invitation and try again.</p></main>;
  return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="alert">{messages.consentCallbackFailed}</p><button type="button" disabled={retrying} onClick={() => void retrySignIn()}>{retrying ? messages.authPreparing : 'Continue sign-in'}</button></main>;
}

async function startTenantSignIn(
  nonce: string,
  tenantId: string,
  setOutcome: (outcome: Outcome) => void,
  signInForTenant: (tenantId: string, returnPath: string) => Promise<void>,
): Promise<void> {
  try {
    tenantAuthority(tenantId);
    updatePendingFlow({ tenantId, step: 'tenant_sign_in_started' });
    await signInForTenant(tenantId, `/invitations/${nonce}`);
    setOutcome('ready');
  } catch {
    updatePendingFlow({ tenantId, step: 'tenant_sign_in' });
    setOutcome('failed');
  }
}
