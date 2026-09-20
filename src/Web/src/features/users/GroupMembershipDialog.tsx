import React from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { mutateUser, type UserCommandResponse } from './userMutationApi';

export function GroupMembershipDialog({ userId, groupId, target, mode, onCompleted }: { userId: string; groupId: string; target: string; mode: 'add' | 'remove'; onCompleted?: (result: UserCommandResponse) => void }) {
  const api = useApi();
  const submit = async () => {
    const path = `/api/users/${encodeURIComponent(userId)}/groups/${encodeURIComponent(groupId)}`;
    onCompleted?.(await mutateUser(api, path, mode === 'add' ? 'POST' : 'DELETE', { groupObjectId: groupId }));
  };

  return (
    <ConfirmationDialog
      title={messages.userGroupDialogTitle}
      target={target}
      proposedChange={mode === 'add' ? messages.userGroupAddProposedChange : messages.userGroupRemoveProposedChange}
      requiredCapability="groups.manage_members"
      onConfirm={submit}
    />
  );
}
