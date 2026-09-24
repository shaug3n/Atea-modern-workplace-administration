import { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { ConnectionStatusCard } from '../../components/ConnectionStatusCard';
import { messages, type ConnectionState } from '../../messages/en';

type Health = { status: ConnectionState; lastVerifiedAt: string | null; correlationId?: string | null };

export function OnboardingPage({ onNavigate }: { onNavigate?: (path: string) => void }) {
  const api = useApi();
  const [health, setHealth] = useState<Health | null>(null);
  const [failed, setFailed] = useState(false);
  const [correlationId, setCorrelationId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [consentUrl, setConsentUrl] = useState<string | null>(null);
  const load = useCallback(async () => {
    setFailed(false); setError(null);
    try {
      const response = await api('/api/workspaces/current/connection-health');
      const value = await response.json() as Health & { correlationId?: string };
      if (!response.ok) throw Object.assign(new Error('connection health unavailable'), { correlationId: value.correlationId });
      setHealth(value); setCorrelationId(value.correlationId ?? null);
    } catch (reason) {
      setFailed(true); setCorrelationId((reason as Error & { correlationId?: string }).correlationId ?? null);
    }
  }, [api]);
  useEffect(() => { void load(); }, [load]);

  const check = async () => {
    setBusy(true); setError(null);
    try {
      const response = await api('/api/workspaces/current/connection-health/check', { method: 'POST' });
      const value = await response.json() as Health & { correlationId?: string };
      if (!response.ok) throw Object.assign(new Error('connection check failed'), { correlationId: value.correlationId });
      setHealth(value); setFailed(false); setCorrelationId(value.correlationId ?? null);
    } catch (reason) {
      setError(messages.connectionActionFailed); setCorrelationId((reason as Error & { correlationId?: string }).correlationId ?? correlationId);
    } finally { setBusy(false); }
  };
  const consent = async () => {
    setBusy(true); setError(null);
    try {
      const response = await api('/api/workspaces/current/consent/start', { method: 'POST' });
      const value = await response.json() as { authorizationUrl?: string; correlationId?: string };
      if (!response.ok || !value.authorizationUrl) throw Object.assign(new Error('consent start failed'), { correlationId: value.correlationId });
      setConsentUrl(value.authorizationUrl); setCorrelationId(value.correlationId ?? null);
    } catch (reason) {
      setError(messages.connectionActionFailed); setCorrelationId((reason as Error & { correlationId?: string }).correlationId ?? correlationId);
    } finally { setBusy(false); }
  };

  return <section className="content-panel onboarding-page" aria-labelledby="onboarding-title">
    <p className="eyebrow">{messages.onboardingEyebrow}</p>
    <h1 id="onboarding-title">{messages.onboardingTitle}</h1>
    <p>{messages.onboardingDescription}</p>
    {failed ? <div role="alert"><p>{messages.connectionUnavailable}</p>{correlationId && <p>{messages.correlationIdLabel}: <code>{correlationId}</code></p>}<button type="button" onClick={() => void load()}>{messages.retry}</button></div>
      : !health ? <p role="status">{messages.connectionLoading}</p>
        : <ConnectionStatusCard state={health.status} lastVerifiedAt={health.lastVerifiedAt} onCheck={() => void check()} onConsent={() => void consent()} consentUrl={consentUrl} actionPending={busy} actionError={error} />}
    {health?.status === 'connected' && <a href="/overview" onClick={event => { if (onNavigate) { event.preventDefault(); onNavigate('/overview'); } }}>{messages.onboardingContinue}</a>}
    {correlationId && !failed && <p>{messages.correlationIdLabel}: <code>{correlationId}</code></p>}
    <p>{messages.onboardingHelp}</p>
  </section>;
}
