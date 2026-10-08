import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
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
import { AuthenticationMethodsSection, type AuthenticationMethodsSummary } from './AuthenticationMethodsSection';
import { PermissionState } from '../../components/PermissionState';
import { DisabledReason } from '../../components/DisabledReason';
import { DomainAccessChip } from '../../components/DomainAccessChip';
import { ActionMenu } from '../../components/ActionMenu';
import { AssociatedDevicesSection } from './AssociatedDevicesSection';
import { RevokeSessionsDialog } from './RevokeSessionsDialog';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { StatusBadge } from '../../components/StatusBadge';
import { DataFreshness } from '../../components/DataFreshness';
import { SectionRetryContext, type UserDetailSectionKey } from './IdentitySection';
import { normalizeUserWriteReason, UserWriteReasonField, type UserWriteReasonError } from './UserWriteReasonField';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { useWorkspaceIssueReporter } from '../../notifications/WorkspaceNotifications';

type ProfileTab = 'identity' | 'devices' | 'activity';

export function UserDetailPage({ userId, loadUserDetail, capabilities = [], modules, onNavigate }: { onNavigate?: (path: string) => void; userId?: string; loadUserDetail?: (userId: string) => Promise<UserDetailResponse>; capabilities?: CapabilityDecision[]; modules?: string[] }) {
  const issueReporter = useWorkspaceIssueReporter();
  const api = useApi();
  const resolvedUserId = userId ?? userIdFromPath(window.location.pathname);
  const currentUserIdRef = useRef(resolvedUserId);
  currentUserIdRef.current = resolvedUserId;
  const sectionRetrySequence = useRef<Record<UserDetailSectionKey, number>>({ identity: 0, licenses: 0, groups: 0, roles: 0 });
  const [partialRetryFor, setPartialRetryFor] = useState<string | null>(null);
  const [refreshingSections, setRefreshingSections] = useState<Partial<Record<UserDetailSectionKey, boolean>>>({});
  const partialRetryPending = partialRetryFor === resolvedUserId;
  const loader = useMemo(() => loadUserDetail ?? ((id: string) => fetchUserDetail(api as ApiFetch, id)), [api, loadUserDetail]);
  const [detail, setDetail] = useState<UserDetailResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [editOpen, setEditOpen] = useState(false);
  const [selectedTab, setSelectedTab] = useState<ProfileTab>('identity');
  const [authenticationSummary, setAuthenticationSummary] = useState<AuthenticationMethodsSummary>({ status: 'unavailable', items: [] });
  const [temporaryAccessPassOpen, setTemporaryAccessPassOpen] = useState(false);
  const tabRefs = useRef<Record<ProfileTab, HTMLButtonElement | null>>({ identity: null, devices: null, activity: null });
  const [reactivateOpen, setReactivateOpen] = useState(false);
  const [resetPasswordOpen, setResetPasswordOpen] = useState(false);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [reasonValidationError, setReasonValidationError] = useState<UserWriteReasonError>(null);
  const [reason, setReason] = useState('');
  const [auditWarning, setAuditWarning] = useState<string | null>(null);
  const [mutationPending, setMutationPending] = useState(false);
  const [groupAction, setGroupAction] = useState<{ id: string | null; target?: string; mode: 'add' | 'remove' } | null>(null);
  const [licenseAction, setLicenseAction] = useState<{ id: string | null; target?: string; mode: 'assign' | 'remove' } | null>(null);
  const [revokeSessionsOpen, setRevokeSessionsOpen] = useState(false);
  const [disableOpen, setDisableOpen] = useState(false);
  const viewDecision = findDecision(capabilities, 'users.view');
  const updateDecision = findDecision(capabilities, 'users.update');
  const disableDecision = findDecision(capabilities, 'users.disable');
  const resetPasswordDecision = findDecision(capabilities, 'users.reset_password');
  const authenticationMethodsDecision = findDecision(capabilities, 'authentication.methods.view');
  const authenticationMethodsManageDecision = findDecision(capabilities, 'authentication.methods.manage');
  const devicesViewDecision = findDecision(capabilities, 'devices.view');
  const revokeSessionsDecision = findDecision(capabilities, 'users.sessions.revoke');
  const reportAuthenticationSummary = useCallback((summary: AuthenticationMethodsSummary) => setAuthenticationSummary(summary), []);

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

  useEffect(() => {
    setSelectedTab('identity');
    setRefreshingSections({});
    setTemporaryAccessPassOpen(false);
    setAuthenticationSummary({
      status: authenticationMethodsDecision.state === 'allowed' || authenticationMethodsDecision.state === 'read_only' ? 'loading' : 'unavailable',
      items: [],
    });
  }, [resolvedUserId, authenticationMethodsDecision.state]);

  useEffect(() => {
    if (detail && !hasSectionErrors(detail)) issueReporter.clear('users:detail');
  }, [detail, issueReporter]);

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
  const retainAuditWarning = (result: unknown) => {
    const warning = result && typeof result === 'object' && 'auditWarning' in result ? (result as { auditWarning?: unknown }).auditWarning : null;
    setAuditWarning(typeof warning === 'string' && warning.trim() ? warning : null);
  };
  const refreshAfterAuditedSuccess = (result: unknown) => {
    retainAuditWarning(result);
    refreshAfterSuccess();
  };

  const displayName = user.displayName || user.userPrincipalName || user.id;
  const refreshDetail = () => { setPartialRetryFor(resolvedUserId); setRefreshVersion(version => version + 1); };
  const retrySection = async (section: UserDetailSectionKey) => {
    if (refreshingSections[section]) return;
    const requestUserId = resolvedUserId;
    const requestVersion = ++sectionRetrySequence.current[section];
    const isCurrentRequest = () => currentUserIdRef.current === requestUserId && sectionRetrySequence.current[section] === requestVersion;
    setRefreshingSections(current => ({ ...current, [section]: true }));
    try {
      const refreshed = sanitizeDetailErrors(await loader(requestUserId));
      if (!isCurrentRequest()) return;
      setDetail(current => {
        if (!current) return refreshed;
        if (section === 'identity') return { ...current, user: refreshed.user, access: refreshed.access };
        if (section === 'licenses') return { ...current, licenses: refreshed.licenses };
        if (section === 'groups') return { ...current, groups: refreshed.groups };
        return { ...current, roles: refreshed.roles, pim: refreshed.pim };
      });
    } catch {
      if (!isCurrentRequest()) return;
      setDetail(current => current ? markSectionUnavailable(current, section) : current);
      issueReporter.report({ key: 'users:detail', area: 'users', kind: 'service', severity: 'warning', title: 'User detail unavailable', detail: 'Try loading user details again.' });
    } finally {
      if (isCurrentRequest()) setRefreshingSections(current => ({ ...current, [section]: false }));
    }
  };
  const sectionFailed = (access: UserDetailResponse['access']) => access.partialData || access.freshness === 'unavailable' || Boolean(access.error);
  const failedSections = [
    sectionFailed(detail.access) && 'Identity',
    sectionFailed(detail.licenses.access) && 'Licenses',
    sectionFailed(detail.groups.access) && 'Groups',
    (sectionFailed(detail.roles.access) || sectionFailed(detail.pim.access)) && 'Roles',
  ].filter((value): value is string => Boolean(value));
  const moreActions = [
    revokeSessionsDecision.state === 'allowed' ? { label: 'Revoke sessions', description: 'Signs the user out everywhere.', onSelect: () => setRevokeSessionsOpen(true) } : null,
    disableDecision.state === 'allowed' && user.accountEnabled !== false ? { label: 'Disable user', description: 'Blocks sign-in for this user.', danger: true, separatorBefore: true, onSelect: () => { setMutationError(null); setReasonValidationError(null); setReason(''); setDisableOpen(true); } } : null,
  ].filter((item): item is NonNullable<typeof item> => item !== null);

  const selectTab = (tab: ProfileTab, focus = false) => {
    setSelectedTab(tab);
    if (focus) tabRefs.current[tab]?.focus();
  };

  const handleTabKeyDown = (event: React.KeyboardEvent<HTMLButtonElement>) => {
    const tabs: ProfileTab[] = ['identity', 'devices', 'activity'];
    const currentIndex = tabs.indexOf(event.currentTarget.dataset.profileTab as ProfileTab);
    let nextTab: ProfileTab | undefined;
    if (event.key === 'ArrowRight') nextTab = tabs[(currentIndex + 1) % tabs.length];
    if (event.key === 'ArrowLeft') nextTab = tabs[(currentIndex - 1 + tabs.length) % tabs.length];
    if (event.key === 'Home') nextTab = tabs[0];
    if (event.key === 'End') nextTab = tabs[tabs.length - 1];
    if (nextTab) {
      event.preventDefault();
      selectTab(nextTab, true);
    }
  };

  const submitDisable = async (value: string) => {
    const normalizedReason = normalizeUserWriteReason(value);
    if (normalizedReason.error) {
      setReasonValidationError(normalizedReason.error);
      return;
    }
    setReasonValidationError(null);
    setMutationPending(true);
    setMutationError(null);
    try {
      const response = await mutateUser(api as ApiFetch, `/api/users/${encodeURIComponent(user.id)}/disable`, 'POST', { reason: normalizedReason.reason });
      if (response.status === 'succeeded') { setDisableOpen(false); refreshAfterAuditedSuccess(response); }
      else setMutationError(formatMutationError(response));
    } catch {
      setMutationError('Sign-in could not be disabled. Review the user and try again.');
    } finally {
      setMutationPending(false);
    }
  };

  const submitReactivate = async (value: string) => {
    const normalizedReason = normalizeUserWriteReason(value);
    if (normalizedReason.error) {
      setReasonValidationError(normalizedReason.error);
      return;
    }
    setReasonValidationError(null);
    if (disableDecision.state !== 'allowed') {
      setMutationError(messages.userDisablePermissionDenied);
      setReactivateOpen(false);
      return;
    }
    setMutationPending(true);
    setMutationError(null);
    try {
      const response = await mutateUser(api as ApiFetch, `/api/users/${encodeURIComponent(user.id)}/reactivate`, 'POST', { reason: normalizedReason.reason });
      if (response.status === 'succeeded') refreshAfterAuditedSuccess(response);
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
            <DomainAccessChip label={messages.userProfileHeading} viewDecision={viewDecision} viewReason={decisionReason(viewDecision)} writeDecision={updateDecision} writeReason={decisionReason(updateDecision)} />
            <DataFreshness fetchedAt={detail.access.fetchedAt} freshness={detail.access.freshness} partialData={false} source="Microsoft Graph" />
          </>}
          actions={<div className="page-action-bar user-detail-hero__actions" role="group" aria-label="User management actions">
            {updateDecision.state === 'allowed' && !user.isReadOnly && <button className="button button--secondary" type="button" onClick={() => { setMutationError(null); setReasonValidationError(null); setEditOpen(true); }}>Edit user</button>}
            {disableDecision.state === 'allowed' && user.accountEnabled === false && <button className="button button--secondary" type="button" onClick={() => { setMutationError(null); setReasonValidationError(null); setReason(''); setReactivateOpen(true); }}>Reactivate user</button>}
            {disableDecision.state !== 'allowed' && disableDecision.state !== 'hidden' && user.accountEnabled === false && <DisabledReason reason={actionDecisionReason(disableDecision)}><button className="button button--secondary" type="button" disabled>Reactivate user</button></DisabledReason>}
            {disableDecision.state !== 'allowed' && disableDecision.state !== 'hidden' && user.accountEnabled !== false && <DisabledReason reason={actionDecisionReason(disableDecision)}><button className="button button--secondary" type="button" disabled>Disable user</button></DisabledReason>}
            {resetPasswordDecision.state === 'allowed' && <button className="button button--secondary" type="button" onClick={() => { setMutationError(null); setReasonValidationError(null); setResetPasswordOpen(true); }}>Reset password</button>}
            {moreActions.length > 0 && <ActionMenu label="More actions" items={moreActions} />}
          </div>}
        />
        <div className="user-action-guidance" aria-label="Action availability">
          <ActionGuidance label="Edit user" decision={updateDecision} readOnlySource={user.isReadOnly} />
          {user.accountEnabled === false && <ActionGuidance label="Reactivate user" decision={disableDecision} readOnlySource={false} />}
          <ActionGuidance label="Reset password" decision={resetPasswordDecision} readOnlySource={false} />
          <ActionGuidance label="Revoke sessions" decision={revokeSessionsDecision} readOnlySource={false} />
        </div>
        <p className="user-audit-attempt-note">{messages.userProfileAuditAttemptNotice}</p>
      </div>
      {failedSections.length > 0 && <WorkspaceDataState kind="partial" title="Some sections couldn't load" message="The rest of this page is up to date." affected={failedSections} retryLabel="Retry user details" retrying={partialRetryPending} onRetry={refreshDetail} />}
      <div className="user-status-strip" role="region" aria-label={messages.userProfileSummaryLabel}>
        <div><span>Account</span><strong>{user.accountEnabled === true ? messages.userAccountEnabled : user.accountEnabled === false ? messages.userAccountDisabled : messages.userProfileAccountUnavailable}</strong></div>
        <div><span>{messages.userProfileAuthenticationMethodsLabel}</span><strong>{authenticationSummary.status === 'loading' ? messages.userAuthenticationMethodsLoading : authenticationSummary.status === 'unavailable' ? messages.userAuthenticationMethodsUnavailable : authenticationSummary.items.length ? authenticationSummary.items.map(item => item.displayName).join(', ') : messages.userProfileNoAuthenticationMethods}</strong></div>
        <div><span>Phishing</span><strong>{messages.userProfilePhishingUnavailable}</strong></div>
        <div><span>Last sign-in</span><strong>{messages.userProfileLastSignInUnavailable}</strong></div>
        {(modules === undefined || modules.includes('licenses')) && <div><span>Licenses</span><strong>{!detail.licenses.access.partialData && detail.licenses.access.freshness !== 'unavailable' && !detail.licenses.access.error && ['allowed', 'read_only'].includes(detail.licenses.access.authorization.state) ? `${detail.licenses.items.length} assigned` : messages.usersUnavailableValue}</strong></div>}
      </div>
      {mutationError && !disableOpen && !reactivateOpen && <p role="alert">{mutationError}</p>}
      {auditWarning && !resetPasswordOpen && !revokeSessionsOpen && !temporaryAccessPassOpen && <p role="alert" className="audit-warning">{auditWarning}</p>}
      {editOpen && <UserEditDialog user={user} onCancel={() => { if (!mutationPending) setEditOpen(false); }} onCompleted={(response) => response.status === 'succeeded' ? refreshAfterAuditedSuccess(response) : (retainAuditWarning(response), setMutationError(formatMutationError(response)))} />}
      {reactivateOpen && <ConfirmationDialog title="Reactivate user" target={user.displayName || user.userPrincipalName || user.id} proposedChange="Restore sign-in for this user." requiredCapability="users.disable" confirmLabel={messages.confirmEnableUser} busy={mutationPending} confirmBlocked={Boolean(reasonValidationError) || !reason.trim()} onConfirm={() => void submitReactivate(reason)} onCancel={() => { if (!mutationPending) { setReactivateOpen(false); setMutationError(null); setReasonValidationError(null); setReason(''); } }}>
        <UserWriteReasonField value={reason} onChange={(value) => { setReason(value); setReasonValidationError(null); setMutationError(null); }} error={reasonValidationError} />
        {mutationError && <p role="alert">{mutationError}</p>}
      </ConfirmationDialog>}
      {resetPasswordOpen && <PasswordResetDialog user={user} onClose={() => setResetPasswordOpen(false)} onAuditWarning={setAuditWarning} />}
      {disableOpen && <ConfirmationDialog title={messages.userDisableDialogTitle} target={displayName} proposedChange={messages.userDisableProposedChange} requiredCapability="users.disable" destructivePhrase="DISABLE" confirmLabel={messages.confirmDisableUser} consequence={messages.confirmDisableUserConsequence(displayName)} tone="danger" busy={mutationPending} confirmBlocked={Boolean(reasonValidationError) || !reason.trim()} onConfirm={() => void submitDisable(reason)} onCancel={() => { if (!mutationPending) { setDisableOpen(false); setMutationError(null); setReasonValidationError(null); setReason(''); } }}>
        <UserWriteReasonField value={reason} onChange={(value) => { setReason(value); setReasonValidationError(null); setMutationError(null); }} error={reasonValidationError} />
        {mutationError && <p role="alert">{mutationError}</p>}
      </ConfirmationDialog>}
      {revokeSessionsOpen && <RevokeSessionsDialog userId={user.id} target={user.displayName || user.userPrincipalName || user.id} onClose={() => setRevokeSessionsOpen(false)} onAuditWarning={setAuditWarning} />}
      <SectionRetryContext.Provider value={(section) => { void retrySection(section); }}>
      <div className="user-profile-tabs">
        <div className="user-profile-tabs__list" role="tablist" aria-label={messages.userProfileHeading}>
          {(['identity', 'devices', 'activity'] as const).map(tab => <button
            key={tab}
            ref={element => { tabRefs.current[tab] = element; }}
            type="button"
            role="tab"
            id={`user-profile-tab-${tab}`}
            data-profile-tab={tab}
            aria-selected={selectedTab === tab}
            aria-controls={`user-profile-panel-${tab}`}
            tabIndex={selectedTab === tab ? 0 : -1}
            onClick={() => selectTab(tab)}
            onKeyDown={handleTabKeyDown}
          >{tabLabel(tab)}</button>)}
        </div>
        <div id="user-profile-panel-identity" role="tabpanel" aria-labelledby="user-profile-tab-identity" tabIndex={0} hidden={selectedTab !== 'identity'}>
          <div className="user-detail-grid">
            <IdentitySection user={user} access={detail.access} canEdit={updateDecision.state === 'allowed'} onEdit={() => { setMutationError(null); setEditOpen(true); }} busy={partialRetryPending || Boolean(refreshingSections.identity)} />
            <JobInformationSection user={user} access={detail.access} canEdit={updateDecision.state === 'allowed'} onEdit={() => { setMutationError(null); setEditOpen(true); }} busy={partialRetryPending || Boolean(refreshingSections.identity)} />
            {(modules === undefined || modules.includes('licenses')) && <LicensesSection section={detail.licenses} canManage={findDecision(capabilities, 'licenses.assign').state === 'allowed'} onAdd={() => setLicenseAction({ id: null, mode: 'assign' })} onRemove={(item) => setLicenseAction({ id: item.skuId, target: item.displayName || item.skuId, mode: 'remove' })} busy={partialRetryPending || Boolean(refreshingSections.licenses)} />}
            <GroupsSection section={detail.groups} canManage={findDecision(capabilities, 'groups.manage_members').state === 'allowed'} onAdd={() => setGroupAction({ id: null, mode: 'add' })} onRemove={(item) => setGroupAction({ id: item.id, target: item.displayName || item.id, mode: 'remove' })} busy={partialRetryPending || Boolean(refreshingSections.groups)} />
            <RolesAndPimSection roles={detail.roles} pim={detail.pim} busy={partialRetryPending || Boolean(refreshingSections.roles)} />
            {authenticationMethodsDecision.state !== 'hidden' && <AuthenticationMethodsSection userId={user.id} userLabel={user.displayName || user.userPrincipalName || user.id} decision={authenticationMethodsDecision} manageDecision={authenticationMethodsManageDecision} onResult={reportAuthenticationSummary} onAuditWarning={setAuditWarning} onTemporaryAccessPassOpenChange={setTemporaryAccessPassOpen} />}
          </div>
        </div>
        <div id="user-profile-panel-devices" role="tabpanel" aria-labelledby="user-profile-tab-devices" tabIndex={0} hidden={selectedTab !== 'devices'}>
          {(modules === undefined || modules.includes('devices')) && <AssociatedDevicesSection userId={user.id} decision={devicesViewDecision} />}
        </div>
        <div id="user-profile-panel-activity" role="tabpanel" aria-labelledby="user-profile-tab-activity" tabIndex={0} hidden={selectedTab !== 'activity'}>
          <section className="detail-section" aria-labelledby="user-profile-activity-title">
            <div className="detail-section__header"><h2 id="user-profile-activity-title">{messages.userProfileActivityTab}</h2></div>
            <p>{messages.userProfileActivityUnavailable}</p>
          </section>
        </div>
      </div>
      </SectionRetryContext.Provider>
      {groupAction && <GroupMembershipDialog userId={user.id} groupId={groupAction.id} target={groupAction.target} assignedGroupIds={detail.groups.items.map((item) => item.id)} mode={groupAction.mode} onCancel={() => setGroupAction(null)} onCompleted={(response) => response.status === 'succeeded' ? (setGroupAction(null), refreshAfterAuditedSuccess(response)) : (retainAuditWarning(response), setMutationError(formatMutationError(response)))} />}
      {licenseAction && (modules === undefined || modules.includes('licenses')) && <LicenseAssignmentDialog userId={user.id} skuId={licenseAction.id} target={licenseAction.target} assignedSkuIds={detail.licenses.items.map((item) => item.skuId)} mode={licenseAction.mode} onCancel={() => setLicenseAction(null)} onCompleted={(response) => response.status === 'succeeded' ? (setLicenseAction(null), refreshAfterAuditedSuccess(response)) : (retainAuditWarning(response), setMutationError(formatMutationError(response)))} />}
    </section>
  );
}

