import React, { createContext, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useState, type ReactNode } from 'react';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import type { AppSession } from '../components/TenantContextHeader';
import type { ConnectionHealth, ConnectionHealthLoader } from '../features/overview/OverviewPage';
import { deriveCapabilityIssues, deriveConnectionIssue, reportedIssueKey, sanitizeReportedIssue, type WorkspaceIssue, type WorkspaceIssueReport } from './workspaceIssues';

type Reporter = { report: (issue: WorkspaceIssueReport) => void; clear: (key: string) => void };
type Notifications = { issues: WorkspaceIssue[]; refresh: () => Promise<void> };
const noOpReporter: Reporter = { report: () => {}, clear: () => {} };
const ReporterContext = createContext<Reporter>(noOpReporter);
const NotificationsContext = createContext<Notifications>({ issues: [], refresh: async () => {} });

export function useWorkspaceIssueReporter() { return useContext(ReporterContext); }
export function useWorkspaceNotifications() { return useContext(NotificationsContext); }

export function WorkspaceNotificationsProvider({ session, sessionScope = 0, capabilities, capabilitiesError, onRefresh, loadConnectionHealth, children }: { session: AppSession; sessionScope?: number; capabilities: CapabilitySnapshot | null; capabilitiesError: Error | null; onRefresh: () => Promise<void>; loadConnectionHealth?: ConnectionHealthLoader; children: ReactNode }) {
  const [reported, setReported] = useState<{ workspaceId: string; sessionScope: number; issues: Record<string, WorkspaceIssue> }>({ workspaceId: session.workspace.id, sessionScope, issues: {} });
  const [health, setHealth] = useState<ConnectionHealth | null>(null);
  const [healthFailed, setHealthFailed] = useState(false);
  const workspaceId = session.workspace.id;
  const reloadHealth = useCallback(async () => {
    if (!loadConnectionHealth) return;
    try { setHealth(await loadConnectionHealth()); setHealthFailed(false); }
    catch { setHealth(null); setHealthFailed(true); }
  }, [loadConnectionHealth]);
  useEffect(() => { setHealth(null); setHealthFailed(false); void reloadHealth(); }, [workspaceId, sessionScope, reloadHealth]);
  useEffect(() => { setReported(current => current.workspaceId === workspaceId && current.sessionScope === sessionScope ? current : { workspaceId, sessionScope, issues: {} }); }, [workspaceId, sessionScope]);
  const reporter = useMemo<Reporter>(() => ({
    report: issue => { const safe = sanitizeReportedIssue(issue, session); if (safe) setReported(current => current.workspaceId === workspaceId && current.sessionScope === sessionScope ? { ...current, issues: { ...current.issues, [safe.key]: safe } } : current); },
    clear: key => setReported(current => { if (current.workspaceId !== workspaceId || current.sessionScope !== sessionScope) return current; const issues = { ...current.issues }; delete issues[reportedIssueKey(key)]; return { ...current, issues }; }),
  }), [workspaceId, sessionScope, session]);
  useEffect(() => {
    if (capabilities?.sourceState !== 'graph_authoritative' || capabilities.workspaceId !== workspaceId) return;
    const recovered = capabilities.capabilities.filter(decision => decision.state === 'allowed' || decision.state === 'read_only').map(decision => reportedIssueKey(`route:${decision.capability.replace(/[^a-z0-9_-]/gi, ':')}`));
    if (!recovered.length) return;
    setReported(current => {
      if (current.workspaceId !== workspaceId || current.sessionScope !== sessionScope || !recovered.some(key => key in current.issues)) return current;
      const issues = { ...current.issues };
      recovered.forEach(key => { delete issues[key]; });
      return { ...current, issues };
    });
  }, [capabilities, workspaceId, sessionScope]);
  const refresh = useCallback(async () => { await Promise.all([onRefresh(), reloadHealth()]); }, [onRefresh, reloadHealth]);
  useLayoutEffect(() => {
    const onReturn = () => { void refresh(); };
    window.addEventListener('focus', onReturn);
    return () => window.removeEventListener('focus', onReturn);
  }, [refresh]);
  const issues = useMemo(() => [
    ...deriveCapabilityIssues(capabilities, session),
    ...deriveConnectionIssue(health, session),
    ...(healthFailed ? [{ key: 'connection:unavailable', area: 'Connection', kind: 'service' as const, severity: 'warning' as const, title: 'Connection check unavailable', detail: 'Try checking the connection again.', audience: 'affected_user' as const, lastCheckedAt: new Date().toISOString() }] : []),
    ...(capabilitiesError ? [{ key: 'capabilities:unavailable', area: 'Your access', kind: 'service' as const, severity: 'warning' as const, title: 'Permission verification unavailable', detail: 'Try checking your access again.', audience: 'affected_user' as const, lastCheckedAt: new Date().toISOString() }] : []),
    ...Object.values(reported.workspaceId === workspaceId && reported.sessionScope === sessionScope ? reported.issues : {}),
  ], [capabilities, session, health, healthFailed, capabilitiesError, reported, workspaceId, sessionScope]);
  return <ReporterContext.Provider value={reporter}><NotificationsContext.Provider value={{ issues, refresh }}>{children}</NotificationsContext.Provider></ReporterContext.Provider>;
}
