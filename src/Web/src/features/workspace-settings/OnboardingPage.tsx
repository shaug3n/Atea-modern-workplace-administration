import { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { ConnectionStatusCard } from '../../components/ConnectionStatusCard';
import { messages, type ConnectionState } from '../../messages/en';
import { writePendingFlow } from '../invitations/pendingFlow';

type Health = { status: ConnectionState; lastVerifiedAt: string | null; correlationId?: string | null };

export function OnboardingPage({ onNavigate, embedded = false }: { onNavigate?: (path: string) => void; embedded?: boolean }) {
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
      const value = await response.json() as { authorizationUrl?: string; challenge?: string; correlationId?: string };
      if (!response.ok || !value.authorizationUrl) throw Object.assign(new Error('consent start failed'), { correlationId: value.correlationId });
      const consent = new URL(value.authorizationUrl);
      const tenantMatch = consent.protocol === 'https:' && consent.hostname === 'login.microsoftonline.com'
        ? /^\/([0-9a-f-]{36})\/v2\.0\/adminconsent$/i.exec(consent.pathname)
        : null;
      const tenantId = tenantMatch?.[1];
      const redirectUriValue = consent.searchParams.get('redirect_uri');
      const redirectUri = redirectUriValue ? new URL(redirectUriValue) : null;
      if (!tenantId || tenantId === '00000000-0000-0000-0000-000000000000' ||
          !value.challenge || consent.searchParams.get('state') !== value.challenge ||
          consent.searchParams.get('scope') !== 'https://graph.microsoft.com/.default' ||
          !redirectUri || redirectUri.origin !== window.location.origin ||
          redirectUri.pathname !== '/onboarding/consent/callback' || redirectUri.search || redirectUri.hash) {
        throw Object.assign(new Error('consent start invalid'), { correlationId: value.correlationId });
      }
      writePendingFlow({
        kind: 'workspace',
        challenge: value.challenge,
        tenantId,
        expiresAt: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
        step: 'workspace_callback',
      });
      setConsentUrl(value.authorizationUrl); setCorrelationId(value.correlationId ?? null);
    } catch (reason) {
      setError(messages.connectionActionFailed); setCorrelationId((reason as Error & { correlationId?: string }).correlationId ?? correlationId);
    } finally { setBusy(false); }
  };

  return <section className="content-panel onboarding-page" aria-labelledby="onboarding-title">
    {!embedded && <p className="eyebrow">{messages.onboardingEyebrow}</p>}
    {embedded ? <h2 id="onboarding-title" tabIndex={-1}>Connection</h2> : <h1 id="onboarding-title">{messages.onboardingTitle}</h1>}
    <p>{messages.onboardingDescription}</p>
    {failed ? <div role="alert"><p>{messages.connectionUnavailable}</p>{correlationId && <p>{messages.correlationIdLabel}: <code>{correlationId}</code></p>}<button type="button" onClick={() => void load()}>{messages.retry}</button></div>
      : !health ? <p role="status">{messages.connectionLoading}</p>
        : <ConnectionStatusCard state={health.status} lastVerifiedAt={health.lastVerifiedAt} onCheck={() => void check()} onConsent={() => void consent()} consentUrl={consentUrl} actionPending={busy} actionError={error} />}
    {health?.status === 'connected' && <a href="/overview" onClick={event => { if (onNavigate) { event.preventDefault(); onNavigate('/overview'); } }}>{messages.onboardingContinue}</a>}
    <details className="setup-permissions" open={!embedded}>
      <summary>Delegated permissions and tenant consent</summary>
      <p>These are separate steps: Atea configures delegated scopes on the API app registration first; a customer Entra administrator then grants tenant consent. Tenant consent cannot add scopes that are missing from the app registration.</p>
      <ul>
        <li><strong>Users, groups and licenses:</strong> <code>User.Read.All</code>, <code>Group.Read.All</code>, <code>Directory.Read.All</code>.</li>
        <li><strong>Devices:</strong> <code>DeviceManagementManagedDevices.Read.All</code>. Device actions require additional delegated write scopes and an active tenant role; an eligible but inactive PIM role must be activated first.</li>
        <li><strong>BitLocker recovery:</strong> <code>BitlockerKey.ReadBasic.All</code> reads key metadata; <code>BitlockerKey.Read.All</code> permits a deliberate key reveal.</li>
        <li><strong>Windows LAPS recovery:</strong> <code>DeviceLocalCredential.ReadBasic.All</code> reads backup metadata; <code>DeviceLocalCredential.Read.All</code> permits a deliberate password reveal.</li>
        <li><strong>Exchange verification:</strong> <code>MailboxSettings.Read</code>, in addition to the directory-read scopes above.</li>
      </ul>
      <p>These four recovery scopes must first be added as Microsoft Graph delegated permissions to the API app registration by the Atea app owner. A customer tenant administrator must then grant tenant consent separately. Consent cannot add a scope missing from the registration. An eligible Entra PIM role may need activation; Graph makes the final access decision for the specific device and recovery record.</p>
      <p>If a scope is absent from the API registration, ask the Atea app owner to add it before retrying consent. If the scope is present but not granted in this tenant, use the consent action above with an appropriately authorized tenant administrator. If the permission check reports PIM activation required, activate the eligible role in Entra PIM and retry.</p>
    </details>
    {correlationId && !failed && <p>{messages.correlationIdLabel}: <code>{correlationId}</code></p>}
    <p>{messages.onboardingHelp}</p>
  </section>;
}
