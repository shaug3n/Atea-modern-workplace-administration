import React, { useCallback, useEffect, useState } from 'react';
import { ConnectionStatusCard } from '../../components/ConnectionStatusCard';
import { overviewMessages } from './messages';
import { messages, type ConnectionState } from '../../messages/en';
import { useApi } from '../../auth/useApi';
import type { AppSession } from '../../components/TenantContextHeader';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { DataFreshness } from '../../components/DataFreshness';
import { InfoTip } from '../../components/InfoTip';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { DateTime } from '../../components/DateTime';
import { useWorkspaceIssueReporter } from '../../notifications/WorkspaceNotifications';

export type ConnectionHealth = { status: ConnectionState; lastVerifiedAt: string | null };
export type ConsentDescriptor = { authorizationUrl: string };
export type ConnectionHealthLoader = () => Promise<ConnectionHealth>;
export type ConnectionHealthActions = { check: ConnectionHealthLoader; startConsent: () => Promise<ConsentDescriptor> };
export type OverviewSource<T> = {
  state: string;
  fetchedAt: string | null;
  partialData: boolean;
  data: T | null;
  scope: string;
};
export type OverviewCapability = {
  capability: string;
  state: string;
  reasonCode?: string;
  pim?: { state: string; activationUrl?: string | null } | null;
  nextStep?: { label: string; href?: string | null } | null;
  missingScopes?: string[] | null;
};
export type OverviewActivityItem = { action: string; outcome: string; timestamp: string };
export type OverviewData = {
  effectiveModules: string[];
  effectiveCapabilities: OverviewCapability[];
  users: OverviewSource<{ totalUsers: number }>;
  licenseCoverage: OverviewSource<{ assignedUsers: number; totalUsers: number; percentage: number }>;
  activity: OverviewSource<{ items: OverviewActivityItem[] }>;
};
export type OverviewLoader = () => Promise<OverviewData>;

type FreshnessState = 'fresh' | 'stale' | 'unavailable';
type Action = { id: string; label: string; rank: number; order: number; href?: string; onClick?: () => void; text?: string };

const supportedFreshness = new Set(['fresh', 'stale', 'unavailable']);
const validCount = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) >= 0;
const capabilityStateLabels: Record<string, string> = {
  allowed: overviewMessages.overviewCapabilityAllowed,
  read_only: overviewMessages.overviewCapabilityReadOnly,
  hidden: overviewMessages.overviewCapabilityHidden,
  disabled: overviewMessages.overviewCapabilityDisabled,
  consent_required: overviewMessages.overviewCapabilityConsentRequired,
  pim_activation_required: overviewMessages.overviewCapabilityPimActivationRequired,
  pim_approval_required: overviewMessages.overviewCapabilityPimApprovalRequired,
  pim_mfa_required: overviewMessages.overviewCapabilityPimMfaRequired,
  pim_eligibility_expired: overviewMessages.overviewCapabilityPimEligibilityExpired,
  temporarily_unavailable: overviewMessages.overviewCapabilityTemporarilyUnavailable,
};
const pimCapabilityStates = new Set([
  'pim_activation_required',
  'pim_approval_required',
  'pim_mfa_required',
  'pim_eligibility_expired',
]);

function sourceFreshness(state: string): FreshnessState {
  return state === 'fresh' || state === 'stale' ? state : 'unavailable';
}

function capabilityStateLabel(state: string) {
  return Object.prototype.hasOwnProperty.call(capabilityStateLabels, state)
    ? capabilityStateLabels[state]
    : overviewMessages.overviewUnknownState;
}

function kpiLinkLabel(label: string, value: string | number, destination: string) {
  return overviewMessages.overviewKpiLinkLabel
    .replace('{label}', label)
    .replace('{value}', String(value))
    .replace('{destination}', destination);
}

function sourceScope(scope: string, activity = false) {
  if (activity && scope !== 'workspace') return overviewMessages.overviewUnverifiedScope;
  if (scope === 'tenant_wide_verified') return overviewMessages.overviewTenantWideScope;
  if (scope === 'workspace') return overviewMessages.overviewWorkspaceScope;
  return overviewMessages.overviewUnverifiedScope;
}

function capabilityFor(overview: OverviewData, capability: string) {
  return overview.effectiveCapabilities.find(item => item.capability === capability);
}

