import React from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { mutateUser, type UserCommandResponse } from './userMutationApi';

export function LicenseAssignmentDialog({ userId, skuId, target, mode, disabledPlans = [], onCompleted }: { userId: string; skuId: string; target: string; mode: 'assign' | 'remove'; disabledPlans?: string[]; onCompleted?: (result: UserCommandResponse) => void }) {
  const api = useApi();
  const submit = async () => {
    const path = `/api/users/${encodeURIComponent(userId)}/licenses/${encodeURIComponent(skuId)}`;
    onCompleted?.(await mutateUser(api, path, mode === 'assign' ? 'POST' : 'DELETE', { skuId, disabledPlans }));
  };

  return (
    <ConfirmationDialog
      title={messages.userLicenseDialogTitle}
      target={target}
      proposedChange={mode === 'assign' ? messages.userLicenseAssignProposedChange : messages.userLicenseRemoveProposedChange}
      requiredCapability="licenses.assign"
      onConfirm={submit}
    />
  );
}
