import React, { useCallback, useEffect, useState } from 'react';
import { ConnectionStatusCard } from '../../components/ConnectionStatusCard';
import { messages, type ConnectionState } from '../../messages/en';
import { useApi } from '../../auth/useApi';
import type { AppSession } from '../../components/TenantContextHeader';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { DataFreshness } from '../../components/DataFreshness';
import { MetricCard } from '../../components/MetricCard';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { useWorkspaceIssueReporter } from '../../notifications/WorkspaceNotifications';

export type ConnectionHealth = { status: ConnectionState; lastVerifiedAt: string | null };
export type ConsentDescriptor = { authorizationUrl: string };
export type ConnectionHealthLoader = () => Promise<ConnectionHealth>;
export type ConnectionHealthActions = { check: ConnectionHealthLoader; startConsent: () => Promise<ConsentDescriptor> };
export type OverviewData = { freshness: string; fetchedAt: string; totalUsers: number; licenseCoverage: { assigned: number; available: number; percentage: number }; permissionHealth: { state: string; allowedCount: number; totalCount: number }; pimAttention: { requiresAttention: boolean; count: number }; partialData: boolean; access: { state: string } };
export type OverviewLoader = () => Promise<OverviewData>;

export function OverviewPage({ loadConnectionHealth, actions, loadOverview, session, onNavigate }: { loadConnectionHealth?: ConnectionHealthLoader; actions?: ConnectionHealthActions; loadOverview?: OverviewLoader; session?: AppSession; onNavigate?: (path: string) => void }) {
  if (loadOverview) return <LoadedOverviewMetrics loadOverview={loadOverview} session={session} onNavigate={onNavigate} />;
  if (loadConnectionHealth) return <LoadedConnectionHealth loadConnectionHealth={loadConnectionHealth} actions={actions} />;
  return <AuthenticatedOverview session={session} onNavigate={onNavigate} />;
}

function AuthenticatedOverview({ session, onNavigate }: { session?: AppSession; onNavigate?: (path: string) => void }) {
  const api = useApi();
  const loadOverview = useCallback(async () => {
    const response = await api('/api/overview');
    if (!response.ok) throw new Error('overview request failed');
    return await response.json() as OverviewData;
  }, [api]);
  return <LoadedOverviewMetrics loadOverview={loadOverview} session={session} onNavigate={onNavigate} />;
}

