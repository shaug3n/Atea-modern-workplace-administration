import React from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import type { UserDetails } from './userDetailApi';
import { mutateUser, type UpdateUserCommand, type UserCommandResponse } from './userMutationApi';

export function UserEditDialog({ user, onCompleted }: { user: UserDetails; onCompleted?: (result: UserCommandResponse) => void }) {
  const api = useApi();
  const sourceLimitation = user.isReadOnly ? user.sourceOfAuthorityReason ?? messages.userSourceReadOnlyTitle : null;
  const submit = async () => {
    const command: UpdateUserCommand = {
      displayName: user.displayName,
      givenName: user.givenName ?? null,
      surname: user.surname ?? null,
      jobTitle: user.jobTitle ?? null,
      department: user.department ?? null,
      officeLocation: user.officeLocation ?? null,
      mobilePhone: user.mobilePhone ?? null,
      usageLocation: user.usageLocation ?? null,
      accountEnabled: user.accountEnabled,
    };
    onCompleted?.(await mutateUser(api, `/api/users/${encodeURIComponent(user.id)}`, 'PATCH', command));
  };

  return (
    <ConfirmationDialog
      title={messages.userEditDialogTitle}
      target={user.displayName || user.userPrincipalName || user.id}
      proposedChange={messages.userEditProposedChange}
      requiredCapability="users.update"
      sourceLimitation={sourceLimitation}
      onConfirm={submit}
    />
  );
}
