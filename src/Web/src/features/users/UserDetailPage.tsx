import React, { useEffect, useMemo, useState } from 'react';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { AsyncState } from '../../components/AsyncState';
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

export function UserDetailPage({ userId, loadUserDetail, capabilities = [] }: { userId?: string; loadUserDetail?: (userId: string) => Promise<UserDetailResponse>; capabilities?: CapabilityDecision[] }) {
  const api = useApi();
  const resolvedUserId = userId ?? userIdFromPath(window.location.pathname);
  const loader = useMemo(() => loadUserDetail ?? ((id: string) => fetchUserDetail(api as ApiFetch, id)), [api, loadUserDetail]);
  const [detail, setDetail] = useState<UserDetailResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [editOpen, setEditOpen] = useState(false);
  const [reactivateOpen, setReactivateOpen] = useState(false);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [mutationPending, setMutationPending] = useState(false);
  const [groupAction, setGroupAction] = useState<{ id: string; target: string; mode: 'add' | 'remove' } | null>(null);
  const [licenseAction, setLicenseAction] = useState<{ id: string; target: string; mode: 'assign' | 'remove' } | null>(null);
  const updateDecision = findDecision(capabilities, 'users.update');
  const disableDecision = findDecision(capabilities, 'users.disable');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    loader(resolvedUserId)
      .then((response) => {
        if (!cancelled) setDetail(response);
      })
      .catch((loadError: unknown) => {
        if (!cancelled) setError(loadError instanceof Error ? loadError : new Error('user_detail_unavailable'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [loader, resolvedUserId, refreshVersion]);

  if (loading) {
    return <AsyncState state="loading"><span>{messages.userDetailLoading}</span></AsyncState>;
  }

  if (error) {
    return (
      <section className="permission-panel" role="alert">
        <h1>{messages.userDetailTitle}</h1>
        <p>{error.message === 'user_not_found' ? messages.userDetailNotFound : messages.userDetailUnavailable}</p>
      </section>
    );
  }

  if (!detail?.user) {
    return (
      <section className="permission-panel" role="status">
        <h1>{messages.userDetailTitle}</h1>
        <p>{detail?.access.error?.message ?? messages.userDetailUnavailable}</p>
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
    <section className="user-detail-page" aria-labelledby="user-detail-title">
      <div className="users-page__header">
        <div>
          <p className="eyebrow">{messages.userDetailTitle}</p>
          <h1 id="user-detail-title">{detail.user.displayName || detail.user.userPrincipalName || messages.usersUnnamedUser}</h1>
          <p>{user.userPrincipalName || messages.usersUnavailableValue}</p>
          <div className="users-page__actions">
            {updateDecision.state === 'allowed' && !user.isReadOnly && <button type="button" onClick={() => { setMutationError(null); setEditOpen(true); }}>Edit user</button>}
            {disableDecision.state === 'allowed' && user.accountEnabled === false && <button type="button" onClick={() => { setMutationError(null); setReactivateOpen(true); }}>Reactivate user</button>}
          </div>
        </div>
      </div>
      {mutationError && <p role="alert">{mutationError}</p>}
      {editOpen && <UserEditDialog user={user} onCompleted={(response) => response.status === 'succeeded' ? refreshAfterSuccess() : setMutationError(formatMutationError(response))} />}
      {reactivateOpen && <ConfirmationDialog title="Reactivate user" target={user.displayName || user.userPrincipalName || user.id} proposedChange="Restore sign-in for this user." requiredCapability="users.disable" busy={mutationPending} onConfirm={submitReactivate} onCancel={() => { if (!mutationPending) setReactivateOpen(false); }} />}
      <div className="detail-grid">
        <IdentitySection user={user} access={detail.access} />
        <JobInformationSection user={user} access={detail.access} />
        <LicensesSection section={detail.licenses} canManage={detail.licenses.access.authorization.state === 'allowed' || findDecision(capabilities, 'licenses.assign').state === 'allowed'} onAdd={() => { const item = detail.licenses.items[0]; if (item) setLicenseAction({ id: item.skuId, target: item.displayName || item.skuId, mode: 'assign' }); }} onRemove={(item) => setLicenseAction({ id: item.skuId, target: item.displayName || item.skuId, mode: 'remove' })} />
        <GroupsSection section={detail.groups} canManage={detail.groups.access.authorization.state === 'allowed' || findDecision(capabilities, 'groups.manage_members').state === 'allowed'} onAdd={() => { const item = detail.groups.items[0]; if (item) setGroupAction({ id: item.id, target: item.displayName || item.id, mode: 'add' }); }} onRemove={(item) => setGroupAction({ id: item.id, target: item.displayName || item.id, mode: 'remove' })} />
        <RolesAndPimSection roles={detail.roles} pim={detail.pim} />
      </div>
      {groupAction && <GroupMembershipDialog userId={user.id} groupId={groupAction.id} target={groupAction.target} mode={groupAction.mode} onCompleted={(response) => response.status === 'succeeded' ? (setGroupAction(null), refreshAfterSuccess()) : setMutationError(formatMutationError(response))} />}
      {licenseAction && <LicenseAssignmentDialog userId={user.id} skuId={licenseAction.id} target={licenseAction.target} mode={licenseAction.mode} onCompleted={(response) => response.status === 'succeeded' ? (setLicenseAction(null), refreshAfterSuccess()) : setMutationError(formatMutationError(response))} />}
    </section>
  );
}

function findDecision(capabilities: CapabilityDecision[], capability: CapabilityDecision['capability']): CapabilityDecision {
  return capabilities.find((item) => item.capability === capability) ?? { capability, state: 'hidden', reasonCode: 'capability_not_returned' };
}

function formatMutationError(response: UserCommandResponse) {
  if (response.error === 'idempotency_key_reused' || response.error === 'conflict') return messages.userMutationConflict;
  if (response.error === 'throttled' || response.status === 'temporarily_unavailable') return messages.userMutationThrottled;
  if (response.error === 'consent_required') return 'Microsoft Graph consent is required before this action can be completed.';
  if (response.error === 'source_of_authority_read_only' || response.status === 'source_of_authority_read_only') return messages.userDisableSourceReadOnly;
  if (response.error === 'capability_required' || response.status === 'denied') return messages.userDisablePermissionDenied;
  return 'Sign-in could not be restored. Review the user and try again.';
}

function userIdFromPath(pathname: string) {
  const [, usersSegment, userId] = pathname.split('/');
  return usersSegment === 'users' && userId ? decodeURIComponent(userId) : '';
}
