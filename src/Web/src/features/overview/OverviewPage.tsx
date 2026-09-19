import { useCallback, useEffect, useState } from 'react';
import { ConnectionStatusCard } from '../../components/ConnectionStatusCard';
import { messages, type ConnectionState } from '../../messages/en';
import { useApi } from '../../auth/useApi';

export type ConnectionHealth = { status: ConnectionState; lastVerifiedAt: string | null };
export type ConsentDescriptor = { authorizationUrl: string };
export type ConnectionHealthLoader = () => Promise<ConnectionHealth>;
export type ConnectionHealthActions = { check: ConnectionHealthLoader; startConsent: () => Promise<ConsentDescriptor> };

export function OverviewPage({ loadConnectionHealth, actions }: { loadConnectionHealth?: ConnectionHealthLoader; actions?: ConnectionHealthActions }) {
  if (loadConnectionHealth) return <LoadedOverview loadConnectionHealth={loadConnectionHealth} actions={actions} />;
  return <AuthenticatedOverview />;
}

function AuthenticatedOverview() {
  const api = useApi();
  const loadConnectionHealth = useCallback(async () => {
    const response = await api('/api/workspaces/current/connection-health');
    if (!response.ok) throw new Error('connection health request failed');
    return await response.json() as ConnectionHealth;
  }, [api]);
  const actions = {
    check: useCallback(async () => {
      const response = await api('/api/workspaces/current/connection-health/check', { method: 'POST' });
      if (!response.ok) throw new Error('connection health check failed');
      return await response.json() as ConnectionHealth;
    }, [api]),
    startConsent: useCallback(async () => {
      const response = await api('/api/workspaces/current/consent/start', { method: 'POST' });
      if (!response.ok) throw new Error('consent start failed');
      return await response.json() as ConsentDescriptor;
    }, [api])
  } satisfies ConnectionHealthActions;
  return <LoadedOverview loadConnectionHealth={loadConnectionHealth} actions={actions} />;
}

function LoadedOverview({ loadConnectionHealth, actions }: { loadConnectionHealth: ConnectionHealthLoader; actions?: ConnectionHealthActions }) {
  const [health, setHealth] = useState<ConnectionHealth | null>(null);
  const [failed, setFailed] = useState(false);
  const [actionPending, setActionPending] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [consentUrl, setConsentUrl] = useState<string | null>(null);
  useEffect(() => {
    loadConnectionHealth().then(setHealth).catch(() => setFailed(true));
  }, [loadConnectionHealth]);
  if (failed) return <main><p role="alert">{messages.connectionUnavailable}</p></main>;
  if (!health) return <main><p>{messages.connectionLoading}</p></main>;
  const runCheck = actions ? async () => { setActionPending(true); setActionError(null); try { setHealth(await actions.check()); } catch { setActionError(messages.connectionActionFailed); } finally { setActionPending(false); } } : undefined;
  const startConsent = actions ? async () => { setActionPending(true); setActionError(null); try { setConsentUrl((await actions.startConsent()).authorizationUrl); } catch { setActionError(messages.connectionActionFailed); } finally { setActionPending(false); } } : undefined;
  return <main><ConnectionStatusCard state={health.status} lastVerifiedAt={health.lastVerifiedAt} onCheck={runCheck} onConsent={startConsent} consentUrl={consentUrl} actionPending={actionPending} actionError={actionError} /></main>;
}