function sourceMessage(source: OverviewSource<unknown>, label: string) {
  if (source.state === 'restricted') return overviewMessages.overviewRestricted.replace('{section}', label);
  if (source.state === 'empty') return overviewMessages.overviewEmpty.replace('{section}', label);
  if (source.state === 'unavailable' || !supportedFreshness.has(source.state)) {
    return overviewMessages.overviewSourceUnavailable.replace('{section}', label);
  }
  return null;
}

function metricStateKind(source: OverviewSource<unknown>, capabilityAllowed: boolean, scopeVerified: boolean) {
  if (!capabilityAllowed || source.state === 'restricted' || !scopeVerified) return 'permission' as const;
  if (source.state === 'empty') return 'empty' as const;
  if (source.state === 'stale') return 'stale' as const;
  return 'unavailable' as const;
}

function metricStateMessage(source: OverviewSource<unknown>, label: string, capabilityAllowed: boolean, scopeVerified: boolean) {
  if (!capabilityAllowed || source.state === 'restricted') return overviewMessages.overviewRestricted.replace('{section}', label.toLowerCase());
  if (source.state === 'empty') return overviewMessages.overviewEmpty.replace('{section}', label.toLowerCase());
  if (!scopeVerified) return overviewMessages.overviewUnverifiedSource.replace('{section}', label.toLowerCase());
  return sourceMessage(source, label) ?? overviewMessages.overviewSourceUnavailable.replace('{section}', label.toLowerCase());
}

