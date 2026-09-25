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

export type SessionLoader = () => Promise<AppSession>;

export function App({ loadCapabilities, loadSession, loadConnectionHealth, themePreferenceStore, signInAction }: { loadCapabilities?: CapabilityLoader; loadSession?: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; themePreferenceStore?: ThemePreferenceStore; signInAction?: () => Promise<void> }) {
  if (loadCapabilities && loadSession) {
    return <ThemeProvider preferenceStore={themePreferenceStore}><AppExperience loadCapabilities={loadCapabilities} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} signInAction={signInAction} /></ThemeProvider>;
  }
  return <AuthenticatedApp loadConnectionHealth={loadConnectionHealth} />;
}

function AuthenticatedApp({ loadConnectionHealth }: { loadConnectionHealth?: ConnectionHealthLoader }) {
  const api = useApi();
  const { signIn } = useAuth();
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
  return <AppThemeProvider><AppExperience loadSession={loadSession} loadConnectionHealth={loadConnectionHealth ?? loadHealth} signInAction={signIn} /></AppThemeProvider>;
}

async function safeProblem(response: Response): Promise<{ title?: string; correlationId?: string }> {
  try { return await response.json() as { title?: string; correlationId?: string }; } catch { return {}; }
}

function AppExperience({ loadCapabilities, loadSession, loadConnectionHealth, signInAction }: { loadCapabilities?: CapabilityLoader; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; signInAction?: () => Promise<void> }) {
  const [path, setPath] = useState(() => window.location.pathname);
  useEffect(() => {
    const onPopState = () => setPath(window.location.pathname);
    window.addEventListener('popstate', onPopState);
    if (window.location.pathname === '/') {
      window.history.replaceState(null, '', '/overview');
      setPath('/overview');
    }
    return () => window.removeEventListener('popstate', onPopState);
  }, []);
  const navigate = useCallback((nextPath: string) => {
    if (window.location.pathname !== nextPath) window.history.pushState(null, '', nextPath);
    setPath(nextPath);
  }, []);

  if (isInvitationPath(path)) return <InvitationRedemptionPage nonce={path.slice('/invitations/'.length)} />;
  return <WorkspaceExperience path={path} navigate={navigate} loadCapabilities={loadCapabilities} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} signInAction={signInAction} />;
}

function WorkspaceExperience({ path, navigate, loadCapabilities, loadSession, loadConnectionHealth, signInAction }: { path: string; navigate: (path: string) => void; loadCapabilities?: CapabilityLoader; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; signInAction?: () => Promise<void> }) {
  if (loadCapabilities) return <InjectedWorkspaceExperience path={path} navigate={navigate} loadCapabilities={loadCapabilities} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} signInAction={signInAction} />;
  return <ApiWorkspaceExperience path={path} navigate={navigate} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} signInAction={signInAction} />;
}

function ApiWorkspaceExperience(props: { path: string; navigate: (path: string) => void; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; signInAction?: () => Promise<void> }) {
  const state = useCapabilities();
  return <LoadedWorkspaceExperience {...props} capabilities={state.capabilities} capabilitiesLoading={state.loading} capabilitiesError={state.error} refreshCapabilities={state.refresh} />;
}

