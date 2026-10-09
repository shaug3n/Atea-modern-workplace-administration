import { ModuleUnavailable } from '../components/ModuleUnavailable';
import React, { useCallback, useEffect, useState } from 'react';
import { useApi } from '../auth/useApi';
import { useAuth } from '../auth/AuthProvider';
import { capabilityDecisionFor, isInvitationPath, matchRoute, type AppRoute } from './routes';
import { AppShell } from '../components/AppShell';
import { AppThemeProvider, ThemeProvider, type ThemePreferenceStore } from '../components/ThemeToggle';
import type { AppSession } from '../components/TenantContextHeader';
import { WorkspaceDataState } from '../components/WorkspaceDataState';
import { WorkspacePageHeader } from '../components/WorkspacePageHeader';
import { useWorkspaceIssueReporter } from '../notifications/WorkspaceNotifications';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import { useCapabilities, type CapabilityLoader } from '../capabilities/useCapabilities';
import { InvitationRedemptionPage } from '../features/invitations/InvitationRedemptionPage';
import { DevicesPage } from '../features/devices/DevicesPage';
import type { ConnectionHealthLoader } from '../features/overview/OverviewPage';
import { messages } from './messages';
import { WorkspaceNotificationsProvider } from '../notifications/WorkspaceNotifications';
import { fetchAuthenticationCampaigns } from '../features/authentication-campaigns/authenticationCampaignsApi';
import type { AuthenticationCampaignsLoader } from '../features/authentication-campaigns/AuthenticationCampaignsPage';

export type SessionLoader = () => Promise<AppSession>;

export function App({ loadCapabilities, loadSession, loadConnectionHealth, loadAuthenticationCampaigns, themePreferenceStore, signInAction }: { loadCapabilities?: CapabilityLoader; loadSession?: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; loadAuthenticationCampaigns?: AuthenticationCampaignsLoader; themePreferenceStore?: ThemePreferenceStore; signInAction?: () => Promise<void> }) {
  if (loadCapabilities && loadSession) {
    return <ThemeProvider preferenceStore={themePreferenceStore}><AppExperience loadCapabilities={loadCapabilities} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} loadAuthenticationCampaigns={loadAuthenticationCampaigns} signInAction={signInAction} /></ThemeProvider>;
  }
  return <AuthenticatedApp loadConnectionHealth={loadConnectionHealth} />;
}

function AuthenticatedApp({ loadConnectionHealth }: { loadConnectionHealth?: ConnectionHealthLoader }) {
  const api = useApi();
  const { signIn, switchAccount } = useAuth();
  const loadSession = useCallback(async () => {
    const response = await api('/api/session');
    if (!response.ok) {
      const body = await safeProblem(response);
      throw Object.assign(new Error(body.title ?? 'session_unavailable'), { status: response.status, correlationId: body.correlationId });
    }
    return await response.json() as AppSession;
  }, [api]);
  const loadHealth = useCallback(async () => {
    const response = await api('/api/workspaces/current/connection-health');
    if (!response.ok) throw new Error('connection_health_unavailable');
    return await response.json() as Awaited<ReturnType<ConnectionHealthLoader>>;
  }, [api]);
  const loadAuthenticationCampaigns = useCallback(() => fetchAuthenticationCampaigns(api), [api]);
  return <AppThemeProvider><AppExperience loadSession={loadSession} loadConnectionHealth={loadConnectionHealth ?? loadHealth} loadAuthenticationCampaigns={loadAuthenticationCampaigns} signInAction={signIn} switchAccountAction={switchAccount} /></AppThemeProvider>;
}

async function safeProblem(response: Response): Promise<{ title?: string; correlationId?: string }> {
  try { return await response.json() as { title?: string; correlationId?: string }; } catch { return {}; }
}