export function OverviewPage({ loadConnectionHealth, actions, loadOverview, session, onNavigate }: {
  loadConnectionHealth?: ConnectionHealthLoader;
  actions?: ConnectionHealthActions;
  loadOverview?: OverviewLoader;
  session?: AppSession;
  onNavigate?: (path: string) => void;
}) {
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

  useEffect(() => {
    let cancelled = false;
    setFailed(false);
    loadOverview().then(value => {
      if (cancelled) return;
      setOverview(value);
      const sourceIssue = [value.users, value.licenseCoverage, value.activity].some(source =>
        source.state === 'unavailable' || source.state === 'stale' || source.partialData,
      );
      if (sourceIssue) issueReporter.report({ key: 'overview:read', area: 'services', kind: 'service', severity: 'warning', title: 'Summary unavailable', detail: 'Try loading the overview again.' });
      else issueReporter.clear('overview:read');
    }).catch(() => {
      if (!cancelled) {
        setFailed(true);
        issueReporter.report({ key: 'overview:read', area: 'services', kind: 'service', severity: 'warning', title: 'Summary unavailable', detail: 'Try loading the overview again.' });
      }
    });
    return () => { cancelled = true; };
  }, [loadOverview, retry, issueReporter]);

  const retryOverview = () => setRetry(value => value + 1);
  if (failed) return <div className="overview-page"><WorkspacePageHeader eyebrow={overviewMessages.overviewEyebrow} title={overviewMessages.overviewTitle} /><WorkspaceDataState state="unavailable" message={overviewMessages.overviewUnavailable} onRetry={retryOverview} /></div>;
  if (!overview) return <div className="overview-page"><WorkspacePageHeader eyebrow={overviewMessages.overviewEyebrow} title={overviewMessages.overviewTitle} /><WorkspaceDataState state="loading" message={overviewMessages.overviewLoading} /></div>;

  const modules = new Set(overview.effectiveModules);
  const usersCapability = capabilityFor(overview, 'users.view');
  const licensesCapability = capabilityFor(overview, 'licenses.view');
  const devicesCapability = capabilityFor(overview, 'devices.view');
  const auditCapability = capabilityFor(overview, 'audit.view');
  const canManageSettings = session?.workspaceAccess?.canManageSettings === true;
  const usersAllowed = modules.has('users') && usersCapability?.state === 'allowed';
  const licensesAllowed = modules.has('licenses') && licensesCapability?.state === 'allowed';
  const devicesAllowed = modules.has('devices') && devicesCapability?.state === 'allowed';
  const activityScopeVerified = overview.activity.scope === 'workspace';
  const activityAllowed = auditCapability?.state === 'allowed' && activityScopeVerified;
  const usersScopeVerified = overview.users.scope === 'tenant_wide_verified';
  const licensesScopeVerified = overview.licenseCoverage.scope === 'tenant_wide_verified';
  const usersDataValid = overview.users.data !== null && validCount(overview.users.data.totalUsers);
  const licensesDataValid = overview.licenseCoverage.data !== null
    && validCount(overview.licenseCoverage.data.assignedUsers)
    && validCount(overview.licenseCoverage.data.totalUsers)
    && overview.licenseCoverage.data.assignedUsers <= overview.licenseCoverage.data.totalUsers;
  const usersHaveCurrentData = usersAllowed && usersScopeVerified && usersDataValid && ['fresh', 'stale'].includes(overview.users.state);
  const licensesHaveCurrentData = licensesAllowed && licensesScopeVerified && licensesDataValid && ['fresh', 'stale'].includes(overview.licenseCoverage.state);

  const actionsList: Action[] = [];
  const addAccessAction = (capability: OverviewCapability | undefined, id: string, label: string, order: number) => {
    if (!capability || capability.state === 'allowed') return;
    const pimRequired = pimCapabilityStates.has(capability.state);
    const consentRequired = capability.state === 'consent_required';
    const href = pimRequired ? '/identity' : consentRequired && canManageSettings ? '/settings#connection' : undefined;
    actionsList.push({
      id,
      label: pimRequired ? overviewMessages.overviewOpenPim : consentRequired ? overviewMessages.overviewOpenSetup : overviewMessages.overviewAccessRestricted,
      rank: 0,
      order,
      ...(href ? { href } : {}),
      text: `${label}: ${capabilityStateLabel(capability.state)}`,
    });
  };
  addAccessAction(usersCapability, 'users-access', overviewMessages.overviewUsers, 0);
  addAccessAction(licensesCapability, 'licenses-access', overviewMessages.overviewLicenses, 1);
  if (auditCapability?.state !== 'allowed' && auditCapability) {
    actionsList.push({ id: 'activity-access', label: overviewMessages.overviewAccessRestricted, rank: 0, order: 2, text: `${overviewMessages.overviewActivity}: ${capabilityStateLabel(auditCapability.state)}` });
  }

  const addFreshnessAction = (source: OverviewSource<unknown>, key: string, label: string, order: number, invalidData = false) => {
    if (source.state !== 'restricted' && source.state !== 'empty'
      && (source.state === 'stale' || source.state === 'unavailable' || !supportedFreshness.has(source.state) || invalidData)) {
      actionsList.push({ id: key, label: overviewMessages.overviewRetry, rank: 1, order, onClick: retryOverview, text: overviewMessages.overviewRetrySource.replace('{section}', label) });
    }
  };
  if (usersAllowed) addFreshnessAction(overview.users, 'users-retry', overviewMessages.overviewUsers, 0, usersScopeVerified && !usersDataValid);
  if (licensesAllowed) addFreshnessAction(overview.licenseCoverage, 'licenses-retry', overviewMessages.overviewLicenses, 1, licensesScopeVerified && !licensesDataValid);
  if (activityAllowed) addFreshnessAction(overview.activity, 'activity-retry', overviewMessages.overviewActivity, 2);
  else if (auditCapability?.state === 'allowed' && !activityScopeVerified) {
    actionsList.push({ id: 'activity-scope-retry', label: overviewMessages.overviewRetry, rank: 1, order: 2, onClick: retryOverview, text: overviewMessages.overviewRetrySource.replace('{section}', overviewMessages.overviewActivity) });
  }

  if (devicesAllowed) actionsList.push({ id: 'devices-navigation', label: overviewMessages.overviewOpenDevices, href: '/devices', rank: 2, order: 0 });
  if (activityAllowed) actionsList.push({ id: 'activity-navigation', label: overviewMessages.overviewOpenActivity, href: '/activity', rank: 2, order: 1 });
  actionsList.sort((left, right) => left.rank - right.rank || left.order - right.order);

  const navigate = (path: string) => (event: React.MouseEvent) => {
    if (onNavigate) {
      event.preventDefault();
      onNavigate(path);
    }
  };
  const navigateKpi = (path: string) => (event: React.MouseEvent<HTMLAnchorElement>) => {
    if (onNavigate && event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey) {
      event.preventDefault();
      onNavigate(path);
    }
  };
  const capabilityRows = [
    [overviewMessages.overviewUsers, usersCapability],
    [overviewMessages.overviewLicenses, licensesCapability],
    [overviewMessages.overviewDevices, devicesCapability],
    [overviewMessages.overviewActivity, auditCapability],
  ] as const;
  const sourceRows = [
    [overviewMessages.overviewUsers, overview.users, overviewMessages.overviewGraphSummary],
    [overviewMessages.overviewLicenseCoverage, overview.licenseCoverage, overviewMessages.overviewGraphSummary],
    [overviewMessages.overviewActivity, overview.activity, overviewMessages.overviewWorkspaceActivity],
  ] as const;
  const activityItems = activityAllowed && overview.activity.state !== 'restricted' && overview.activity.data?.items
    ? overview.activity.data.items.slice(0, 5)
    : [];
  const activityEmpty = activityAllowed && overview.activity.state === 'empty';
  const activityUnavailable = auditCapability?.state === 'allowed' && (!activityScopeVerified || overview.activity.state === 'unavailable' || !supportedFreshness.has(overview.activity.state));
  const activityRestricted = auditCapability?.state !== 'allowed' || overview.activity.state === 'restricted';

  return <div className="overview-page">
    <WorkspacePageHeader eyebrow={overviewMessages.overviewEyebrow} title={overviewMessages.overviewTitle} />

    <section className="overview-card" aria-labelledby="overview-context-title">
      <h2 id="overview-context-title">{overviewMessages.overviewContext}</h2>
      <dl className="overview-facts">
        <div><dt>{overviewMessages.overviewRole}</dt><dd>{session?.workspaceAccess?.role ?? overviewMessages.overviewUnknownRole}</dd></div>
        <div><dt>{overviewMessages.overviewEffectiveCapabilities}</dt><dd>{capabilityRows.map(([label, capability]) => <div className="overview-card__muted" key={label}>{label} access: {capability ? capabilityStateLabel(capability.state) : overviewMessages.overviewUnknownState}</div>)}</dd></div>
        <div><dt>{overviewMessages.overviewScope}</dt><dd>{sourceRows.map(([label, source, sourceName]) => <div className="overview-card__muted" key={label}>{label}: {sourceScope(source.scope, sourceName === overviewMessages.overviewWorkspaceActivity)}</div>)}</dd></div>
        <div><dt>{overviewMessages.overviewFreshness}</dt><dd>{sourceRows.map(([label, source, sourceName]) => <div className="overview-card__muted" key={label}>
          {label}:{' '}
          {source.state === 'restricted'
            ? overviewMessages.overviewRestricted.replace('{section}', label.toLowerCase())
            : source.state === 'empty'
              ? overviewMessages.overviewEmpty.replace('{section}', label.toLowerCase())
              : <DataFreshness fetchedAt={source.fetchedAt} freshness={sourceFreshness(source.state)} partialData={source.partialData} presentation="pill" source={sourceName} onRefresh={retryOverview} />}
        </div>)}</dd></div>
      </dl>
    </section>

    <section className="content-panel" aria-label={overviewMessages.overviewMetrics}>
      <div className="overview-card__heading">
        <h2>{overviewMessages.overviewMetrics}</h2>
        {licensesHaveCurrentData && <InfoTip label={overviewMessages.overviewLicenseInfoLabel} content={overviewMessages.overviewLicenseInfo} />}
      </div>
      <div className="metric-grid">
        {usersHaveCurrentData && <a className="metric-card metric-card--filter metric-card__link" href="/users" aria-label={kpiLinkLabel(overviewMessages.overviewUsers, overview.users.data!.totalUsers, overviewMessages.overviewUsersDestination)} onClick={navigateKpi('/users')}>
          <span className="metric-card__content"><span className="metric-card__label">{overviewMessages.overviewUsers}</span><strong className="metric-card__value">{overview.users.data!.totalUsers}</strong></span>
        </a>}
        {licensesHaveCurrentData && <a className="metric-card metric-card--filter metric-card__link" href="/licenses" aria-label={kpiLinkLabel(overviewMessages.overviewLicenseCoverage, `${overview.licenseCoverage.data!.assignedUsers} of ${overview.licenseCoverage.data!.totalUsers}`, overviewMessages.overviewLicensesDestination)} onClick={navigateKpi('/licenses')}>
          <span className="metric-card__content"><span className="metric-card__label">{overviewMessages.overviewLicenseCoverage}</span><strong className="metric-card__value">{overview.licenseCoverage.data!.assignedUsers} of {overview.licenseCoverage.data!.totalUsers}</strong><span className="metric-card__detail">{overviewMessages.overviewAssignedUsersOfTotal}</span></span>
        </a>}
      </div>
      {!usersHaveCurrentData && <WorkspaceDataState kind={metricStateKind(overview.users, usersAllowed, usersScopeVerified)} compact title={overviewMessages.overviewUsers} message={metricStateMessage(overview.users, overviewMessages.overviewUsers, usersAllowed, usersScopeVerified)} onRetry={usersAllowed && (overview.users.state === 'stale' || overview.users.state === 'unavailable' || !supportedFreshness.has(overview.users.state) || (usersScopeVerified && !usersDataValid)) ? retryOverview : undefined} />}
      {!licensesHaveCurrentData && <WorkspaceDataState kind={metricStateKind(overview.licenseCoverage, licensesAllowed, licensesScopeVerified)} compact title={overviewMessages.overviewLicenseCoverage} message={metricStateMessage(overview.licenseCoverage, overviewMessages.overviewLicenseCoverage, licensesAllowed, licensesScopeVerified)} onRetry={licensesAllowed && (overview.licenseCoverage.state === 'stale' || overview.licenseCoverage.state === 'unavailable' || !supportedFreshness.has(overview.licenseCoverage.state) || (licensesScopeVerified && !licensesDataValid)) ? retryOverview : undefined} />}
    </section>

    <section className="overview-card overview-card--attention" aria-labelledby="overview-attention-title">
      <h2 id="overview-attention-title">{overviewMessages.overviewPriorityActions}</h2>
      {actionsList.length ? <ul className="record-list overview-priority-actions">{actionsList.map(item => <li key={item.id}>
        {item.text && <span className="record-list__main">{item.text}</span>}
        {item.href
          ? <a className="button button--secondary" href={item.href} onClick={navigate(item.href)}>{item.label}</a>
          : item.onClick
            ? <button type="button" className="button button--secondary" onClick={item.onClick}>{item.label}</button>
            : <span className="overview-card__muted">{item.label}</span>}
      </li>)}</ul> : <WorkspaceDataState kind="empty" compact title={overviewMessages.overviewNoPriorityActions} message={overviewMessages.overviewNoPriorityActionsBody} />}
    </section>

    <section className="overview-card" aria-label={overviewMessages.overviewRecentActivity}>
      <div className="overview-card__heading"><h2>{overviewMessages.overviewRecentActivity}</h2><span className="overview-card__muted">{overviewMessages.overviewWorkspaceActivityNotice}</span></div>
      {activityRestricted
        ? <WorkspaceDataState kind="permission" compact message={overviewMessages.overviewActivityRestricted} />
        : activityEmpty
          ? <WorkspaceDataState kind="empty" compact message={overviewMessages.overviewEmpty.replace('{section}', overviewMessages.overviewActivity.toLowerCase())} />
          : activityUnavailable
            ? <WorkspaceDataState kind="unavailable" compact message={overviewMessages.overviewSourceUnavailable.replace('{section}', overviewMessages.overviewActivity.toLowerCase())} onRetry={retryOverview} />
            : activityItems.length
              ? <ul className="record-list">{activityItems.map((item, index) => <li key={`${item.timestamp}-${index}`}>
                <span className="record-list__main">{item.action}</span><span>{item.outcome}</span><DateTime value={item.timestamp} />
              </li>)}</ul>
              : <WorkspaceDataState kind="unavailable" compact message={overviewMessages.overviewSourceUnavailable.replace('{section}', overviewMessages.overviewActivity.toLowerCase())} onRetry={retryOverview} />}
    </section>
  </div>;
}

