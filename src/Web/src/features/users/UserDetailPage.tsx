import React, { useEffect, useMemo, useState } from 'react';
import { isPimCapabilityState, type CapabilityDecision } from '../../capabilities/capabilityTypes';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { GroupsSection } from './GroupsSection';
import { IdentitySection } from './IdentitySection';
import { JobInformationSection } from './JobInformationSection';
import { LicensesSection } from './LicensesSection';
import { RolesAndPimSection } from './RolesAndPimSection';
import { fetchUserDetail, type ApiFetch, type UserDetailResponse } from './userDetailApi';
import { UserEditDialog } from './UserEditDialog';
import { mutateUser, type UserCommandResponse } from './userMutationApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { GroupMembershipDialog } from './GroupMembershipDialog';
import { LicenseAssignmentDialog } from './LicenseAssignmentDialog';
import { PasswordResetDialog } from './PasswordResetDialog';
import { AuthenticationMethodsSection } from './AuthenticationMethodsSection';
import { PermissionState } from '../../components/PermissionState';
import { ActionMenu } from '../../components/ActionMenu';
import { AssociatedDevicesSection } from './AssociatedDevicesSection';
import { RevokeSessionsDialog } from './RevokeSessionsDialog';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { StatusBadge } from '../../components/StatusBadge';
import { DataFreshness } from '../../components/DataFreshness';
import { SectionRetryContext } from './IdentitySection';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { useWorkspaceIssueReporter } from '../../notifications/WorkspaceNotifications';