function LoadedOverviewMetrics({ loadOverview, session, onNavigate }: { loadOverview: OverviewLoader; session?: AppSession; onNavigate?: (path: string) => void }) {
  const issueReporter = useWorkspaceIssueReporter();
  const [overview, setOverview] = useState<OverviewData | null>(null);
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => { let cancelled = false; setFailed(false); loadOverview().then(value => { if (!cancelled) { setOverview(value); if (value.freshness === 'unavailable' || value.partialData) issueReporter.report({ key: 'overview:read', area: 'services', kind: 'service', severity: 'warning', title: 'Summary unavailable', detail: 'Try loading the overview again.' }); else issueReporter.clear('overview:read'); } }).catch(() => { if (!cancelled) { setFailed(true); issueReporter.report({ key: 'overview:read', area: 'services', kind: 'service', severity: 'warning', title: 'Summary unavailable', detail: 'Try loading the overview again.' }); } }); return () => { cancelled = true; }; }, [loadOverview, retry, issueReporter]);
  if (failed) return <div className="overview-page"><WorkspacePageHeader eyebrow={messages.overviewEyebrow} title={messages.overviewTitle} /><WorkspaceDataState state="unavailable" message={messages.overviewUnavailable} onRetry={() => setRetry(value => value + 1)} /></div>;
  if (!overview) return <div className="overview-page"><WorkspacePageHeader eyebrow={messages.overviewEyebrow} title={messages.overviewTitle} /><WorkspaceDataState state="loading" message={messages.overviewLoading} /></div>;
  const assignedModules = session?.workspace.moduleAccess ?? ['users', 'devices', 'licenses'];
  const modules = session?.workspace.enabledModules ? assignedModules.filter(module => session.workspace.enabledModules?.includes(module)) : assignedModules;
  const usersVisible = modules.includes('users');
  const devicesVisible = modules.includes('devices');
  const licensesVisible = modules.includes('licenses');
  const accessReadable = overview.access.state === 'allowed' || overview.access.state === 'read_only';
  const summaryAvailable = accessReadable && overview.freshness !== 'unavailable';
  const permissionGuidance = overview.access.state === 'hidden' || overview.access.state === 'consent_required' || overview.access.state.startsWith('pim_');
  const unavailableSummaryMessage = permissionGuidance ? 'Entra permission needed' : 'Summary data temporarily unavailable.';
  const validCount = (value: number) => Number.isSafeInteger(value) && value >= 0;
  const canManageSettings = Boolean(session?.workspaceAccess?.canManageSettings);
  const permissionsPartial = usersVisible && overview.permissionHealth.state === 'incomplete' && validCount(overview.permissionHealth.allowedCount) && validCount(overview.permissionHealth.totalCount) && overview.permissionHealth.allowedCount <= overview.permissionHealth.totalCount;
  type Attention = { key: string; text: string; action: { label: string; href?: string; onClick?: () => void } | null };
  const attention: Attention[] = [
    ...(usersVisible && overview.pimAttention.requiresAttention ? [{ key: 'pim', text: messages.overviewPimAttention, action: { label: 'Open PIM guidance', href: '/identity' } }] : []),
    ...(permissionsPartial ? [{ key: 'consent', text: `Workspace permissions need attention (${overview.permissionHealth.allowedCount} of ${overview.permissionHealth.totalCount} available).`, action: canManageSettings ? { label: 'Open setup', href: '/settings#connection' } : null }] : []),
    ...(overview.partialData ? [{ key: 'partial', text: 'Some summary data is unavailable.', action: { label: 'Retry', onClick: () => setRetry(value => value + 1) } }] : []),
  ];
  const go = (href: string) => (event: React.MouseEvent) => { if (onNavigate) { event.preventDefault(); onNavigate(href); } };
  const licenseValue = summaryAvailable && validCount(overview.licenseCoverage.assigned) && validCount(overview.licenseCoverage.available) ? `${overview.licenseCoverage.assigned} of ${overview.licenseCoverage.assigned + overview.licenseCoverage.available}` : null;
  const permissionValue = summaryAvailable && ['healthy', 'incomplete'].includes(overview.permissionHealth.state) && (validCount(overview.permissionHealth.allowedCount) && validCount(overview.permissionHealth.totalCount) && overview.permissionHealth.allowedCount <= overview.permissionHealth.totalCount) ? `${overview.permissionHealth.allowedCount}/${overview.permissionHealth.totalCount}` : null;
  return <div className="overview-page">
    <WorkspacePageHeader eyebrow={messages.overviewEyebrow} title={messages.overviewTitle} meta={<DataFreshness fetchedAt={overview.fetchedAt} freshness={overview.freshness === 'unavailable' ? 'unavailable' : overview.freshness === 'stale' ? 'stale' : 'fresh'} partialData={overview.partialData} source="Microsoft Graph" onRefresh={() => setRetry(value => value + 1)} />} />
    <section className="content-panel" aria-label="Summary">
      <div className="metric-grid">
        {usersVisible && <MetricCard label="Users" value={summaryAvailable && validCount(overview.totalUsers) ? overview.totalUsers : null} unavailableReason={unavailableSummaryMessage} href="/users" onNavigate={onNavigate} linkLabel="users" />}
        {licensesVisible && <MetricCard label="Licenses" value={licenseValue} detail={licenseValue ? 'assigned' : undefined} unavailableReason={unavailableSummaryMessage} href="/licenses" onNavigate={onNavigate} linkLabel="licenses" />}
        {devicesVisible && <MetricCard label="Managed devices" detail="View compliance and remote actions" href="/devices" onNavigate={onNavigate} linkLabel="devices" />}
        {usersVisible && <MetricCard label={messages.overviewPermissionHealth} value={permissionValue} unavailableReason={unavailableSummaryMessage} />}
      </div>
      {!modules.length && <p>You do not currently have an operational module assigned. Ask a workspace administrator to grant access.</p>}
    </section>
    <section className="overview-card overview-card--attention" aria-labelledby="overview-attention-title">
      <h2 id="overview-attention-title">Needs attention</h2>
      {attention.length ? <ul className="record-list">{attention.map(item => <li key={item.key}><span className="record-list__main">{item.text}</span>{item.action && (item.action.href ? <a className="button button--secondary" href={item.action.href} onClick={go(item.action.href)}>{item.action.label}</a> : <button type="button" className="button button--secondary" onClick={item.action.onClick}>{item.action.label}</button>)}</li>)}</ul> : <WorkspaceDataState kind="empty" compact title="Nothing needs your attention." message="No issues need attention right now." />}
    </section>
  </div>;
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
