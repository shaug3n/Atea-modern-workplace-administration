import React, { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import type { AppSession } from '../components/TenantContextHeader';
import type { ConnectionHealth, ConnectionHealthLoader } from '../features/overview/OverviewPage';
import { deriveCapabilityIssues, deriveConnectionIssue, reportedIssueKey, sanitizeReportedIssue, type WorkspaceIssue } from './workspaceIssues';

type Reporter = { report: (issue: WorkspaceIssue) => void; clear: (key: string) => void };
type Notifications = { issues: WorkspaceIssue[]; refresh: () => Promise<void> };
const noOpReporter: Reporter = { report: () => {}, clear: () => {} };
const ReporterContext = createContext<Reporter>(noOpReporter);
const NotificationsContext = createContext<Notifications>({ issues: [], refresh: async () => {} });

export function useWorkspaceIssueReporter() { return useContext(ReporterContext); }
export function useWorkspaceNotifications() { return useContext(NotificationsContext); }

export function WorkspaceNotificationsProvider({ session, capabilities, capabilitiesError, onRefresh, loadConnectionHealth, children }: { session: AppSession; capabilities: CapabilitySnapshot | null; capabilitiesError: Error | null; onRefresh: () => Promise<void>; loadConnectionHealth?: ConnectionHealthLoader; children: ReactNode }) {
  const [reported, setReported] = useState<{ workspaceId: string; issues: Record<string, WorkspaceIssue> }>({ workspaceId: session.workspace.id, issues: {} });
  const [health, setHealth] = useState<ConnectionHealth | null>(null);
  const [healthFailed, setHealthFailed] = useState(false);
  const workspaceId = session.workspace.id;
  const reloadHealth = useCallback(async () => {
    if (!loadConnectionHealth) return;
    try { setHealth(await loadConnectionHealth()); setHealthFailed(false); }
    catch { setHealth(null); setHealthFailed(true); }
  }, [loadConnectionHealth]);
  useEffect(() => { setHealth(null); setHealthFailed(false); void reloadHealth(); }, [workspaceId, reloadHealth]);
  useEffect(() => { setReported(current => current.workspaceId === workspaceId ? current : { workspaceId, issues: {} }); }, [workspaceId]);
  const reporter = useMemo<Reporter>(() => ({
    report: issue => { const safe = sanitizeReportedIssue(issue); if (safe) setReported(current => current.workspaceId === workspaceId ? { ...current, issues: { ...current.issues, [safe.key]: safe } } : current); },
    clear: key => setReported(current => { if (current.workspaceId !== workspaceId) return current; const issues = { ...current.issues }; delete issues[reportedIssueKey(key)]; return { ...current, issues }; }),
  }), [workspaceId]);
  const refresh = useCallback(async () => { await Promise.all([onRefresh(), reloadHealth()]); }, [onRefresh, reloadHealth]);
  useEffect(() => {
    const onReturn = () => { void refresh(); };
    window.addEventListener('focus', onReturn);
    return () => window.removeEventListener('focus', onReturn);
  }, [refresh]);
  useEffect(() => {
    if (capabilities?.workspaceId === workspaceId) setReported(current => current.workspaceId === workspaceId && Object.keys(current.issues).length ? { workspaceId, issues: {} } : current);
  }, [capabilities, workspaceId]);
  const issues = useMemo(() => [
    ...deriveCapabilityIssues(capabilities, session),
    ...deriveConnectionIssue(health, session),
    ...(healthFailed ? [{ key: 'connection:unavailable', area: 'Connection', kind: 'service' as const, severity: 'warning' as const, title: 'Connection check unavailable', detail: 'Try checking the connection again.' }] : []),
    ...(capabilitiesError ? [{ key: 'capabilities:unavailable', area: 'Your access', kind: 'service' as const, severity: 'warning' as const, title: 'Permission verification unavailable', detail: 'Try checking your access again.' }] : []),
    ...Object.values(reported.workspaceId === workspaceId ? reported.issues : {}),
  ], [capabilities, session, health, healthFailed, capabilitiesError, reported, workspaceId]);
  return <ReporterContext.Provider value={reporter}><NotificationsContext.Provider value={{ issues, refresh }}>{children}</NotificationsContext.Provider></ReporterContext.Provider>;
}