export function UserDetailPage({ userId, loadUserDetail, capabilities = [], modules, onNavigate }: { onNavigate?: (path: string) => void; userId?: string; loadUserDetail?: (userId: string) => Promise<UserDetailResponse>; capabilities?: CapabilityDecision[]; modules?: string[] }) {
  const issueReporter = useWorkspaceIssueReporter();
  const api = useApi();
  const resolvedUserId = userId ?? userIdFromPath(window.location.pathname);
  const [partialRetryFor, setPartialRetryFor] = useState<string | null>(null);
  const partialRetryPending = partialRetryFor === resolvedUserId;
  const loader = useMemo(() => loadUserDetail ?? ((id: string) => fetchUserDetail(api as ApiFetch, id)), [api, loadUserDetail]);
  const [detail, setDetail] = useState<UserDetailResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [editOpen, setEditOpen] = useState(false);
  const [reactivateOpen, setReactivateOpen] = useState(false);
  const [resetPasswordOpen, setResetPasswordOpen] = useState(false);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [mutationPending, setMutationPending] = useState(false);
  const [groupAction, setGroupAction] = useState<{ id: string | null; target?: string; mode: 'add' | 'remove' } | null>(null);
  const [licenseAction, setLicenseAction] = useState<{ id: string | null; target?: string; mode: 'assign' | 'remove' } | null>(null);
  const [revokeSessionsOpen, setRevokeSessionsOpen] = useState(false);
  const [disableOpen, setDisableOpen] = useState(false);
  const updateDecision = findDecision(capabilities, 'users.update');
  const disableDecision = findDecision(capabilities, 'users.disable');
  const resetPasswordDecision = findDecision(capabilities, 'users.reset_password');
  const authenticationMethodsDecision = findDecision(capabilities, 'authentication.methods.view');
  const authenticationMethodsManageDecision = findDecision(capabilities, 'authentication.methods.manage');
  const devicesViewDecision = findDecision(capabilities, 'devices.view');
  const revokeSessionsDecision = findDecision(capabilities, 'users.sessions.revoke');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    loader(resolvedUserId)
      .then((response) => {
        if (!cancelled) { setDetail(sanitizeDetailErrors(response)); if ([response.access, response.licenses.access, response.groups.access, response.roles.access, response.pim.access].some(access => access.partialData || access.freshness === 'unavailable' || access.error)) issueReporter.report({ key: 'users:detail', area: 'users', kind: 'service', severity: 'warning', title: 'User detail unavailable', detail: 'Try loading user details again.' }); else issueReporter.clear('users:detail'); }
      })
      .catch((loadError: unknown) => {
        if (!cancelled) { setError(loadError instanceof Error ? loadError : new Error('user_detail_unavailable')); issueReporter.report({ key: 'users:detail', area: 'users', kind: 'service', severity: 'warning', title: 'User detail unavailable', detail: 'Try loading user details again.' }); }
      })
      .finally(() => {
        if (!cancelled) { setLoading(false); setPartialRetryFor(null); }
      });
    return () => { cancelled = true; };
  }, [loader, resolvedUserId, refreshVersion, issueReporter]);

  if (loading && (!partialRetryPending || !detail?.user)) {
    return <section className="user-detail-page"><WorkspacePageHeader eyebrow="Users" title={messages.userDetailTitle} /><WorkspaceDataState state="loading" message={messages.userDetailLoading} /></section>;
  }

  if (error) {
    return (
      <section className="user-detail-page">
        <WorkspacePageHeader eyebrow="Users" title={messages.userDetailTitle} />
        <WorkspaceDataState state="unavailable" message={error.message === 'user_not_found' ? messages.userDetailNotFound : messages.userDetailUnavailable} onRetry={() => setRefreshVersion(version => version + 1)} />
      </section>
    );
  }

  if (!detail?.user) {
    const access = detail?.access.authorization;
    const isPimGated = access ? isPimCapabilityState(access.state) : false;
    const title = access?.state === 'consent_required'
      ? messages.permissionConsentTitle
      : isPimGated
        ? messages.usersPimRequiredTitle
        : messages.userDetailTitle;
    const body = access?.state === 'consent_required'
      ? messages.permissionConsentBody
      : isPimGated
        ? messages.usersPimRequiredBody
        : messages.userDetailUnavailable;
    return (
      <section className="permission-panel" role="status">
        <h1>{title}</h1>
        {access && access.state !== 'hidden' ? <PermissionState decision={access}><p>{body}</p></PermissionState> : <p>{body}</p>}
      </section>
    );
  }

  const user = detail.user;
  const refreshAfterSuccess = () => {
    setEditOpen(false);
    setReactivateOpen(false);
    setMutationError(null);
    setRefreshVersion((version) => version + 1);
  };

  const displayName = user.displayName || user.userPrincipalName || user.id;
  const refreshDetail = () => { setPartialRetryFor(resolvedUserId); setRefreshVersion(version => version + 1); };
  const sectionFailed = (access: UserDetailResponse['access']) => access.partialData || access.freshness === 'unavailable' || Boolean(access.error);
  const failedSections = [
    sectionFailed(detail.access) && 'Identity',
    sectionFailed(detail.licenses.access) && 'Licenses',
    sectionFailed(detail.groups.access) && 'Groups',
    (sectionFailed(detail.roles.access) || sectionFailed(detail.pim.access)) && 'Roles',
  ].filter((value): value is string => Boolean(value));
  const moreActions = [
    revokeSessionsDecision.state === 'allowed' && !user.isReadOnly ? { label: 'Revoke sessions', description: 'Signs the user out everywhere.', onSelect: () => setRevokeSessionsOpen(true) } : null,
    disableDecision.state === 'allowed' && !user.isReadOnly && user.accountEnabled !== false ? { label: 'Disable user', description: 'Blocks sign-in for this user.', danger: true, separatorBefore: true, onSelect: () => { setMutationError(null); setDisableOpen(true); } } : null,
  ].filter((item): item is NonNullable<typeof item> => item !== null);

  const submitDisable = async () => {
    setMutationPending(true);
    setMutationError(null);
    try {
      const response = await mutateUser(api as ApiFetch, `/api/users/${encodeURIComponent(user.id)}/disable`, 'POST', {});
      if (response.status === 'succeeded') { setDisableOpen(false); refreshAfterSuccess(); }
      else setMutationError(formatMutationError(response));
    } catch {
      setMutationError('Sign-in could not be disabled. Review the user and try again.');
    } finally {
      setMutationPending(false);
    }
  };

  const submitReactivate = async () => {
    if (disableDecision.state !== 'allowed') {
      setMutationError(messages.userDisablePermissionDenied);
      setReactivateOpen(false);
      return;
    }
    setMutationPending(true);
    setMutationError(null);
    try {
      const response = await mutateUser(api as ApiFetch, `/api/users/${encodeURIComponent(user.id)}/reactivate`, 'POST', {});
      if (response.status === 'succeeded') refreshAfterSuccess();
      else setMutationError(formatMutationError(response));
    } catch {
      setMutationError('Sign-in could not be restored. Review the user and try again.');
    } finally {
      setMutationPending(false);
    }
  };

  return (
    <section className="user-detail-page" aria-label="User details">
      <div className="user-detail-hero">
        <WorkspacePageHeader
          title={user.displayName || user.userPrincipalName || messages.usersUnnamedUser}
          description={user.userPrincipalName || messages.usersUnavailableValue}
          backLink={{ label: 'Back to Users', href: '/users', onNavigate }}
          meta={<>
            <StatusBadge tone={user.accountEnabled === false ? 'neutral' : user.accountEnabled === true ? 'success' : 'warning'} label={user.accountEnabled === false ? messages.userAccountDisabled : user.accountEnabled === true ? messages.userAccountEnabled : 'Account status unavailable'} />
            {user.userType && <StatusBadge tone="info" label={user.userType} />}
            {user.isReadOnly && <StatusBadge tone="warning" label="Read-only source" />}
            <DataFreshness fetchedAt={detail.access.fetchedAt} freshness={detail.access.freshness} partialData={false} source="Microsoft Graph" />
          </>}
          actions={<div className="page-action-bar user-detail-hero__actions" role="group" aria-label="User management actions">
            {updateDecision.state === 'allowed' && !user.isReadOnly && <button className="button button--secondary" type="button" onClick={() => { setMutationError(null); setEditOpen(true); }}>Edit user</button>}
            {disableDecision.state === 'allowed' && user.accountEnabled === false && <button className="button button--secondary" type="button" onClick={() => { setMutationError(null); setReactivateOpen(true); }}>Reactivate user</button>}
            {resetPasswordDecision.state === 'allowed' && !user.isReadOnly && <button className="button button--secondary" type="button" onClick={() => { setMutationError(null); setResetPasswordOpen(true); }}>Reset password</button>}
            {moreActions.length > 0 && <ActionMenu label="More actions" items={moreActions} />}
          </div>}
        />
        <div className="user-action-guidance" aria-label="Action availability">
          <ActionGuidance label="Edit user" decision={updateDecision} readOnlySource={user.isReadOnly} />
          {user.accountEnabled === false && <ActionGuidance label="Reactivate user" decision={disableDecision} readOnlySource={user.isReadOnly} />}
          <ActionGuidance label="Reset password" decision={resetPasswordDecision} readOnlySource={user.isReadOnly} />
          <ActionGuidance label="Revoke sessions" decision={revokeSessionsDecision} readOnlySource={user.isReadOnly} />
        </div>
      </div>
      {failedSections.length > 0 && <WorkspaceDataState kind="partial" title="Some sections couldn't load" message="The rest of this page is up to date." affected={failedSections} retryLabel="Retry user details" retrying={partialRetryPending} onRetry={refreshDetail} />}
      <div className="user-status-strip" role="region" aria-label="User status summary">
        <div><span>Account</span><strong>{user.accountEnabled === true ? 'Enabled' : user.accountEnabled === false ? 'Disabled' : 'Unavailable'}</strong></div>
        <div><span>MFA</span><strong>{authenticationMethodsDecision.state === 'allowed' || authenticationMethodsDecision.state === 'read_only' ? <a href="#authentication-methods-section-title">Review methods</a> : 'Unavailable in summary'}</strong></div>
        {(modules === undefined || modules.includes('licenses')) && <div><span>Licenses</span><strong>{!detail.licenses.access.partialData && ['allowed', 'read_only'].includes(detail.licenses.access.authorization.state) ? `${detail.licenses.items.length} assigned` : 'Unavailable'}</strong></div>}
      </div>
      {mutationError && <p role="alert">{mutationError}</p>}
      {editOpen && <UserEditDialog user={user} onCancel={() => { if (!mutationPending) setEditOpen(false); }} onCompleted={(response) => response.status === 'succeeded' ? refreshAfterSuccess() : setMutationError(formatMutationError(response))} />}
      {reactivateOpen && <ConfirmationDialog title="Reactivate user" target={user.displayName || user.userPrincipalName || user.id} proposedChange="Restore sign-in for this user." requiredCapability="users.disable" confirmLabel={messages.confirmEnableUser} busy={mutationPending} onConfirm={submitReactivate} onCancel={() => { if (!mutationPending) setReactivateOpen(false); }} />}
      {resetPasswordOpen && <PasswordResetDialog user={user} onClose={() => setResetPasswordOpen(false)} />}
      {disableOpen && <ConfirmationDialog title={messages.userDisableDialogTitle} target={displayName} proposedChange={messages.userDisableProposedChange} requiredCapability="users.disable" destructivePhrase="DISABLE" confirmLabel={messages.confirmDisableUser} consequence={messages.confirmDisableUserConsequence(displayName)} tone="danger" busy={mutationPending} onConfirm={submitDisable} onCancel={() => { if (!mutationPending) setDisableOpen(false); }} />}
      {revokeSessionsOpen && <RevokeSessionsDialog userId={user.id} target={user.displayName || user.userPrincipalName || user.id} onClose={() => setRevokeSessionsOpen(false)} />}
      <SectionRetryContext.Provider value={refreshDetail}>
      <div className="user-detail-grid">
        <IdentitySection user={user} access={detail.access} />
        <JobInformationSection user={user} access={detail.access} />
        {(modules === undefined || modules.includes('licenses')) && <LicensesSection section={detail.licenses} canManage={findDecision(capabilities, 'licenses.assign').state === 'allowed'} onAdd={() => setLicenseAction({ id: null, mode: 'assign' })} onRemove={(item) => setLicenseAction({ id: item.skuId, target: item.displayName || item.skuId, mode: 'remove' })} />}
        <GroupsSection section={detail.groups} canManage={findDecision(capabilities, 'groups.manage_members').state === 'allowed'} onAdd={() => setGroupAction({ id: null, mode: 'add' })} onRemove={(item) => setGroupAction({ id: item.id, target: item.displayName || item.id, mode: 'remove' })} />
        <RolesAndPimSection roles={detail.roles} pim={detail.pim} />
        {authenticationMethodsDecision.state !== 'hidden' && <AuthenticationMethodsSection userId={user.id} userLabel={user.displayName || user.userPrincipalName || user.id} decision={authenticationMethodsDecision} manageDecision={authenticationMethodsManageDecision} />}
        {(modules === undefined || modules.includes('devices')) && <AssociatedDevicesSection userId={user.id} decision={devicesViewDecision} />}
      </div>
      </SectionRetryContext.Provider>
      {groupAction && <GroupMembershipDialog userId={user.id} groupId={groupAction.id} target={groupAction.target} assignedGroupIds={detail.groups.items.map((item) => item.id)} mode={groupAction.mode} onCancel={() => setGroupAction(null)} onCompleted={(response) => response.status === 'succeeded' ? (setGroupAction(null), refreshAfterSuccess()) : setMutationError(formatMutationError(response))} />}
      {licenseAction && (modules === undefined || modules.includes('licenses')) && <LicenseAssignmentDialog userId={user.id} skuId={licenseAction.id} target={licenseAction.target} assignedSkuIds={detail.licenses.items.map((item) => item.skuId)} mode={licenseAction.mode} onCancel={() => setLicenseAction(null)} onCompleted={(response) => response.status === 'succeeded' ? (setLicenseAction(null), refreshAfterSuccess()) : setMutationError(formatMutationError(response))} />}
    </section>
  );
}

