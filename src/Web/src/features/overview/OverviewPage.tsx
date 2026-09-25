import { useCallback, useEffect, useState } from 'react';
import { ConnectionStatusCard } from '../../components/ConnectionStatusCard';
import { messages, type ConnectionState } from '../../messages/en';
import { useApi } from '../../auth/useApi';
import type { AppSession } from '../../components/TenantContextHeader';

export type ConnectionHealth = { status: ConnectionState; lastVerifiedAt: string | null };
export type ConsentDescriptor = { authorizationUrl: string };
export type ConnectionHealthLoader = () => Promise<ConnectionHealth>;
export type ConnectionHealthActions = { check: ConnectionHealthLoader; startConsent: () => Promise<ConsentDescriptor> };
export type OverviewData = { freshness: string; fetchedAt: string; totalUsers: number; licenseCoverage: { assigned: number; available: number; percentage: number }; permissionHealth: { state: string; allowedCount: number; totalCount: number }; pimAttention: { requiresAttention: boolean; count: number }; partialData: boolean; access: { state: string }; totalDevices?: number | null; deviceAccess?: { state: string } };
export type OverviewLoader = () => Promise<OverviewData>;

export function OverviewPage({ loadConnectionHealth, actions, loadOverview, session }: { loadConnectionHealth?: ConnectionHealthLoader; actions?: ConnectionHealthActions; loadOverview?: OverviewLoader; session?: AppSession }) {
  if (loadOverview) return <LoadedOverviewMetrics loadOverview={loadOverview} />;
  if (loadConnectionHealth) return <LoadedConnectionHealth loadConnectionHealth={loadConnectionHealth} actions={actions} />;
  return <AuthenticatedOverview session={session} />;
}

function AuthenticatedOverview({ session }: { session?: AppSession }) {
  const api = useApi();
  const loadOverview = useCallback(async () => {
    const response = await api('/api/overview');
    if (!response.ok) throw new Error('overview request failed');
    const overview = await response.json() as OverviewData;
    const modules = session?.workspace.moduleAccess ?? [];
    if (modules.includes('devices')) {
      try {
        const devices = await api('/api/devices?pageSize=1');
        if (devices.ok) {
          const value = await devices.json() as { total?: number; access?: { state: string } };
          overview.totalDevices = typeof value.total === 'number' ? value.total : null;
          overview.deviceAccess = value.access;
        } else overview.deviceAccess = { state: devices.status === 403 ? 'hidden' : 'temporarily_unavailable' };
      } catch { overview.deviceAccess = { state: 'temporarily_unavailable' }; }
    }
    return overview;
  }, [api, session]);
  const loadConnectionHealth = useCallback(async () => { const response = await api('/api/workspaces/current/connection-health'); if (!response.ok) throw new Error('connection health request failed'); return await response.json() as ConnectionHealth; }, [api]);
  const actions = {
    check: useCallback(async () => { const response = await api('/api/workspaces/current/connection-health/check', { method: 'POST' }); if (!response.ok) throw new Error('connection check failed'); return await response.json() as ConnectionHealth; }, [api]),
    startConsent: useCallback(async () => { const response = await api('/api/workspaces/current/consent/start', { method: 'POST' }); if (!response.ok) throw new Error('consent start failed'); return await response.json() as ConsentDescriptor; }, [api])
  } satisfies ConnectionHealthActions;
  return <LoadedOverviewMetrics loadOverview={loadOverview} loadConnectionHealth={loadConnectionHealth} actions={actions} session={session} />;
}

function LoadedOverviewMetrics({ loadOverview, loadConnectionHealth, actions, session }: { loadOverview: OverviewLoader; loadConnectionHealth?: ConnectionHealthLoader; actions?: ConnectionHealthActions; session?: AppSession }) {
  const [overview, setOverview] = useState<OverviewData | null>(null);
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => { let cancelled = false; setFailed(false); loadOverview().then(value => { if (!cancelled) setOverview(value); }).catch(() => { if (!cancelled) setFailed(true); }); return () => { cancelled = true; }; }, [loadOverview, retry]);
  if (failed) return <section className="content-panel"><p role="alert">{messages.overviewUnavailable}</p><button type="button" onClick={() => setRetry(value => value + 1)}>{messages.retry}</button></section>;
  if (!overview) return <section className="content-panel"><p role="status">{messages.overviewLoading}</p></section>;
  const modules = session?.workspace.moduleAccess ?? ['users', 'devices', 'licenses'];
  const usersVisible = modules.includes('users');
  const devicesVisible = modules.includes('devices');
  const licensesVisible = modules.includes('licenses');
  const graphReady = overview.access.state === 'allowed';
  return <><section className="content-panel overview-page" aria-labelledby="overview-title"><p className="eyebrow">{messages.overviewEyebrow}</p><h1 id="overview-title">{messages.overviewTitle}</h1><p>{messages.overviewFreshness}: {overview.freshness}</p><div className="overview-metrics">
    {usersVisible && <article><strong>{graphReady ? overview.totalUsers : '—'}</strong><span>Users</span>{!graphReady && <small>Entra permission needed</small>}</article>}
    {devicesVisible && <article><strong>{overview.totalDevices ?? '—'}</strong><span>Devices</span>{overview.deviceAccess?.state !== 'allowed' && overview.deviceAccess?.state !== 'read_only' && <small>Device access needs setup</small>}</article>}
    {licensesVisible && <article><strong>{graphReady ? `${overview.licenseCoverage.percentage}%` : '—'}</strong><span>{messages.overviewLicenseCoverage}</span>{!graphReady && <small>Entra permission needed</small>}</article>}
    {usersVisible && <article><strong>{overview.permissionHealth.allowedCount}/{overview.permissionHealth.totalCount}</strong><span>{messages.overviewPermissionHealth}</span></article>}
  </div>{overview.pimAttention.requiresAttention && usersVisible && <p role="status">{messages.overviewPimAttention}</p>}{!modules.length && <p>You do not currently have an operational module assigned. Ask a workspace administrator to grant access.</p>}</section>{loadConnectionHealth && <LoadedConnectionHealth loadConnectionHealth={loadConnectionHealth} actions={actions} />}</>;
}

function LoadedConnectionHealth({ loadConnectionHealth, actions }: { loadConnectionHealth: ConnectionHealthLoader; actions?: ConnectionHealthActions }) {
  const [health, setHealth] = useState<ConnectionHealth | null>(null); const [failed, setFailed] = useState(false); const [actionPending, setActionPending] = useState(false); const [actionError, setActionError] = useState<string | null>(null); const [consentUrl, setConsentUrl] = useState<string | null>(null);
  useEffect(() => { loadConnectionHealth().then(setHealth).catch(() => setFailed(true)); }, [loadConnectionHealth]);
  if (failed) return <section className="content-panel"><p role="alert">{messages.connectionUnavailable}</p></section>;
  if (!health) return <section className="content-panel"><p>{messages.connectionLoading}</p></section>;
  const runCheck = actions ? async () => { setActionPending(true); setActionError(null); try { setHealth(await actions.check()); } catch { setActionError(messages.connectionActionFailed); } finally { setActionPending(false); } } : undefined;
  const startConsent = actions ? async () => { setActionPending(true); setActionError(null); try { setConsentUrl((await actions.startConsent()).authorizationUrl); } catch { setActionError(messages.connectionActionFailed); } finally { setActionPending(false); } } : undefined;
  return <ConnectionStatusCard state={health.status} lastVerifiedAt={health.lastVerifiedAt} onCheck={runCheck} onConsent={startConsent} consentUrl={consentUrl} actionPending={actionPending} actionError={actionError} />;
}
