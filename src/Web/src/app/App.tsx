import React, { useCallback, useEffect, useState } from 'react';
import { useApi } from '../auth/useApi';
import { capabilityDecisionFor, matchRoute } from './routes';
import { AppShell } from '../components/AppShell';
import { AppThemeProvider, ThemeProvider, type ThemePreferenceStore } from '../components/ThemeToggle';
import type { AppSession } from '../components/TenantContextHeader';
import { PermissionState } from '../components/PermissionState';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import { useCapabilities, type CapabilityLoader } from '../capabilities/useCapabilities';
import type { ConnectionHealthLoader } from '../features/overview/OverviewPage';
import { messages } from './messages';

export type SessionLoader = () => Promise<AppSession>;

export function App({ loadCapabilities, loadSession, loadConnectionHealth, themePreferenceStore }: { loadCapabilities?: CapabilityLoader; loadSession?: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader; themePreferenceStore?: ThemePreferenceStore }) {
  if (loadCapabilities && loadSession) {
    return (
      <ThemeProvider preferenceStore={themePreferenceStore}>
        <AppExperience loadCapabilities={loadCapabilities} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} />
      </ThemeProvider>
    );
  }

  return <AuthenticatedApp loadConnectionHealth={loadConnectionHealth} />;
}

function AuthenticatedApp({ loadConnectionHealth }: { loadConnectionHealth?: ConnectionHealthLoader }) {
  const api = useApi();
  const loadSession = useCallback(async () => {
    const response = await api('/api/session');
    if (!response.ok) {
      throw new Error('session_unavailable');
    }
    return await response.json() as AppSession;
  }, [api]);

  return (
    <AppThemeProvider>
      <AppExperience loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} />
    </AppThemeProvider>
  );
}

function AppExperience({ loadCapabilities, loadSession, loadConnectionHealth }: { loadCapabilities?: CapabilityLoader; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader }) {
  if (loadCapabilities) {
    return <InjectedAppExperience loadCapabilities={loadCapabilities} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} />;
  }

  return <ApiAppExperience loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} />;
}

function ApiAppExperience({ loadSession, loadConnectionHealth }: { loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader }) {
  const { capabilities, loading: capabilitiesLoading, error: capabilitiesError } = useCapabilities();
  return <LoadedAppExperience capabilities={capabilities} capabilitiesLoading={capabilitiesLoading} capabilitiesError={capabilitiesError} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} />;
}

function InjectedAppExperience({ loadCapabilities, loadSession, loadConnectionHealth }: { loadCapabilities: CapabilityLoader; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader }) {
  const { capabilities, loading: capabilitiesLoading, error: capabilitiesError } = useInjectedCapabilities(loadCapabilities);
  return <LoadedAppExperience capabilities={capabilities} capabilitiesLoading={capabilitiesLoading} capabilitiesError={capabilitiesError} loadSession={loadSession} loadConnectionHealth={loadConnectionHealth} />;
}

function LoadedAppExperience({ capabilities, capabilitiesLoading, capabilitiesError, loadSession, loadConnectionHealth }: { capabilities: CapabilitySnapshot | null; capabilitiesLoading: boolean; capabilitiesError: Error | null; loadSession: SessionLoader; loadConnectionHealth?: ConnectionHealthLoader }) {
  const { session, loading: sessionLoading, error: sessionError } = useSession(loadSession);
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
    window.history.pushState(null, '', nextPath);
    setPath(nextPath);
  }, []);

  if (capabilitiesLoading || sessionLoading) {
    return <main className="loading-state" role="status">{messages.shellLoading}</main>;
  }

  if (capabilitiesError || sessionError || !capabilities || !session) {
    return <main className="permission-panel" role="alert">{messages.shellUnavailable}</main>;
  }

  const route = matchRoute(path);
  const decision = capabilityDecisionFor(route, capabilities.capabilities);
  const routeContent = decision && decision.state !== 'allowed'
    ? <RoutePermissionState decision={decision} />
    : route.render({ loadConnectionHealth, capabilities: capabilities.capabilities, navigate });

  return (
    <AppShell capabilities={capabilities} currentPath={path} session={session} onNavigate={navigate}>
      {routeContent}
    </AppShell>
  );
}

function useInjectedCapabilities(loadCapabilities: CapabilityLoader) {
  const [capabilities, setCapabilities] = useState<CapabilitySnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    loadCapabilities()
      .then((snapshot) => {
        if (!cancelled) {
          setCapabilities(snapshot);
        }
      })
      .catch((loadError: unknown) => {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError : new Error('capabilities_unavailable'));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });
    return () => { cancelled = true; };
  }, [loadCapabilities]);

  return { capabilities, loading, error };
}

function RoutePermissionState({ decision }: { decision: NonNullable<ReturnType<typeof capabilityDecisionFor>> }) {
  if (decision.state === 'hidden') {
    return (
      <section className="permission-panel" role="status" data-capability={decision.capability} data-capability-state={decision.state}>
        <h1>{messages.permissionRequiredTitle}</h1>
        <p>{messages.permissionRequiredBody}</p>
      </section>
    );
  }

  return (
    <section className="permission-panel" aria-labelledby="permission-title">
      <h1 id="permission-title">{messages.permissionRequiredTitle}</h1>
      <PermissionState decision={decision}>
        <span>{messages.permissionRequiredBody}</span>
      </PermissionState>
    </section>
  );
}

function useSession(loadSession: SessionLoader) {
  const [session, setSession] = useState<AppSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    loadSession()
      .then((loadedSession) => {
        if (!cancelled) {
          setSession(loadedSession);
        }
      })
      .catch((loadError: unknown) => {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError : new Error('session_unavailable'));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });
    return () => { cancelled = true; };
  }, [loadSession]);

  return { session, loading, error };
}