function ActionGuidance({ label, decision, readOnlySource }: { label: string; decision: CapabilityDecision; readOnlySource: boolean }) {
  if (decision.state === 'hidden' || (!readOnlySource && decision.state === 'allowed')) return null;
  const explanation = readOnlySource
    ? 'This account is read-only at its source of authority.'
    : ({
      read_only: 'This action is read-only for your current Entra role.',
      pim_activation_required: 'Activate the required Entra role in PIM before continuing.',
      pim_approval_required: 'This action is waiting for PIM approval.',
      pim_mfa_required: 'Complete MFA for PIM activation before continuing.',
      pim_eligibility_expired: 'Your PIM eligibility has expired. Request renewed access.',
      consent_required: 'Delegated Microsoft Graph consent is required before this action can run.',
      disabled: 'This action is disabled for the current workspace.',
      temporarily_unavailable: 'Microsoft Graph authorization could not be verified. Try again later.',
    } as Partial<Record<CapabilityDecision['state'], string>>)[decision.state] ?? 'This action is unavailable with your current permissions.';
  return <p role="status">{label}: {explanation}</p>;
}

function sanitizeDetailErrors(response: UserDetailResponse): UserDetailResponse {
  const safeAccess = (access: UserDetailResponse['access']) => access.error ? { ...access, error: { ...access.error, message: 'Section data is unavailable. Check Notifications for details.' } } : access;
  return {
    ...response,
    access: safeAccess(response.access),
    licenses: { ...response.licenses, access: safeAccess(response.licenses.access) },
    groups: { ...response.groups, access: safeAccess(response.groups.access) },
    roles: { ...response.roles, access: safeAccess(response.roles.access) },
    pim: { ...response.pim, access: safeAccess(response.pim.access) },
  };
}

