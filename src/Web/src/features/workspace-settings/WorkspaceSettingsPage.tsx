import { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { messages } from '../../messages/en';
import { WorkspaceAdminForm } from './WorkspaceAdminForm';

export type WorkspaceSettings = { displayName: string; enabledModules: string[]; defaultColumns: string[]; defaultFilters: Record<string, string>; supportInstructions: string; defaultTheme: 'light' | 'dark'; access: { state: string } };
export type SettingsLoader = () => Promise<WorkspaceSettings>;
export type SettingsSaver = (settings: WorkspaceSettings) => Promise<WorkspaceSettings>;

export function WorkspaceSettingsPage({ loadSettings, saveSettings }: { loadSettings?: SettingsLoader; saveSettings?: SettingsSaver }) {
  if (loadSettings) return <LoadedWorkspaceSettingsPage loader={loadSettings} saver={saveSettings} />;
  return <AuthenticatedWorkspaceSettingsPage />;
}

function AuthenticatedWorkspaceSettingsPage() {
  const api = useApi();
  const defaultLoader = useCallback(async () => { const response = await api('/api/workspaces/current/settings'); if (!response.ok) throw new Error('settings load failed'); return await response.json() as WorkspaceSettings; }, [api]);
  const defaultSaver = useCallback(async (settings: WorkspaceSettings) => { const response = await api('/api/workspaces/current/settings', { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(settings) }); if (!response.ok) throw new Error('settings save failed'); return await response.json() as WorkspaceSettings; }, [api]);
  return <LoadedWorkspaceSettingsPage loader={defaultLoader} saver={defaultSaver} />;
}

function LoadedWorkspaceSettingsPage({ loader, saver }: { loader: SettingsLoader; saver?: SettingsSaver }) {
  const [settings, setSettings] = useState<WorkspaceSettings | null>(null); const [failed, setFailed] = useState(false); const [retry, setRetry] = useState(0);
  useEffect(() => { let cancelled = false; setFailed(false); loader().then(value => { if (!cancelled) setSettings(value); }).catch(() => { if (!cancelled) setFailed(true); }); return () => { cancelled = true; }; }, [loader, retry]);
  if (failed) return <section className="content-panel"><p role="alert">Workspace settings are unavailable.</p><button type="button" onClick={() => setRetry(value => value + 1)}>{messages.retry}</button></section>;
  if (!settings) return <section className="content-panel"><p role="status">Loading workspace settings…</p></section>;
  if (settings.access.state !== 'allowed') return <section className="content-panel"><h1>{messages.permissionRequiredTitle}</h1><p>{messages.permissionRequiredBody}</p></section>;
  return <section className="content-panel"><p className="eyebrow">Workspace administration</p><h1>Workspace settings</h1>{saver && <WorkspaceAdminForm settings={settings} saveSettings={async next => { const saved = await saver(next); setSettings(saved); return saved; }} />}</section>;
}
