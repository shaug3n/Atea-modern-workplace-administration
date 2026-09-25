import { useCallback, useEffect, useRef, useState } from 'react';
import { useApi } from '../../auth/useApi';

export const workspaceModules = [
  { key: 'users', label: 'Users', description: 'Directory users, lifecycle and identity details.' },
  { key: 'devices', label: 'Devices', description: 'Managed device inventory and compliance.' },
  { key: 'licenses', label: 'Licenses', description: 'License catalog and seat utilization.' },
  { key: 'exchange', label: 'Exchange', description: 'Read-only mailbox directory and lookup details.' },
] as const;

export type WorkspaceModulesResponse = { enabledModules: string[]; dormantGrantCount?: number; restoredDormantGrants?: boolean };

export function WorkspaceModulesPage({ embedded = false }: { embedded?: boolean } = {}) {
  const api = useApi();
  const [enabledModules, setEnabledModules] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [retrievedAt, setRetrievedAt] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [dormantGrantCount, setDormantGrantCount] = useState(0);
  const [restoreConfirmation, setRestoreConfirmation] = useState(false);
  const originalModules = useRef<string[]>([]);
  const load = useCallback(async () => {
    setLoading(true); setError(null); setRetrievedAt(null);
    try {
      const response = await api('/api/workspaces/current/modules');
      if (!response.ok) throw new Error('modules unavailable');
      const value = await response.json() as WorkspaceModulesResponse;
      setEnabledModules(value.enabledModules);
      originalModules.current = value.enabledModules;
      setDormantGrantCount(value.dormantGrantCount ?? 0);
      setRetrievedAt(new Date().toISOString());
    } catch { setError('Workspace modules are unavailable.'); } finally { setLoading(false); }
  }, [api]);
  useEffect(() => { void load(); }, [load]);
  const save = async (confirmed = false) => {
    const reenabled = enabledModules.some(key => !originalModules.current.includes(key));
    if (!confirmed && reenabled && dormantGrantCount > 0) {
      setRestoreConfirmation(true);
      return;
    }
    setSaving(true); setSaved(false); setError(null);
    try {
      const response = await api('/api/workspaces/current/modules', { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ enabledModules }) });
      if (!response.ok) throw new Error('save failed');
      const value = await response.json() as WorkspaceModulesResponse;
      setEnabledModules(value.enabledModules);
      setRestoreConfirmation(false);
      await load();
      setSaved(true);
    } catch { setError('Modules could not be saved. Review owner permissions and try again.'); } finally { setSaving(false); }
  };
  return <section className="settings-page" aria-labelledby="modules-title">
    <header className="page-header"><div>{!embedded && <p className="eyebrow">Settings</p>}{embedded ? <h2 id="modules-title" tabIndex={-1}>Modules</h2> : <h1 id="modules-title">Modules</h1>}<p>Enable services for this workspace. People only see modules that are enabled here and assigned to them.</p></div></header>
    {loading && <p className="async-state" role="status">Loading modules…</p>}
    {!loading && error && <div className="async-state" role="alert">{error} <button type="button" onClick={() => void load()}>Retry</button></div>}
    {!loading && !error && <p className="data-freshness">Source: Workspace module configuration. Retrieved: {retrievedAt ? new Date(retrievedAt).toLocaleString() : 'Not retrieved'}</p>}
    {!loading && !error && <>
    {dormantGrantCount > 0 && <p className="integration-note">{dormantGrantCount} existing member or invitation module grant{dormantGrantCount === 1 ? '' : 's'} are dormant because a module is disabled. They remain stored and will become effective again if you re-enable that module.</p>}
    <div className="module-settings-list">{workspaceModules.map(module => <label className="module-setting" key={module.key}>
      <input type="checkbox" aria-label={module.label} checked={enabledModules.includes(module.key)} onChange={() => setEnabledModules(current => current.includes(module.key) ? current.filter(key => key !== module.key) : [...current, module.key])} />
      <span><strong>{module.label}</strong><small>{module.description}</small></span>
    </label>)}</div>
    {restoreConfirmation && <section className="integration-note" role="alertdialog" aria-labelledby="restore-grants-title"><h2 id="restore-grants-title">Restore existing module access?</h2><p>Re-enabling a module will reactivate stored access grants for existing members and pending invitations. Those people will be able to use the module, still subject to their Entra permissions and PIM state.</p><div className="settings-form-actions"><button type="button" disabled={saving} onClick={() => setRestoreConfirmation(false)}>Keep module disabled</button><button type="button" className="button--primary" disabled={saving} onClick={() => void save(true)}>Confirm and restore access</button></div></section>}
    <div className="settings-form-actions"><button type="button" className="button--primary" disabled={saving} onClick={() => void save()}>{saving ? 'Saving…' : 'Save modules'}</button>{saved && <span role="status">Modules saved</span>}</div>
    </>}
  </section>;
}
