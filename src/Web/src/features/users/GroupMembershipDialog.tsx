import React, { useEffect, useMemo, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { mutateUser, type UserCommandResponse } from './userMutationApi';
import { UserWriteReasonField, normalizeUserWriteReason } from './UserWriteReasonField';

type GroupChoice = { id: string; displayName: string | null; mailNickname?: string | null };
type GroupCatalogPayload = { items?: GroupChoice[]; error?: string | { category?: string; code?: string }; access?: { state?: string } };

export function GroupMembershipDialog({ userId, groupId, target, mode, assignedGroupIds = [], onCancel, onCompleted }: { userId: string; groupId: string | null; target?: string; mode: 'add' | 'remove'; assignedGroupIds?: string[]; onCancel?: () => void; onCompleted?: (result: UserCommandResponse) => void }) {
  const api = useApi();
  const [choices, setChoices] = useState<GroupChoice[]>([]);
  const [selectedId, setSelectedId] = useState(groupId ?? '');
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [catalogLoading, setCatalogLoading] = useState(mode === 'add');
  const [pending, setPending] = useState(false);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<'reason_required' | 'reason_too_long' | null>(null);
  useEffect(() => {
    if (mode !== 'add') return;
    let cancelled = false;
    setCatalogLoading(true);
    api('/api/groups?pageSize=100').then(async (response) => {
      const payload = await response.json().catch(() => ({})) as GroupCatalogPayload;
      const category = catalogErrorCategory(payload, response.ok);
      if (category) throw new Error(category);
      if (!cancelled) setChoices(payload.items ?? []);
    }).catch((error: unknown) => { if (!cancelled) setCatalogError(mapCatalogError(error instanceof Error ? error.message : 'catalog_unavailable')); })
      .finally(() => { if (!cancelled) setCatalogLoading(false); });
    return () => { cancelled = true; };
  }, [api, mode]);
  const selectable = useMemo(() => choices.filter((choice) => !assignedGroupIds.includes(choice.id)), [assignedGroupIds, choices]);
  const selected = selectable.find((choice) => choice.id === selectedId);
  const effectiveId = mode === 'add' ? selectedId : groupId;
  const effectiveTarget = mode === 'add' ? selected?.displayName || selected?.id || 'Select a group' : target || groupId || '';
  const submit = async () => {
    if (!effectiveId || pending) return;
    const normalizedReason = normalizeUserWriteReason(reason);
    if (normalizedReason.error) {
      setReasonError(normalizedReason.error);
      return;
    }
    setReasonError(null);
    const path = `/api/users/${encodeURIComponent(userId)}/groups/${encodeURIComponent(effectiveId)}`;
    setPending(true);
    try {
      const result = await mutateUser(api, path, mode === 'add' ? 'POST' : 'DELETE', { groupObjectId: effectiveId, reason: normalizedReason.reason });
      onCompleted?.(result);
    }
    catch { onCompleted?.({ status: 'temporarily_unavailable', requiredCapability: 'groups.manage_members', replayed: false, error: 'temporarily_unavailable' }); }
    finally { setPending(false); }
  };

  return (
    <ConfirmationDialog
      title={messages.userGroupDialogTitle}
      target={effectiveTarget}
      proposedChange={mode === 'add' ? messages.userGroupAddProposedChange : messages.userGroupRemoveProposedChange}
      requiredCapability="groups.manage_members"
      confirmLabel={mode === 'add' ? messages.confirmAddToGroup : messages.confirmRemoveFromGroup}
      busy={pending}
      confirmBlocked={Boolean(reasonError)}
      onConfirm={submit}
      onCancel={onCancel}
      sourceLimitation={catalogError || (mode === 'add' && !catalogLoading && choices.length === 0 ? 'No eligible groups are available for this user.' : mode === 'add' && !selectedId ? 'Select a group before confirming.' : null)}
    >
      {mode === 'add' && <label>Group<select aria-label="Group" value={selectedId} onChange={(event) => setSelectedId(event.target.value)}><option value="">Select a group</option>{selectable.map((choice) => <option key={choice.id} value={choice.id}>{choice.displayName || choice.mailNickname || choice.id}</option>)}</select></label>}
      <UserWriteReasonField value={reason} onChange={(value) => { setReason(value); setReasonError(null); }} error={reasonError} />
    </ConfirmationDialog>
  );
}

function mapCatalogError(category: string) {
  if (category === 'capability_required' || category === 'consent_required') return 'Microsoft Graph permissions are required to read the group catalog. Review permissions and try again.';
  if (category === 'not_authorized') return 'Microsoft Graph denied access to the group catalog. Review permissions and try again.';
  if (category === 'not_found') return 'The group catalog is no longer available. Refresh and try again.';
  if (category === 'invalid_request' || category === 'invalid_target') return 'The group catalog request was rejected. Refresh and try again.';
  return 'The group catalog is unavailable. Refresh and try again.';
}

function catalogErrorCategory(payload: GroupCatalogPayload, responseOk: boolean) {
  const error = typeof payload.error === 'string' ? payload.error : payload.error?.category || payload.error?.code;
  if (error) return error;
  if (!responseOk) return 'catalog_unavailable';
  if (payload.access?.state && !['allowed', 'read_only'].includes(payload.access.state)) return 'capability_required';
  return null;
}
