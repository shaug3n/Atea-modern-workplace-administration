import { useState } from 'react';
import type { WorkspaceSettings } from './WorkspaceSettingsPage';
import { messages } from '../../messages/en';

export function WorkspaceAdminForm({ settings, saveSettings }: { settings: WorkspaceSettings; saveSettings: (settings: WorkspaceSettings) => Promise<WorkspaceSettings> }) {
  const [displayName, setDisplayName] = useState(settings.displayName); const [saving, setSaving] = useState(false); const [saved, setSaved] = useState(false);
  return <form onSubmit={async event => { event.preventDefault(); setSaving(true); setSaved(false); try { await saveSettings({ ...settings, displayName }); setSaved(true); } finally { setSaving(false); } }}><label htmlFor="workspace-display-name">Workspace display name</label><input id="workspace-display-name" value={displayName} onChange={event => setDisplayName(event.target.value)} /><button type="submit" disabled={saving}>{saving ? 'Saving…' : messages.saveSettings}</button>{saved && <span role="status">Settings saved</span>}</form>;
}
