import { useEffect, useRef, useState } from 'react';
import { messages, type ConnectionState } from '../../messages/en';
import { useApi } from '../../auth/useApi';

type Completion = { valid: boolean; status: string };
type Health = { status: ConnectionState };
export type ConsentCallbackApi = { complete: (request: { state: string; tenant: string; errorCode?: string }) => Promise<Completion>; check: () => Promise<Health> };

export function ConsentCallbackPage({ api }: { api?: ConsentCallbackApi }) {
  if (!api) return <AuthenticatedConsentCallback />;
  return <LoadedConsentCallback api={api} />;
}

function AuthenticatedConsentCallback() {
  const api = useApi();
  return <LoadedConsentCallback api={{
    complete: async (value) => { const response = await api('/api/workspaces/current/consent/complete', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(value) }); if (!response.ok) throw new Error('completion_failed'); return await response.json() as Completion; },
    check: async () => { const response = await api('/api/workspaces/current/connection-health/check', { method: 'POST' }); if (!response.ok) throw new Error('check_failed'); return await response.json() as Health; },
  }} />;
}

function LoadedConsentCallback({ api }: { api: ConsentCallbackApi }) {
  const [state, setState] = useState<'loading' | 'success' | 'denied' | 'invalid' | 'failed'>('loading');
  const [health, setHealth] = useState<ConnectionState | null>(null);
  const completion = useRef<Completion | null>(null);
  const params = new URLSearchParams(window.location.search);
  const run = async () => {
    setState('loading');
    try {
      const alreadyCompleted = completion.current !== null;
      if (!completion.current) {
        const request = { state: params.get('state') ?? '', tenant: params.get('tenant') ?? '', ...(params.get('error') ? { errorCode: params.get('error') ?? undefined } : {}) };
        const result = await api.complete(request);
        if (!result.valid) { setState('invalid'); return; }
        completion.current = result;
      }
      if (completion.current.status === 'consent_denied') {
        if (alreadyCompleted) await api.check();
        setState('denied');
        return;
      }
      setHealth((await api.check()).status); setState('success');
    } catch { setState('failed'); }
  };
  useEffect(() => { void run(); }, []);
  if (state === 'loading') return <section className="content-panel"><p role="status">{messages.consentCallbackLoading}</p></section>;
  if (state === 'success') return <section className="content-panel"><h1>{messages.connectionTitle}</h1><p role="status">{messages.consentCallbackSuccess}</p><p>{health ? messages.connectionState[health].label : ''}</p><a href="/overview">{messages.consentCallbackBack}</a></section>;
  const text = state === 'denied' ? messages.consentCallbackDenied : state === 'invalid' ? messages.consentCallbackInvalid : messages.consentCallbackFailed;
  return <section className="content-panel"><h1>{messages.connectionTitle}</h1><p role="alert">{text}</p><button type="button" onClick={() => void run()}>{messages.consentCallbackRetry}</button>{state !== 'denied' && <p><a href="/overview">{messages.consentCallbackBack}</a></p>}</section>;
}
