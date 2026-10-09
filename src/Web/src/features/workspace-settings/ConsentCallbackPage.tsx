import { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { ConnectionStatusCard } from '../../components/ConnectionStatusCard';
import { messages, type ConnectionState } from '../../messages/en';
import {
  clearPendingFlow,
  readPendingFlow,
  updatePendingFlow,
} from '../invitations/pendingFlow';
import { InvitationConsentCallbackPage } from './InvitationConsentCallbackPage';

type Health = {
  status: ConnectionState;
  lastVerifiedAt?: string | null;
  permissionCoverage?: { availableScopes: string[]; missingScopes: string[]; unknownScopes: string[] } | null;
};
type Completion = { valid: boolean; status: string };
export type ConsentCallbackApi = {
  complete: (request: { state: string; tenant: string; errorCode?: string }) => Promise<Completion>;
  check: () => Promise<Health>;
};

type CallbackResult = { state: 'success' | 'denied' | 'invalid' | 'unknown'; health?: Health };
const workspaceCallbackRuns = new Map<string, Promise<CallbackResult>>();

export function ConsentCallbackPage({ api }: { api?: ConsentCallbackApi }) {
  const flow = readPendingFlow();
  if (flow?.kind === 'invitation') return <InvitationConsentCallbackPage />;
  if (!flow || flow.kind !== 'workspace') return <UnsolicitedCallback />;
  if (!api) return <AuthenticatedConsentCallback />;
  return <LoadedConsentCallback api={api} />;
}

function UnsolicitedCallback() {
  useEffect(() => window.history.replaceState(null, '', window.location.pathname), []);
  return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="alert">Open workspace settings and start consent again.</p><a href="/settings#connection">Open workspace settings</a></main>;
}

function AuthenticatedConsentCallback() {
  const api = useApi();
  return <LoadedConsentCallback api={{
    complete: async (value) => {
      const response = await api('/api/workspaces/current/consent/complete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(value),
      });
      if (!response.ok) throw new Error('completion_failed');
      return await response.json() as Completion;
    },
    check: async () => {
      const response = await api('/api/workspaces/current/connection-health/check', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ includePermissionCoverage: true }),
      });
      if (!response.ok) throw new Error('check_failed');
      return await response.json() as Health;
    },
  }} />;
}

function LoadedConsentCallback({ api }: { api: ConsentCallbackApi }) {
  const [result, setResult] = useState<CallbackResult | null>(null);
  const [failed, setFailed] = useState(false);
  const [retrying, setRetrying] = useState(false);
  const flow = readPendingFlow();
  const params = new URLSearchParams(window.location.search);
  const callbackState = params.get('state') ?? '';
  const callbackTenant = params.get('tenant') ?? '';
  const callbackError = params.get('error');

  const run = useCallback(async () => {
    window.history.replaceState(null, '', window.location.pathname);
    if (!flow || flow.kind !== 'workspace' || flow.step !== 'workspace_callback' ||
        callbackState !== flow.challenge || callbackTenant.toLowerCase() !== flow.tenantId?.toLowerCase()) {
      setResult({ state: 'invalid' });
      return;
    }
    const key = flow.challenge;
    const existing = workspaceCallbackRuns.get(key);
    if (existing) {
      try { setResult(await existing); } catch { setFailed(true); }
      return;
    }
    updatePendingFlow({ step: 'completion_submitted' });
    const task = (async (): Promise<CallbackResult> => {
      const completion = await api.complete({
        state: callbackState,
        tenant: callbackTenant,
        ...(callbackError ? { errorCode: 'consent_denied' } : {}),
      });
      if (!completion.valid) {
        clearPendingFlow();
        return { state: 'invalid' };
      }
      if (completion.status === 'consent_denied') {
        await api.check();
        clearPendingFlow();
        return { state: 'denied' };
      }
      const health = await api.check();
      clearPendingFlow();
      return { state: health.status === 'temporarily_unavailable' ? 'unknown' : 'success', health };
    })();
    workspaceCallbackRuns.set(key, task);
    try { setResult(await task); } catch { setFailed(true); }
  }, [api, callbackError, callbackState, callbackTenant, flow]);

  useEffect(() => {
    if (flow?.step === 'completion_submitted') {
      window.history.replaceState(null, '', window.location.pathname);
      const existing = workspaceCallbackRuns.get(flow.challenge);
      const check = existing ?? api.check().then(health => ({
        state: health.status === 'temporarily_unavailable' ? 'unknown' : 'success',
        health,
      }));
      if (!existing) workspaceCallbackRuns.set(flow.challenge, check);
      void check.then(setResult).catch(() => setFailed(true));
      return;
    }
    void run();
  }, []);

  async function retryHealth() {
    setRetrying(true);
    setFailed(false);
    try {
      setResult({ state: 'success', health: await api.check() });
    } catch {
      setFailed(true);
    } finally {
      setRetrying(false);
    }
  }

  if ((!flow || flow.kind !== 'workspace') && !result) return <UnsolicitedCallback />;
  if (failed) return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="alert">{messages.consentCallbackFailed}</p><button type="button" disabled={retrying} onClick={() => void retryHealth()}>{retrying ? messages.connectionLoading : messages.consentCallbackRetry}</button></main>;
  if (!result) return <main className="content-panel"><p role="status">{messages.consentCallbackLoading}</p></main>;
  if (result.state === 'invalid') return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="alert">{messages.consentCallbackInvalid}</p><a href="/settings#connection">Open workspace settings</a></main>;
  if (result.state === 'denied') return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="alert">{messages.consentCallbackDenied}</p><a href="/settings#connection">Open workspace settings</a></main>;
  const coverage = result.health?.permissionCoverage;
  const health = result.health!;
  if (health.status === 'permission_incomplete' || (coverage?.missingScopes.length ?? 0) > 0) {
    return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="status">Connected to Microsoft, but some delegated permissions are unavailable.</p><ul>{(coverage?.missingScopes ?? []).map(scope => <li key={scope}>{scope}</li>)}</ul><a href="/settings#connection">Review permissions and consent</a>{(coverage?.unknownScopes.length ?? 0) > 0 && <><p>Some permissions could not be verified. Retry the health check for these scopes:</p><ul>{coverage!.unknownScopes.map(scope => <li key={scope}>{scope}</li>)}</ul><button type="button" disabled={retrying} onClick={() => void retryHealth()}>{messages.consentCallbackRetry}</button></>}</main>;
  }
  if (result.state === 'unknown' || (coverage?.unknownScopes.length ?? 0) > 0) return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="status">Connection coverage could not be verified. Retry the health check before continuing.</p>{(coverage?.unknownScopes ?? []).map(scope => <p key={scope}>{scope}</p>)}<button type="button" disabled={retrying} onClick={() => void retryHealth()}>{messages.consentCallbackRetry}</button></main>;
  return <main className="content-panel"><h1>{messages.connectionTitle}</h1><p role="status">{messages.consentCallbackVerified}</p><ConnectionStatusCard state={health.status} lastVerifiedAt={health.lastVerifiedAt} /><a href="/overview">{messages.consentCallbackBack}</a></main>;
}
