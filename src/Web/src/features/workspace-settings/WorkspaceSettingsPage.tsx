import { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { messages } from '../../messages/en';
import { WorkspaceAdminForm } from './WorkspaceAdminForm';

export type WorkspaceSettings = { displayName: string; enabledModules: string[]; defaultColumns: string[]; defaultFilters: Record<string, string>; supportInstructions: string; defaultTheme: 'light' | 'dark'; access: { state: string } };
export type SettingsLoader = () => Promise<WorkspaceSettings>;
export type SettingsSaver = (settings: WorkspaceSettings) => Promise<WorkspaceSettings>;

export function WorkspaceSettingsPage({ loadSettings, saveSettings, embedded = false }: { loadSettings?: SettingsLoader; saveSettings?: SettingsSaver; embedded?: boolean }) {
  if (loadSettings) return <LoadedWorkspaceSettingsPage loader={loadSettings} saver={saveSettings} embedded={embedded} />;
  return <AuthenticatedWorkspaceSettingsPage embedded={embedded} />;
}

function AuthenticatedWorkspaceSettingsPage({ embedded }: { embedded: boolean }) {
  const api = useApi();
  const defaultLoader = useCallback(async () => { const response = await api('/api/workspaces/current/settings'); if (!response.ok) throw new Error('settings load failed'); return await response.json() as WorkspaceSettings; }, [api]);
  const defaultSaver = useCallback(async (settings: WorkspaceSettings) => { const response = await api('/api/workspaces/current/settings', { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(settings) }); if (!response.ok) throw new Error('settings save failed'); return await response.json() as WorkspaceSettings; }, [api]);
  return <LoadedWorkspaceSettingsPage loader={defaultLoader} saver={defaultSaver} embedded={embedded} />;
}

function LoadedWorkspaceSettingsPage({ loader, saver, embedded }: { loader: SettingsLoader; saver?: SettingsSaver; embedded: boolean }) {
  const [settings, setSettings] = useState<WorkspaceSettings | null>(null); const [failed, setFailed] = useState(false); const [loading, setLoading] = useState(true); const [retrievedAt, setRetrievedAt] = useState<string | null>(null); const [retry, setRetry] = useState(0);
  useEffect(() => { let cancelled = false; setLoading(true); setFailed(false); setSettings(null); setRetrievedAt(null); loader().then(value => { if (!cancelled) { setSettings(value); setRetrievedAt(new Date().toISOString()); } }).catch(() => { if (!cancelled) setFailed(true); }).finally(() => { if (!cancelled) setLoading(false); }); return () => { cancelled = true; }; }, [loader, retry]);
  return <section className="settings-page content-panel" aria-labelledby="general-title">
    <header className="page-header"><div>{!embedded && <p className="eyebrow">Settings</p>}{embedded ? <h2 id="general-title" tabIndex={-1}>General</h2> : <h1 id="general-title">General</h1>}<p>Update the workspace name and customer-facing preferences.</p></div></header>
    {loading && <p role="status">Loading workspace settings…</p>}
    {!loading && failed && <div className="async-state" role="alert">Workspace settings are unavailable. <button type="button" onClick={() => setRetry(value => value + 1)}>{messages.retry}</button></div>}
    {!loading && !failed && settings?.access.state !== 'allowed' && <div className="permission-panel"><h2>{messages.permissionRequiredTitle}</h2><p>{messages.permissionRequiredBody}</p></div>}
    {!loading && !failed && settings?.access.state === 'allowed' && <><p className="data-freshness">Source: Workspace settings. Retrieved: {retrievedAt ? new Date(retrievedAt).toLocaleString() : 'Not retrieved'}</p>{saver && <WorkspaceAdminForm settings={settings} saveSettings={async next => { const saved = await saver(next); setSettings(saved); setRetrievedAt(new Date().toISOString()); return saved; }} />}</>}
  </section>;
}