function InjectedWorkspaceExperience(props: { path: string; navigate: (path: string) => void; loadCapabilities: CapabilityLoader; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; signInAction?: () => Promise<void> }) {
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

function LoadedWorkspaceExperience({ path, navigate, capabilities, capabilitiesLoading, capabilitiesError, refreshCapabilities, loadSession, loadConnectionHealth, signInAction }: { path: string; navigate: (path: string) => void; capabilities: CapabilitySnapshot | null; capabilitiesLoading: boolean; capabilitiesError: Error | null; refreshCapabilities: () => Promise<void>; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; signInAction?: () => Promise<void> }) {
  const { session, sessionRevision, loading: sessionLoading, error: sessionError, retry } = useSession(loadSession);
  const hasModuleContract = Array.isArray(session?.workspace.enabledModules) || Array.isArray(session?.workspace.moduleAccess);
  const legacyRedirect = hasModuleContract && (path === '/onboarding' ? '/settings/setup' : path === '/workspace-settings' ? '/settings' : path === '/workspace-access' ? '/settings/access' : null);
  useEffect(() => {
    if (legacyRedirect) navigate(legacyRedirect);
  }, [legacyRedirect, navigate]);

  if (sessionLoading) return <main className="loading-state" role="status">{messages.shellLoading}</main>;
  if (sessionError || !session) return <SessionFailure error={sessionError ?? new Error('session_unavailable')} onRetry={retry} onSignIn={signInAction} />;

  const route = matchRoute(path);
  if (legacyRedirect) return <main className="loading-state" role="status">Redirecting…</main>;
  const canManageMembers = session.workspaceAccess?.canManageMembers === true;
  const canManageSettings = session.workspaceAccess?.canManageSettings === true;
  const canManageModules = session.workspaceAccess?.canManageModules === true;
  const hasWorkspaceAccess = route.workspaceAccess === 'members' ? canManageMembers : route.workspaceAccess === 'modules' ? canManageModules : route.workspaceAccess === 'settings' ? canManageSettings : true;
  const availableModules = session.workspace.moduleAccess ?? session.workspace.enabledModules;
  const isDeviceSetupAdmin = route.module === 'devices' && canManageSettings;
  const hasAssignedModuleAccess = !route.module || !availableModules || ((!session.workspace.enabledModules || session.workspace.enabledModules.includes(route.module)) && availableModules.includes(route.module));
  const hasModuleAccess = hasAssignedModuleAccess || isDeviceSetupAdmin;
  const unavailableSnapshot = capabilities ?? { workspaceId: session.workspace.id, evaluatedAt: new Date().toISOString(), sourceState: 'unavailable', capabilities: [] } satisfies CapabilitySnapshot;
  let routeContent: React.ReactNode;
  if (!hasWorkspaceAccess) {
    routeContent = <section className="permission-panel" role="status"><h1>{messages.workspaceAccessDeniedTitle}</h1><p>{messages.workspaceAccessDeniedBody}</p></section>;
  } else if (!hasModuleAccess) {
    routeContent = <section className="permission-panel" role="status"><h1>{messages.moduleDisabledTitle}</h1><p>{messages.moduleDisabledBody}</p></section>;
  } else if (isDeviceSetupAdmin && !hasAssignedModuleAccess) {
    routeContent = <DevicesPage moduleAssigned={false} moduleEnabled={session.workspace.enabledModules?.includes('devices') ?? true} />;
  } else if (route.capability && capabilitiesLoading) {
    routeContent = <GraphRouteState route={route} state="loading" />;
  } else if (route.capability && (capabilitiesError || !capabilities || capabilities.sourceState !== 'graph_authoritative' || capabilities.workspaceId !== session.workspace.id)) {
    routeContent = <GraphRouteState route={route} state="unavailable" onRetry={refreshCapabilities} reportCause={capabilitiesError ? undefined : 'service'} />;
  } else {
    const decision = capabilityDecisionFor(route, unavailableSnapshot.capabilities);
    routeContent = decision && decision.state !== 'allowed' && decision.state !== 'read_only'
      ? <GraphRouteState route={route} state="unavailable" onRetry={refreshCapabilities} reportCause={decision.state === 'hidden' || decision.state === 'disabled' ? 'access' : undefined} />
      : route.render({ loadConnectionHealth, capabilities: unavailableSnapshot.capabilities, navigate, session });
  }

  return <WorkspaceNotificationsProvider key={`${session.workspace.id}:${sessionRevision}`} session={session} sessionScope={sessionRevision} capabilities={capabilities} capabilitiesError={capabilitiesError} onRefresh={refreshCapabilities} loadConnectionHealth={loadConnectionHealth}><AppShell capabilities={capabilities} currentPath={path} session={session} onNavigate={navigate}>{routeContent}</AppShell></WorkspaceNotificationsProvider>;
}

function SessionFailure({ error, onRetry, onSignIn }: { error: Error; onRetry: () => void; onSignIn?: () => Promise<void> }) {
  const status = (error as Error & { status?: number }).status;
  const correlationId = (error as Error & { correlationId?: string }).correlationId;
  const isSignIn = status === 401;
  const isMembership = status === 403;
  const title = isSignIn ? messages.sessionSignInTitle : isMembership ? messages.sessionMembershipTitle : messages.sessionUnavailableTitle;
  const body = isSignIn ? messages.sessionSignInBody : isMembership ? messages.sessionMembershipBody : messages.sessionUnavailableBody;
  return <main className="permission-panel" role="alert"><h1>{title}</h1><p>{body}</p>{correlationId && <p>{messages.correlationIdLabel}: <code>{correlationId}</code></p>}{isSignIn ? <button type="button" onClick={() => onSignIn ? void onSignIn() : window.location.assign('/')}>{messages.sessionSignInAction}</button> : <button type="button" onClick={onRetry}>{messages.retry}</button>}</main>;
}

function GraphRouteState({ route, state, onRetry, reportCause }: { route: AppRoute; state: 'loading' | 'unavailable'; onRetry?: () => Promise<void>; reportCause?: 'access' | 'service' }) {
  const reporter = useWorkspaceIssueReporter();
  useEffect(() => {
    if (!reportCause) return;
    const key = `route:${(route.capability ?? route.path).replace(/[^a-z0-9_-]/gi, ':')}`;
    reporter.report({ key, area: route.module ?? 'activity', kind: reportCause, severity: 'warning', title: 'Data unavailable', detail: 'Review your access or try again.' });
    return () => reporter.clear(key);
  }, [reportCause, route, reporter]);
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
