import React, { useState } from 'react';

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function WorkspaceCreateForm({ onSubmit, onCancel }: { onSubmit: (input: { tenantId: string; displayName: string }) => Promise<void>; onCancel: () => void }) {
  const [tenantId, setTenantId] = useState(''); const [displayName, setDisplayName] = useState(''); const [error, setError] = useState(''); const [saving, setSaving] = useState(false);
  async function submit(event: React.FormEvent) { event.preventDefault(); if (!guid.test(tenantId)) return setError('Enter a valid tenant ID.'); if (!displayName.trim()) return setError('Enter a workspace display name.'); setError(''); setSaving(true); try { await onSubmit({ tenantId, displayName: displayName.trim() }); } catch (e) { setError(e instanceof Error ? e.message : 'Workspace creation failed.'); } finally { setSaving(false); } }
  return <form className="admin-card" onSubmit={submit} aria-label="Create workspace form"><h2>Create workspace</h2>{error && <p role="alert">{error}</p>}<label>Tenant ID<input aria-label="Tenant ID" value={tenantId} onChange={(e) => setTenantId(e.target.value)} /></label><label>Display name<input aria-label="Display name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} /></label><div className="admin-actions"><button type="submit" disabled={saving}>{saving ? 'Creating…' : 'Create workspace'}</button><button type="button" onClick={onCancel}>Cancel</button></div></form>;
}