function AppExperience({ loadCapabilities, loadSession, loadConnectionHealth, loadAuthenticationCampaigns, signInAction, switchAccountAction }: { loadCapabilities?: CapabilityLoader; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; loadAuthenticationCampaigns?: AuthenticationCampaignsLoader; signInAction?: () => Promise<void>; switchAccountAction?: () => Promise<void> }) {
  const [path, setPath] = useState(() => window.location.pathname + window.location.hash);
  useEffect(() => {
    const onPopState = () => setPath(window.location.pathname + window.location.hash);
    window.addEventListener('popstate', onPopState);
    if (window.location.pathname === '/') {
      window.history.replaceState(null, '', '/overview');
      setPath('/overview');
    }
    return () => window.removeEventListener('popstate', onPopState);
  }, []);
  const navigate = useCallback((nextPath: string) => {
    if (window.location.pathname + window.location.hash !== nextPath) window.history.pushState(null, '', nextPath);
    setPath(nextPath);
  }, []);

  if (isInvitationPath(path)) return <InvitationRedemptionPage nonce={path.slice('/invitations/'.length)} />;
  return <WorkspaceExperience path={path} navigate={navigate} loadCapabilities={loadCapabilities} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} loadAuthenticationCampaigns={loadAuthenticationCampaigns} signInAction={signInAction} switchAccountAction={switchAccountAction} />;
}

function WorkspaceExperience({ path, navigate, loadCapabilities, loadSession, loadConnectionHealth, loadAuthenticationCampaigns, signInAction, switchAccountAction }: { path: string; navigate: (path: string) => void; loadCapabilities?: CapabilityLoader; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; loadAuthenticationCampaigns?: AuthenticationCampaignsLoader; signInAction?: () => Promise<void>; switchAccountAction?: () => Promise<void> }) {
  if (loadCapabilities) return <InjectedWorkspaceExperience path={path} navigate={navigate} loadCapabilities={loadCapabilities} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} loadAuthenticationCampaigns={loadAuthenticationCampaigns} signInAction={signInAction} switchAccountAction={switchAccountAction} />;
  return <ApiWorkspaceExperience path={path} navigate={navigate} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} loadAuthenticationCampaigns={loadAuthenticationCampaigns} signInAction={signInAction} switchAccountAction={switchAccountAction} />;
}

function ApiWorkspaceExperience(props: { path: string; navigate: (path: string) => void; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; loadAuthenticationCampaigns?: AuthenticationCampaignsLoader; signInAction?: () => Promise<void>; switchAccountAction?: () => Promise<void> }) {
  const state = useCapabilities();
  return <LoadedWorkspaceExperience {...props} capabilities={state.capabilities} capabilitiesLoading={state.loading} capabilitiesError={state.error} refreshCapabilities={state.refresh} />;
}

function InjectedWorkspaceExperience(props: { path: string; navigate: (path: string) => void; loadCapabilities: CapabilityLoader; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; loadAuthenticationCampaigns?: AuthenticationCampaignsLoader; signInAction?: () => Promise<void>; switchAccountAction?: () => Promise<void> }) {
  const state = useInjectedCapabilities(props.loadCapabilities);
  return <LoadedWorkspaceExperience {...props} capabilities={state.capabilities} capabilitiesLoading={state.loading} capabilitiesError={state.error} refreshCapabilities={state.refresh} />;
}