function ActionGuidance({ label, decision, readOnlySource }: { label: string; decision: CapabilityDecision; readOnlySource: boolean }) {
  if (decision.state === 'hidden' || (!readOnlySource && decision.state === 'allowed')) return null;
  const explanation = readOnlySource ? 'This account is read-only at its source of authority.' : actionDecisionReason(decision);
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

function markSectionUnavailable(detail: UserDetailResponse, section: UserDetailSectionKey): UserDetailResponse {
  const unavailableAccess = (access: UserDetailResponse['access']) => ({
    ...access,
    freshness: 'unavailable' as const,
    partialData: true,
    error: { category: 'unavailable', message: 'Section data is unavailable. Check Notifications for details.' },
  });
  if (section === 'identity') return { ...detail, access: unavailableAccess(detail.access) };
  if (section === 'licenses') return { ...detail, licenses: { ...detail.licenses, access: unavailableAccess(detail.licenses.access) } };
  if (section === 'groups') return { ...detail, groups: { ...detail.groups, access: unavailableAccess(detail.groups.access) } };
  return {
    ...detail,
    roles: { ...detail.roles, access: unavailableAccess(detail.roles.access) },
    pim: { ...detail.pim, access: unavailableAccess(detail.pim.access) },
  };
}

function hasSectionErrors(detail: UserDetailResponse) {
  return [detail.access, detail.licenses.access, detail.groups.access, detail.roles.access, detail.pim.access]
    .some(access => access.partialData || access.freshness === 'unavailable' || Boolean(access.error));
}

function initials(value: string) {
  const words = value.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  return words.slice(0, 2).map(word => word[0]).join('').toUpperCase();
}

function findDecision(capabilities: CapabilityDecision[], capability: CapabilityDecision['capability']): CapabilityDecision {
  return capabilities.find((item) => item.capability === capability) ?? { capability, state: 'hidden', reasonCode: 'capability_not_returned' };
}

function tabLabel(tab: ProfileTab) {
  if (tab === 'identity') return messages.userProfileIdentityTab;
  if (tab === 'devices') return messages.userProfileDevicesTab;
  return messages.userProfileActivityTab;
}

function decisionReason(decision: CapabilityDecision) {
  if (decision.state === 'allowed') return 'This action is allowed by the current workspace capability snapshot.';
  if (decision.state === 'read_only') return 'The current Entra role can view this data but cannot change it.';
  if (decision.state === 'consent_required') return 'Microsoft Graph delegated consent is required.';
  if (decision.state.startsWith('pim_')) return 'Activate the required Entra role in PIM before continuing.';
  if (decision.state === 'disabled') return 'This action is disabled for the current workspace.';
  if (decision.state === 'temporarily_unavailable') return 'The current Microsoft Graph authorization could not be verified.';
  return 'This action is unavailable with the current workspace permissions.';
}

function actionDecisionReason(decision: CapabilityDecision) {
  return ({
    read_only: 'This action is read-only for your current Entra role.',
    pim_activation_required: 'Activate the required Entra role in PIM before continuing.',
    pim_approval_required: 'This action is waiting for PIM approval.',
    pim_mfa_required: 'Complete MFA for PIM activation before continuing.',
    pim_eligibility_expired: 'Your PIM eligibility has expired. Request renewed access.',
    consent_required: 'Delegated Microsoft Graph consent is required before this action can run.',
    disabled: 'This action is disabled for the current workspace.',
    temporarily_unavailable: 'Microsoft Graph authorization could not be verified. Try again later.',
    allowed: 'This action is allowed by the current workspace capability snapshot.',
    hidden: 'This action is unavailable with your current permissions.',
  } as Record<CapabilityDecision['state'], string>)[decision.state];
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