function initials(value: string) {
  const words = value.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  return words.slice(0, 2).map(word => word[0]).join('').toUpperCase();
}

function findDecision(capabilities: CapabilityDecision[], capability: CapabilityDecision['capability']): CapabilityDecision {
  return capabilities.find((item) => item.capability === capability) ?? { capability, state: 'hidden', reasonCode: 'capability_not_returned' };
}

function formatMutationError(response: UserCommandResponse) {
  if (response.error === 'idempotency_key_reused' || response.error === 'conflict') return messages.userMutationConflict;
  if (response.error === 'throttled' || response.status === 'temporarily_unavailable') return messages.userMutationThrottled;
  if (response.error === 'consent_required') return 'Microsoft Graph consent is required before this action can be completed.';
  if (response.error === 'source_of_authority_read_only' || response.status === 'source_of_authority_read_only') return messages.userDisableSourceReadOnly;
  if (response.error === 'group_not_found') return 'The selected group is no longer available. Refresh the catalog and try again.';
  if (response.error === 'license_not_found') return 'The selected license is no longer available. Refresh the catalog and try again.';
  if (response.error === 'not_found') return 'The selected directory item is no longer available. Refresh the catalog and try again.';
  if (response.error === 'not_authorized') return 'Microsoft Graph denied access to the catalog. Review permissions and try again.';
  if (response.error === 'invalid_request' || response.error === 'invalid_license' || response.status === 'invalid_target') return 'The selected catalog item is invalid. Refresh the catalog and try again.';
  if (response.error === 'capability_required' || response.status === 'denied') return messages.userDisablePermissionDenied;
  return 'Sign-in could not be restored. Review the user and try again.';
}

function userIdFromPath(pathname: string) {
  const [, usersSegment, userId] = pathname.split('/');
  return usersSegment === 'users' && userId ? decodeURIComponent(userId) : '';
}