function useInjectedCapabilities(loadCapabilities: CapabilityLoader) {
  const [capabilities, setCapabilities] = useState<CapabilitySnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const [attempt, setAttempt] = useState(0);
  const refresh = useCallback(async () => { setAttempt(value => value + 1); }, []);
  useEffect(() => {
    let cancelled = false;
    setLoading(true); setError(null);
    loadCapabilities().then(snapshot => { if (!cancelled) setCapabilities(snapshot); }).catch((reason: unknown) => {
      if (!cancelled) setError(reason instanceof Error ? reason : new Error('capabilities_unavailable'));
    }).finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [loadCapabilities, attempt]);
  return { capabilities, loading, error, refresh };
}

function LoadedWorkspaceExperience({ path, navigate, capabilities, capabilitiesLoading, capabilitiesError, refreshCapabilities, loadSession, loadConnectionHealth, loadAuthenticationCampaigns, signInAction, switchAccountAction }: { path: string; navigate: (path: string) => void; capabilities: CapabilitySnapshot | null; capabilitiesLoading: boolean; capabilitiesError: Error | null; refreshCapabilities: () => Promise<void>; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; loadAuthenticationCampaigns?: AuthenticationCampaignsLoader; signInAction?: () => Promise<void>; switchAccountAction?: () => Promise<void> }) {
  const { session, sessionRevision, loading: sessionLoading, error: sessionError, retry } = useSession(loadSession);
  const legacyDestinations: Record<string, string> = {
    '/onboarding': '/settings#connection', '/settings/setup': '/settings#connection',
    '/workspace-settings': '/settings#general', '/settings/general': '/settings#general',
    '/settings/modules': '/settings#modules', '/workspace-access': '/settings#access', '/settings/access': '/settings#access',
  };
  const legacyPath = path.split('#')[0].replace(/\/+$/, '') || '/';
  const legacyRedirect = legacyDestinations[legacyPath] ?? null;
  useEffect(() => {
    if (legacyRedirect) navigate(legacyRedirect);
  }, [legacyRedirect, navigate]);

  if (sessionLoading) return <main className="loading-state" role="status">{messages.shellLoading}</main>;
  if (sessionError || !session) return <SessionFailure error={sessionError ?? new Error('session_unavailable')} onRetry={retry} onSignIn={signInAction} onSwitchAccount={switchAccountAction} />;

  const route = matchRoute(path.split('#')[0]);
  if (legacyRedirect) return <main className="loading-state" role="status">Redirecting…</main>;
  const canManageMembers = session.workspaceAccess?.canManageMembers === true;
  const canManageSettings = session.workspaceAccess?.canManageSettings === true;
  const canManageModules = session.workspaceAccess?.canManageModules === true;
  const hasWorkspaceAccess = route.workspaceAccess === 'members' ? canManageMembers : route.workspaceAccess === 'modules' ? canManageModules : route.workspaceAccess === 'settings' ? canManageSettings : route.workspaceAccess === 'any' ? canManageMembers || canManageSettings || canManageModules : true;
  const availableModules = session.workspace.moduleAccess ?? session.workspace.enabledModules;
  const isDeviceSetupAdmin = route.module === 'devices' && canManageSettings;
  const hasAssignedModuleAccess = !route.module || !availableModules || ((!session.workspace.enabledModules || session.workspace.enabledModules.includes(route.module)) && availableModules.includes(route.module));
  const hasModuleAccess = route.module === 'license-hygiene'
    ? session.workspace.enabledModules?.includes('license-hygiene') === true && session.workspace.moduleAccess?.includes('license-hygiene') === true
    : hasAssignedModuleAccess || isDeviceSetupAdmin;
  const unavailableSnapshot = capabilities ?? { workspaceId: session.workspace.id, evaluatedAt: new Date().toISOString(), sourceState: 'unavailable', capabilities: [] } satisfies CapabilitySnapshot;
  let routeContent: React.ReactNode;
  if (!hasWorkspaceAccess) {
    routeContent = <ModuleUnavailable kind="no-access" moduleName={route.label} canManageModules={canManageModules} onNavigate={navigate} message={messages.workspaceAccessDeniedBody} />;
  } else if (!hasModuleAccess) {
    routeContent = <ModuleUnavailable kind={route.module && session.workspace.enabledModules && !session.workspace.enabledModules.includes(route.module) ? 'module-off' : 'no-access'} moduleName={route.label} canManageModules={canManageModules} onNavigate={navigate} />;
  } else if (isDeviceSetupAdmin && !hasAssignedModuleAccess) {
    routeContent = <DevicesPage moduleAssigned={false} moduleEnabled={session.workspace.enabledModules?.includes('devices') ?? true} />;
  } else if (route.capability && capabilitiesLoading) {
    routeContent = <GraphRouteState route={route} state="loading" />;
  } else if (route.capability && (capabilitiesError || !capabilities || capabilities.sourceState !== 'graph_authoritative' || capabilities.workspaceId !== session.workspace.id)) {
    routeContent = route.module === 'authentication-campaigns'
      ? route.render({ loadConnectionHealth, loadAuthenticationCampaigns, capabilities: unavailableSnapshot.capabilities, navigate, session, authorizationUnavailable: true, onAuthorizationRetry: refreshCapabilities })
      : <GraphRouteState route={route} state="unavailable" onRetry={refreshCapabilities} reportCause={capabilitiesError ? undefined : 'service'} session={session} navigate={navigate} loadConnectionHealth={loadConnectionHealth} />;
  } else {
    const decision = capabilityDecisionFor(route, unavailableSnapshot.capabilities);
    routeContent = decision && decision.state !== 'allowed' && decision.state !== 'read_only'
      ? route.module === 'authentication-campaigns'
        ? route.render({ loadConnectionHealth, loadAuthenticationCampaigns, capabilities: unavailableSnapshot.capabilities, navigate, session, authorizationUnavailable: true, onAuthorizationRetry: refreshCapabilities })
        : <GraphRouteState route={route} state="unavailable" onRetry={refreshCapabilities} reportCause={decision.state === 'hidden' || decision.state === 'disabled' ? 'access' : undefined} session={session} navigate={navigate} loadConnectionHealth={loadConnectionHealth} />
      : route.render({ loadConnectionHealth, loadAuthenticationCampaigns, capabilities: unavailableSnapshot.capabilities, navigate, session, onRefreshAccess: refreshCapabilities });
  }

  return <WorkspaceNotificationsProvider key={`${session.workspace.id}:${sessionRevision}`} session={session} sessionScope={sessionRevision} capabilities={capabilities} capabilitiesError={capabilitiesError} onRefresh={refreshCapabilities} loadConnectionHealth={loadConnectionHealth}><AppShell capabilities={capabilities} currentPath={path.split('#')[0]} session={session} onNavigate={navigate} accessState={{ loading: capabilitiesLoading, error: capabilitiesError !== null, refresh: refreshCapabilities }}>{routeContent}</AppShell></WorkspaceNotificationsProvider>;
}

function SessionFailure({ error, onRetry, onSignIn, onSwitchAccount }: { error: Error; onRetry: () => void; onSignIn?: () => Promise<void>; onSwitchAccount?: () => Promise<void> }) {
  const status = (error as Error & { status?: number }).status;
  const correlationId = (error as Error & { correlationId?: string }).correlationId;
  const isSignIn = status === 401;
  const isMembership = status === 403;
  const title = isSignIn ? messages.sessionSignInTitle : isMembership ? messages.sessionMembershipTitle : messages.sessionUnavailableTitle;
  const body = isSignIn ? messages.sessionSignInBody : isMembership ? messages.sessionMembershipBody : messages.sessionUnavailableBody;
  return <main className="permission-panel" role="alert"><h1>{title}</h1><p>{body}</p>{correlationId && <p>{messages.correlationIdLabel}: <code>{correlationId}</code></p>}{isSignIn ? <button type="button" onClick={() => onSignIn ? void onSignIn() : window.location.assign('/')}>{messages.sessionSignInAction}</button> : isMembership ? <><button type="button" onClick={() => onSwitchAccount ? void onSwitchAccount() : onRetry()}>{messages.authSwitchAccount}</button><button type="button" onClick={onRetry}>{messages.retry}</button></> : <button type="button" onClick={onRetry}>{messages.retry}</button>}</main>;
}

function GraphRouteState({ route, state, onRetry, reportCause, session, navigate, loadConnectionHealth }: { route: AppRoute; state: 'loading' | 'unavailable'; onRetry?: () => Promise<void>; reportCause?: 'access' | 'service'; session?: AppSession; navigate?: (path: string) => void; loadConnectionHealth?: ConnectionHealthLoader }) {
  const reporter = useWorkspaceIssueReporter();
  useEffect(() => {
    if (!reportCause) return;
    const key = `route:${(route.capability ?? route.path).replace(/[^a-z0-9_-]/gi, ':')}`;
    reporter.report({ key, area: route.module ?? 'activity', kind: reportCause, severity: 'warning', title: 'Data unavailable', detail: 'Review your access or try again.' });
  }, [reportCause, route, reporter]);
  if (state === 'unavailable' && session && ['/users', '/devices', '/audit', '/activity'].includes(route.path)) {
    return <>{route.render({ capabilities: [], session, navigate, loadConnectionHealth, authorizationUnavailable: true, onAuthorizationRetry: onRetry })}</>;
  }
  return <section className="content-panel workspace-graph-route">
    <WorkspacePageHeader title={route.pageTitle ?? route.label} />
    <WorkspaceDataState state={state} message={state === 'loading' ? 'Checking access…' : 'Data cannot be shown right now. Check Notifications for details.'} onRetry={onRetry ? () => void onRetry() : undefined} />
  </section>;
}

function useSession(loadSession: SessionLoader) {
  const [session, setSession] = useState<AppSession | null>(null);
  const [sessionRevision, setSessionRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const [attempt, setAttempt] = useState(0);
  const retry = useCallback(() => setAttempt(value => value + 1), []);
  useEffect(() => {
    let cancelled = false;
    setLoading(true); setError(null);
    loadSession().then(value => { if (!cancelled) { setSession(value); setSessionRevision(revision => revision + 1); } }).catch((reason: unknown) => {
      if (!cancelled) setError(reason instanceof Error ? reason : new Error('session_unavailable'));
    }).finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [loadSession, attempt]);
  return { session, sessionRevision, loading, error, retry };
}