function LoadedConnectionHealth({ loadConnectionHealth, actions }: { loadConnectionHealth: ConnectionHealthLoader; actions?: ConnectionHealthActions }) {
  const [health, setHealth] = useState<ConnectionHealth | null>(null);
  const [failed, setFailed] = useState(false);
  const [actionPending, setActionPending] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [consentUrl, setConsentUrl] = useState<string | null>(null);
  useEffect(() => { loadConnectionHealth().then(setHealth).catch(() => setFailed(true)); }, [loadConnectionHealth]);
  if (failed) return <section className="content-panel"><p role="alert">{messages.connectionUnavailable}</p></section>;
  if (!health) return <section className="content-panel"><p>{messages.connectionLoading}</p></section>;
  const runCheck = actions ? async () => { setActionPending(true); setActionError(null); try { setHealth(await actions.check()); } catch { setActionError(messages.connectionActionFailed); } finally { setActionPending(false); } } : undefined;
  const startConsent = actions ? async () => { setActionPending(true); setActionError(null); try { setConsentUrl((await actions.startConsent()).authorizationUrl); } catch { setActionError(messages.connectionActionFailed); } finally { setActionPending(false); } } : undefined;
  return <ConnectionStatusCard state={health.status} lastVerifiedAt={health.lastVerifiedAt} onCheck={runCheck} onConsent={startConsent} consentUrl={consentUrl} actionPending={actionPending} actionError={actionError} />;
}
