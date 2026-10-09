import { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { StatusBadge } from '../../components/StatusBadge';
import { SettingsFormFooter } from './SettingsFormFooter';

export const workspaceModules = [
  { key: 'users', label: 'Users', description: 'Directory users, lifecycle and identity details.' },
  { key: 'devices', label: 'Devices', description: 'Managed device inventory and compliance.' },
  { key: 'licenses', label: 'Licenses', description: 'License catalog and seat utilization.' },
  { key: 'exchange', label: 'Exchange', description: 'Read-only mailbox directory and lookup details.' },
  { key: 'authentication-campaigns', label: 'Authentication campaigns', description: 'Read-only authentication methods campaign reporting.' },
  { key: 'license-hygiene', label: 'License Hygiene', description: 'Opt-in, read-only license allocation review.' },
  { key: 'about', label: 'About', description: 'Product information, security and release history.' },
  { key: 'feedback', label: 'Feedback', description: 'Submit feedback and review your own submissions.' },
] as const;

export type WorkspaceModulesResponse = { enabledModules: string[]; dormantGrantCount?: number; restoredDormantGrants?: boolean };

export function WorkspaceModulesPage({ embedded = false }: { embedded?: boolean } = {}) {
  const api = useApi();
  const [enabledModules, setEnabledModules] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [dormantGrantCount, setDormantGrantCount] = useState(0);
  const [restoreConfirmation, setRestoreConfirmation] = useState(false);
  const [originalModules, setOriginalModules] = useState<string[]>([]);
  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try {
      const response = await api('/api/workspaces/current/modules');
      if (!response.ok) throw new Error('modules unavailable');
      const value = await response.json() as WorkspaceModulesResponse;
      setEnabledModules(value.enabledModules);
      setOriginalModules(value.enabledModules);
      setDormantGrantCount(value.dormantGrantCount ?? 0);
    } catch { setError('Workspace modules are unavailable.'); } finally { setLoading(false); }
  }, [api]);
  useEffect(() => { void load(); }, [load]);
  const dirty = enabledModules.length !== originalModules.length || enabledModules.some(key => !originalModules.includes(key));
  const save = async (confirmed = false) => {
    const reenabled = enabledModules.some(key => !originalModules.includes(key));
    if (!confirmed && reenabled && dormantGrantCount > 0) {
      setRestoreConfirmation(true);
      return;
    }
    setSaving(true); setSavedAt(null); setError(null);
    try {
      const response = await api('/api/workspaces/current/modules', { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ enabledModules }) });
      if (!response.ok) throw new Error('save failed');
      const value = await response.json() as WorkspaceModulesResponse;
      setEnabledModules(value.enabledModules);
      setRestoreConfirmation(false);
      await load();
      setSavedAt(new Date().toISOString());
    } catch { setError('Modules could not be saved. Review owner permissions and try again.'); } finally { setSaving(false); }
  };
  return <section className="settings-page" aria-labelledby="modules-title">
    <header className="page-header"><div>{!embedded && <p className="eyebrow">Settings</p>}{embedded ? <h2 id="modules-title" tabIndex={-1}>Modules</h2> : <h1 id="modules-title">Modules</h1>}<p>Enable services for this workspace. People only see modules that are enabled here and assigned to them.</p></div></header>
    {loading && <p className="async-state" role="status">Loading modules…</p>}
    {!loading && error && <div className="async-state" role="alert">{error} <button type="button" onClick={() => void load()}>Retry</button></div>}
    {!loading && !error && <>
    {dormantGrantCount > 0 && <p className="integration-note">{dormantGrantCount} existing member or invitation module grant{dormantGrantCount === 1 ? '' : 's'} are dormant because a module is disabled. They remain stored and will become effective again if you re-enable that module.</p>}
    <div className="module-settings-list">{workspaceModules.map(module => <label className="module-setting" key={module.key}>
      <input type="checkbox" role="switch" aria-label={module.label} checked={enabledModules.includes(module.key)} onChange={() => { setSavedAt(null); setEnabledModules(current => current.includes(module.key) ? current.filter(key => key !== module.key) : [...current, module.key]); }} />
      <span><strong>{module.label}</strong><small>{module.description}</small></span><StatusBadge tone={enabledModules.includes(module.key) ? 'success' : 'neutral'} label={enabledModules.includes(module.key) ? 'Enabled' : 'Disabled'} />
    </label>)}</div>
    {restoreConfirmation && <section className="integration-note" role="alertdialog" aria-labelledby="restore-grants-title"><h2 id="restore-grants-title">Restore existing module access?</h2><p>Re-enabling a module will reactivate stored access grants for existing members and pending invitations. Those people will be able to use the module, still subject to their Entra permissions and PIM state.</p><div className="settings-form-actions"><button type="button" disabled={saving} onClick={() => setRestoreConfirmation(false)}>Keep module disabled</button><button type="button" className="button--primary" disabled={saving} onClick={() => void save(true)}>Confirm and restore access</button></div></section>}
    <SettingsFormFooter dirty={dirty} saving={saving} savedAt={savedAt} onReset={() => { setEnabledModules(originalModules); setRestoreConfirmation(false); setError(null); }} onSave={() => void save()} />
    </>}
  </section>;
}
