import React, { useState } from 'react';
import type { WorkspaceSettings } from './WorkspaceSettingsPage';
import { SettingsFormFooter } from './SettingsFormFooter';

export function WorkspaceAdminForm({ settings, saveSettings }: { settings: WorkspaceSettings; saveSettings: (settings: WorkspaceSettings) => Promise<WorkspaceSettings> }) {
  const [displayName, setDisplayName] = useState(settings.displayName);
  const [baseline, setBaseline] = useState(settings.displayName);
  const [saving, setSaving] = useState(false);
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const dirty = displayName !== baseline;
  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!dirty) return;
    setSaving(true); setError(null);
    try {
      const saved = await saveSettings({ ...settings, displayName });
      setDisplayName(saved.displayName); setBaseline(saved.displayName);
      setSavedAt(new Date().toISOString());
    } catch {
      setError('Settings could not be saved. Review your changes and try again.');
    } finally { setSaving(false); }
  };
  return <form onSubmit={submit}>
    <label htmlFor="workspace-display-name">Workspace display name</label>
    <input id="workspace-display-name" value={displayName} onChange={event => { setDisplayName(event.target.value); setSavedAt(null); }} />
    <SettingsFormFooter dirty={dirty} saving={saving} savedAt={savedAt} error={error} onReset={() => { setDisplayName(baseline); setError(null); }} />
  </form>;
}
