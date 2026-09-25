import { useCallback, useEffect, useState } from 'react';
import { ConnectionStatusCard } from '../../components/ConnectionStatusCard';
import { messages, type ConnectionState } from '../../messages/en';
import { useApi } from '../../auth/useApi';
import type { AppSession } from '../../components/TenantContextHeader';

export type ConnectionHealth = { status: ConnectionState; lastVerifiedAt: string | null };
export type ConsentDescriptor = { authorizationUrl: string };
export type ConnectionHealthLoader = () => Promise<ConnectionHealth>;
export type ConnectionHealthActions = { check: ConnectionHealthLoader; startConsent: () => Promise<ConsentDescriptor> };
export type OverviewData = { freshness: string; fetchedAt: string; totalUsers: number; licenseCoverage: { assigned: number; available: number; percentage: number }; permissionHealth: { state: string; allowedCount: number; totalCount: number }; pimAttention: { requiresAttention: boolean; count: number }; partialData: boolean; access: { state: string } };
export type OverviewLoader = () => Promise<OverviewData>;

export function OverviewPage({ loadConnectionHealth, actions, loadOverview, session }: { loadConnectionHealth?: ConnectionHealthLoader; actions?: ConnectionHealthActions; loadOverview?: OverviewLoader; session?: AppSession }) {
  if (loadOverview) return <LoadedOverviewMetrics loadOverview={loadOverview} loadConnectionHealth={loadConnectionHealth} actions={actions} session={session} />;
  if (loadConnectionHealth) return <LoadedConnectionHealth loadConnectionHealth={loadConnectionHealth} actions={actions} />;
  return <AuthenticatedOverview session={session} />;
}

function AuthenticatedOverview({ session }: { session?: AppSession }) {
  const api = useApi();
  const loadOverview = useCallback(async () => {
    const response = await api('/api/overview');
    if (!response.ok) throw new Error('overview request failed');
    return await response.json() as OverviewData;
  }, [api]);
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
  const health = loadConnectionHealth && <LoadedConnectionHealth loadConnectionHealth={loadConnectionHealth} actions={actions} />;
  if (failed) return <div className="overview-page"><section className="content-panel"><p role="alert">{messages.overviewUnavailable}</p><button type="button" onClick={() => setRetry(value => value + 1)}>{messages.retry}</button></section>{health}</div>;
  if (!overview) return <div className="overview-page"><section className="content-panel"><p role="status">{messages.overviewLoading}</p></section>{health}</div>;
  const assignedModules = session?.workspace.moduleAccess ?? ['users', 'devices', 'licenses'];
  const modules = session?.workspace.enabledModules ? assignedModules.filter(module => session.workspace.enabledModules?.includes(module)) : assignedModules;
  const usersVisible = modules.includes('users');
  const devicesVisible = modules.includes('devices');
  const licensesVisible = modules.includes('licenses');
  const graphReady = overview.access.state === 'allowed' && !overview.partialData && overview.freshness !== 'unavailable';
  const validCount = (value: number) => Number.isSafeInteger(value) && value >= 0;
  const attention = [
    ...(usersVisible && overview.pimAttention.requiresAttention ? [messages.overviewPimAttention] : []),
    ...(overview.partialData ? ['Some summary data is unavailable.'] : []),
  ];
  return <div className="overview-page"><section className="content-panel" aria-labelledby="overview-title"><p className="eyebrow">{messages.overviewEyebrow}</p><h1 id="overview-title">{messages.overviewTitle}</h1><p>{messages.overviewFreshness}: {overview.freshness}</p><div className="overview-metrics">
    {usersVisible && <article className="overview-metric"><span className="overview-metric__label">Users</span><strong>{graphReady && validCount(overview.totalUsers) ? overview.totalUsers : 'Unavailable'}</strong>{!graphReady && <small>Entra permission needed</small>}</article>}
    {devicesVisible && <article className="overview-metric"><span className="overview-metric__label">Devices</span><strong>Unavailable</strong><small>No verified tenant total</small></article>}
    {licensesVisible && <article className="overview-metric"><span className="overview-metric__label">{messages.overviewLicenseCoverage}</span><strong>{graphReady && validCount(overview.licenseCoverage.percentage) && overview.licenseCoverage.percentage <= 100 ? `${overview.licenseCoverage.percentage}%` : 'Unavailable'}</strong>{!graphReady && <small>Entra permission needed</small>}</article>}
    {usersVisible && <article className="overview-metric"><span className="overview-metric__label">{messages.overviewPermissionHealth}</span><strong>{['healthy', 'incomplete'].includes(overview.permissionHealth.state) && validCount(overview.permissionHealth.allowedCount) && validCount(overview.permissionHealth.totalCount) && overview.permissionHealth.allowedCount <= overview.permissionHealth.totalCount ? `${overview.permissionHealth.allowedCount}/${overview.permissionHealth.totalCount}` : 'Unavailable'}</strong></article>}
  </div>{!modules.length && <p>You do not currently have an operational module assigned. Ask a workspace administrator to grant access.</p>}</section><section className="overview-card overview-card--attention" aria-labelledby="overview-attention-title"><h2 id="overview-attention-title">Needs attention</h2>{attention.length ? <ul>{attention.map(item => <li key={item}>{item}</li>)}</ul> : <p>No issues need attention right now.</p>}</section>{health}</div>;
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
