import React, { useCallback, useEffect, useState } from 'react';
import { adminApi, type InvitationResult, type WorkspaceAdminDetail, type WorkspaceOnboardingResult } from './adminApi';

export function WorkspaceDetailPage({ workspaceId, firstAdminInvitation }: { workspaceId: string; firstAdminInvitation?: WorkspaceOnboardingResult | null }) {
  const [detail, setDetail] = useState<WorkspaceAdminDetail | null>(null);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [issuedLink, setIssuedLink] = useState<InvitationResult | null>(firstAdminInvitation ? { invitationUrl: firstAdminInvitation.invitationUrl, expiresAt: firstAdminInvitation.expiresAt } : null);
  const [recoveryOpen, setRecoveryOpen] = useState(false);
  const [recoveryEmail, setRecoveryEmail] = useState('');
  const [recoveryName, setRecoveryName] = useState('');
  const [busyInvitationId, setBusyInvitationId] = useState<string | null>(null);
  const [revokeConfirmId, setRevokeConfirmId] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try { setDetail(await adminApi.getWorkspace(workspaceId)); setError(''); }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Workspace details are unavailable.'); }
  }, [workspaceId]);
  useEffect(() => { void refresh(); }, [refresh]);
  useEffect(() => {
    setIssuedLink(firstAdminInvitation?.workspace.id === workspaceId
      ? { invitationUrl: firstAdminInvitation.invitationUrl, expiresAt: firstAdminInvitation.expiresAt }
      : null);
  }, [workspaceId, firstAdminInvitation]);

  if (error && !detail) return <section className="admin-card"><h1>Workspace details</h1><p role="alert">{error}</p><button type="button" onClick={() => void refresh()}>Retry</button></section>;
  if (!detail) return <p role="status">Loading workspace details…</p>;

  const pendingInvitations = detail.invitations.filter(invitation => !invitation.redeemedAt && !invitation.revokedAt);
  const activeInvitations = pendingInvitations.filter(invitation => new Date(invitation.expiresAt).getTime() > Date.now());
  const hasCustomerAdmin = detail.memberships.some(membership => !membership.isAteaOperator && ['customer_admin', 'customeradmin', 'admin', 'owner'].includes(membership.platformRole.toLowerCase().replace('-', '_')));
  const connected = detail.connectionStatus.toLowerCase() === 'connected';
  const isWaitingForAdmin = activeInvitations.length > 0 && !hasCustomerAdmin;
  const nextAction = connected ? 'Onboarding complete. The customer administrator can now invite other workspace users.'
    : isWaitingForAdmin ? 'Waiting for the first customer administrator to redeem the invitation.'
      : hasCustomerAdmin ? 'The first administrator is in the workspace. Complete tenant consent and verify the connection.'
        : 'Issue a recovery invitation to the nominated customer administrator.';

  const reissue = async (invitationId: string) => {
    setBusyInvitationId(invitationId); setError(''); setSuccess(''); setIssuedLink(null);
    try { setIssuedLink(await adminApi.reissueInvitation(workspaceId, invitationId)); setSuccess('A replacement link is ready. The previous invitation link has been invalidated.'); await refresh(); }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Invitation could not be reissued.'); }
    finally { setBusyInvitationId(null); }
  };

  const revoke = async (invitationId: string) => {
    setBusyInvitationId(invitationId); setError(''); setSuccess('');
    try { await adminApi.revokeInvitation(workspaceId, invitationId); setIssuedLink(null); setRevokeConfirmId(null); setSuccess('Pending invitation revoked.'); await refresh(); }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Invitation could not be revoked.'); }
    finally { setBusyInvitationId(null); }
  };

  const createRecoveryInvitation = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setError(''); setSuccess(''); setIssuedLink(null);
    try {
      const expiresAt = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString();
      setIssuedLink(await adminApi.createInvitation(workspaceId, { email: recoveryEmail.trim(), displayName: recoveryName.trim(), expiresAt }));
      setSuccess('Recovery invitation created. Share the link through your approved channel; the invited user must sign in with the matching Entra account.');
      setRecoveryOpen(false); setRecoveryEmail(''); setRecoveryName(''); await refresh();
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Recovery invitation could not be created.'); }
  };

  const linkToShow = issuedLink;
  return <section className="admin-workspace-detail">
    <p className="eyebrow">Customer workspace</p>
    <h1>{detail.displayName}</h1>
    <dl className="admin-detail-grid"><div><dt>Tenant ID</dt><dd><code>{detail.tenantId}</code></dd></div><div><dt>Connection status</dt><dd><strong>{detail.connectionStatus}</strong></dd></div><div><dt>Last verified</dt><dd>{detail.lastVerifiedAt ? new Date(detail.lastVerifiedAt).toLocaleString() : 'Not verified yet'}</dd></div><div><dt>Last connection issue</dt><dd>{detail.connectionFailureCategory ?? 'None recorded'}</dd></div></dl>
    {error && <p role="alert">{error}</p>}{success && <p role="status">{success}</p>}

    <section className="admin-card onboarding-progress" aria-labelledby="onboarding-progress-title">
      <h2 id="onboarding-progress-title">Onboarding progress</h2>
      <ol><li data-complete="true">Workspace created</li><li data-complete={hasCustomerAdmin || activeInvitations.length > 0 || detail.invitations.some(invitation => invitation.redeemedAt !== null)}>{hasCustomerAdmin ? 'First customer administrator joined' : activeInvitations.length ? 'First administrator invited' : 'First administrator not yet invited'}</li><li data-complete={connected}>Tenant connection verified</li></ol>
      <p><strong>Next action:</strong> {nextAction}</p>
    </section>

    <section className="admin-card" aria-labelledby="first-admin-invite-title">
      <h2 id="first-admin-invite-title">Administrator invitations</h2>
      <p>Workspace roles control this application only. Microsoft 365 actions continue to respect the signed-in user’s Entra RBAC and PIM state.</p>
      {linkToShow && <div className="admin-card admin-invitation-link" role="status"><h3>One-time invitation link</h3><p>Copy this link and share it through an approved channel. It is not sent by email and is shown only in this admin session.</p><code>{linkToShow.invitationUrl}</code><p>Expires: {new Date(linkToShow.expiresAt).toLocaleString()}</p><button type="button" onClick={() => { void navigator.clipboard?.writeText(linkToShow.invitationUrl); }}>Copy invitation link</button></div>}
      {detail.invitations.length === 0 ? <p>No invitations have been issued.</p> : <div className="admin-table-wrap"><table className="admin-table"><thead><tr><th scope="col">Invitee</th><th scope="col">Role</th><th scope="col">Status</th><th scope="col">Expires</th><th scope="col">Actions</th></tr></thead><tbody>{detail.invitations.map(invitation => {
        const pending = !invitation.redeemedAt && !invitation.revokedAt;
        const status = invitation.redeemedAt ? 'Redeemed' : invitation.revokedAt ? 'Revoked' : new Date(invitation.expiresAt) < new Date() ? 'Expired' : 'Pending';
        return <tr key={invitation.id}><td>{invitation.displayName}<br />{invitation.email}</td><td>{invitation.role.replace('_', ' ')}</td><td>{status}</td><td>{new Date(invitation.expiresAt).toLocaleDateString()}</td><td>{pending && <div className="admin-actions"><button type="button" disabled={busyInvitationId !== null} onClick={() => void reissue(invitation.id)}>Reissue invitation for {invitation.email}</button>{revokeConfirmId === invitation.id ? <><span role="status">This invalidates the pending link.</span><button type="button" disabled={busyInvitationId !== null} onClick={() => void revoke(invitation.id)}>Confirm revoke invitation for {invitation.email}</button><button type="button" disabled={busyInvitationId !== null} onClick={() => setRevokeConfirmId(null)}>Cancel revoke for {invitation.email}</button></> : <button type="button" disabled={busyInvitationId !== null} onClick={() => setRevokeConfirmId(invitation.id)}>Revoke invitation for {invitation.email}</button>}</div>}</td></tr>;
      })}</tbody></table></div>}
      {!hasCustomerAdmin && <div className="admin-recovery-actions"><button type="button" onClick={() => setRecoveryOpen(value => !value)}>Create recovery invitation</button>{recoveryOpen && <form className="admin-card" onSubmit={event => void createRecoveryInvitation(event)}><h3>Scoped workspace recovery</h3><p>Invite the customer administrator without assigning a manual Entra object ID. The recipient must redeem while signed in to the matching tenant.</p><label>Admin sign-in address<input aria-label="Recovery admin sign-in address" type="email" required maxLength={320} value={recoveryEmail} onChange={event => setRecoveryEmail(event.target.value)} /></label><label>Admin display name<input aria-label="Recovery admin display name" required maxLength={200} value={recoveryName} onChange={event => setRecoveryName(event.target.value)} /></label><button type="submit">Issue recovery invitation</button></form>}</div>}
    </section>

    <section className="admin-card" aria-labelledby="workspace-memberships-title"><h2 id="workspace-memberships-title">Workspace members</h2>{detail.memberships.length ? <ul>{detail.memberships.map(membership => <li key={membership.id}>{membership.email} — {membership.platformRole} — {membership.isAteaOperator ? 'Atea operator' : 'Customer user'}</li>)}</ul> : <p>No members have redeemed an invitation yet.</p>}</section>
  </section>;
}
