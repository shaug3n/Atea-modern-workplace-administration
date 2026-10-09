import React, { useState } from 'react';
import type { WorkspaceCreateInput } from './adminApi';

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export type FirstAdminInvite = { workspace: { id: string; tenantId: string; displayName: string; connectionStatus: string }; invitationUrl: string; expiresAt: string };

export function WorkspaceCreateForm({ onSubmit, onCancel }: { onSubmit: (input: WorkspaceCreateInput & { displayName: string; adminUpn: string; adminDisplayName?: string }) => Promise<FirstAdminInvite>; onCancel: () => void }) {
  const [tenantInput, setTenantInput] = useState(''); const [displayName, setDisplayName] = useState(''); const [adminUpn, setAdminUpn] = useState(''); const [adminDisplayName, setAdminDisplayName] = useState(''); const [error, setError] = useState(''); const [saving, setSaving] = useState(false);
  async function submit(event: React.FormEvent) {
    event.preventDefault();
    const normalizedTenantInput = tenantInput.trim();
    if (!normalizedTenantInput) return setError('Enter a tenant domain or ID.');
    if (!displayName.trim()) return setError('Enter a workspace name.');
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(adminUpn.trim())) return setError('Enter the first administrator’s Entra sign-in address.');
    setError(''); setSaving(true);
    const tenant = guid.test(normalizedTenantInput)
      ? { tenantId: normalizedTenantInput }
      : { tenantDomain: normalizedTenantInput };
    try { await onSubmit({ ...tenant, displayName: displayName.trim(), adminUpn: adminUpn.trim(), ...(adminDisplayName.trim() ? { adminDisplayName: adminDisplayName.trim() } : {}) }); }
    catch (e) { setError(e instanceof Error ? e.message : 'Workspace onboarding failed.'); }
    finally { setSaving(false); }
  }
  return <form className="admin-card workspace-onboarding-form" onSubmit={submit} aria-label="Onboard customer workspace" noValidate>
    <p className="eyebrow">Customer onboarding</p><h2>Create workspace and invite its first administrator</h2>
    <p>The nominated administrator receives an invitation to sign in with their existing Entra account. Their workspace role does not grant Microsoft 365 privileges.</p>
    {error && <p role="alert">{error}</p>}
    <label>Tenant domain or ID<input aria-label="Tenant domain or ID" autoComplete="off" value={tenantInput} onChange={event => setTenantInput(event.target.value)} required /></label>
    <label>Workspace name<input aria-label="Workspace name" value={displayName} onChange={event => setDisplayName(event.target.value)} required maxLength={200} /></label>
    <label>First admin sign-in address<input aria-label="First admin sign-in address" type="email" autoComplete="email" value={adminUpn} onChange={event => setAdminUpn(event.target.value)} required maxLength={320} /></label>
    <label>First admin display name<input aria-label="First admin display name" value={adminDisplayName} onChange={event => setAdminDisplayName(event.target.value)} maxLength={200} /></label>
    <div className="admin-actions"><button type="submit" disabled={saving}>{saving ? 'Creating workspace and invitation…' : 'Create workspace and invite admin'}</button><button type="button" disabled={saving} onClick={onCancel}>Cancel</button></div>
  </form>;
}
