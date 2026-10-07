import { useCallback, useEffect, useRef, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { useFocusContainment } from '../../components/useFocusContainment';

type Membership = { id: string; tenantObjectId: string; email: string; platformRole: 'member' | 'customer_admin' | string; moduleKeys?: string[]; createdAt: string };
type Invitation = { id: string; email: string; displayName: string; role: 'member' | 'customer_admin' | string; moduleKeys?: string[]; expiresAt: string; redeemedAt: string | null; revokedAt: string | null };
type AccessResponse = { memberships: Membership[]; invitations: Invitation[] };
type OneTimeLink = { invitationUrl: string; expiresAt: string };

export function WorkspaceAccessPage({ isOwner = false, canManageModules = false, availableModules = ['users', 'devices', 'licenses', 'exchange'], embedded = false }: { isOwner?: boolean; canManageModules?: boolean; availableModules?: string[]; embedded?: boolean } = {}) {
  const api = useApi();
  const [data, setData] = useState<AccessResponse | null>(null);
  const [loadError, setLoadError] = useState(false);
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [issuedLink, setIssuedLink] = useState<OneTimeLink | null>(null);
  const [removeTarget, setRemoveTarget] = useState<Membership | null>(null);
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [role, setRole] = useState<'member' | 'customer_admin'>('member');
  const [moduleKeys, setModuleKeys] = useState<string[]>([]);
  const [roleEdits, setRoleEdits] = useState<Record<string, string>>({});
  const [moduleEdits, setModuleEdits] = useState<Record<string, string[]>>({});
  const [transferTargetId, setTransferTargetId] = useState('');
  const [transferConfirmationOpen, setTransferConfirmationOpen] = useState(false);
  const removeTriggerRef = useRef<HTMLElement | null>(null);
  const removeDialogRef = useFocusContainment<HTMLElement>(Boolean(removeTarget), () => { if (!busy) setRemoveTarget(null); }, removeTriggerRef);

  const load = useCallback(async () => {
    setLoading(true); setLoadError(false);
    try {
      const response = await api('/api/workspaces/current/access');
      if (!response.ok) throw new Error('access unavailable');
      setData(await response.json() as AccessResponse);
    } catch { setLoadError(true); } finally { setLoading(false); }
  }, [api]);

  useEffect(() => { void load(); }, [load, retry]);

  const request = async (path: string, init: RequestInit) => {
    const response = await api(path, init);
    if (!response.ok) {
      let code = '';
      try { code = ((await response.json()) as { error?: string }).error ?? ''; } catch { /* response may have no JSON body */ }
      if (code === 'last_customer_admin_required') throw new Error('At least one customer administrator must remain in the workspace.');
      if (code === 'user_already_has_workspace_access') throw new Error('This identity already has workspace access.');
      throw new Error('That workspace access change could not be completed. Try again.');
    }
    return response;
  };

  const createInvitation = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setBusy(true); setError(null); setNotice(null); setIssuedLink(null);
    try {
      const response = await request('/api/workspaces/current/access/invitations', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: email.trim(), displayName: displayName.trim(), role, moduleKeys }) });
      const value = await response.json() as OneTimeLink;
      setIssuedLink(value); setNotice('Invitation created. Copy and share this link through your approved channel; it will only be shown here once.');
      setEmail(''); setDisplayName('');
      await load();
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'That invitation could not be created.'); }
    finally { setBusy(false); }
  };

  const changeRole = async (membership: Membership) => {
    setBusy(true); setError(null); setNotice(null);
    const nextRole = roleEdits[membership.id] ?? normalizeRole(membership.platformRole);
    if (nextRole === 'customer_admin' && !isOwner) {
      setError('Only the workspace owner can assign customer administrators.');
      setBusy(false);
      return;
    }
    try {
      await request(`/api/workspaces/current/access/memberships/${membership.id}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ role: nextRole }) });
      setNotice(`Workspace role updated for ${membership.email}.`); await load();
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'The workspace role could not be changed.'); }
    finally { setBusy(false); }
  };

  const changeModules = async (membership: Membership) => {
    setBusy(true); setError(null); setNotice(null);
    try {
      await request(`/api/workspaces/current/access/memberships/${membership.id}/modules`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ moduleKeys: moduleEdits[membership.id] ?? membership.moduleKeys ?? [] }) });
      setNotice(`Module access updated for ${membership.email}.`); await load();
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Module access could not be changed.'); }
    finally { setBusy(false); }
  };

  const transferOwnership = async () => {
    if (!transferTargetId) return;
    const target = data?.memberships.find(membership => membership.id === transferTargetId);
    if (!target) return;
    setBusy(true); setError(null); setNotice(null);
    try {
      await request('/api/workspaces/current/access/ownership/transfer', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ newOwnerMembershipId: target.id }) });
      setNotice(`Ownership transfer requested for ${target.email}.`);
      setTransferTargetId('');
      setTransferConfirmationOpen(false);
      await load();
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Ownership transfer could not be completed.'); }
    finally { setBusy(false); }
  };

  const removeMember = async () => {
    if (!removeTarget) return;
    setBusy(true); setError(null); setNotice(null);
    try {
      await request(`/api/workspaces/current/access/memberships/${removeTarget.id}`, { method: 'DELETE' });
      setNotice(`Workspace access removed for ${removeTarget.email}.`); setRemoveTarget(null); await load();
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Workspace access could not be removed.'); }
    finally { setBusy(false); }
  };

  const reissue = async (invitation: Invitation) => {
    setBusy(true); setError(null); setNotice(null); setIssuedLink(null);
    try {
      const response = await request(`/api/workspaces/current/access/invitations/${invitation.id}/reissue`, { method: 'POST' });
      setIssuedLink(await response.json() as OneTimeLink); setNotice(`A replacement invitation was issued for ${invitation.email}. The older link is no longer valid.`); await load();
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'The invitation could not be reissued.'); }
    finally { setBusy(false); }
  };

  const revoke = async (invitation: Invitation) => {
    setBusy(true); setError(null); setNotice(null);
    try {
      await request(`/api/workspaces/current/access/invitations/${invitation.id}`, { method: 'DELETE' });
      setNotice(`Invitation for ${invitation.email} was revoked.`); await load();
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'The invitation could not be revoked.'); }
    finally { setBusy(false); }
  };

  const header = <header className="page-header"><div>{!embedded && <p className="eyebrow">Settings</p>}{embedded ? <h2 id="workspace-access-title" tabIndex={-1}>Access</h2> : <h1 id="workspace-access-title">Workspace access</h1>}<p>Grant application access to people who already exist in your Microsoft Entra tenant. Microsoft 365 actions still follow each person’s Entra roles and PIM activation.</p></div></header>;
  if (loading || loadError || !data) return <section className="content-panel workspace-access-page" aria-labelledby="workspace-access-title">{header}{loading ? <p role="status">Loading workspace access…</p> : <div className="async-state" role="alert">Workspace members and invitations are unavailable right now. <button type="button" onClick={() => setRetry(value => value + 1)}>Retry</button></div>}</section>;

  const pendingInvitations = data.invitations.filter(invitation => !invitation.redeemedAt && !invitation.revokedAt);
  return <section className="content-panel workspace-access-page" aria-labelledby="workspace-access-title">
    {header}
    <p>Invitation links are shown once. Share them through an approved channel; this application does not send email.</p>
    {!isOwner && <p className="integration-note"><strong>Owner transfer:</strong> only the current workspace owner can transfer ownership.</p>}
    {error && <p role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    {issuedLink && <section aria-labelledby="issued-link-title" className="invitation-link-panel"><h2 id="issued-link-title">One-time invitation link</h2><p><code>{issuedLink.invitationUrl}</code></p><p>Expires {new Date(issuedLink.expiresAt).toLocaleString()}</p><button type="button" onClick={() => { void navigator.clipboard?.writeText(issuedLink.invitationUrl).then(() => setNotice('Invitation link copied.')); }}>Copy link</button></section>}

    <section aria-labelledby="invite-user-title" className="workspace-access-section">
      <h2 id="invite-user-title">Invite an existing Entra user</h2>
      <form className="workspace-access-form" onSubmit={event => void createInvitation(event)}>
        <label>Email address<input type="email" required maxLength={320} autoComplete="email" value={email} onChange={event => setEmail(event.target.value)} /></label>
        <label>Display name<input type="text" required maxLength={200} value={displayName} onChange={event => setDisplayName(event.target.value)} /></label>
        <label>Workspace role<select value={role} onChange={event => setRole(event.target.value as 'member' | 'customer_admin')}><option value="member">Member</option>{isOwner && <option value="customer_admin">Customer administrator</option>}</select></label>
        <fieldset className="module-checkboxes"><legend>Module access</legend>{['users', 'devices', 'licenses', 'exchange'].map(key => <label key={key}><input type="checkbox" aria-label={key[0].toUpperCase() + key.slice(1)} disabled={!canManageModules || !availableModules.includes(key)} checked={moduleKeys.includes(key)} onChange={() => setModuleKeys(current => toggle(current, key))} />{key[0].toUpperCase() + key.slice(1)}</label>)}</fieldset>
        <button type="submit" disabled={busy}>Create invitation</button>
      </form>
    </section>

    <section aria-labelledby="workspace-members-title" className="workspace-access-section">
      <h2 id="workspace-members-title">Members ({data.memberships.length})</h2>
      {data.memberships.length === 0 ? <p>No workspace members have been added yet.</p> : <div className="users-table-wrap"><table className="users-table"><thead><tr><th scope="col">User</th><th scope="col">Workspace role</th><th scope="col">Module access</th><th scope="col">Actions</th></tr></thead><tbody>{data.memberships.map(member => { const isWorkspaceOwner = normalizeRole(member.platformRole) === 'workspace_owner'; return <tr key={member.id}><th scope="row" data-label="User">{member.email}</th><td data-label="Workspace role"><label className="visually-hidden" htmlFor={`role-${member.id}`}>Role for {member.email}</label><select id={`role-${member.id}`} value={roleEdits[member.id] ?? normalizeRole(member.platformRole)} disabled={isWorkspaceOwner || !isOwner} onChange={event => setRoleEdits(value => ({ ...value, [member.id]: event.target.value }))}><option value="member">Member</option><option value="customer_admin">Customer administrator</option>{isWorkspaceOwner && <option value="workspace_owner">Workspace owner</option>}</select></td><td data-label="Module access"><div className="module-inline-checkboxes">{['users', 'devices', 'licenses', 'exchange'].map(key => <label key={key}><input type="checkbox" aria-label={`${key[0].toUpperCase() + key.slice(1)} for ${member.email}`} disabled={!canManageModules || isWorkspaceOwner || !availableModules.includes(key)} checked={(moduleEdits[member.id] ?? member.moduleKeys ?? []).includes(key)} onChange={() => setModuleEdits(current => ({ ...current, [member.id]: toggle(current[member.id] ?? member.moduleKeys ?? [], key) }))} />{key[0].toUpperCase() + key.slice(1)}</label>)}</div></td><td data-label="Actions"><div className="workspace-access-actions">{!isWorkspaceOwner && <><button type="button" disabled={busy || !isOwner} onClick={() => void changeRole(member)}>Save role for {member.email}</button><button type="button" disabled={busy || !canManageModules} onClick={() => void changeModules(member)}>Save modules for {member.email}</button><button type="button" disabled={busy} onClick={event => { removeTriggerRef.current = event.currentTarget; setRemoveTarget(member); }}>Remove access for {member.email}</button></>}</div></td></tr>; })}</tbody></table></div>}
    </section>

    {isOwner && data.memberships.some(member => normalizeRole(member.platformRole) === 'customer_admin') && <section aria-labelledby="ownership-transfer-title" className="workspace-access-section ownership-transfer">
      <h2 id="ownership-transfer-title">Transfer ownership</h2>
      <p>Transfer workspace ownership to an existing customer administrator. You will no longer be the owner after the transfer completes.</p>
      <label htmlFor="new-owner">New owner<select id="new-owner" value={transferTargetId} onChange={event => { setTransferTargetId(event.target.value); setTransferConfirmationOpen(false); }}><option value="">Select a customer administrator</option>{data.memberships.filter(member => normalizeRole(member.platformRole) === 'customer_admin').map(member => <option key={member.id} value={member.id}>{member.email}</option>)}</select></label>
      <button type="button" disabled={!transferTargetId || busy} onClick={() => setTransferConfirmationOpen(true)}>Review ownership transfer</button>
      {transferConfirmationOpen && <div className="ownership-transfer__confirm"><p>Transfer ownership to {data.memberships.find(member => member.id === transferTargetId)?.email}?</p><div className="workspace-access-actions"><button type="button" disabled={busy} onClick={() => setTransferConfirmationOpen(false)}>Cancel</button><button type="button" disabled={busy} onClick={() => void transferOwnership()}>Confirm ownership transfer</button></div></div>}
    </section>}

    <section aria-labelledby="workspace-invitations-title" className="workspace-access-section">
      <h2 id="workspace-invitations-title">Pending invitations ({pendingInvitations.length})</h2>
      {pendingInvitations.length === 0 ? <p>There are no pending invitations.</p> : <div className="users-table-wrap"><table className="users-table"><thead><tr><th scope="col">Invitee</th><th scope="col">Role</th><th scope="col">Modules</th><th scope="col">Expires</th><th scope="col">Actions</th></tr></thead><tbody>{pendingInvitations.map(invitation => <tr key={invitation.id}><th scope="row" data-label="Invitee">{invitation.displayName}<br />{invitation.email}</th><td data-label="Role">{normalizeRole(invitation.role) === 'customer_admin' ? 'Customer administrator' : 'Member'}</td><td data-label="Modules">{(invitation.moduleKeys ?? []).join(', ') || 'None'}</td><td data-label="Expires">{new Date(invitation.expiresAt).toLocaleDateString()}</td><td data-label="Actions"><div className="workspace-access-actions"><button type="button" disabled={busy} onClick={() => void reissue(invitation)}>Reissue invitation for {invitation.email}</button><button type="button" disabled={busy} onClick={() => void revoke(invitation)}>Revoke invitation for {invitation.email}</button></div></td></tr>)}</tbody></table></div>}
    </section>
    {removeTarget && <div className="modal-backdrop"><section ref={removeDialogRef} className="mutation-dialog" role="dialog" aria-modal="true" aria-labelledby="remove-access-title"><h2 id="remove-access-title">Remove workspace access?</h2><p>Remove {removeTarget.email} from this workspace? This does not delete their Entra account.</p><div className="workspace-access-actions"><button type="button" disabled={busy} onClick={() => setRemoveTarget(null)}>Cancel</button><button type="button" disabled={busy} onClick={() => void removeMember()}>Confirm remove access</button></div></section></div>}
  </section>;
}

function normalizeRole(role: string): 'member' | 'customer_admin' | 'workspace_owner' {
  if (role.toLowerCase() === 'workspace_owner') return 'workspace_owner';
  return role.replace(/[-_]/g, '').toLowerCase() === 'customeradmin' || role.toLowerCase() === 'admin' || role.toLowerCase() === 'owner' ? 'customer_admin' : 'member';
}

function toggle(values: string[], value: string) {
  return values.includes(value) ? values.filter(item => item !== value) : [...values, value];
}
